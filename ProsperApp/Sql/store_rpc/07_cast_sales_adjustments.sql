drop function if exists store.get_business_day_cast_sales_adjustment_status(bigint, bigint);

create or replace function store.get_business_day_cast_sales_adjustment_status(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    required_slip_count integer,
    completed_slip_count integer,
    missing_slip_count integer
)
language sql
security definer
set search_path = public
as $$
    with required_casts as (
        select
            s.slip_id,
            sc.slip_cast_id
        from public.store_slips s
        join public.store_checkouts c
          on c.slip_id = s.slip_id
         and c.status = 'confirmed'
        join public.store_slip_casts sc
          on sc.slip_id = s.slip_id
         and sc.status = 'active'
         and sc.nomination_type in ('nomination', 'in_store', 'companion')
        where s.department_id = p_department_id
          and s.business_day_id = p_business_day_id
          and s.status = 'checked_out'
    ),
    slip_status as (
        select
            rc.slip_id,
            count(*)::integer as required_cast_count,
            count(a.adjustment_id)::integer as saved_cast_count
        from required_casts rc
        left join public.store_slip_cast_sales_adjustments a
          on a.slip_cast_id = rc.slip_cast_id
         and a.status = 'confirmed'
        group by rc.slip_id
    )
    select
        count(*)::integer as required_slip_count,
        count(*) filter (where saved_cast_count >= required_cast_count)::integer as completed_slip_count,
        count(*) filter (where saved_cast_count < required_cast_count)::integer as missing_slip_count
    from slip_status;
$$;

drop function if exists store.get_cast_sales_adjustment_slips(bigint, bigint);

create or replace function store.get_cast_sales_adjustment_slips(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    slip_id bigint,
    slip_no text,
    table_id bigint,
    table_code text,
    table_name text,
    checkout_id bigint,
    checkout_at timestamp with time zone,
    subtotal_amount numeric,
    service_tax_amount numeric,
    total_amount numeric,
    customer_names text,
    cast_names text,
    required_cast_count integer,
    saved_cast_count integer,
    adjusted_sales_amount_total numeric
)
language sql
security definer
set search_path = public
as $$
    with target_slips as (
        select
            s.slip_id,
            s.slip_no,
            s.table_id,
            s.status,
            t.table_code,
            t.table_name,
            c.checkout_id,
            c.checkout_at,
            c.subtotal_amount,
            c.service_tax_amount,
            c.total_amount
        from public.store_slips s
        join public.store_checkouts c
          on c.slip_id = s.slip_id
         and c.status = 'confirmed'
        left join public.store_table_master t
          on t.table_id = s.table_id
        where s.department_id = p_department_id
          and s.business_day_id = p_business_day_id
          and s.status = 'checked_out'
    ),
    customer_summary as (
        select
            c.slip_id,
            string_agg(
                coalesce(nullif(c.customer_label, ''), '客' || c.line_no::text),
                '、'
                order by c.line_no
            ) filter (where c.status <> 'cancelled') as customer_names
        from target_slips s
        join public.store_slip_customers c
          on c.slip_id = s.slip_id
        group by c.slip_id
    ),
    required_casts as (
        select
            s.slip_id,
            sc.slip_cast_id,
            sc.cast_id,
            cm.display_name as cast_display_name,
            sc.started_at
        from target_slips s
        join public.store_slip_casts sc
          on sc.slip_id = s.slip_id
         and sc.status = 'active'
         and sc.nomination_type in ('nomination', 'in_store', 'companion')
        join public.cast_master cm
          on cm.cast_id = sc.cast_id
    ),
    required_cast_names as (
        select
            rc.slip_id,
            rc.cast_id,
            rc.cast_display_name,
            min(rc.started_at) as first_started_at,
            min(rc.slip_cast_id) as first_slip_cast_id
        from required_casts rc
        group by rc.slip_id, rc.cast_id, rc.cast_display_name
    ),
    cast_name_summary as (
        select
            rcn.slip_id,
            string_agg(
                rcn.cast_display_name,
                '、'
                order by rcn.first_started_at asc nulls last, rcn.first_slip_cast_id asc
            ) as cast_names
        from required_cast_names rcn
        group by rcn.slip_id
    ),
    slip_status as (
        select
            rc.slip_id,
            count(*)::integer as required_cast_count,
            count(a.adjustment_id)::integer as saved_cast_count,
            coalesce(sum(a.sales_amount), 0) as adjusted_sales_amount_total
        from required_casts rc
        left join public.store_slip_cast_sales_adjustments a
          on a.slip_cast_id = rc.slip_cast_id
         and a.status = 'confirmed'
        group by rc.slip_id
    )
    select
        ts.slip_id,
        ts.slip_no,
        ts.table_id,
        ts.table_code,
        ts.table_name,
        ts.checkout_id,
        ts.checkout_at,
        ts.subtotal_amount,
        ts.service_tax_amount,
        ts.total_amount,
        coalesce(cs.customer_names, '') as customer_names,
        coalesce(cns.cast_names, '') as cast_names,
        ss.required_cast_count,
        ss.saved_cast_count,
        ss.adjusted_sales_amount_total
    from target_slips ts
    join slip_status ss
      on ss.slip_id = ts.slip_id
    left join customer_summary cs
      on cs.slip_id = ts.slip_id
    left join cast_name_summary cns
      on cns.slip_id = ts.slip_id
    order by ts.checkout_at asc, ts.slip_id asc;
