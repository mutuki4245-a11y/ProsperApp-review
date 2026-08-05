-- 勤怠保存の現在営業日取得・営業日作成・出退勤保存・保存後再読取を1 transactionへ集約します。

begin;

drop function if exists store.save_current_business_day_attendance_v2(bigint, bigint, date, jsonb, jsonb);

create or replace function store.save_current_business_day_attendance_v2(
    p_department_id bigint,
    p_expected_business_day_id bigint default null,
    p_business_date date default null,
    p_cast_entries jsonb default '[]'::jsonb,
    p_staff_entries jsonb default '[]'::jsonb
)
returns table (
    business_day jsonb,
    attendance jsonb,
    saved_clock_out_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_cast_closing_entries jsonb := '[]'::jsonb;
    v_staff_closing_entries jsonb := '[]'::jsonb;
    v_attendance jsonb := '[]'::jsonb;
    v_saved_clock_out_count integer := 0;
    v_saved_count integer;
begin
    if p_department_id <= 0 or p_business_date is null or
       jsonb_typeof(p_cast_entries) <> 'array' or jsonb_typeof(p_staff_entries) <> 'array' or
       jsonb_array_length(p_cast_entries) > 300 or jsonb_array_length(p_staff_entries) > 300 then
        raise exception 'invalid_attendance_batch';
    end if;

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_current_attendance:%s', p_department_id),
        0));

    select business_day.*
      into v_business_day
      from store.get_current_business_day(p_department_id) business_day;

    if v_business_day.business_day_id is null then
        if p_expected_business_day_id is not null then
            raise exception 'business_day_revision_conflict';
        end if;

        select opened_business_day.*
          into v_business_day
          from store.open_business_day(p_department_id, p_business_date, null) opened_business_day;
    else
        if p_expected_business_day_id is not null and
           p_expected_business_day_id <> v_business_day.business_day_id then
            raise exception 'business_day_revision_conflict';
        end if;

        if v_business_day.business_date <> p_business_date then
            raise exception 'business_day_closing_required';
        end if;
    end if;

    if jsonb_array_length(p_cast_entries) > 0 then
        perform 1
          from store.save_business_day_attendance(
              p_department_id,
              v_business_day.business_day_id,
              p_cast_entries);
    end if;

    if jsonb_array_length(p_staff_entries) > 0 then
        perform 1
          from store.save_business_day_staff_attendance(
              p_department_id,
              v_business_day.business_day_id,
              p_staff_entries);
    end if;

    select coalesce(jsonb_agg(jsonb_build_object(
        'attendance_id', cast_attendance.attendance_id,
        'clock_in_time', trim(entry.value->>'clock_in_time'),
        'clock_out_time', trim(entry.value->>'clock_out_time'),
        'uses_send_service', coalesce((entry.value->>'uses_send_service')::boolean, false)
    )), '[]'::jsonb)
      into v_cast_closing_entries
      from jsonb_array_elements(p_cast_entries) entry
      join public.store_cast_attendance cast_attendance
        on cast_attendance.department_id = p_department_id
       and cast_attendance.business_day_id = v_business_day.business_day_id
       and cast_attendance.cast_id = case
           when coalesce(entry.value->>'cast_id', '') ~ '^[1-9][0-9]*$'
               then (entry.value->>'cast_id')::bigint
           else 0
       end
     where coalesce((entry.value->>'is_selected')::boolean, false)
       and nullif(trim(coalesce(entry.value->>'clock_out_time', '')), '') is not null
       and cast_attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out');

    if jsonb_array_length(v_cast_closing_entries) > 0 then
        select store.save_business_day_closing_attendance(
            p_department_id,
            v_business_day.business_day_id,
            v_cast_closing_entries)
          into v_saved_count;
        v_saved_clock_out_count := v_saved_clock_out_count + coalesce(v_saved_count, 0);
    end if;

    select coalesce(jsonb_agg(jsonb_build_object(
        'attendance_id', staff_attendance.staff_attendance_id,
        'clock_in_time', trim(entry.value->>'clock_in_time'),
        'clock_out_time', trim(entry.value->>'clock_out_time'),
        'uses_send_service', coalesce((entry.value->>'uses_send_service')::boolean, false)
    )), '[]'::jsonb)
      into v_staff_closing_entries
      from jsonb_array_elements(p_staff_entries) entry
      join public.store_staff_attendance staff_attendance
        on staff_attendance.department_id = p_department_id
       and staff_attendance.business_day_id = v_business_day.business_day_id
       and staff_attendance.staff_id = case
           when coalesce(entry.value->>'staff_id', '') ~ '^[1-9][0-9]*$'
               then (entry.value->>'staff_id')::bigint
           else 0
       end
     where coalesce((entry.value->>'is_selected')::boolean, false)
       and nullif(trim(coalesce(entry.value->>'clock_out_time', '')), '') is not null
       and staff_attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out');

    if jsonb_array_length(v_staff_closing_entries) > 0 then
        select store.save_business_day_staff_closing_attendance(
            p_department_id,
            v_business_day.business_day_id,
            v_staff_closing_entries)
          into v_saved_count;
        v_saved_clock_out_count := v_saved_clock_out_count + coalesce(v_saved_count, 0);
    end if;

    select coalesce(jsonb_agg(to_jsonb(attendance_row) order by attendance_row.display_name, attendance_row.attendance_id), '[]'::jsonb)
      into v_attendance
      from store.get_business_day_closing_attendance(
          p_department_id,
          v_business_day.business_day_id) attendance_row;

    return query
    select to_jsonb(v_business_day), v_attendance, v_saved_clock_out_count;
end;
$$;

revoke all on function store.save_current_business_day_attendance_v2(bigint, bigint, date, jsonb, jsonb)
    from public, anon, authenticated, service_role;

commit;
