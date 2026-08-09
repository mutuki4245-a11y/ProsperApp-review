-- 営業中の一般編集と伝票作成を、現在営業日の検証から最終snapshotまで
-- 1 transactionで確定します。旧flush RPCとの後方互換は持ちません。

begin;

drop function if exists store.flush_current_business_home_changes(bigint, text, jsonb, jsonb);
drop function if exists store.flush_business_home_changes(bigint, bigint, text, jsonb, jsonb);
drop table if exists public.store_business_home_flush_batches;

create table if not exists store.business_home_sync_results (
    department_id bigint not null,
    client_batch_id uuid not null,
    request_body jsonb not null,
    response jsonb not null,
    created_at timestamp with time zone not null default now(),
    primary key (department_id, client_batch_id)
);

revoke all on table store.business_home_sync_results from public, anon, authenticated, service_role;
create index if not exists business_home_sync_results_created_at_idx
    on store.business_home_sync_results (created_at);

create table if not exists store.current_business_day_operation_results (
    department_id bigint not null,
    operation_id text not null,
    operation_type text not null,
    request_body jsonb not null,
    response jsonb not null,
    created_at timestamp with time zone not null default now(),
    primary key (department_id, operation_id)
);

revoke all on table store.current_business_day_operation_results from public, anon, authenticated, service_role;

drop function if exists store.sync_business_home_changes_v2(bigint, uuid, bigint, bigint, date, jsonb, jsonb);

