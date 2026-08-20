-- 勤怠保存をoperation_id付きの1 transactionへ集約し、確定snapshotを返します。

begin;


drop function if exists store.save_current_business_day_attendance_v2(bigint, bigint, date, jsonb, jsonb);
drop function if exists store.save_current_business_day_attendance_v2(bigint, text, bigint, date, jsonb, jsonb);
drop function if exists store.save_current_business_day_attendance_v2(bigint, text, bigint, bigint, date, jsonb, jsonb);

create or replace function store.save_current_business_day_attendance_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint default null,
    p_expected_business_day_revision bigint default null,
    p_business_date date default null,
    p_cast_entries jsonb default '[]'::jsonb,
    p_staff_entries jsonb default '[]'::jsonb
)
returns table (
    operation_id text,
    status text,
    message text,
    has_business_day boolean,
    unchanged boolean,
    business_day jsonb,
    business_day_revision bigint,
    attendance_entries jsonb,
    attending_casts jsonb,
    saved_count integer,
    saved_clock_out_count integer,
    row_results jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_opened_business_day_id bigint;
    v_cast_closing_entries jsonb := '[]'::jsonb;
    v_staff_closing_entries jsonb := '[]'::jsonb;
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_request_body jsonb;
    v_existing_request_body jsonb;
    v_response jsonb;
    v_status text := 'confirmed';
    v_message text := '勤怠入力を保存しました。';
    v_row_results jsonb := '[]'::jsonb;
    v_saved_count integer := 0;
    v_saved_clock_out_count integer := 0;
    v_company_id bigint;
    v_entry jsonb;
    v_person_id bigint;
    v_is_selected boolean;
    v_expected_version text;
    v_current_version text;
    v_error text;
    v_has_business_day boolean := false;
    v_state_business_day jsonb;
    v_state_revision bigint := 0;
    v_attendance_entries jsonb := '[]'::jsonb;
    v_attending_casts jsonb := '[]'::jsonb;
begin
    if p_department_id <= 0 or p_business_date is null or
       v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' then
        raise exception 'invalid_attendance_batch';
    end if;

    if jsonb_typeof(p_cast_entries) is distinct from 'array' or
       jsonb_typeof(p_staff_entries) is distinct from 'array' then
        raise exception 'invalid_attendance_batch';
    end if;

    if jsonb_array_length(p_cast_entries) > 300 or jsonb_array_length(p_staff_entries) > 300 then
        raise exception 'invalid_attendance_batch';
    end if;

    v_request_body := jsonb_build_object(
        'operation_type', 'save_current_business_day_attendance_v2',
        'expected_business_day_id', p_expected_business_day_id,
        'expected_business_day_revision', p_expected_business_day_revision,
        'business_date', p_business_date,
        'cast_entries', p_cast_entries,
        'staff_entries', p_staff_entries
    );

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_business_day_state:%s', p_department_id),
        0));

    select operation.request_body, operation.response
      into v_existing_request_body, v_response
      from store.current_business_day_operation_results operation
     where operation.department_id = p_department_id
       and operation.operation_id = v_operation_id;

    if found then
        if v_existing_request_body <> v_request_body then
            raise exception 'business_day_operation_id_reused';
        end if;

        return query select
            v_response->>'operation_id',
            v_response->>'status',
            v_response->>'message',
            coalesce((v_response->>'has_business_day')::boolean, false),
            false,
            v_response->'business_day',
            coalesce(nullif(v_response->>'business_day_revision', '')::bigint, 0),
            coalesce(v_response->'attendance_entries', '[]'::jsonb),
            coalesce(v_response->'attending_casts', '[]'::jsonb),
            coalesce(nullif(v_response->>'saved_count', '')::integer, 0),
            coalesce(nullif(v_response->>'saved_clock_out_count', '')::integer, 0),
            coalesce(v_response->'row_results', '[]'::jsonb);
        return;
    end if;

    <<save_attempt>>
    begin
        select current_day.*
          into v_business_day
          from public.store_business_days current_day
         where current_day.department_id = p_department_id
           and current_day.status = 'open'
         order by current_day.opened_at desc, current_day.business_day_id desc
         limit 1;

        if v_business_day.business_day_id is null then
            if p_expected_business_day_id is not null or p_expected_business_day_revision is not null then
                v_status := 'conflict';
                v_message := '営業日が変更されています。現在の勤怠を再取得してください。';
                exit save_attempt;
            end if;
        else
            if p_expected_business_day_id is null or
               p_expected_business_day_revision is null or
               p_expected_business_day_id <> v_business_day.business_day_id or
               p_expected_business_day_revision <> v_business_day.business_ui_revision then
                v_status := 'conflict';
                v_message := '別の更新が先に保存されています。現在の勤怠を確認してください。';
                exit save_attempt;
            end if;

            if v_business_day.business_date <> p_business_date then
                v_status := 'conflict';
                v_message := '営業日が切り替わっています。現在の勤怠を再取得してください。';
                exit save_attempt;
            end if;
        end if;

        select department.company_id
          into v_company_id
          from public.department_master department
         where department.department_id = p_department_id
           and department.is_active = true;
        if v_company_id is null then
            v_status := 'validation_error';
            v_message := '店舗設定を確認してください。';
            exit save_attempt;
        end if;

        for v_entry in select value from jsonb_array_elements(p_cast_entries)
        loop
            v_person_id := case
                when coalesce(v_entry->>'cast_id', '') ~ '^[1-9][0-9]*$' then (v_entry->>'cast_id')::bigint
                else 0
            end;
            v_is_selected := case
                when jsonb_typeof(v_entry->'is_selected') = 'boolean' then (v_entry->>'is_selected')::boolean
                else false
            end;

            if v_person_id <= 0 then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', 'cast', 'person_id', v_person_id, 'status', 'validation_error',
                    'message', 'キャストを確認してください。'));
            elsif jsonb_typeof(v_entry->'is_selected') is distinct from 'boolean' then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', 'cast', 'person_id', v_person_id, 'status', 'validation_error',
                    'message', '出勤状態を確認してください。'));
            elsif v_is_selected and coalesce(v_entry->>'clock_in_time', '') !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$' then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', 'cast', 'person_id', v_person_id, 'status', 'validation_error',
                    'message', '出勤時刻を確認してください。'));
            elsif nullif(trim(coalesce(v_entry->>'clock_out_time', '')), '') is not null and
                  (v_entry->>'clock_out_time') !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$' then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', 'cast', 'person_id', v_person_id, 'status', 'validation_error',
                    'message', '退勤時刻を確認してください。'));
            elsif v_is_selected and not exists (
                select 1
                  from public.cast_master cast_member
                  join public.department_master department
                    on department.department_id = cast_member.department_id
                 where cast_member.cast_id = v_person_id
                   and cast_member.company_id = v_company_id
                   and cast_member.is_active = true
                   and cast_member.status = 'active'
                   and department.is_active = true
            ) then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', 'cast', 'person_id', v_person_id, 'status', 'validation_error',
                    'message', '在籍中のキャストではありません。'));
            end if;
        end loop;

        for v_entry in select value from jsonb_array_elements(p_staff_entries)
        loop
            v_person_id := case
                when coalesce(v_entry->>'staff_id', '') ~ '^[1-9][0-9]*$' then (v_entry->>'staff_id')::bigint
                else 0
            end;
            v_is_selected := case
                when jsonb_typeof(v_entry->'is_selected') = 'boolean' then (v_entry->>'is_selected')::boolean
                else false
            end;

            if v_person_id <= 0 then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', 'staff', 'person_id', v_person_id, 'status', 'validation_error',
                    'message', 'スタッフを確認してください。'));
            elsif jsonb_typeof(v_entry->'is_selected') is distinct from 'boolean' then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', 'staff', 'person_id', v_person_id, 'status', 'validation_error',
                    'message', '出勤状態を確認してください。'));
            elsif v_is_selected and coalesce(v_entry->>'clock_in_time', '') !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$' then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', 'staff', 'person_id', v_person_id, 'status', 'validation_error',
                    'message', '出勤時刻を確認してください。'));
            elsif nullif(trim(coalesce(v_entry->>'clock_out_time', '')), '') is not null and
                  (v_entry->>'clock_out_time') !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$' then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', 'staff', 'person_id', v_person_id, 'status', 'validation_error',
                    'message', '退勤時刻を確認してください。'));
            elsif v_is_selected and not exists (
                select 1
                  from public.store_staff_master staff_member
                 where staff_member.staff_id = v_person_id
                   and staff_member.company_id = v_company_id
                   and staff_member.department_id = p_department_id
                   and staff_member.is_active = true
                   and staff_member.status = 'active'
            ) then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', 'staff', 'person_id', v_person_id, 'status', 'validation_error',
                    'message', '在籍中のスタッフではありません。'));
            end if;
        end loop;

        if exists (
            select 1
              from (
                  select value->>'cast_id' as person_id
                    from jsonb_array_elements(p_cast_entries)
                  group by value->>'cast_id'
                  having count(*) > 1
              ) duplicate_cast
        ) or exists (
            select 1
              from (
                  select value->>'staff_id' as person_id
                    from jsonb_array_elements(p_staff_entries)
                  group by value->>'staff_id'
                  having count(*) > 1
              ) duplicate_staff
        ) then
            v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                'status', 'validation_error', 'message', '同じ出勤者が重複しています。'));
        end if;

        select count(*)::integer
          into v_saved_count
          from (
              select value from jsonb_array_elements(p_cast_entries)
              union all
              select value from jsonb_array_elements(p_staff_entries)
          ) entry
         where jsonb_typeof(entry.value->'is_selected') = 'boolean'
           and (entry.value->>'is_selected')::boolean;
        if v_saved_count = 0 then
            v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                'status', 'validation_error', 'message', '出勤者を1名以上選択してください。'));
        end if;

        if jsonb_array_length(v_row_results) > 0 then
            v_status := 'validation_error';
            v_message := '入力内容を確認してください。';
            exit save_attempt;
        end if;

        for v_entry in
            select jsonb_build_object('person_type', 'cast', 'person_id', value->'cast_id', 'entry', value)
              from jsonb_array_elements(p_cast_entries)
            union all
            select jsonb_build_object('person_type', 'staff', 'person_id', value->'staff_id', 'entry', value)
              from jsonb_array_elements(p_staff_entries)
        loop
            v_person_id := (v_entry->>'person_id')::bigint;
            v_expected_version := nullif(trim(coalesce(v_entry->'entry'->>'expected_version', '')), '');
            v_current_version := null;

            if v_business_day.business_day_id is not null then
                if v_entry->>'person_type' = 'staff' then
                    select to_char(attendance.updated_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
                      into v_current_version
                      from public.store_staff_attendance attendance
                     where attendance.department_id = p_department_id
                       and attendance.business_day_id = v_business_day.business_day_id
                       and attendance.staff_id = v_person_id
                       and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out');
                else
                    select to_char(attendance.updated_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
                      into v_current_version
                      from public.store_cast_attendance attendance
                     where attendance.department_id = p_department_id
                       and attendance.business_day_id = v_business_day.business_day_id
                       and attendance.cast_id = v_person_id
                       and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out');
                end if;
            end if;

            if v_expected_version is distinct from v_current_version then
                v_row_results := v_row_results || jsonb_build_array(jsonb_build_object(
                    'person_type', v_entry->>'person_type',
                    'person_id', v_person_id,
                    'status', 'conflict',
                    'message', 'この行は別の端末で更新されています。'));
            end if;
        end loop;

        if jsonb_array_length(v_row_results) > 0 then
            v_status := 'conflict';
            v_message := '別の更新が先に保存されています。現在の勤怠を確認してください。';
            exit save_attempt;
        end if;

        begin
            if v_business_day.business_day_id is null then
                select opened_day.business_day_id
                  into v_opened_business_day_id
                  from store.open_business_day_internal(p_department_id, p_business_date, null) opened_day;

                select opened_day.*
                  into v_business_day
                  from public.store_business_days opened_day
                 where opened_day.department_id = p_department_id
                   and opened_day.business_day_id = v_opened_business_day_id
                   and opened_day.status = 'open';
            end if;

            if jsonb_array_length(p_cast_entries) > 0 then
                perform 1
                  from store.save_business_day_attendance_internal(
                      p_department_id,
                      v_business_day.business_day_id,
                      p_cast_entries);
            end if;

            if jsonb_array_length(p_staff_entries) > 0 then
                perform 1
                  from store.save_business_day_staff_attendance_internal(
                      p_department_id,
                      v_business_day.business_day_id,
                      p_staff_entries);
            end if;

            select coalesce(jsonb_agg(jsonb_build_object(
                'attendance_id', attendance.attendance_id,
                'clock_in_time', trim(entry.value->>'clock_in_time'),
                'clock_out_time', trim(entry.value->>'clock_out_time'),
                'uses_send_service', coalesce((entry.value->>'uses_send_service')::boolean, false)
            )), '[]'::jsonb)
              into v_cast_closing_entries
              from jsonb_array_elements(p_cast_entries) entry
              join public.store_cast_attendance attendance
                on attendance.department_id = p_department_id
               and attendance.business_day_id = v_business_day.business_day_id
               and attendance.cast_id = (entry.value->>'cast_id')::bigint
             where (entry.value->>'is_selected')::boolean
               and nullif(trim(coalesce(entry.value->>'clock_out_time', '')), '') is not null
               and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out');

            if jsonb_array_length(v_cast_closing_entries) > 0 then
                select store.save_business_day_closing_attendance_internal(
                    p_department_id,
                    v_business_day.business_day_id,
                    v_cast_closing_entries)
                  into v_saved_clock_out_count;
            end if;

            select coalesce(jsonb_agg(jsonb_build_object(
                'attendance_id', attendance.staff_attendance_id,
                'clock_in_time', trim(entry.value->>'clock_in_time'),
                'clock_out_time', trim(entry.value->>'clock_out_time'),
                'uses_send_service', coalesce((entry.value->>'uses_send_service')::boolean, false)
            )), '[]'::jsonb)
              into v_staff_closing_entries
              from jsonb_array_elements(p_staff_entries) entry
              join public.store_staff_attendance attendance
                on attendance.department_id = p_department_id
               and attendance.business_day_id = v_business_day.business_day_id
               and attendance.staff_id = (entry.value->>'staff_id')::bigint
             where (entry.value->>'is_selected')::boolean
               and nullif(trim(coalesce(entry.value->>'clock_out_time', '')), '') is not null
               and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out');

            if jsonb_array_length(v_staff_closing_entries) > 0 then
                select v_saved_clock_out_count + store.save_business_day_staff_closing_attendance_internal(
                    p_department_id,
                    v_business_day.business_day_id,
                    v_staff_closing_entries)
                  into v_saved_clock_out_count;
            end if;

            update public.store_business_days business_day_row
               set business_ui_revision = business_day_row.business_ui_revision + 1
             where business_day_row.business_day_id = v_business_day.business_day_id
               and business_day_row.department_id = p_department_id
               and business_day_row.status = 'open'
            returning business_day_row.* into v_business_day;

            if v_business_day.business_day_id is null then
                raise exception 'business_day_revision_conflict';
            end if;
        exception when others then
            v_error := sqlerrm;
            v_status := case
                when v_error ilike '%revision%' or v_error ilike '%closing_required%' then 'conflict'
                else 'validation_error'
            end;
            v_message := case
                when v_error ilike '%attendance_required%' then '出勤者を1名以上選択してください。'
                when v_error ilike '%clock_in%' then '出勤時刻を確認してください。'
                when v_error ilike '%clock_out%' then '退勤時刻を確認してください。'
                when v_error ilike '%not_found%' then '在籍中の出勤者を確認してください。'
                when v_status = 'conflict' then '営業日が変更されています。現在の勤怠を再取得してください。'
                else '勤怠入力を保存できませんでした。'
            end;
            v_saved_count := 0;
            v_saved_clock_out_count := 0;
            v_row_results := jsonb_build_array(jsonb_build_object(
                'status', v_status,
                'message', v_message));
            exit save_attempt;
        end;

        select coalesce(jsonb_agg(jsonb_build_object(
            'person_type', saved.person_type,
            'person_id', saved.person_id,
            'status', 'confirmed',
            'message', null
        ) order by saved.person_type, saved.person_id), '[]'::jsonb)
          into v_row_results
          from (
              select 'cast'::text as person_type, (value->>'cast_id')::bigint as person_id
                from jsonb_array_elements(p_cast_entries)
              union all
              select 'staff'::text as person_type, (value->>'staff_id')::bigint as person_id
                from jsonb_array_elements(p_staff_entries)
          ) saved;
    end save_attempt;

    select state.has_business_day,
           state.business_day,
           state.business_day_revision,
           state.attendance_entries,
           state.attending_casts
      into v_has_business_day,
           v_state_business_day,
           v_state_revision,
           v_attendance_entries,
           v_attending_casts
      from store.get_current_attendance_editor_state(p_department_id) state;

    v_response := jsonb_build_object(
        'operation_id', v_operation_id,
        'status', v_status,
        'message', v_message,
        'has_business_day', v_has_business_day,
        'business_day', v_state_business_day,
        'business_day_revision', v_state_revision,
        'attendance_entries', v_attendance_entries,
        'attending_casts', v_attending_casts,
        'saved_count', case when v_status = 'confirmed' then v_saved_count else 0 end,
        'saved_clock_out_count', case when v_status = 'confirmed' then v_saved_clock_out_count else 0 end,
        'row_results', v_row_results
    );

    if v_status = 'confirmed' then
        insert into store.current_business_day_operation_results (
            department_id,
            operation_id,
            operation_type,
            request_body,
            response
        )
        values (
            p_department_id,
            v_operation_id,
            'save_current_business_day_attendance_v2',
            v_request_body,
            v_response
        );
    end if;

    return query select
        v_operation_id,
        v_status,
        v_message,
        v_has_business_day,
        false,
        v_state_business_day,
        v_state_revision,
        v_attendance_entries,
        v_attending_casts,
        case when v_status = 'confirmed' then v_saved_count else 0 end,
        case when v_status = 'confirmed' then v_saved_clock_out_count else 0 end,
        v_row_results;
end;
$$;

revoke all on function store.save_current_business_day_attendance_v2(bigint, text, bigint, bigint, date, jsonb, jsonb)
    from public, anon, authenticated, service_role;

commit;
