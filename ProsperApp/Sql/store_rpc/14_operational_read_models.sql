begin;

create or replace function store.get_business_day_champagne_back_status(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    required_cast_count integer,
    completed_cast_count integer,
    missing_cast_count integer,
    total_back_amount numeric
)
language sql
security definer
set search_path = public
as $$
    with required_casts as (
        select a.cast_id
        from public.store_cast_attendance a
        where a.department_id = p_department_id
          and a.business_day_id = p_business_day_id
          and a.attendance_status in ('scheduled', 'checked_in', 'checked_out')
        group by a.cast_id
    ),
    saved_casts as (
        select cb.cast_id, cb.back_amount
        from public.store_business_day_champagne_backs cb
        join required_casts rc
          on rc.cast_id = cb.cast_id
        where cb.department_id = p_department_id
          and cb.business_day_id = p_business_day_id
          and cb.status = 'active'
    )
    select
        (select count(*)::integer from required_casts),
        (select count(*)::integer from saved_casts),
        (select count(*)::integer from required_casts rc
          where not exists (select 1 from saved_casts sc where sc.cast_id = rc.cast_id)),
        coalesce((select sum(sc.back_amount) from saved_casts sc), 0);
$$;

create or replace function store.get_business_day_champagne_back_overview(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    status jsonb,
    casts jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_status jsonb;
    v_casts jsonb;
begin
    select to_jsonb(s)
      into v_status
    from store.get_business_day_champagne_back_status(
        p_department_id,
        p_business_day_id
    ) s;

    with required_casts as (
        select
            a.cast_id,
            min(a.attendance_id) as first_attendance_id
        from public.store_cast_attendance a
        join public.store_business_days bd
          on bd.business_day_id = a.business_day_id
         and bd.department_id = p_department_id
         and bd.status = 'open'
        where a.department_id = p_department_id
          and a.business_day_id = p_business_day_id
          and a.attendance_status in ('scheduled', 'checked_in', 'checked_out')
        group by a.cast_id
    )
    select coalesce(jsonb_agg(jsonb_build_object(
        'cast_id', rc.cast_id,
        'display_name', c.display_name,
        'department_name', cd.department_name,
        'back_amount', cb.back_amount,
        'is_saved', coalesce(cb.status = 'active', false)
    ) order by c.sort_order, c.cast_id), '[]'::jsonb)
      into v_casts
    from required_casts rc
    join public.cast_master c
      on c.cast_id = rc.cast_id
    left join public.department_master cd
      on cd.department_id = c.department_id
    left join public.store_business_day_champagne_backs cb
      on cb.business_day_id = p_business_day_id
     and cb.department_id = p_department_id
     and cb.cast_id = rc.cast_id;

    return query
    select
        coalesce(v_status, '{}'::jsonb),
        coalesce(v_casts, '[]'::jsonb);
end;
$$;

create or replace function store.save_business_day_champagne_backs(
    p_department_id bigint,
    p_business_day_id bigint,
    p_backs jsonb default '[]'::jsonb
)
returns table (
    saved_cast_count integer,
    total_back_amount numeric
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_saved_cast_count integer := 0;
    v_total_back_amount numeric := 0;
begin
    if jsonb_typeof(p_backs) <> 'array' or jsonb_array_length(p_backs) > 100 then
        raise exception 'invalid_champagne_back_payload';
    end if;

    if exists (
        select 1
        from jsonb_array_elements(p_backs) item
        where jsonb_typeof(item.value) <> 'object'
           or coalesce(item.value->>'cast_id', '') !~ '^[1-9][0-9]*$'
           or coalesce(item.value->>'back_amount', '') !~ '^[0-9]+(?:\.0+)?$'
    ) then
        raise exception 'invalid_champagne_back_payload';
    end if;

    if (
        select count(*)
        from jsonb_array_elements(p_backs) item
    ) <> (
        select count(distinct item.value->>'cast_id')
        from jsonb_array_elements(p_backs) item
    ) then
        raise exception 'duplicate_champagne_back_cast';
    end if;

    select bd.*
      into v_business_day
    from public.store_business_days bd
    where bd.business_day_id = p_business_day_id
      and bd.department_id = p_department_id
      and bd.status = 'open'
    for update;

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_open';
    end if;

    if (
        select count(*)
        from public.store_cast_attendance a
        where a.department_id = p_department_id
          and a.business_day_id = p_business_day_id
          and a.attendance_status in ('scheduled', 'checked_in', 'checked_out')
    ) = 0 then
        raise exception 'champagne_back_attendance_required';
    end if;

    if exists (
        with required_casts as (
            select a.cast_id
            from public.store_cast_attendance a
            where a.department_id = p_department_id
              and a.business_day_id = p_business_day_id
              and a.attendance_status in ('scheduled', 'checked_in', 'checked_out')
            group by a.cast_id
        ),
        submitted_casts as (
            select (item.value->>'cast_id')::bigint as cast_id
            from jsonb_array_elements(p_backs) item
        )
        select 1
        from (
            (select cast_id from required_casts except select cast_id from submitted_casts)
            union all
            (select cast_id from submitted_casts except select cast_id from required_casts)
        ) mismatched
    ) then
        raise exception 'champagne_back_attendance_changed';
    end if;

    update public.store_business_day_champagne_backs cb
       set status = 'voided'
     where cb.department_id = p_department_id
       and cb.business_day_id = p_business_day_id
       and cb.status = 'active'
       and not exists (
           select 1
           from jsonb_array_elements(p_backs) item
           where (item.value->>'cast_id')::bigint = cb.cast_id
       );

    insert into public.store_business_day_champagne_backs (
        business_day_id,
        business_date,
        company_id,
        department_id,
        cast_id,
        back_type,
        quantity,
        back_unit_amount,
        back_amount,
        status,
        memo
    )
    select
        v_business_day.business_day_id,
        v_business_day.business_date,
        v_business_day.company_id,
        v_business_day.department_id,
        (item.value->>'cast_id')::bigint,
        'champagne',
        1,
        (item.value->>'back_amount')::numeric,
        (item.value->>'back_amount')::numeric,
        'active',
        null
    from jsonb_array_elements(p_backs) item
    on conflict (business_day_id, cast_id) do update
        set business_date = excluded.business_date,
            company_id = excluded.company_id,
            department_id = excluded.department_id,
            back_type = excluded.back_type,
            quantity = excluded.quantity,
            back_unit_amount = excluded.back_unit_amount,
            back_amount = excluded.back_amount,
            status = 'active',
            memo = excluded.memo;

    select
        count(*)::integer,
        coalesce(sum(cb.back_amount), 0)
      into v_saved_cast_count,
           v_total_back_amount
    from public.store_business_day_champagne_backs cb
    where cb.department_id = p_department_id
      and cb.business_day_id = p_business_day_id
      and cb.status = 'active';

    return query select v_saved_cast_count, v_total_back_amount;
end;
$$;

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
revoke all on function store.get_business_day_champagne_back_status(bigint, bigint)
    from public, anon, authenticated, service_role;
revoke all on function store.get_business_day_champagne_back_overview(bigint, bigint)
    from public, anon, authenticated, service_role;
revoke all on function store.save_business_day_champagne_backs(bigint, bigint, jsonb)
    from public, anon, authenticated, service_role;

commit;
