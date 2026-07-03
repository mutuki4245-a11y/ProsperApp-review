drop function if exists public.quick_enter_receipt(bigint, text, date, numeric, text, text, text, text);
drop function if exists public.quick_enter_receipt(bigint, text, date, numeric, text, text, text, jsonb, text);

create or replace function public.get_pending_receipts(
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
set search_path = public
as $$
    select
        d.document_id::text,
        d.file_id,
        d.document_date,
        d.amount,
        d.status,
        f.file_name,
        f.drive_file_id,
        f.drive_url,
        f.storage_path
    from public.documents d
    left join public.document_files f
      on f.file_id = d.file_id
    where d.department_id = p_department_id
      and d.status = p_status
    order by d.created_at asc;
$$;

create or replace function public.quick_enter_receipt(
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
set search_path = public
as $$
begin
    return query
    update public.documents d
       set document_date = p_payment_date,
           amount = p_amount,
           title = trim(p_account_subject),
           memo = trim(p_description) || case
               when nullif(trim(coalesce(p_group_code, '')), '') is null then ''
               else ' [G:' || trim(p_group_code) || ']'
           end,
           status = p_status,
           updated_at = now()
     where d.document_id::text = p_document_id
       and d.department_id = p_department_id
    returning d.document_id::text;
end;
$$;

create or replace function public.mark_receipt_scan_mistake(
    p_department_id bigint,
    p_document_id text,
    p_status text default 'excluded'
)
returns table (
    document_id text
)
language plpgsql
security definer
set search_path = public
as $$
begin
    return query
    update public.documents d
       set status = p_status,
           updated_at = now()
     where d.document_id::text = p_document_id
       and d.department_id = p_department_id
    returning d.document_id::text;
end;
$$;
