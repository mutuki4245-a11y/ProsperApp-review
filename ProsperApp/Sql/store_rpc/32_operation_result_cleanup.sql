begin;

create or replace function store.cleanup_operation_results(
  p_before timestamp with time zone default now() - interval '30 days'
)
returns jsonb
language plpgsql
security definer
set search_path = pg_catalog, store, public
as $function$
declare
  v_business_home bigint;
  v_business_day bigint;
  v_receipt bigint;
  v_management bigint;
begin
  delete from store.business_home_sync_results where created_at < p_before;
  get diagnostics v_business_home = row_count;

  delete from store.current_business_day_operation_results
   where created_at < p_before
     and operation_type is distinct from 'save_sales_history_correction_v1';
  get diagnostics v_business_day = row_count;

  delete from store.receipt_work_queue_operations where created_at < p_before;
  get diagnostics v_receipt = row_count;

  delete from store.management_master_operation_results where created_at < p_before;
  get diagnostics v_management = row_count;

  return jsonb_build_object(
    'business_home_sync_results', v_business_home,
    'current_business_day_operation_results', v_business_day,
    'receipt_work_queue_operations', v_receipt,
    'management_master_operation_results', v_management
  );
end;
$function$;

revoke all on function store.cleanup_operation_results(timestamp with time zone)
  from public, anon, authenticated, service_role;

-- 保持期間の定期実行はpg_cronに任せます。pg_cronが入っていないDB
-- （CIの素のPostgreSQLやローカル検証用）ではスケジュール登録だけを飛ばし、
-- cleanup関数自体は同じように作ります。手動実行もそのまま可能です。
do $$
begin
  if to_regnamespace('cron') is null then
    raise notice 'pg_cron is not installed; skipping cleanup schedule registration';
    return;
  end if;

  perform cron.schedule(
    'prosper-operation-results-cleanup',
    '30 3 * * *',
    $cron$select store.cleanup_operation_results();$cron$
  );
end;
$$;

commit;
