begin;

drop function if exists store.get_current_business_home_snapshot(bigint);

create or replace function store.get_current_business_home_snapshot(
    p_department_id bigint
)
returns table (
    has_business_day boolean,
    business_day jsonb,
    snapshot jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day store_business_days%rowtype;
begin
    select business_day.*
      into v_business_day
      from store.get_current_business_day(p_department_id) business_day;

    if v_business_day.business_day_id is null then
        return query
        select false, null::jsonb, null::jsonb;
        return;
    end if;

    return query
    select
        true,
        to_jsonb(v_business_day),
        business_snapshot.snapshot
    from store.get_business_day_snapshot(
        p_department_id,
        v_business_day.business_day_id
    ) business_snapshot;
end;
$$;

revoke all on function store.get_current_business_home_snapshot(bigint)
    from public, anon, authenticated, service_role;

commit;
