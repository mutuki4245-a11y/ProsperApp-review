-- 営業中編集を保存する前の current-business-day 読取を廃止します。
-- 既存の flush 本体へ委譲するため、batch の冪等性と行単位の結果契約は維持されます。

begin;

drop function if exists store.flush_current_business_home_changes(bigint, text, jsonb, jsonb);

create or replace function store.flush_current_business_home_changes(
    p_department_id bigint,
    p_client_batch_id text,
    p_operations jsonb default '[]'::jsonb,
    p_karaoke_lines jsonb default '[]'::jsonb
)
returns table (
    business_day_revision bigint,
    snapshot jsonb,
    operation_results jsonb,
    karaoke_results jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day_id bigint;
begin
    select business_day.business_day_id
      into v_business_day_id
      from store.get_current_business_day(p_department_id) business_day;

    if coalesce(v_business_day_id, 0) <= 0 then
        raise exception 'business_day_not_open';
    end if;

    return query
    select *
      from store.flush_business_home_changes(
          p_department_id,
          v_business_day_id,
          p_client_batch_id,
          p_operations,
          p_karaoke_lines);
end;
$$;

revoke all on function store.flush_current_business_home_changes(bigint, text, jsonb, jsonb)
    from public, anon, authenticated, service_role;

commit;