$$;

drop function if exists store.get_cast_sales_adjustment_detail(bigint, bigint);

create or replace function store.get_cast_sales_adjustment_detail(
    p_department_id bigint,
    p_slip_id bigint
)
returns table (
    slip_id bigint,
    slip_no text,
    business_day_id bigint,
    business_date date,
    table_id bigint,
    table_code text,
    table_name text,
    checkout_id bigint,
    checkout_at timestamp with time zone,
    subtotal_amount numeric,
    service_tax_amount numeric,
    total_amount numeric,
    slip_cast_id bigint,
    cast_id bigint,
    cast_display_name text,
    cast_department_name text,
    nomination_kind text,
    nomination_type text,
    nomination_display_name text,
    started_at timestamp with time zone,
    sales_amount numeric,
    source_amount_type text,
    split_mode text
)
language sql
security definer
set search_path = public
as $$
    select
        s.slip_id,
        s.slip_no,
        s.business_day_id,
        s.business_date,
        s.table_id,
        t.table_code,
        t.table_name,
        c.checkout_id,
        c.checkout_at,
        c.subtotal_amount,
        c.service_tax_amount,
        c.total_amount,
        sc.slip_cast_id,
        sc.cast_id,
        cm.display_name as cast_display_name,
        d.department_name as cast_department_name,
        sc.nomination_kind,
        sc.nomination_type,
        m.display_name as nomination_display_name,
        sc.started_at,
        a.sales_amount,
        a.source_amount_type,
        a.split_mode
    from public.store_slips s
    join public.store_checkouts c
      on c.slip_id = s.slip_id
     and c.status = 'confirmed'
    join public.store_slip_casts sc
      on sc.slip_id = s.slip_id
     and sc.status = 'active'
     and sc.nomination_type in ('nomination', 'in_store', 'companion')
    join public.cast_master cm
      on cm.cast_id = sc.cast_id
    left join public.department_master d
      on d.department_id = cm.department_id
    left join public.store_nomination_back_master m
      on m.company_id = s.company_id
     and m.department_id = s.department_id
     and m.nomination_kind = sc.nomination_kind
    left join public.store_table_master t
      on t.table_id = s.table_id
    left join public.store_slip_cast_sales_adjustments a
      on a.slip_cast_id = sc.slip_cast_id
     and a.status = 'confirmed'
    where s.department_id = p_department_id
      and s.slip_id = p_slip_id
      and s.status = 'checked_out'
    order by sc.started_at asc nulls last, sc.slip_cast_id asc;
$$;

drop function if exists store.save_cast_sales_adjustment(bigint, bigint, jsonb, text, text);

create or replace function store.save_cast_sales_adjustment(
    p_department_id bigint,
    p_slip_id bigint,
    p_adjustments jsonb default '[]'::jsonb,
    p_source_amount_type text default 'total',
    p_split_mode text default 'split'
)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip public.store_slips%rowtype;
    v_checkout public.store_checkouts%rowtype;
    v_source_amount_type text;
    v_split_mode text;
    v_base_amount numeric(12, 0);
    v_required_count integer;
    v_payload_count integer;
    v_payload_distinct_count integer;
    v_invalid_payload_count integer;
    v_missing_count integer;
    v_extra_count integer;
    v_saved_count integer;
