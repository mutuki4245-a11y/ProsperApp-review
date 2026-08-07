begin;

drop function if exists store.get_business_home_bootstrap(bigint);
drop function if exists store.get_store_bootstrap(bigint);
drop function if exists store.get_business_home_bootstrap_v2(bigint);

create or replace function store.get_business_home_bootstrap_v2(
    p_department_id bigint
)
returns table (
    master_revision text,
    store_context jsonb,
    tables jsonb,
    order_items jsonb,
    nomination_options jsonb,
    payment_methods jsonb,
    has_business_day boolean,
    business_day jsonb,
    business_day_revision bigint,
    attendance_casts jsonb,
    snapshot jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_store_context jsonb;
    v_tables jsonb;
    v_order_items jsonb;
    v_nomination_options jsonb;
    v_payment_methods jsonb;
    v_has_business_day boolean;
    v_business_day jsonb;
    v_business_day_revision bigint;
    v_attendance_casts jsonb;
    v_snapshot jsonb;
begin
    select to_jsonb(context_row)
      into v_store_context
      from store.get_context_internal(p_department_id) context_row;
    if v_store_context is null then
        raise exception 'store_department_not_found';
    end if;

    select coalesce(jsonb_agg(to_jsonb(table_row) order by table_row.table_category_no, table_row.sort_order, table_row.table_code), '[]'::jsonb)
      into v_tables
      from store.get_tables(p_department_id) table_row;

    select coalesce(jsonb_agg(to_jsonb(item_row) order by item_row.category_name, item_row.item_name, item_row.item_id), '[]'::jsonb)
      into v_order_items
      from store.get_order_items(p_department_id) item_row;

    select coalesce(jsonb_agg(to_jsonb(nomination_row) order by nomination_row.sort_order, nomination_row.nomination_kind), '[]'::jsonb)
      into v_nomination_options
      from store.get_nomination_back_master(p_department_id) nomination_row;

    select coalesce(jsonb_agg(to_jsonb(payment_row) order by payment_row.sort_order, payment_row.method_code), '[]'::jsonb)
      into v_payment_methods
      from store.get_payment_methods(p_department_id) payment_row;

    select current_snapshot.has_business_day,
           current_snapshot.business_day,
           current_snapshot.business_day_revision,
           current_snapshot.attendance_casts,
           current_snapshot.snapshot
      into v_has_business_day,
           v_business_day,
           v_business_day_revision,
           v_attendance_casts,
           v_snapshot
      from store.get_current_business_home_snapshot(p_department_id, null) current_snapshot;

    return query select
        md5(jsonb_build_array(
            v_store_context,
            v_tables,
            v_order_items,
            v_nomination_options,
            v_payment_methods
        )::text),
        v_store_context,
        v_tables,
        v_order_items,
        v_nomination_options,
        v_payment_methods,
        v_has_business_day,
        v_business_day,
        v_business_day_revision,
        v_attendance_casts,
        v_snapshot;
end;
$$;

revoke all on function store.get_business_home_bootstrap_v2(bigint)
    from public, anon, authenticated, service_role;

commit;
