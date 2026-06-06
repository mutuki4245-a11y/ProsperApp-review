begin;

create or replace function public.get_store_context(p_department_id bigint)
returns table (
    company_id bigint,
    department_id bigint,
    department_name text
)
language sql
security definer
set search_path = public
as $$
    select
        d.company_id,
        d.department_id,
        d.department_name
    from public.department_master d
    where d.department_id = p_department_id
      and d.is_active = true
    limit 1;
$$;

create or replace function public.get_current_business_day(p_department_id bigint)
returns table (
    business_day_id bigint,
    company_id bigint,
    department_id bigint,
    business_date date,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    status text,
    memo text
)
language sql
security definer
set search_path = public
as $$
    select
        b.business_day_id,
        b.company_id,
        b.department_id,
        b.business_date,
        b.opened_at,
        b.closed_at,
        b.status,
        b.memo
    from public.store_business_days b
    where b.department_id = p_department_id
      and b.status = 'open'
    order by b.opened_at desc
    limit 1;
$$;

create or replace function public.open_business_day(
    p_department_id bigint,
    p_business_date date,
    p_memo text default null
)
returns table (
    business_day_id bigint,
    company_id bigint,
    department_id bigint,
    business_date date,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    status text,
    memo text
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
begin
    select d.company_id
      into v_company_id
    from public.department_master d
    where d.department_id = p_department_id
      and d.is_active = true
    limit 1;

    if v_company_id is null then
        raise exception 'store_department_not_found';
    end if;

    if exists (
        select 1
        from public.store_business_days b
        where b.company_id = v_company_id
          and b.department_id = p_department_id
          and b.status = 'open'
    ) then
        raise exception 'business_day_already_open';
    end if;

    return query
    insert into public.store_business_days (
        company_id,
        department_id,
        business_date,
        opened_at,
        status,
        memo
    )
    values (
        v_company_id,
        p_department_id,
        p_business_date,
        now(),
        'open',
        nullif(trim(coalesce(p_memo, '')), '')
    )
    returning
        store_business_days.business_day_id,
        store_business_days.company_id,
        store_business_days.department_id,
        store_business_days.business_date,
        store_business_days.opened_at,
        store_business_days.closed_at,
        store_business_days.status,
        store_business_days.memo;
end;
$$;

create or replace function public.get_open_slip_count(
    p_department_id bigint,
    p_business_day_id bigint
)
returns integer
language sql
security definer
set search_path = public
as $$
    select count(*)::integer
    from public.store_slips s
    where s.department_id = p_department_id
      and s.business_day_id = p_business_day_id
      and s.status = 'open';
$$;

create or replace function public.close_business_day(
    p_department_id bigint,
    p_business_day_id bigint,
    p_memo text default null
)
returns table (
    business_day_id bigint,
    company_id bigint,
    department_id bigint,
    business_date date,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    status text,
    memo text
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_open_slip_count integer;
begin
    select public.get_open_slip_count(p_department_id, p_business_day_id)
      into v_open_slip_count;

    if coalesce(v_open_slip_count, 0) > 0 then
        raise exception 'open_slips_exist:%', v_open_slip_count;
    end if;

    return query
    update public.store_business_days b
       set status = 'closed',
           closed_at = now(),
           memo = coalesce(nullif(trim(coalesce(p_memo, '')), ''), b.memo)
     where b.business_day_id = p_business_day_id
       and b.department_id = p_department_id
       and b.status = 'open'
    returning
        b.business_day_id,
        b.company_id,
        b.department_id,
        b.business_date,
        b.opened_at,
        b.closed_at,
        b.status,
        b.memo;
end;
$$;

create or replace function public.get_store_tables(p_department_id bigint)
returns table (
    table_id bigint,
    table_code text,
    table_name text
)
language sql
security definer
set search_path = public
as $$
    select
        t.table_id,
        t.table_code,
        t.table_name
    from public.store_table_master t
    where t.department_id = p_department_id
      and t.is_active = true
    order by t.sort_order asc, t.table_code asc;
$$;

create or replace function public.get_store_casts(p_department_id bigint)
returns table (
    cast_id bigint,
    cast_code text,
    display_name text
)
language sql
security definer
set search_path = public
as $$
    select
        c.cast_id,
        c.cast_code,
        c.display_name
    from public.cast_master c
    where c.department_id = p_department_id
      and c.is_active = true
      and c.status = 'active'
    order by c.sort_order asc, c.display_name asc;
$$;

create or replace function public.get_business_day_slips(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    slip_id bigint,
    slip_no text,
    table_id bigint,
    table_code text,
    table_name text,
    opened_at timestamp with time zone,
    status text,
    customer_count integer,
    memo text
)
language sql
security definer
set search_path = public
as $$
    select
        s.slip_id,
        s.slip_no,
        s.table_id,
        t.table_code,
        t.table_name,
        s.opened_at,
        s.status,
        s.customer_count,
        s.memo
    from public.store_slips s
    left join public.store_table_master t
      on t.table_id = s.table_id
    where s.department_id = p_department_id
      and s.business_day_id = p_business_day_id
    order by s.opened_at asc;
$$;

create or replace function public.create_store_slip(
    p_department_id bigint,
    p_table_id bigint,
    p_opened_at timestamp with time zone,
    p_customer_labels text[] default array[]::text[],
    p_cast_ids bigint[] default array[]::bigint[],
    p_memo text default null
)
returns table (
    slip_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_business_day public.store_business_days%rowtype;
    v_slip_id bigint;
    v_line_no integer;
    v_label text;
    v_cast_id bigint;
    v_customer_count integer;
    v_slip_no text;
begin
    select d.company_id
      into v_company_id
    from public.department_master d
    where d.department_id = p_department_id
      and d.is_active = true
    limit 1;

    if v_company_id is null then
        raise exception 'store_department_not_found';
    end if;

    select *
      into v_business_day
    from public.store_business_days b
    where b.department_id = p_department_id
      and b.status = 'open'
    order by b.opened_at desc
    limit 1;

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_open';
    end if;

    if not exists (
        select 1
        from public.store_table_master t
        where t.table_id = p_table_id
          and t.department_id = p_department_id
          and t.is_active = true
    ) then
        raise exception 'store_table_not_found';
    end if;

    v_customer_count := greatest(coalesce(array_length(p_customer_labels, 1), 0), 1);
    v_slip_no := 'S' || to_char(p_opened_at at time zone 'Asia/Tokyo', 'YYMMDDHH24MISS') ||
                 '-T' || p_table_id::text ||
                 '-' || floor(random() * 900 + 100)::integer::text;

    insert into public.store_slips (
        company_id,
        department_id,
        business_day_id,
        business_date,
        table_id,
        slip_no,
        opened_at,
        status,
        customer_count,
        memo
    )
    values (
        v_company_id,
        p_department_id,
        v_business_day.business_day_id,
        v_business_day.business_date,
        p_table_id,
        v_slip_no,
        p_opened_at,
        'open',
        v_customer_count,
        nullif(trim(coalesce(p_memo, '')), '')
    )
    returning store_slips.slip_id into v_slip_id;

    for v_line_no in 1..v_customer_count loop
        v_label := nullif(trim(coalesce(p_customer_labels[v_line_no], '')), '');
        insert into public.store_slip_customers (
            slip_id,
            line_no,
            customer_label,
            entered_at,
            status
        )
        values (
            v_slip_id,
            v_line_no,
            v_label,
            p_opened_at,
            'active'
        );
    end loop;

    foreach v_cast_id in array coalesce(p_cast_ids, array[]::bigint[]) loop
        if exists (
            select 1
            from public.cast_master c
            where c.cast_id = v_cast_id
              and c.department_id = p_department_id
              and c.is_active = true
              and c.status = 'active'
        ) then
            insert into public.store_slip_casts (
                slip_id,
                cast_id,
                nomination_type,
                started_at,
                status
            )
            values (
                v_slip_id,
                v_cast_id,
                'nomination',
                p_opened_at,
                'active'
            );
        end if;
    end loop;

    return query select v_slip_id;
end;
$$;

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

grant execute on function public.get_store_context(bigint) to anon, authenticated;
grant execute on function public.get_current_business_day(bigint) to anon, authenticated;
grant execute on function public.open_business_day(bigint, date, text) to anon, authenticated;
grant execute on function public.get_open_slip_count(bigint, bigint) to anon, authenticated;
grant execute on function public.close_business_day(bigint, bigint, text) to anon, authenticated;
grant execute on function public.get_store_tables(bigint) to anon, authenticated;
grant execute on function public.get_store_casts(bigint) to anon, authenticated;
grant execute on function public.get_business_day_slips(bigint, bigint) to anon, authenticated;
grant execute on function public.create_store_slip(bigint, bigint, timestamp with time zone, text[], bigint[], text) to anon, authenticated;
grant execute on function public.get_pending_receipts(bigint, text) to anon, authenticated;
grant execute on function public.quick_enter_receipt(bigint, text, date, numeric, text, text, text, text) to anon, authenticated;
grant execute on function public.mark_receipt_scan_mistake(bigint, text, text) to anon, authenticated;

commit;
