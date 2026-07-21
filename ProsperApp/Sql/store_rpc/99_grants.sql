-- ProsperApp RPCs are executed only through the prosper-rpc Edge Function.
-- Direct PostgREST RPC execution is not an application route.
do $$
declare
    target_functions text[] := array[
        'store.get_departments',
        'store.delete_non_master_records',
        'store.get_context',
        'store.get_current_business_day',
        'store.get_open_slip_count',
        'store.get_business_day_drink_delivery_status',
        'store.get_business_day_closing_attendance',
        'store.get_tables',
        'store.get_casts',
        'store.get_casts_admin',
        'store.get_business_day_slips',
        'store.get_business_day_snapshot',
        'store.get_order_entry_slips',
        'store.get_order_items',
        'store.get_item_admin_catalog',
        'store.get_nomination_back_master',
        'store.save_nomination_back_master',
        'store.get_order_attending_casts',
        'store.get_slip_detail',
        'store.get_pending_receipts',
        'store.open_business_day',
        'store.open_business_day_with_attendance',
        'store.save_business_day_attendance',
        'store.save_business_day_drink_delivery_amount',
        'store.save_business_day_closing_attendance',
        'store.get_business_day_cast_sales_adjustment_status',
        'store.get_cast_sales_adjustment_slips',
        'store.get_cast_sales_adjustment_detail',
        'store.save_cast_sales_adjustment',
        'store.close_business_day',
        'store.create_cast',
        'store.update_cast_drink_memo',
        'store.delete_cast',
        'store.upsert_item_category',
        'store.upsert_item',
        'store.delete_item',
        'store.reorder_items',
        'store.add_order_lines',
        'store.add_slip_customers',
        'store.add_slip_nominations',
        'store.save_slip_adjustments',
        'store.add_slip_adjustment',
        'store.apply_business_slip_editor_operation',
        'store.flush_business_home_changes',
        'store.save_karaoke_lines',
        'store.save_order_line_quantities',
        'store.leave_slip_customer',
        'store.update_slip_customer_label',
        'store.void_order_line',
        'store.confirm_checkout',
        'store.cancel_checkout',
        'store.issue_checkout_statement',
        'store.get_checkout_statement_print_data',
        'store.release_checkout_ready',
        'store.get_checkout_receipt_print_data',
        'store.create_slip',
        'store.quick_enter_receipt',
        'store.mark_receipt_scan_mistake'
    ];
    r record;
begin
    revoke usage on schema store from public, anon, authenticated, service_role;

    for r in
        select
            n.nspname as schema_name,
            p.proname as function_name,
            pg_get_function_identity_arguments(p.oid) as identity_arguments
        from pg_proc p
        join pg_namespace n on n.oid = p.pronamespace
        where (n.nspname || '.' || p.proname) = any(target_functions)
    loop
        execute format(
            'revoke execute on function %I.%I(%s) from public, anon, authenticated, service_role',
            r.schema_name,
            r.function_name,
            r.identity_arguments
        );
    end loop;
end $$;
