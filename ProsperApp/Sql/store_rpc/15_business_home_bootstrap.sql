drop function if exists store.get_business_home_bootstrap(bigint);
drop function if exists store.get_store_bootstrap(bigint);

create or replace function store.get_store_bootstrap(p_department_id bigint)
returns table (
    store_context jsonb,
    business_day jsonb,
    departments jsonb,
    tables jsonb,
    table_admin_list jsonb,
    casts jsonb,
    casts_admin jsonb,
    staffs jsonb,
    staffs_admin jsonb,
    order_items jsonb,
    item_admin_catalog jsonb,
    nomination_options jsonb,
    payment_methods jsonb,
    pricing_plan jsonb,
    attendance_casts jsonb,
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
    v_departments jsonb := '[]'::jsonb;
    v_tables jsonb := '[]'::jsonb;
    v_table_admin_list jsonb := '[]'::jsonb;
    v_casts jsonb := '[]'::jsonb;
    v_casts_admin jsonb := '[]'::jsonb;
    v_staffs jsonb := '[]'::jsonb;
    v_staffs_admin jsonb := '[]'::jsonb;
    v_order_items jsonb := '[]'::jsonb;
    v_item_admin_catalog jsonb := '[]'::jsonb;
    v_nomination_options jsonb := '[]'::jsonb;
    v_payment_methods jsonb := '[]'::jsonb;
    v_pricing_plan jsonb;
    v_attendance_casts jsonb := '[]'::jsonb;
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

    select coalesce(jsonb_agg(to_jsonb(d)), '[]'::jsonb)
      into v_departments
    from store.get_departments() d;

    select coalesce(jsonb_agg(to_jsonb(t)), '[]'::jsonb)
      into v_tables
    from store.get_tables(p_department_id) t;

    select coalesce(jsonb_agg(to_jsonb(t)), '[]'::jsonb)
      into v_table_admin_list
    from store.get_table_admin_list(p_department_id) t;

    select coalesce(jsonb_agg(to_jsonb(c)), '[]'::jsonb)
      into v_casts
    from store.get_casts(p_department_id) c;

    select coalesce(jsonb_agg(to_jsonb(c)), '[]'::jsonb)
      into v_casts_admin
    from store.get_casts_admin(p_department_id) c;

    select coalesce(jsonb_agg(to_jsonb(s)), '[]'::jsonb)
      into v_staffs
    from store.get_staffs(p_department_id) s;

    select coalesce(jsonb_agg(to_jsonb(s)), '[]'::jsonb)
      into v_staffs_admin
    from store.get_staffs_admin(p_department_id) s;

    select coalesce(jsonb_agg(to_jsonb(i)), '[]'::jsonb)
      into v_order_items
    from store.get_order_items(p_department_id) i;

    select coalesce(jsonb_agg(to_jsonb(i)), '[]'::jsonb)
      into v_item_admin_catalog
    from store.get_item_admin_catalog(p_department_id) i;

    select coalesce(jsonb_agg(to_jsonb(n)), '[]'::jsonb)
      into v_nomination_options
    from store.get_nomination_back_master(p_department_id) n;

    select coalesce(jsonb_agg(to_jsonb(pm)), '[]'::jsonb)
      into v_payment_methods
    from store.get_payment_methods(p_department_id) pm;

    select to_jsonb(p)
      into v_pricing_plan
    from store.get_pricing_plan(p_department_id) p;

    if v_business_day_id is not null then
        select coalesce(jsonb_agg(to_jsonb(a)), '[]'::jsonb)
          into v_attendance_casts
        from store.get_order_attending_casts(p_department_id, v_business_day_id) a;

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
        v_departments,
        v_tables,
        v_table_admin_list,
        v_casts,
        v_casts_admin,
        v_staffs,
        v_staffs_admin,
        v_order_items,
        v_item_admin_catalog,
        v_nomination_options,
        v_payment_methods,
        v_pricing_plan,
        v_attendance_casts,
        v_snapshot;
end;
$$;

revoke execute on function store.get_store_bootstrap(bigint) from public, anon, authenticated, service_role;
