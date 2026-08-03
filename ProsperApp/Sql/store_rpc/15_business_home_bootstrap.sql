drop function if exists store.get_business_home_bootstrap(bigint);

create or replace function store.get_business_home_bootstrap(p_department_id bigint)
returns table (
    store_context jsonb,
    business_day jsonb,
    tables jsonb,
    nomination_options jsonb,
    order_items jsonb,
    attendance_casts jsonb,
    payment_methods jsonb,
    snapshot jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_store_context jsonb;
    v_business_day jsonb;
    v_business_day_id bigint;
    v_business_date date;
    v_tables jsonb := '[]'::jsonb;
    v_nomination_options jsonb := '[]'::jsonb;
    v_order_items jsonb := '[]'::jsonb;
    v_attendance_casts jsonb := '[]'::jsonb;
    v_payment_methods jsonb := '[]'::jsonb;
    v_snapshot jsonb;
    v_current_business_date date := case
        when (now() at time zone 'Asia/Tokyo')::time < time '12:00'
            then ((now() at time zone 'Asia/Tokyo')::date - 1)
        else (now() at time zone 'Asia/Tokyo')::date
    end;
begin
    select to_jsonb(c)
      into v_store_context
    from store.get_context(p_department_id) c;

    if v_store_context is null then
        raise exception 'store_department_not_found';
    end if;

    select to_jsonb(b), b.business_day_id, b.business_date
      into v_business_day, v_business_day_id, v_business_date
    from store.get_current_business_day(p_department_id) b;

    select coalesce(
        jsonb_agg(to_jsonb(t) - 'ordinality' order by t.ordinality),
        '[]'::jsonb
    )
      into v_tables
    from store.get_tables(p_department_id) with ordinality
      as t(table_id, table_code, table_name, table_category_no, ordinality);

    select coalesce(
        jsonb_agg(to_jsonb(n) - 'ordinality' order by n.ordinality),
        '[]'::jsonb
    )
      into v_nomination_options
    from store.get_nomination_back_master(p_department_id) with ordinality
      as n(nomination_kind, nomination_type, display_name, companion_time, back_type, back_unit_amount, sort_order, is_active, ordinality);

    select coalesce(
        jsonb_agg(to_jsonb(i) - 'ordinality' order by i.ordinality),
        '[]'::jsonb
    )
      into v_order_items
    from store.get_order_items(p_department_id) with ordinality
      as i(item_id, item_name, item_type, default_price, category_code, category_name, is_cast_back_target, cast_back_regular_unit_amount, cast_back_nomination_unit_amount, cast_back_type, ordinality);

    select coalesce(
        jsonb_agg(to_jsonb(pm) - 'ordinality' order by pm.ordinality),
        '[]'::jsonb
    )
      into v_payment_methods
    from store.get_payment_methods(p_department_id) with ordinality
      as pm(method_code, method_name, requires_received_amount, sort_order, ordinality);

    if v_business_day_id is not null then
        select coalesce(
            jsonb_agg(to_jsonb(a) - 'ordinality' order by a.ordinality),
            '[]'::jsonb
        )
          into v_attendance_casts
        from store.get_order_attending_casts(p_department_id, v_business_day_id) with ordinality
          as a(cast_id, display_name, drink_memo, department_name, clock_in_time, ordinality);

        select s.snapshot
          into v_snapshot
        from store.get_business_day_snapshot(p_department_id, v_business_day_id) s;
    else
        v_business_date := v_current_business_date;
        v_snapshot := jsonb_build_object(
            'businessDayId', null,
            'businessDate', to_char(v_business_date, 'YYYY-MM-DD'),
            'businessDateDisplay', to_char(v_business_date, 'YYYY-MM-DD') || ' / 自動作成待ち',
            'hasBusinessDay', false,
            'openSlipCount', 0,
            'checkedOutSlipCount', 0,
            'estimatedSalesAmount', 0,
            'slips', '[]'::jsonb
        );
    end if;

    return query
    select
        v_store_context,
        v_business_day,
        v_tables,
        v_nomination_options,
        v_order_items,
        v_attendance_casts,
        v_payment_methods,
        v_snapshot;
end;
$$;

revoke execute on function store.get_business_home_bootstrap(bigint) from public, anon, authenticated, service_role;
