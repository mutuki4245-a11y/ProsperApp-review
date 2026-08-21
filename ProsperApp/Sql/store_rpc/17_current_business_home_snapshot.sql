begin;

drop function if exists store.get_current_business_home_snapshot(bigint);
drop function if exists store.get_current_business_home_snapshot(bigint, bigint);

create or replace function store.get_current_business_home_snapshot(
    p_department_id bigint,
    p_known_revision bigint default null
)
returns table (
    has_business_day boolean,
    business_day_revision bigint,
    unchanged boolean,
    business_day jsonb,
    attendance_casts jsonb,
    snapshot jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_revision bigint := 0;
    v_snapshot jsonb;
    v_attendance_casts jsonb := '[]'::jsonb;
    v_business_date date := case
        when (now() at time zone store.business_timezone())::time < store.business_day_cutover_time()
            then ((now() at time zone store.business_timezone())::date - 1)
        else (now() at time zone store.business_timezone())::date
    end;
begin
    select business_day.*
      into v_business_day
      from store.get_current_business_day_internal(p_department_id) business_day;

    if v_business_day.business_day_id is null then
        v_snapshot := jsonb_build_object(
            'businessDayId', null,
            'businessDayRevision', 0,
            'businessDate', to_char(v_business_date, 'YYYY-MM-DD'),
            'businessDateDisplay', to_char(v_business_date, 'YYYY-MM-DD') || ' / 自動作成待ち',
            'hasBusinessDay', false,
            'openSlipCount', 0,
            'checkedOutSlipCount', 0,
            'estimatedSalesAmount', 0,
            'slips', '[]'::jsonb
        );
        return query select
            false,
            0::bigint,
            coalesce(p_known_revision = 0, false),
            null::jsonb,
            case when p_known_revision = 0 then null::jsonb else '[]'::jsonb end,
            case when p_known_revision = 0 then null::jsonb else v_snapshot end;
        return;
    end if;

    select business_snapshot.business_day_revision, business_snapshot.snapshot
      into v_revision, v_snapshot
      from store.get_business_day_snapshot_internal(
          p_department_id,
          v_business_day.business_day_id
      ) business_snapshot;

    if p_known_revision is distinct from v_revision then
        select coalesce(jsonb_agg(to_jsonb(attendance_row) order by attendance_row.display_name, attendance_row.cast_id), '[]'::jsonb)
          into v_attendance_casts
          from store.get_order_attending_casts_internal(
              p_department_id,
              v_business_day.business_day_id
          ) attendance_row;
    end if;

    return query select
        true,
        v_revision,
        p_known_revision = v_revision,
        to_jsonb(v_business_day),
        case when p_known_revision = v_revision then null::jsonb else v_attendance_casts end,
        case when p_known_revision = v_revision then null::jsonb else v_snapshot end;
end;
$$;

revoke all on function store.get_current_business_home_snapshot(bigint, bigint)
    from public, anon, authenticated, service_role;

commit;
