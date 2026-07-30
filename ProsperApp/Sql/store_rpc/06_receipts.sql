begin;

drop function if exists store.quick_enter_receipt(bigint, text, date, numeric, text, text, text, text);
drop function if exists store.quick_enter_receipt(bigint, text, date, numeric, text, text, text, jsonb, text);

create or replace function store.get_pending_receipts(
    p_department_id bigint,
    p_status text default 'unprocessed'
)
returns table (
    document_id text,
    file_id bigint,
    document_date date,
    amount numeric,
    status text,
    file_name text,
    drive_file_id text,
    drive_url text,
    storage_path text
)
language sql
security definer
set search_path = pg_catalog, public
as $$
    with department as (
        select d.department_id, d.company_id
        from public.department_master d
        where d.department_id = p_department_id
          and d.is_active = true
    ),
    document_status as (
        select
            d.document_id,
            case
                when count(link.link_id) = 0 then 'unlinked'
                when bool_or(entry.status = 'draft') then 'draft'
                when bool_or(entry.status = 'confirmed') then 'confirmed'
                when bool_or(entry.status = 'exported') then 'exported'
                when bool_or(entry.status = 'voided') then 'voided'
                else 'linked'
            end as link_status
        from accounting.documents d
        left join accounting.document_journal_links link
          on link.document_id = d.document_id
        left join accounting.journal_entries entry
          on entry.journal_entry_id = link.journal_entry_id
        where d.deleted_at is null
        group by d.document_id
    )
    select
        document.document_id,
        null::bigint as file_id,
        draft_entry.entry_date as document_date,
        draft_entry.amount,
        case document_status.link_status
            when 'unlinked' then 'unprocessed'
            when 'confirmed' then 'quick_entered'
            when 'voided' then 'excluded'
            else document_status.link_status
        end as status,
        document.original_file_name as file_name,
        document.drive_file_id,
        document.drive_url,
        document.storage_path
    from accounting.documents document
    join document_status
      on document_status.document_id = document.document_id
    join department
      on department.company_id = document.company_id
    left join accounting.upload_source_master upload_source
      on upload_source.upload_source_id = document.upload_source_id
    left join lateral (
        select
            entry.entry_date,
            (
                select sum(line.amount)
                from accounting.journal_entry_lines line
                where line.journal_entry_id = entry.journal_entry_id
                  and line.dc_flag = 'D'
            ) as amount
        from accounting.document_journal_links link
        join accounting.journal_entries entry
          on entry.journal_entry_id = link.journal_entry_id
        where link.document_id = document.document_id
          and entry.status = 'draft'
        order by entry.updated_at desc, entry.journal_entry_id
        limit 1
    ) draft_entry on true
    where document.deleted_at is null
      and (
          upload_source.default_department_id = p_department_id
          or exists (
              select 1
              from accounting.document_journal_links link
              join accounting.journal_entry_lines line
                on line.journal_entry_id = link.journal_entry_id
              where link.document_id = document.document_id
                and line.department_id = p_department_id
          )
      )
      and case lower(coalesce(nullif(trim(p_status), ''), 'unprocessed'))
          when 'unprocessed' then document_status.link_status = 'unlinked'
          when 'quick_entered' then document_status.link_status = 'confirmed'
          when 'excluded' then document_status.link_status = 'voided'
          else document_status.link_status = lower(trim(p_status))
      end
    order by document.uploaded_at asc, document.file_no asc, document.document_id asc;
$$;