create or replace function store.sync_business_home_changes_v2(
    p_department_id bigint,
    p_client_batch_id uuid,
    p_expected_business_day_id bigint default null,
    p_expected_business_day_revision bigint default null,
    p_business_date date default null,
    p_operations jsonb default '[]'::jsonb,
    p_karaoke_lines jsonb default '[]'::jsonb
)
returns table (
    status text,
    business_day jsonb,
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
    v_request_body jsonb;
    v_existing_request jsonb;
    v_response jsonb;
    v_business_day public.store_business_days%rowtype;
    v_operation jsonb;
    v_karaoke_line jsonb;
    v_operation_id text;
    v_operation_type text;
    v_slip_id bigint;
    v_client_draft_id text;
    v_saved_count integer;
    v_error_message text;
    v_error_state text;
    v_result_status text := 'confirmed';
    v_operation_results jsonb := '[]'::jsonb;
    v_karaoke_results jsonb := '[]'::jsonb;
    v_seen_operation_ids text[] := array[]::text[];
    v_snapshot jsonb;
    v_snapshot_business_day jsonb;
    v_revision bigint := 0;
begin
    if p_department_id <= 0 or p_client_batch_id is null or
       jsonb_typeof(p_operations) <> 'array' or
       jsonb_typeof(p_karaoke_lines) <> 'array' or
       jsonb_array_length(p_operations) > 100 or
       jsonb_array_length(p_karaoke_lines) > 100 then
        raise exception 'invalid_business_home_batch';
    end if;

    v_request_body := jsonb_build_object(
        'expected_business_day_id', p_expected_business_day_id,
        'expected_business_day_revision', p_expected_business_day_revision,
        'business_date', p_business_date,
        'operations', p_operations,
        'karaoke_lines', p_karaoke_lines
    );

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_business_day_state:%s', p_department_id),
        0));

    delete from store.business_home_sync_results
     where created_at < now() - interval '30 days';
    delete from store.current_business_day_operation_results
     where created_at < now() - interval '30 days';

    select result.request_body, result.response
      into v_existing_request, v_response
      from store.business_home_sync_results result
     where result.department_id = p_department_id
       and result.client_batch_id = p_client_batch_id;

    if found then
        if v_existing_request <> v_request_body then
            raise exception 'business_home_batch_id_reused';
        end if;

        return query select
            v_response->>'status',
            v_response->'business_day',
            coalesce(nullif(v_response->>'business_day_revision', '')::bigint, 0),
            v_response->'snapshot',
            coalesce(v_response->'operation_results', '[]'::jsonb),
            coalesce(v_response->'karaoke_results', '[]'::jsonb);
        return;
    end if;

    -- 例外をこのblockで捕捉すると、このblock内の全書込みだけがrollbackされる。
    -- これにより行単位の部分成功を残さず、競合snapshotを同じRPCで返せる。
    begin
        select current_day.*
          into v_business_day
          from store.get_current_business_day_internal(p_department_id) current_day;

        if v_business_day.business_day_id is null then
            if p_expected_business_day_id is not null or p_expected_business_day_revision is not null then
                raise exception 'business_day_revision_conflict';
            end if;

            if not exists (
                select 1
                  from jsonb_array_elements(p_operations) line
                 where line.value->>'operation_type' = 'create_slip'
            ) or p_business_date is null then
                raise exception 'business_day_not_open';
            end if;

            select opened_day.*
              into v_business_day
              from store.open_business_day_internal(p_department_id, p_business_date, null) opened_day;
        elsif p_expected_business_day_id is null or
              p_expected_business_day_revision is null or
              p_expected_business_day_id <> v_business_day.business_day_id or
              p_expected_business_day_revision <> v_business_day.business_ui_revision then
            raise exception 'business_day_revision_conflict';
        end if;

        if p_business_date is not null and v_business_day.business_date <> p_business_date then
            raise exception 'business_day_closing_required';
        end if;

        -- 複数伝票を扱うbatchはID順にlockし、別batchとのdeadlockを避ける。
        perform 1
          from public.store_slips slip
         where slip.department_id = p_department_id
           and slip.business_day_id = v_business_day.business_day_id
           and slip.slip_id in (
               select distinct (line.value->>'slip_id')::bigint
                 from jsonb_array_elements(p_operations) line
                where coalesce(line.value->>'slip_id', '') ~ '^[1-9][0-9]*$'
               union
               select distinct (line.value->>'slip_id')::bigint
                 from jsonb_array_elements(p_karaoke_lines) line
                where coalesce(line.value->>'slip_id', '') ~ '^[1-9][0-9]*$'
           )
         order by slip.slip_id
         for update;

        for v_operation in
            select line.value
              from jsonb_array_elements(p_operations) with ordinality line(value, ordinal)
             order by line.ordinal
        loop
            v_operation_id := lower(trim(coalesce(v_operation->>'operation_id', '')));
            v_operation_type := trim(coalesce(v_operation->>'operation_type', ''));
            v_client_draft_id := nullif(trim(coalesce(v_operation->>'client_draft_id', '')), '');

            if jsonb_typeof(v_operation) <> 'object' or
               v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' or
               v_operation_type = '' or
               v_operation_id = any(v_seen_operation_ids) then
                raise exception 'invalid_business_editor_operation';
            end if;

            if exists (
                select 1
                  from store.current_business_day_operation_results existing
                 where existing.department_id = p_department_id
                   and existing.operation_id = v_operation_id
            ) then
                raise exception 'business_day_operation_id_reused';
            end if;
            v_seen_operation_ids := array_append(v_seen_operation_ids, v_operation_id);

            if v_operation_type = 'create_slip' then
                if v_client_draft_id is null or
                   coalesce(v_operation->'payload'->>'table_id', '') !~ '^[1-9][0-9]*$' or
                   nullif(v_operation->'payload'->>'opened_at', '') is null or
                   jsonb_typeof(v_operation->'payload'->'customer_labels') <> 'array' or
                   jsonb_array_length(v_operation->'payload'->'customer_labels') not between 1 and 20 or
                   jsonb_typeof(coalesce(v_operation->'payload'->'cast_nominations', '[]'::jsonb)) <> 'array' then
                    raise exception 'invalid_create_slip_input';
                end if;

                if exists (
                    select 1
                      from jsonb_array_elements(coalesce(v_operation->'payload'->'cast_nominations', '[]'::jsonb)) nomination
                      left join public.cast_master cast_member
                        on coalesce(nomination.value->>'cast_id', '') ~ '^[1-9][0-9]*$'
                       and cast_member.cast_id = (nomination.value->>'cast_id')::bigint
                       and cast_member.department_id = p_department_id
                       and cast_member.is_active = true
                      left join public.store_cast_attendance attendance
                        on attendance.cast_id = cast_member.cast_id
                       and attendance.department_id = p_department_id
                       and attendance.business_day_id = v_business_day.business_day_id
                       and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')
                     where cast_member.cast_id is null
                        or attendance.attendance_id is null
                ) then
                    raise exception 'nomination_cast_not_attending';
                end if;

                select created.slip_id
                  into v_slip_id
                  from store.create_slip_internal(
                      p_department_id,
                      (v_operation->'payload'->>'table_id')::bigint,
                      (v_operation->'payload'->>'opened_at')::timestamp with time zone,
                      array(
                          select label.value #>> '{}'
                            from jsonb_array_elements(v_operation->'payload'->'customer_labels') with ordinality label(value, ordinal)
                           order by label.ordinal
                      ),
                      coalesce(v_operation->'payload'->'cast_nominations', '[]'::jsonb),
                      nullif(trim(coalesce(v_operation->'payload'->>'memo', '')), '')
                  ) created;

                v_operation_results := v_operation_results || jsonb_build_array(jsonb_build_object(
                    'operation_id', v_operation_id,
                    'client_draft_id', v_client_draft_id,
                    'slip_id', v_slip_id,
                    'succeeded', true
                ));
            else
                if coalesce(v_operation->>'slip_id', '') !~ '^[1-9][0-9]*$' then
                    raise exception 'invalid_business_editor_operation';
                end if;

                v_slip_id := (v_operation->>'slip_id')::bigint;
                perform store.apply_business_slip_editor_operation_internal(
                    p_department_id,
                    v_business_day.business_day_id,
                    v_slip_id,
                    v_operation_type,
                    v_operation_id,
                    coalesce(v_operation->'payload', '{}'::jsonb));

                v_operation_results := v_operation_results || jsonb_build_array(jsonb_build_object(
                    'operation_id', v_operation_id,
                    'slip_id', v_slip_id,
                    'succeeded', true
                ));
            end if;
        end loop;

        for v_karaoke_line in
            select line.value
              from jsonb_array_elements(p_karaoke_lines) with ordinality line(value, ordinal)
             order by line.ordinal
        loop
            v_operation_id := lower(trim(coalesce(
                v_karaoke_line->>'operation_id',
                v_karaoke_line->>'draft_id',
                '')));

            if v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' or
               v_operation_id = any(v_seen_operation_ids) then
                raise exception 'invalid_karaoke_quantity';
            end if;
            v_seen_operation_ids := array_append(v_seen_operation_ids, v_operation_id);

            select saved.saved_count
              into v_saved_count
              from store.save_karaoke_lines(
                  p_department_id,
                  v_business_day.business_day_id,
                  jsonb_build_array(jsonb_build_object(
                      'slip_id', v_karaoke_line->>'slip_id',
                      'quantity', v_karaoke_line->>'quantity'
                  ))) saved;

            v_karaoke_results := v_karaoke_results || jsonb_build_array(jsonb_build_object(
                'operation_id', v_operation_id,
                'draft_id', v_operation_id,
                'slip_id', v_karaoke_line->>'slip_id',
                'succeeded', true,
                'saved_count', coalesce(v_saved_count, 0)
            ));
        end loop;
    exception when others then
        get stacked diagnostics v_error_message = message_text, v_error_state = returned_sqlstate;
        v_result_status := case
            when v_error_message like '%revision_conflict%' or
                 v_error_message like '%not_found%' or
                 v_error_message like '%not_open%' or
                 v_error_message like '%closing_required%' or
                 v_error_message like '%operation_id_reused%'
                then 'conflict'
            else 'validation_error'
        end;
        select coalesce(jsonb_agg(jsonb_build_object(
            'operation_id', lower(trim(coalesce(line.value->>'operation_id', ''))),
            'client_draft_id', nullif(trim(coalesce(line.value->>'client_draft_id', '')), ''),
            'slip_id', nullif(trim(coalesce(line.value->>'slip_id', '')), ''),
            'succeeded', false,
            'status', v_result_status,
            'message', coalesce(v_error_message, 'business_home_sync_failed'),
            'sqlstate', coalesce(v_error_state, '')
        ) order by line.ordinal), '[]'::jsonb)
          into v_operation_results
          from jsonb_array_elements(p_operations) with ordinality line(value, ordinal);

        select coalesce(jsonb_agg(jsonb_build_object(
            'operation_id', lower(trim(coalesce(line.value->>'operation_id', line.value->>'draft_id', ''))),
            'draft_id', lower(trim(coalesce(line.value->>'draft_id', line.value->>'operation_id', ''))),
            'slip_id', nullif(trim(coalesce(line.value->>'slip_id', '')), ''),
            'succeeded', false,
            'status', v_result_status,
            'message', coalesce(v_error_message, 'business_home_sync_failed'),
            'sqlstate', coalesce(v_error_state, '')
        ) order by line.ordinal), '[]'::jsonb)
          into v_karaoke_results
          from jsonb_array_elements(p_karaoke_lines) with ordinality line(value, ordinal);
    end;

    select current_snapshot.business_day,
           current_snapshot.business_day_revision,
           current_snapshot.snapshot
      into v_snapshot_business_day, v_revision, v_snapshot
      from store.get_current_business_home_snapshot(p_department_id, null) current_snapshot;

    v_response := jsonb_build_object(
        'status', v_result_status,
        'business_day', v_snapshot_business_day,
        'business_day_revision', coalesce(v_revision, 0),
        'snapshot', coalesce(v_snapshot, '{}'::jsonb),
        'operation_results', v_operation_results,
        'karaoke_results', v_karaoke_results
    );

    insert into store.business_home_sync_results (
        department_id,
        client_batch_id,
        request_body,
        response
    ) values (
        p_department_id,
        p_client_batch_id,
        v_request_body,
        v_response
    );

    insert into store.current_business_day_operation_results (
        department_id,
        operation_id,
        operation_type,
        request_body,
        response
    )
    select
        p_department_id,
        lower(line.value->>'operation_id'),
        coalesce(nullif(line.value->>'operation_type', ''), 'business_home_operation'),
        line.value,
        v_response
      from jsonb_array_elements(p_operations) line
     where coalesce(line.value->>'operation_id', '') ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
    union all
    select
        p_department_id,
        lower(coalesce(line.value->>'operation_id', line.value->>'draft_id')),
        'save_karaoke_quantity',
        line.value,
        v_response
      from jsonb_array_elements(p_karaoke_lines) line
     where coalesce(line.value->>'operation_id', line.value->>'draft_id', '') ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
    on conflict (department_id, operation_id) do nothing;

    return query select
        v_result_status,
        v_snapshot_business_day,
        coalesce(v_revision, 0),
        v_snapshot,
        v_operation_results,
        v_karaoke_results;
end;
$$;

revoke all on function store.sync_business_home_changes_v2(bigint, uuid, bigint, bigint, date, jsonb, jsonb)
    from public, anon, authenticated, service_role;

commit;
