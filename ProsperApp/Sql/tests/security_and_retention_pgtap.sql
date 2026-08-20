begin;
set local search_path = extensions, pg_catalog, public, store;

select plan(10);

select ok(
  (select count(*) = 2 from store.app_users where is_active),
  'two active application users are registered'
);
select ok(
  (select count(*) = 2 from store.app_user_department_access
    where department_id = 1 and access_role = 'administrator'),
  'both initial users are department 1 administrators'
);
select ok(
  (select rolcanlogin and not rolinherit and not rolsuper and not rolcreatedb and not rolcreaterole
     from pg_roles where rolname = 'prosper_rpc_executor'),
  'RPC executor role is login-capable and hardened'
);
select ok(
  not has_table_privilege('prosper_rpc_executor', 'store.app_users', 'select') and
  not has_table_privilege('prosper_rpc_executor', 'public.department_master', 'select'),
  'RPC executor cannot select application tables'
);
select ok(
  (select count(*) = 0
     from pg_proc p
     join pg_namespace n on n.oid = p.pronamespace
    where n.nspname = 'store'
      and has_function_privilege('anon', p.oid, 'execute')),
  'anon cannot execute store functions'
);
select ok(
  exists (select 1 from cron.job
    where jobname = 'prosper-operation-results-cleanup'
      and schedule = '30 3 * * *' and active),
  'daily cleanup cron is active at 03:30 UTC'
);
select ok(
  store.business_timezone() = 'Asia/Tokyo' and
  store.business_day_cutover_time() = time '12:00:00' and
  store.service_charge_rate() = 0.20::numeric and
  store.consumption_tax_inclusive_ratio() = 10::numeric / 110::numeric,
  'business constants match the C# contract'
);
select ok(
  pg_get_functiondef('store.save_sales_history_correction_v1(bigint,text,bigint,integer,date,text,text,jsonb)'::regprocedure)
    ilike '%is distinct from ''daily-report-v3''%',
  'sales history correction rejects NULL and legacy schema versions'
);

insert into store.current_business_day_operation_results(
  department_id, operation_id, operation_type, request_body, response, created_at
) values
  (1, 'pgtap-cleanup-normal', 'pgtap_normal', '{}'::jsonb, '{}'::jsonb, now() - interval '31 days'),
  (1, 'pgtap-cleanup-correction', 'save_sales_history_correction_v1', '{}'::jsonb, '{}'::jsonb, now() - interval '31 days');

select store.cleanup_operation_results(now() - interval '30 days');
select ok(
  not exists (select 1 from store.current_business_day_operation_results
    where operation_id = 'pgtap-cleanup-normal'),
  'ordinary operation result is cleaned up'
);
select ok(
  exists (select 1 from store.current_business_day_operation_results
    where operation_id = 'pgtap-cleanup-correction'),
  'sales history correction operation result is retained'
);

select * from finish();
rollback;