create or replace function store.quick_enter_receipt(
    p_department_id bigint,
    p_document_id text,
    p_payment_date date,
    p_amount numeric,
    p_account_subject text,
    p_description text,
    p_group_code text,
    p_journal_payload jsonb default null,
    p_status text default 'quick_entered'
)
returns table (
    document_id text
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_company_id bigint;
    v_entry_id text;
    v_save_result jsonb;
begin
    if nullif(trim(coalesce(p_document_id, '')), '') is null
       or p_payment_date is null
       or coalesce(p_amount, 0) <= 0
       or nullif(trim(coalesce(p_account_subject, '')), '') is null
       or nullif(trim(coalesce(p_description, '')), '') is null then
        raise exception 'invalid_receipt_input';
    end if;

    if lower(coalesce(nullif(trim(p_status), ''), 'quick_entered')) <> 'quick_entered' then
        raise exception 'invalid_receipt_status';
    end if;

    select department.company_id
      into v_company_id
    from public.department_master department
    where department.department_id = p_department_id
      and department.is_active = true;

    if v_company_id is null then
        raise exception 'store_department_not_found';
    end if;

    if not exists (
        select 1
        from accounting.documents document
        left join accounting.upload_source_master upload_source
          on upload_source.upload_source_id = document.upload_source_id
        where document.document_id = trim(p_document_id)
          and document.company_id = v_company_id
          and document.deleted_at is null
          and upload_source.default_department_id = p_department_id
          and not exists (
              select 1
              from accounting.document_journal_links link
              where link.document_id = document.document_id
          )
    ) then
        raise exception 'pending_receipt_not_found';
    end if;

    if p_journal_payload is null
       or jsonb_typeof(p_journal_payload) <> 'object'
       or jsonb_typeof(p_journal_payload->'journal_entries') <> 'array'
       or jsonb_array_length(p_journal_payload->'journal_entries') <> 1
       or jsonb_typeof(p_journal_payload->'journal_entry_lines') <> 'array'
       or jsonb_array_length(p_journal_payload->'journal_entry_lines') <> 2
       or jsonb_typeof(p_journal_payload->'document_journal_links') <> 'array'
       or jsonb_array_length(p_journal_payload->'document_journal_links') <> 1 then
        raise exception 'invalid_receipt_journal_payload';
    end if;

    v_entry_id := p_journal_payload->'journal_entries'->0->>'journal_entry_id';
    if nullif(trim(coalesce(v_entry_id, '')), '') is null
       or v_entry_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
       or p_journal_payload->'journal_entries'->0->>'journal_date' <> p_payment_date::text
       or lower(coalesce(
           nullif(trim(p_journal_payload->'journal_entries'->0->>'status'), ''),
           'confirmed'
       )) not in ('confirmed', 'ready') then
        raise exception 'invalid_receipt_journal_payload';
    end if;

    if exists (
        select 1
        from jsonb_array_elements(p_journal_payload->'journal_entry_lines') line
        where line->>'journal_entry_id' <> v_entry_id
           or coalesce(nullif(line->>'company_id', '')::bigint, v_company_id) <> v_company_id
           or (
               nullif(line->>'department_id', '') is not null
               and (line->>'department_id')::bigint <> p_department_id
           )
    ) or exists (
        select 1
        from jsonb_array_elements(p_journal_payload->'document_journal_links') link
        where link->>'journal_entry_id' <> v_entry_id
           or link->>'document_id' <> trim(p_document_id)
    ) then
        raise exception 'receipt_journal_scope_mismatch';
    end if;

    if (
        select coalesce(sum(
            case when upper(line->>'side') in ('DEBIT', 'D')
                 then coalesce(nullif(line->>'amount', '')::numeric, 0)
                 else 0
            end
        ), 0)
        from jsonb_array_elements(p_journal_payload->'journal_entry_lines') line
    ) <> p_amount or (
        select coalesce(sum(
            case when upper(line->>'side') in ('CREDIT', 'C')
                 then coalesce(nullif(line->>'amount', '')::numeric, 0)
                 else 0
            end
        ), 0)
        from jsonb_array_elements(p_journal_payload->'journal_entry_lines') line
    ) <> p_amount then
        raise exception 'receipt_journal_amount_mismatch';
    end if;

    select accounting.save_journal_payload(p_journal_payload)
      into v_save_result;

    if coalesce((v_save_result->>'documents')::integer, 0) <> 1
       or coalesce((v_save_result->>'journal_entries')::integer, 0) <> 1
       or coalesce((v_save_result->>'journal_entry_lines')::integer, 0) <> 2
       or coalesce((v_save_result->>'document_journal_links')::integer, 0) <> 1 then
        raise exception 'receipt_journal_save_incomplete';
    end if;

    return query select trim(p_document_id);
end;
$$;

create or replace function store.mark_receipt_scan_mistake(
    p_department_id bigint,
    p_document_id text,
    p_status text default 'excluded'
)
returns table (
    document_id text
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_company_id bigint;
begin
    if nullif(trim(coalesce(p_document_id, '')), '') is null
       or lower(coalesce(nullif(trim(p_status), ''), 'excluded')) <> 'excluded' then
        raise exception 'invalid_receipt_exclusion';
    end if;

    select department.company_id
      into v_company_id
    from public.department_master department
    where department.department_id = p_department_id
      and department.is_active = true;

    if v_company_id is null then
        raise exception 'store_department_not_found';
    end if;

    return query
    update accounting.documents document
       set deleted_at = now(),
           updated_at = now()
     where document.document_id = trim(p_document_id)
       and document.company_id = v_company_id
       and document.deleted_at is null
       and exists (
           select 1
           from accounting.upload_source_master upload_source
           where upload_source.upload_source_id = document.upload_source_id
             and upload_source.default_department_id = p_department_id
       )
       and not exists (
           select 1
           from accounting.document_journal_links link
           where link.document_id = document.document_id
       )
    returning document.document_id;
end;
$$;

revoke all on function store.get_pending_receipts(bigint, text)
    from public, anon, authenticated, service_role;
revoke all on function store.quick_enter_receipt(bigint, text, date, numeric, text, text, text, jsonb, text)
    from public, anon, authenticated, service_role;
revoke all on function store.mark_receipt_scan_mistake(bigint, text, text)
    from public, anon, authenticated, service_role;

commit;
