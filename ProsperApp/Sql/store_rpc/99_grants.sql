-- ProsperApp RPCs are executed only through the prosper-rpc Edge Function.
-- Direct PostgREST RPC execution is not an application route.
do $$
declare
    target_functions text[] := array[
        'get_store_departments',
        'get_store_context',
        'get_current_business_day',
        'get_open_slip_count',
        'get_business_day_drink_delivery_status',
        'get_business_day_closing_attendance',
        'get_store_tables',
        'get_store_casts',
        'get_store_cast_admin_list',
        'get_business_day_slips',
        'get_order_entry_slips',
        'get_store_order_items',
        'get_store_item_admin_catalog',
        'get_order_attending_casts',
        'get_store_slip_detail',
        'get_pending_receipts',
        'open_business_day',
        'open_business_day_with_attendance',
        'save_business_day_attendance',
        'save_business_day_drink_delivery_amount',
        'save_business_day_closing_attendance',
        'get_business_day_cast_sales_adjustment_status',
        'get_cast_sales_adjustment_slips',
        'get_cast_sales_adjustment_detail',
        'save_cast_sales_adjustment',
        'close_business_day',
        'create_store_cast',
        'delete_store_cast',
        'upsert_store_item_category',
        'upsert_store_item',
        'delete_store_item',
        'reorder_store_items',
        'add_store_order_lines',
        'add_store_slip_customers',
        'add_store_slip_nominations',
        'leave_store_slip_customer',
        'update_store_slip_customer_label',
        'void_store_order_line',
        'confirm_store_checkout',
        'create_store_slip',
        'quick_enter_receipt',
        'mark_receipt_scan_mistake'
    ];
    r record;
begin
    for r in
        select
            n.nspname as schema_name,
            p.proname as function_name,
            pg_get_function_identity_arguments(p.oid) as identity_arguments
        from pg_proc p
        join pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public'
          and p.proname = any(target_functions)
    loop
        execute format(
            'revoke execute on function %I.%I(%s) from public, anon, authenticated, service_role',
            r.schema_name,
            r.function_name,
            r.identity_arguments
        );
    end loop;
end $$;
