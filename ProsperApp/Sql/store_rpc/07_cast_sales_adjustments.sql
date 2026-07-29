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
    service_charge_amount numeric,
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
            c.service_charge_amount,
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
                coalesce(nullif(c.customer_label, ''), 'ご新規様' || c.line_no::text),
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
        ts.service_charge_amount,
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
    service_charge_amount numeric,
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
    split_mode text,
    suggested_subtotal_sales_amount numeric,
    subtotal_suggestion_fallback_reason text,
    suggested_total_sales_amount numeric,
    total_suggestion_fallback_reason text
)
language sql
security definer
set search_path = public
as $$
    with target as (
        select
            s.slip_id,
            s.slip_no,
            s.business_day_id,
            s.business_date,
            s.company_id,
            s.department_id,
            s.table_id,
            c.checkout_id,
            c.checkout_at,
            c.subtotal_amount,
            c.service_charge_amount,
            c.total_amount
        from public.store_slips s
        join public.store_checkouts c
          on c.slip_id = s.slip_id
         and c.status = 'confirmed'
        where s.department_id = p_department_id
          and s.slip_id = p_slip_id
          and s.status = 'checked_out'
    ),
    required_casts as (
        select
            sc.slip_cast_id,
            sc.cast_id,
            sc.nomination_kind,
            sc.nomination_type,
            sc.started_at
        from target t
        join public.store_slip_casts sc
          on sc.slip_id = t.slip_id
         and sc.status = 'active'
         and sc.nomination_type in ('nomination', 'in_store', 'companion')
    ),
    active_order_lines as (
        select
            ol.order_line_id,
            ol.ordered_at,
            ol.amount,
            t.subtotal_amount,
            t.service_charge_amount,
            case
                when t.subtotal_amount > 0 then floor(t.service_charge_amount * ol.amount / t.subtotal_amount)
                else 0::numeric
            end as service_floor_amount,
            case
                when t.subtotal_amount > 0 then t.service_charge_amount * ol.amount / t.subtotal_amount
                else 0::numeric
            end as service_exact_amount
        from target t
        join public.store_order_lines ol
          on ol.slip_id = t.slip_id
         and ol.status = 'active'
    ),
    service_ranked_orders as (
        select
            o.*,
            row_number() over (
                order by o.service_exact_amount - o.service_floor_amount desc, o.order_line_id asc
            ) as service_remainder_rank,
            coalesce(sum(o.service_floor_amount) over (), 0) as service_floor_total
        from active_order_lines o
    ),
    order_events as (
        select
            o.order_line_id,
            o.ordered_at,
            o.amount as subtotal_event_amount,
            o.amount
                + o.service_floor_amount
                + case
                    when o.service_remainder_rank <= o.service_charge_amount - o.service_floor_total then 1
                    else 0
                  end as total_event_amount
        from service_ranked_orders o
    ),
    allocation_events as (
        select
            'order'::text as event_kind,
            o.order_line_id as event_id,
            o.ordered_at as event_at,
            o.subtotal_event_amount,
            o.total_event_amount
        from order_events o

        union all

        select
            'adjustment'::text as event_kind,
            cl.charge_line_id as event_id,
            cl.created_at as event_at,
            0::numeric as subtotal_event_amount,
            cl.amount as total_event_amount
        from target t
        join public.store_slip_charge_lines cl
          on cl.slip_id = t.slip_id
         and cl.charge_type = 'adjustment'
         and cl.status = 'active'
    ),
    event_candidate_counts as (
        select
            e.event_kind,
            e.event_id,
            e.event_at,
            e.subtotal_event_amount,
            e.total_event_amount,
            count(rc.slip_cast_id)::integer as eligible_cast_count
        from allocation_events e
        left join required_casts rc
          on rc.started_at <= e.event_at
        group by
            e.event_kind,
            e.event_id,
            e.event_at,
            e.subtotal_event_amount,
            e.total_event_amount
    ),
    event_candidates as (
        select
            e.*,
            rc.slip_cast_id,
            row_number() over (
                partition by e.event_kind, e.event_id
                order by rc.started_at asc, rc.slip_cast_id asc
            )::integer as eligible_cast_order
        from event_candidate_counts e
        join required_casts rc
          on rc.started_at <= e.event_at
        where e.eligible_cast_count > 0
    ),
    subtotal_allocations as (
        select
            e.slip_cast_id,
            sum(
                case when e.subtotal_event_amount < 0 then -1 else 1 end
                * (
                    abs(e.subtotal_event_amount)::bigint / e.eligible_cast_count
                    + case
                        when e.eligible_cast_order <= mod(abs(e.subtotal_event_amount)::bigint, e.eligible_cast_count) then 1
                        else 0
                      end
                )
            )::numeric as sales_amount
        from event_candidates e
        where e.subtotal_event_amount <> 0
        group by e.slip_cast_id
    ),
    total_allocations as (
        select
            e.slip_cast_id,
            sum(
                case when e.total_event_amount < 0 then -1 else 1 end
                * (
                    abs(e.total_event_amount)::bigint / e.eligible_cast_count
                    + case
                        when e.eligible_cast_order <= mod(abs(e.total_event_amount)::bigint, e.eligible_cast_count) then 1
                        else 0
                      end
                )
            )::numeric as sales_amount
        from event_candidates e
        where e.total_event_amount <> 0
        group by e.slip_cast_id
    ),
    allocation_totals as (
        select
            rc.slip_cast_id,
            coalesce(sa.sales_amount, 0)::numeric as suggested_subtotal_sales_amount,
            coalesce(ta.sales_amount, 0)::numeric as suggested_total_sales_amount
        from required_casts rc
        left join subtotal_allocations sa
          on sa.slip_cast_id = rc.slip_cast_id
        left join total_allocations ta
          on ta.slip_cast_id = rc.slip_cast_id
    ),
    suggestion_validation as (
        select
            case
                when exists (select 1 from required_casts where started_at is null)
                    then 'missing_nomination_start_time'
                when coalesce((select sum(o.amount) from active_order_lines o), 0) <> t.subtotal_amount
                    then 'checkout_snapshot_mismatch'
                when exists (
                    select 1
                    from event_candidate_counts e
                    where e.subtotal_event_amount <> 0
                      and e.eligible_cast_count = 0
                ) then 'unallocated_sales_event'
                when exists (
                    select 1
                    from allocation_totals a
                    where a.suggested_subtotal_sales_amount < 0
                ) then 'negative_cast_sales_amount'
                else null
            end as subtotal_suggestion_fallback_reason,
            case
                when exists (select 1 from required_casts where started_at is null)
                    then 'missing_nomination_start_time'
                when coalesce((select sum(e.total_event_amount) from allocation_events e), 0) <> t.total_amount
                    then 'checkout_snapshot_mismatch'
                when exists (
                    select 1
                    from event_candidate_counts e
                    where e.total_event_amount <> 0
                      and e.eligible_cast_count = 0
                ) then 'unallocated_sales_event'
                when exists (
                    select 1
                    from allocation_totals a
                    where a.suggested_total_sales_amount < 0
                ) then 'negative_cast_sales_amount'
                else null
            end as total_suggestion_fallback_reason
        from target t
    )
    select
        t.slip_id,
        t.slip_no,
        t.business_day_id,
        t.business_date,
        t.table_id,
        tm.table_code,
        tm.table_name,
        t.checkout_id,
        t.checkout_at,
        t.subtotal_amount,
        t.service_charge_amount,
        t.total_amount,
        rc.slip_cast_id,
        rc.cast_id,
        cm.display_name as cast_display_name,
        d.department_name as cast_department_name,
        rc.nomination_kind,
        rc.nomination_type,
        m.display_name as nomination_display_name,
        rc.started_at,
        a.sales_amount,
        a.source_amount_type,
        a.split_mode,
        at.suggested_subtotal_sales_amount,
        sv.subtotal_suggestion_fallback_reason,
        at.suggested_total_sales_amount,
        sv.total_suggestion_fallback_reason
    from target t
    join required_casts rc
      on true
    join public.cast_master cm
      on cm.cast_id = rc.cast_id
    left join public.department_master d
      on d.department_id = cm.department_id
    left join public.store_nomination_back_master m
      on m.company_id = t.company_id
     and m.department_id = t.department_id
     and m.nomination_kind = rc.nomination_kind
    left join public.store_table_master tm
      on tm.table_id = t.table_id
    left join public.store_slip_cast_sales_adjustments a
      on a.slip_cast_id = rc.slip_cast_id
     and a.status = 'confirmed'
    join allocation_totals at
      on at.slip_cast_id = rc.slip_cast_id
    cross join suggestion_validation sv
    order by rc.started_at asc nulls last, rc.slip_cast_id asc;
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
