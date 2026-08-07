-- 勤怠画面のcold bootstrapと現在営業日の勤怠snapshotを1 RPCへ集約します。

begin;

drop function if exists store.get_attendance_editor_bootstrap_v2(bigint);
drop function if exists store.get_current_attendance_editor_snapshot(bigint, bigint, bigint);
drop function if exists store.get_current_attendance_editor_state(bigint);

create or replace function store.get_current_attendance_editor_state(
    p_department_id bigint
)
returns table (
    has_business_day boolean,
    business_day jsonb,
    business_day_revision bigint,
    attendance_entries jsonb,
    attending_casts jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_attendance_entries jsonb := '[]'::jsonb;
    v_attending_casts jsonb := '[]'::jsonb;
begin
    if p_department_id <= 0 then
        raise exception 'store_department_not_found';
    end if;

    perform pg_advisory_xact_lock_shared(hashtextextended(
        format('store_business_day_state:%s', p_department_id),
        0));

    select current_day.*
      into v_business_day
      from public.store_business_days current_day
     where current_day.department_id = p_department_id
       and current_day.status = 'open'
     order by current_day.opened_at desc, current_day.business_day_id desc
     limit 1;

    if v_business_day.business_day_id is null then
        return query select false, null::jsonb, 0::bigint, '[]'::jsonb, '[]'::jsonb;
        return;
    end if;

    select coalesce(
        jsonb_agg(attendance_row.payload order by
            attendance_row.sort_group,
            attendance_row.sort_order,
            attendance_row.display_name,
            attendance_row.attendance_id),
        '[]'::jsonb)
      into v_attendance_entries
      from (
          select
              0 as sort_group,
              c.sort_order,
              c.display_name,
              attendance.attendance_id,
              jsonb_build_object(
                  'attendance_id', attendance.attendance_id,
                  'person_type', 'cast',
                  'person_id', attendance.cast_id,
                  'display_name', c.display_name,
                  'department_name', department.department_name,
                  'attendance_status', attendance.attendance_status,
                  'clock_in_time', to_char(attendance.clock_in_at at time zone 'Asia/Tokyo', 'HH24:MI'),
                  'clock_out_time', case
                      when attendance.clock_out_at is null then null
                      else to_char(attendance.clock_out_at at time zone 'Asia/Tokyo', 'HH24:MI')
                  end,
                  'uses_send_service', coalesce(attendance.uses_send_service, false),
                  'version', to_char(attendance.updated_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
              ) as payload
          from public.store_cast_attendance attendance
          join public.cast_master c
            on c.cast_id = attendance.cast_id
          left join public.department_master department
            on department.department_id = c.department_id
          where attendance.department_id = p_department_id
            and attendance.business_day_id = v_business_day.business_day_id
            and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')

          union all

          select
              1 as sort_group,
              staff.sort_order,
              staff.display_name,
              attendance.staff_attendance_id as attendance_id,
              jsonb_build_object(
                  'attendance_id', attendance.staff_attendance_id,
                  'person_type', 'staff',
                  'person_id', attendance.staff_id,
                  'display_name', staff.display_name,
                  'department_name', department.department_name,
                  'attendance_status', attendance.attendance_status,
                  'clock_in_time', to_char(attendance.clock_in_at at time zone 'Asia/Tokyo', 'HH24:MI'),
                  'clock_out_time', case
                      when attendance.clock_out_at is null then null
                      else to_char(attendance.clock_out_at at time zone 'Asia/Tokyo', 'HH24:MI')
                  end,
                  'uses_send_service', coalesce(attendance.uses_send_service, false),
                  'version', to_char(attendance.updated_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
              ) as payload
          from public.store_staff_attendance attendance
          join public.store_staff_master staff
            on staff.staff_id = attendance.staff_id
          left join public.department_master department
            on department.department_id = staff.department_id
          where attendance.department_id = p_department_id
            and attendance.business_day_id = v_business_day.business_day_id
            and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')
      ) attendance_row;

    select coalesce(
        jsonb_agg(to_jsonb(attending_cast) order by attending_cast.department_name, attending_cast.display_name, attending_cast.cast_id),
        '[]'::jsonb)
      into v_attending_casts
      from store.get_order_attending_casts_internal(
          p_department_id,
          v_business_day.business_day_id) attending_cast;

    return query select
        true,
        to_jsonb(v_business_day),
        v_business_day.business_ui_revision,
        v_attendance_entries,
        v_attending_casts;
end;
$$;

revoke all on function store.get_current_attendance_editor_state(bigint)
    from public, anon, authenticated, service_role;

create or replace function store.get_current_attendance_editor_snapshot(
    p_department_id bigint,
    p_known_business_day_id bigint default null,
    p_known_business_day_revision bigint default null
)
returns table (
    has_business_day boolean,
    unchanged boolean,
    business_day jsonb,
    business_day_revision bigint,
    attendance_entries jsonb,
    attending_casts jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_has_business_day boolean;
    v_business_day jsonb;
    v_business_day_revision bigint;
    v_attendance_entries jsonb;
    v_attending_casts jsonb;
    v_unchanged boolean;
begin
    select state.has_business_day,
           state.business_day,
           state.business_day_revision,
           state.attendance_entries,
           state.attending_casts
      into v_has_business_day,
           v_business_day,
           v_business_day_revision,
           v_attendance_entries,
           v_attending_casts
      from store.get_current_attendance_editor_state(p_department_id) state;

    v_unchanged := p_known_business_day_revision is not null
        and p_known_business_day_revision = v_business_day_revision
        and (
            (not v_has_business_day and p_known_business_day_id is null)
            or (v_has_business_day and p_known_business_day_id = nullif(v_business_day->>'business_day_id', '')::bigint)
        );

    return query select
        v_has_business_day,
        v_unchanged,
        v_business_day,
        v_business_day_revision,
        case when v_unchanged then null::jsonb else v_attendance_entries end,
        case when v_unchanged then null::jsonb else v_attending_casts end;
end;
$$;

revoke all on function store.get_current_attendance_editor_snapshot(bigint, bigint, bigint)
    from public, anon, authenticated, service_role;

create or replace function store.get_attendance_editor_bootstrap_v2(
    p_department_id bigint
)
returns table (
    master_revision text,
    store_context jsonb,
    casts jsonb,
    staffs jsonb,
    has_business_day boolean,
    unchanged boolean,
    business_day jsonb,
    business_day_revision bigint,
    attendance_entries jsonb,
    attending_casts jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_store_context jsonb;
    v_casts jsonb;
    v_staffs jsonb;
    v_has_business_day boolean;
    v_business_day jsonb;
    v_business_day_revision bigint;
    v_attendance_entries jsonb;
    v_attending_casts jsonb;
begin
    select state.has_business_day,
           state.business_day,
           state.business_day_revision,
           state.attendance_entries,
           state.attending_casts
      into v_has_business_day,
           v_business_day,
           v_business_day_revision,
           v_attendance_entries,
           v_attending_casts
      from store.get_current_attendance_editor_state(p_department_id) state;

    select to_jsonb(context_row)
      into v_store_context
      from store.get_context_internal(p_department_id) context_row;
    if v_store_context is null then
        raise exception 'store_department_not_found';
    end if;

    select coalesce(jsonb_agg(to_jsonb(cast_row) order by cast_row.department_name, cast_row.display_name, cast_row.cast_id), '[]'::jsonb)
      into v_casts
      from store.get_casts(p_department_id) cast_row;

    select coalesce(jsonb_agg(to_jsonb(staff_row) order by staff_row.display_name, staff_row.staff_id), '[]'::jsonb)
      into v_staffs
      from store.get_staffs(p_department_id) staff_row;

    return query select
        md5(jsonb_build_array(v_store_context, v_casts, v_staffs)::text),
        v_store_context,
        v_casts,
        v_staffs,
        v_has_business_day,
        false,
        v_business_day,
        v_business_day_revision,
        v_attendance_entries,
        v_attending_casts;
end;
$$;

revoke all on function store.get_attendance_editor_bootstrap_v2(bigint)
    from public, anon, authenticated, service_role;

commit;