begin
    v_source_amount_type := coalesce(nullif(trim(coalesce(p_source_amount_type, '')), ''), 'total');
    v_split_mode := coalesce(nullif(trim(coalesce(p_split_mode, '')), ''), 'split');

    if v_source_amount_type not in ('subtotal', 'total') or v_split_mode not in ('split', 'full') then
        raise exception 'invalid_cast_sales_adjustment_settings';
    end if;

    if coalesce(jsonb_typeof(p_adjustments), 'null') <> 'array' then
        raise exception 'invalid_cast_sales_adjustment_payload';
    end if;

    select *
      into v_slip
    from public.store_slips s
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id
      and s.status = 'checked_out'
    limit 1;

    if v_slip.slip_id is null then
        raise exception 'store_slip_not_checked_out';
    end if;

    select *
      into v_checkout
    from public.store_checkouts c
    where c.slip_id = p_slip_id
      and c.department_id = p_department_id
      and c.status = 'confirmed'
    limit 1;

    if v_checkout.checkout_id is null then
        raise exception 'cast_sales_checkout_not_found';
    end if;

    v_base_amount := case v_source_amount_type
        when 'subtotal' then v_checkout.subtotal_amount
        else v_checkout.total_amount
    end;

    select count(*)::integer
      into v_required_count
    from public.store_slip_casts sc
    where sc.slip_id = p_slip_id
      and sc.status = 'active'
      and sc.nomination_type in ('nomination', 'in_store', 'companion');

    if coalesce(v_required_count, 0) = 0 then
        raise exception 'cast_sales_adjustment_not_required';
    end if;

    with payload as (
        select
            nullif(value->>'slip_cast_id', '')::bigint as slip_cast_id,
            nullif(value->>'sales_amount', '')::numeric as sales_amount
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_adjustments) = 'array' then p_adjustments
                else '[]'::jsonb
            end
        )
    )
    select
        count(*)::integer,
        count(distinct slip_cast_id)::integer,
        count(*) filter (
            where slip_cast_id is null
               or sales_amount is null
               or sales_amount < 0
               or sales_amount <> trunc(sales_amount)
        )::integer
      into v_payload_count,
           v_payload_distinct_count,
           v_invalid_payload_count
    from payload;

    if coalesce(v_payload_count, 0) <> v_required_count
       or coalesce(v_payload_distinct_count, 0) <> coalesce(v_payload_count, 0)
       or coalesce(v_invalid_payload_count, 0) > 0 then
        raise exception 'invalid_cast_sales_adjustment_payload';
    end if;

    with required as (
        select sc.slip_cast_id
        from public.store_slip_casts sc
        where sc.slip_id = p_slip_id
          and sc.status = 'active'
          and sc.nomination_type in ('nomination', 'in_store', 'companion')
    ),
    payload as (
        select nullif(value->>'slip_cast_id', '')::bigint as slip_cast_id
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_adjustments) = 'array' then p_adjustments
                else '[]'::jsonb
            end
        )
    )
    select
        count(*) filter (where p.slip_cast_id is null)::integer
      into v_missing_count
    from required r
    left join payload p
      on p.slip_cast_id = r.slip_cast_id;

    with required as (
        select sc.slip_cast_id
        from public.store_slip_casts sc
        where sc.slip_id = p_slip_id
          and sc.status = 'active'
          and sc.nomination_type in ('nomination', 'in_store', 'companion')
    ),
    payload as (
        select nullif(value->>'slip_cast_id', '')::bigint as slip_cast_id
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_adjustments) = 'array' then p_adjustments
                else '[]'::jsonb
            end
        )
    )
    select
        count(*) filter (where r.slip_cast_id is null)::integer
      into v_extra_count
    from payload p
    left join required r
      on r.slip_cast_id = p.slip_cast_id;

    if coalesce(v_missing_count, 0) > 0 or coalesce(v_extra_count, 0) > 0 then
        raise exception 'invalid_cast_sales_adjustment_payload';
    end if;

    delete from public.store_slip_cast_sales_adjustments a
    where a.department_id = p_department_id
      and a.slip_id = p_slip_id;

    with required as (
        select
            sc.slip_cast_id,
            sc.cast_id
        from public.store_slip_casts sc
        where sc.slip_id = p_slip_id
          and sc.status = 'active'
          and sc.nomination_type in ('nomination', 'in_store', 'companion')
    ),
    payload as (
        select
            nullif(value->>'slip_cast_id', '')::bigint as slip_cast_id,
            nullif(value->>'sales_amount', '')::numeric as sales_amount
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_adjustments) = 'array' then p_adjustments
                else '[]'::jsonb
            end
        )
    )
    insert into public.store_slip_cast_sales_adjustments (
        slip_id,
        checkout_id,
        business_day_id,
        business_date,
        company_id,
        department_id,
        slip_cast_id,
        cast_id,
        source_amount_type,
        split_mode,
        base_amount,
        sales_amount,
        status
    )
    select
        v_slip.slip_id,
        v_checkout.checkout_id,
        v_slip.business_day_id,
        v_slip.business_date,
        v_slip.company_id,
        v_slip.department_id,
        r.slip_cast_id,
        r.cast_id,
        v_source_amount_type,
        v_split_mode,
        v_base_amount,
        p.sales_amount,
        'confirmed'
    from required r
    join payload p
      on p.slip_cast_id = r.slip_cast_id;

    get diagnostics v_saved_count = row_count;
    return coalesce(v_saved_count, 0);
end;
$$;
