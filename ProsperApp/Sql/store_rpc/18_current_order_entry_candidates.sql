begin;

drop function if exists store.get_current_order_entry_candidates(bigint);

create or replace function store.get_current_order_entry_candidates(
    p_department_id bigint
)
returns table (
    has_business_day boolean,
    business_day jsonb,
    revision text,
    slips jsonb,
    attendance_casts jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day store_business_days%rowtype;
    v_slips jsonb := '[]'::jsonb;
    v_attendance_casts jsonb := '[]'::jsonb;
begin
    select business_day.*
      into v_business_day
      from store.get_current_business_day_internal(p_department_id) business_day;

    if v_business_day.business_day_id is null then
        return query select false, null::jsonb, 'no-business-day', '[]'::jsonb, '[]'::jsonb;
        return;
    end if;

    select coalesce(jsonb_agg(to_jsonb(slip_row) order by slip_row.opened_at, slip_row.slip_id), '[]'::jsonb)
      into v_slips
      from store.get_order_entry_slips_internal(p_department_id, v_business_day.business_day_id) slip_row;

    select coalesce(jsonb_agg(to_jsonb(cast_row) order by cast_row.display_name, cast_row.cast_id), '[]'::jsonb)
      into v_attendance_casts
      from store.get_order_attending_casts_internal(p_department_id, v_business_day.business_day_id) cast_row;

    return query
    select
        true,
        to_jsonb(v_business_day),
        md5(concat(v_business_day.business_day_id::text, ':', v_slips::text, ':', v_attendance_casts::text)),
        v_slips,
        v_attendance_casts;
end;
$$;

revoke all on function store.get_current_order_entry_candidates(bigint)
    from public, anon, authenticated, service_role;

commit;
