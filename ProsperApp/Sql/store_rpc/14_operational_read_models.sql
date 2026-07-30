begin;

create or replace function store.get_payment_methods(
    p_department_id bigint
)
returns table (
    method_code text,
    method_name text,
    requires_received_amount boolean,
    sort_order integer
)
language sql
security definer
set search_path = public
as $$
    select
        pm.payment_method_code,
        pm.payment_method_name,
        pm.requires_received_amount,
        pm.sort_order
    from public.payment_method_master pm
    where pm.department_id = p_department_id
      and pm.is_active = true
    order by pm.sort_order, pm.payment_method_id;
$$;

create or replace function store.get_business_day_cast_sales_adjustment_overview(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    status jsonb,
    slips jsonb,
    details jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_status jsonb;
    v_slips jsonb;
    v_details jsonb;
begin
    select to_jsonb(s)
      into v_status
    from store.get_business_day_cast_sales_adjustment_status(
        p_department_id,
        p_business_day_id
    ) s;

    select coalesce(jsonb_agg(to_jsonb(s) order by s.checkout_at, s.slip_id), '[]'::jsonb)
      into v_slips
    from store.get_cast_sales_adjustment_slips(
        p_department_id,
        p_business_day_id
    ) s;

    select coalesce(
        jsonb_agg(to_jsonb(d) order by d.checkout_at, d.slip_id, d.started_at nulls last, d.slip_cast_id),
        '[]'::jsonb)
      into v_details
    from store.get_cast_sales_adjustment_slips(
        p_department_id,
        p_business_day_id
    ) s
    cross join lateral store.get_cast_sales_adjustment_detail(
        p_department_id,
        s.slip_id
    ) d;

    return query
    select
        coalesce(v_status, '{}'::jsonb),
        coalesce(v_slips, '[]'::jsonb),
        coalesce(v_details, '[]'::jsonb);
end;
$$;

create or replace function store.save_business_day_cast_sales_adjustments(
    p_department_id bigint,
    p_business_day_id bigint,
    p_slips jsonb default '[]'::jsonb
)
returns table (
    saved_slip_count integer,
    saved_cast_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip jsonb;
    v_slip_id bigint;
    v_saved_slip_count integer := 0;
    v_saved_cast_count integer := 0;
begin
    if jsonb_typeof(p_slips) <> 'array' or
       jsonb_array_length(p_slips) = 0 or
       jsonb_array_length(p_slips) > 100 then
        raise exception 'invalid_cast_sales_adjustment_batch';
    end if;

    if exists (
        select 1
        from jsonb_array_elements(p_slips) line
        where jsonb_typeof(line.value) <> 'object'
           or coalesce(line.value->>'slip_id', '') !~ '^[1-9][0-9]*$'
    ) then
        raise exception 'invalid_cast_sales_adjustment_batch';
    end if;

    if (
        select count(*)
        from jsonb_array_elements(p_slips) line
    ) <> (
        select count(distinct line.value->>'slip_id')
        from jsonb_array_elements(p_slips) line
    ) then
        raise exception 'duplicate_cast_sales_adjustment_slip';
    end if;

    for v_slip in
        select line.value
        from jsonb_array_elements(p_slips) line
        order by (line.value->>'slip_id')::bigint
    loop
        v_slip_id := (v_slip->>'slip_id')::bigint;
        if not exists (
            select 1
            from public.store_slips s
            join public.store_business_days b
              on b.business_day_id = s.business_day_id
             and b.department_id = p_department_id
             and b.status = 'open'
            where s.department_id = p_department_id
              and s.business_day_id = p_business_day_id
              and s.slip_id = v_slip_id
              and s.status = 'checked_out'
        ) then
            raise exception 'store_slip_not_checked_out';
        end if;

        v_saved_cast_count := v_saved_cast_count + store.save_cast_sales_adjustment(
            p_department_id,
            v_slip_id,
            coalesce(v_slip->'adjustments', '[]'::jsonb),
            coalesce(nullif(trim(v_slip->>'source_amount_type'), ''), 'total'),
            coalesce(nullif(trim(v_slip->>'split_mode'), ''), 'split')
        );
        v_saved_slip_count := v_saved_slip_count + 1;
    end loop;

    return query select v_saved_slip_count, v_saved_cast_count;
end;
$$;

revoke all on function store.get_payment_methods(bigint)
    from public, anon, authenticated, service_role;
revoke all on function store.get_business_day_cast_sales_adjustment_overview(bigint, bigint)
    from public, anon, authenticated, service_role;
revoke all on function store.save_business_day_cast_sales_adjustments(bigint, bigint, jsonb)
    from public, anon, authenticated, service_role;

commit;
