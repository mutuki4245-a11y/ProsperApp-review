-- accounting.save_journal_payload のbaseline定義。
--
-- Sql/store_rpc/06_receipts.sql がこの関数を呼び出します。plpgsqlの本体は
-- create時に名前解決されないため、関数が無くてもRPCの作成自体は通りますが、
-- 実際に仕訳を起票した時点で失敗します。テスト用DBで締め→仕訳までを
-- 通すために、本番と同じ定義をここに置きます。

create or replace function accounting.save_journal_payload(payload jsonb)
returns jsonb
language plpgsql
security definer
set search_path to 'pg_catalog', 'accounting', 'public'
as $function$
declare
  document_count integer := 0;
  entry_count integer := jsonb_array_length(coalesce(payload->'journal_entries', '[]'::jsonb));
  line_count integer := jsonb_array_length(coalesce(payload->'journal_entry_lines', '[]'::jsonb));
  link_count integer := jsonb_array_length(coalesce(payload->'document_journal_links', payload->'journal_entry_documents', '[]'::jsonb));
  v_message text;
begin
  drop table if exists pg_temp.tmp_save_entries;
  drop table if exists pg_temp.tmp_save_links;
  drop table if exists pg_temp.tmp_save_lines;
  drop table if exists pg_temp.tmp_save_entry_summary;
  drop table if exists pg_temp.tmp_save_resolved_lines;

  create temporary table tmp_save_entries on commit drop as
  select
    src.journal_entry_id,
    src.journal_date,
    case lower(coalesce(nullif(trim(src.status), ''), 'confirmed'))
      when 'ready' then 'confirmed'
      when 'confirmed' then 'confirmed'
      when 'exported' then 'exported'
      when 'voided' then 'voided'
      else 'draft'
    end as status
  from jsonb_to_recordset(coalesce(payload->'journal_entries', '[]'::jsonb))
    as src(journal_entry_id uuid, journal_date date, status text);

  create temporary table tmp_save_links on commit drop as
  select distinct
    src.journal_entry_id,
    nullif(trim(src.document_id), '') as document_id
  from jsonb_to_recordset(coalesce(payload->'document_journal_links', payload->'journal_entry_documents', '[]'::jsonb))
    as src(journal_entry_id uuid, document_id text);

  select count(distinct document_id) into document_count
  from pg_temp.tmp_save_links
  where document_id is not null;

  create temporary table tmp_save_lines on commit drop as
  select
    src.journal_entry_id,
    src.line_no,
    nullif(trim(src.side), '') as side,
    case when upper(nullif(trim(src.side), '')) in ('DEBIT', 'D') then 'D'
         when upper(nullif(trim(src.side), '')) in ('CREDIT', 'C') then 'C'
         else null end as dc_flag,
    nullif(trim(src.account_code), '') as account_code,
    src.company_id,
    src.department_id,
    nullif(trim(src.department_code), '') as department_code,
    coalesce(src.is_reduced_tax_rate, false) as is_reduced_tax_rate,
    nullif(trim(src.invoice_category_code), '') as invoice_category_code,
    coalesce(src.amount, 0) as amount,
    coalesce(nullif(trim(src.line_memo), ''), nullif(trim(src.description), '')) as line_memo
  from jsonb_to_recordset(coalesce(payload->'journal_entry_lines', '[]'::jsonb))
    as src(journal_entry_id uuid, line_no integer, side text, account_code text, company_id bigint, department_id bigint, department_code text, is_reduced_tax_rate boolean, invoice_category_code text, amount numeric, line_memo text, description text);

  if entry_count = 0 then
    return jsonb_build_object(
      'documents', document_count,
      'journal_entries', 0,
      'journal_entry_lines', line_count,
      'document_journal_links', link_count
    );
  end if;

  select string_agg('<empty>', ', ')
    into v_message
  from pg_temp.tmp_save_entries e
  where e.journal_entry_id is null;
  if v_message is not null then
    raise exception 'journal_entry_id is required for every journal entry';
  end if;

  select string_agg(e.journal_entry_id::text, ', ')
    into v_message
  from pg_temp.tmp_save_entries e
  where not exists (
    select 1 from pg_temp.tmp_save_links l where l.journal_entry_id = e.journal_entry_id
  );
  if v_message is not null then
    raise exception 'every journal entry must be linked to at least one document: %', v_message;
  end if;

  select string_agg(e.journal_entry_id::text, ', ')
    into v_message
  from pg_temp.tmp_save_entries e
  where not exists (
    select 1 from pg_temp.tmp_save_lines l where l.journal_entry_id = e.journal_entry_id
  );
  if v_message is not null then
    raise exception 'every journal entry must have at least one line: %', v_message;
  end if;

  select string_agg(distinct coalesce(l.document_id, '<empty>'), ', ')
    into v_message
  from pg_temp.tmp_save_links l
  left join accounting.documents d on d.document_id = l.document_id
  where l.document_id is null or d.document_id is null;
  if v_message is not null then
    raise exception 'documents not found for save payload: %', v_message;
  end if;

  create temporary table tmp_save_entry_summary on commit drop as
  select
    l.journal_entry_id,
    min(d.company_id) as document_company_id,
    count(distinct d.company_id) as document_company_count
  from pg_temp.tmp_save_links l
  join accounting.documents d on d.document_id = l.document_id
  group by l.journal_entry_id;

  select string_agg(s.journal_entry_id::text, ', ')
    into v_message
  from pg_temp.tmp_save_entry_summary s
  where s.document_company_count <> 1;
  if v_message is not null then
    raise exception 'documents for one journal entry must belong to exactly one company: %', v_message;
  end if;

  select string_agg(format('%s:%s', coalesce(l.journal_entry_id::text, '<empty>'), coalesce(l.side, '<empty>')), ', ')
    into v_message
  from pg_temp.tmp_save_lines l
  where l.journal_entry_id is null
     or l.line_no is null
     or l.account_code is null
     or l.dc_flag is null;
  if v_message is not null then
    raise exception 'invalid journal entry line payload: %', v_message;
  end if;

  select string_agg(format('%s line %s company %s', l.journal_entry_id, l.line_no, l.company_id), ', ')
    into v_message
  from pg_temp.tmp_save_lines l
  join pg_temp.tmp_save_entry_summary s on s.journal_entry_id = l.journal_entry_id
  where l.company_id is not null
    and l.company_id <> s.document_company_id;
  if v_message is not null then
    raise exception 'line company_id must match linked document company: %', v_message;
  end if;

  create temporary table tmp_save_resolved_lines on commit drop as
  select
    l.journal_entry_id,
    l.line_no,
    l.dc_flag,
    l.account_code as input_account_code,
    acct.account_code,
    acct.account_name,
    acct.requires_subaccount,
    l.company_id as input_company_id,
    l.department_id as input_department_id,
    l.department_code as input_department_code,
    explicit_department.department_id as explicit_department_id,
    explicit_department.company_id as explicit_department_company_id,
    explicit_department.department_id as department_id,
    coalesce(explicit_department.company_id, l.company_id, s.document_company_id) as company_id,
    case when acct.requires_subaccount then def_sub.subaccount_id else null end as subaccount_id,
    case when acct.requires_subaccount then def_sub.subaccount_name else null end as subaccount_name,
    l.is_reduced_tax_rate,
    l.invoice_category_code,
    l.amount,
    l.line_memo
  from pg_temp.tmp_save_lines l
  join pg_temp.tmp_save_entry_summary s on s.journal_entry_id = l.journal_entry_id
  left join lateral (
    select am.account_code, am.account_name, am.requires_subaccount
    from accounting.account_master am
    where am.account_code = l.account_code
       or am.account_name = l.account_code
    order by case when am.account_code = l.account_code then 0 else 1 end, am.account_code
    limit 1
  ) acct on true
  left join lateral (
    select dm.department_id, dm.company_id
    from public.department_master dm
    where (
        l.department_id is not null
        and dm.department_id = l.department_id
        and dm.company_id = coalesce(l.company_id, s.document_company_id)
      )
      or (
        l.department_id is null
        and l.department_code is not null
        and dm.company_id = coalesce(l.company_id, s.document_company_id)
        and (dm.department_code = l.department_code or dm.department_name = l.department_code)
      )
    order by
      case
        when l.department_id is not null and dm.department_id = l.department_id then 0
        when dm.department_code = l.department_code then 1
        else 2
      end,
      dm.department_id
    limit 1
  ) explicit_department on true
  left join lateral (
    select m.subaccount_id, sm.subaccount_name
    from accounting.account_subaccount_map m
    join accounting.subaccount_master sm on sm.subaccount_id = m.subaccount_id
    where m.company_id = coalesce(explicit_department.company_id, l.company_id, s.document_company_id)
      and m.account_code = acct.account_code
      and m.is_default = true
    order by m.sort_order nulls last, m.subaccount_id
    limit 1
  ) def_sub on true;

  select string_agg(format('%s line %s account %s', r.journal_entry_id, r.line_no, coalesce(r.input_account_code, '<empty>')), ', ')
    into v_message
  from pg_temp.tmp_save_resolved_lines r
  where r.account_code is null;
  if v_message is not null then
    raise exception 'account_master not found for lines: %', v_message;
  end if;

  select string_agg(format('%s line %s department %s', r.journal_entry_id, r.line_no, coalesce(r.input_department_code, r.input_department_id::text, '<empty>')), ', ')
    into v_message
  from pg_temp.tmp_save_resolved_lines r
  where (r.input_department_id is not null or r.input_department_code is not null)
    and r.explicit_department_id is null;
  if v_message is not null then
    raise exception 'department_master not found for lines: %', v_message;
  end if;

  select string_agg(format('%s line %s company %s', r.journal_entry_id, r.line_no, coalesce(r.input_company_id::text, '<empty>')), ', ')
    into v_message
  from pg_temp.tmp_save_resolved_lines r
  where r.company_id is null;
  if v_message is not null then
    raise exception 'company_id could not be resolved for lines: %', v_message;
  end if;

  select string_agg(format('%s line %s account %s', r.journal_entry_id, r.line_no, r.account_code), ', ')
    into v_message
  from pg_temp.tmp_save_resolved_lines r
  where r.requires_subaccount = true and r.subaccount_id is null;
  if v_message is not null then
    raise exception 'required default subaccount is missing: %', v_message;
  end if;

  select string_agg(format('%s company %s D:%s C:%s', b.journal_entry_id, b.company_id, b.debit_amount, b.credit_amount), ', ')
    into v_message
  from (
    select
      r.journal_entry_id,
      r.company_id,
      sum(case when r.dc_flag = 'D' then r.amount else 0 end) as debit_amount,
      sum(case when r.dc_flag = 'C' then r.amount else 0 end) as credit_amount
    from pg_temp.tmp_save_resolved_lines r
    group by r.journal_entry_id, r.company_id
  ) b
  where b.debit_amount <> b.credit_amount;
  if v_message is not null then
    raise exception 'journal entry is not balanced: %', v_message;
  end if;

  insert into accounting.journal_entries (
    journal_entry_id,
    entry_date,
    status,
    updated_at
  )
  select
    e.journal_entry_id,
    coalesce(e.journal_date, current_date),
    e.status,
    now()
  from pg_temp.tmp_save_entries e
  join pg_temp.tmp_save_entry_summary s on s.journal_entry_id = e.journal_entry_id
  on conflict (journal_entry_id) do update set
    entry_date = excluded.entry_date,
    status = excluded.status,
    updated_at = now();

  delete from accounting.journal_entry_lines line
  using pg_temp.tmp_save_entries input
  where line.journal_entry_id = input.journal_entry_id;

  delete from accounting.document_journal_links link
  using pg_temp.tmp_save_entries input
  where link.journal_entry_id = input.journal_entry_id;

  insert into accounting.journal_entry_lines (
    journal_entry_id,
    line_no,
    company_id,
    account_code,
    subaccount_id,
    department_id,
    dc_flag,
    amount,
    is_reduced_tax_rate,
    invoice_category_code,
    line_memo,
    account_name_snapshot,
    subaccount_name_snapshot
  )
  select
    r.journal_entry_id,
    r.line_no,
    r.company_id,
    r.account_code,
    r.subaccount_id,
    r.department_id,
    r.dc_flag,
    r.amount,
    r.is_reduced_tax_rate,
    r.invoice_category_code,
    r.line_memo,
    r.account_name,
    r.subaccount_name
  from pg_temp.tmp_save_resolved_lines r
  order by r.journal_entry_id, r.line_no;

  insert into accounting.document_journal_links (
    document_id,
    journal_entry_id
  )
  select
    l.document_id,
    l.journal_entry_id
  from pg_temp.tmp_save_links l
  on conflict (document_id, journal_entry_id) do nothing;

  return jsonb_build_object(
    'documents', document_count,
    'journal_entries', entry_count,
    'journal_entry_lines', line_count,
    'document_journal_links', link_count
  );
end;
$function$;
