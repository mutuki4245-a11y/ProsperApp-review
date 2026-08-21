-- ProsperApp RPCs are executed only through the prosper-rpc Edge Function.
-- Direct PostgREST RPC execution is not an application route.
revoke usage on schema store from public, anon, authenticated, service_role;
revoke execute on all functions in schema store from public, anon, authenticated, service_role;

alter default privileges in schema store revoke execute on functions from public;
alter default privileges in schema store revoke execute on functions from anon, authenticated, service_role;

do $block$
begin
  if not exists (select 1 from pg_roles where rolname = 'prosper_rpc_executor') then
    create role prosper_rpc_executor login noinherit nosuperuser nocreatedb nocreaterole noreplication;
  end if;
end;
$block$;

revoke all on all tables in schema public from prosper_rpc_executor;
revoke all on all tables in schema store from prosper_rpc_executor;
revoke all on all sequences in schema store from prosper_rpc_executor;
revoke execute on all functions in schema store from prosper_rpc_executor;
grant usage on schema store to prosper_rpc_executor;
grant execute on function store.bind_app_user_access(text, text, boolean) to prosper_rpc_executor;
grant execute on function store.authorize_app_user_access(text, text, bigint, text) to prosper_rpc_executor;
grant execute on function store.get_app_user_access(text, text) to prosper_rpc_executor;

do $block$
declare
  v_function_name text;
  v_signature regprocedure;
  v_allowlist constant text[] := array[
    'advance_receipt_work_queue_v2', 'cancel_checkout_v2', 'close_business_day_v2',
    'confirm_checkout_v2', 'confirm_current_cast_sales_adjustments_v2',
    'delete_non_master_records', 'get_attendance_editor_bootstrap_v2',
    'get_business_day_daily_report', 'get_business_home_bootstrap_v2',
    'get_checkout_receipt_print_data', 'get_checkout_statement_print_data',
    'get_current_attendance_editor_snapshot', 'get_current_business_home_snapshot',
    'get_current_cast_sales_adjustment_overview', 'get_current_closing_dashboard',
    'get_current_drink_back_editor', 'get_current_drink_delivery_editor',
    'get_current_order_entry_candidates', 'get_current_receipt_work_queue',
    'get_departments', 'get_management_master_snapshot',
    'get_sales_history_correction_editor', 'get_sales_history_page',
    'is_pending_receipt_drive_file_allowed', 'issue_checkout_statement_v2',
    'release_checkout_ready_v2', 'save_current_business_day_attendance_v2',
    'save_current_business_day_drink_delivery_amount_v2',
    'save_current_cast_sales_adjustment_v2', 'save_drink_back_adjustments_v2',
    'save_management_master_v2', 'save_sales_history_correction_v1',
    'submit_current_order_entry_v2', 'sync_business_home_changes_v2'
  ];
begin
  foreach v_function_name in array v_allowlist loop
    for v_signature in
      select p.oid::regprocedure
        from pg_proc p
        join pg_namespace n on n.oid = p.pronamespace
       where n.nspname = 'store'
         and p.proname = v_function_name
    loop
      execute format('grant execute on function %s to prosper_rpc_executor', v_signature);
    end loop;
  end loop;
end;
$block$;
