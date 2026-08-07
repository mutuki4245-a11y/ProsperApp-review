-- ドリンクバック調整の物理モデル移行、editor read、idempotent mutationです。

begin;

drop trigger if exists trg_store_business_day_drink_back_adjustments_set_updated_at
    on public.store_business_day_drink_back_adjustments;
create trigger trg_store_business_day_drink_back_adjustments_set_updated_at
before update on public.store_business_day_drink_back_adjustments
for each row execute function public.set_updated_at();

drop trigger if exists trg_store_business_day_drink_back_adjustments_closed_day_guard
    on public.store_business_day_drink_back_adjustments;
create trigger trg_store_business_day_drink_back_adjustments_closed_day_guard
before insert or update or delete on public.store_business_day_drink_back_adjustments
for each row execute function store.guard_closed_business_day_mutation();

create table if not exists store.current_business_day_operation_results (
    department_id bigint not null,
    operation_id text not null,
    operation_type text not null,
    request_body jsonb not null,
    response jsonb not null,
    created_at timestamp with time zone not null default now(),
    primary key (department_id, operation_id)
);
revoke all on table store.current_business_day_operation_results
    from public, anon, authenticated, service_role;
create index if not exists current_business_day_operation_results_created_at_idx
    on store.current_business_day_operation_results(created_at);

create or replace function store.get_business_day_drink_back_status(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    required_cast_count integer,
    completed_cast_count integer,
    missing_cast_count integer,
    total_amount numeric
)
language sql
security definer
set search_path = pg_catalog, public
as $$
    with required_casts as (
        select attendance.cast_id
          from public.store_cast_attendance attendance
         where attendance.department_id = p_department_id
           and attendance.business_day_id = p_business_day_id
           and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')
         group by attendance.cast_id
    ),
    active_adjustments as (
        select adjustment.cast_id, adjustment.adjustment_amount
          from public.store_business_day_drink_back_adjustments adjustment
         where adjustment.department_id = p_department_id
           and adjustment.business_day_id = p_business_day_id
           and adjustment.status = 'active'
    )
    select
        (select count(*)::integer from required_casts),
        (select count(*)::integer
           from required_casts required_cast
           join active_adjustments adjustment using (cast_id)),
        (select count(*)::integer
           from required_casts required_cast
          where not exists (
              select 1
                from active_adjustments adjustment
               where adjustment.cast_id = required_cast.cast_id
          )),
        coalesce((select sum(adjustment.adjustment_amount) from active_adjustments adjustment), 0);
$$;

revoke all on function store.get_business_day_drink_back_status(bigint, bigint)
    from public, anon, authenticated, service_role;

create or replace function store.build_business_day_drink_back_editor(
    p_department_id bigint,
    p_business_day_id bigint
)
returns jsonb
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_status jsonb;
    v_required_rows jsonb := '[]'::jsonb;
    v_optional_rows jsonb := '[]'::jsonb;
    v_checked_at timestamp with time zone := clock_timestamp();
begin
    select business_day.*
      into v_business_day
      from public.store_business_days business_day
     where business_day.department_id = p_department_id
       and business_day.business_day_id = p_business_day_id;

    if v_business_day.business_day_id is null then
        return jsonb_build_object(
            'department_id', p_department_id,
            'has_business_day', false,
            'business_day_id', null,
            'business_day_revision', 0,
            'business_date', null,
            'required_rows', '[]'::jsonb,
            'optional_rows', '[]'::jsonb,
            'status', jsonb_build_object(
                'required_cast_count', 0,
                'completed_cast_count', 0,
                'missing_cast_count', 0,
                'total_amount', 0),
            'optional_adjustment_count', 0,
            'checked_at', v_checked_at);
    end if;

    select to_jsonb(status_row)
      into v_status
      from store.get_business_day_drink_back_status(
          p_department_id,
          p_business_day_id) status_row;

    with required_casts as (
        select attendance.cast_id
          from public.store_cast_attendance attendance
         where attendance.department_id = p_department_id
           and attendance.business_day_id = p_business_day_id
           and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')
         group by attendance.cast_id
    )
    select coalesce(jsonb_agg(jsonb_build_object(
        'cast_id', required_cast.cast_id,
        'display_name', cast_member.display_name,
        'department_name', department.department_name,
        'adjustment_amount', adjustment.adjustment_amount,
        'is_saved', adjustment.status = 'active',
        'is_required', true
    ) order by cast_member.sort_order, cast_member.cast_id), '[]'::jsonb)
      into v_required_rows
      from required_casts required_cast
      join public.cast_master cast_member
        on cast_member.cast_id = required_cast.cast_id
      left join public.department_master department
        on department.department_id = cast_member.department_id
      left join public.store_business_day_drink_back_adjustments adjustment
        on adjustment.department_id = p_department_id
       and adjustment.business_day_id = p_business_day_id
       and adjustment.cast_id = required_cast.cast_id
       and adjustment.status = 'active';

    with required_casts as (
        select attendance.cast_id
          from public.store_cast_attendance attendance
         where attendance.department_id = p_department_id
           and attendance.business_day_id = p_business_day_id
           and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')
         group by attendance.cast_id
    )
    select coalesce(jsonb_agg(jsonb_build_object(
        'cast_id', adjustment.cast_id,
        'display_name', cast_member.display_name,
        'department_name', department.department_name,
        'adjustment_amount', adjustment.adjustment_amount,
        'is_saved', true,
        'is_required', false
    ) order by cast_member.sort_order, cast_member.cast_id), '[]'::jsonb)
      into v_optional_rows
      from public.store_business_day_drink_back_adjustments adjustment
      join public.cast_master cast_member
        on cast_member.cast_id = adjustment.cast_id
      left join public.department_master department
        on department.department_id = cast_member.department_id
     where adjustment.department_id = p_department_id
       and adjustment.business_day_id = p_business_day_id
       and adjustment.status = 'active'
       and not exists (
           select 1
             from required_casts required_cast
            where required_cast.cast_id = adjustment.cast_id
       );

    return jsonb_build_object(
        'department_id', p_department_id,
        'has_business_day', v_business_day.status = 'open',
        'business_day_id', v_business_day.business_day_id,
        'business_day_revision', v_business_day.business_ui_revision,
        'business_date', v_business_day.business_date,
        'required_rows', v_required_rows,
        'optional_rows', v_optional_rows,
        'status', coalesce(v_status, '{}'::jsonb),
        'optional_adjustment_count', jsonb_array_length(v_optional_rows),
        'checked_at', v_checked_at);
end;
$$;

revoke all on function store.build_business_day_drink_back_editor(bigint, bigint)
    from public, anon, authenticated, service_role;

drop function if exists store.get_current_drink_back_editor(bigint);
create or replace function store.get_current_drink_back_editor(
    p_department_id bigint
)
returns table (
    editor jsonb,
    cast_master_revision text,
    active_casts jsonb
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day_id bigint;
    v_active_casts jsonb;
begin
    if p_department_id <= 0 then
        raise exception 'invalid_drink_back_input';
    end if;

    select business_day.business_day_id
      into v_business_day_id
      from public.store_business_days business_day
     where business_day.department_id = p_department_id
       and business_day.status = 'open'
     order by business_day.opened_at desc
     limit 1;

    select coalesce(jsonb_agg(jsonb_build_object(
        'cast_id', cast_member.cast_id,
        'display_name', cast_member.display_name,
        'department_name', department.department_name
    ) order by cast_member.sort_order, cast_member.cast_id), '[]'::jsonb)
      into v_active_casts
      from public.cast_master cast_member
      left join public.department_master department
        on department.department_id = cast_member.department_id
     where cast_member.department_id = p_department_id
       and cast_member.is_active = true;

    return query
    select
        case
            when v_business_day_id is null then jsonb_build_object(
                'department_id', p_department_id,
                'has_business_day', false,
                'business_day_id', null,
                'business_day_revision', 0,
                'business_date', null,
                'required_rows', '[]'::jsonb,
                'optional_rows', '[]'::jsonb,
                'status', jsonb_build_object(
                    'required_cast_count', 0,
                    'completed_cast_count', 0,
                    'missing_cast_count', 0,
                    'total_amount', 0),
                'optional_adjustment_count', 0,
                'checked_at', clock_timestamp())
            else store.build_business_day_drink_back_editor(p_department_id, v_business_day_id)
        end,
        md5(v_active_casts::text),
        v_active_casts;
end;
$$;

revoke all on function store.get_current_drink_back_editor(bigint)
    from public, anon, authenticated, service_role;

drop function if exists store.save_drink_back_adjustments_v2(bigint, text, bigint, bigint, jsonb, jsonb, jsonb);
create or replace function store.save_drink_back_adjustments_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint,
    p_expected_business_day_revision bigint,
    p_required_adjustments jsonb default '[]'::jsonb,
    p_optional_adjustments jsonb default '[]'::jsonb,
    p_remove_cast_ids jsonb default '[]'::jsonb
)
returns table (
    status text,
    operation_id text,
    message text,
    editor jsonb,
    drink_back_status jsonb,
    dashboard_delta jsonb,
    business_day_revision bigint
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_required jsonb := coalesce(p_required_adjustments, '[]'::jsonb);
    v_optional jsonb := coalesce(p_optional_adjustments, '[]'::jsonb);
    v_remove jsonb := coalesce(p_remove_cast_ids, '[]'::jsonb);
    v_request_body jsonb;
    v_existing_request_body jsonb;
    v_response jsonb;
    v_editor jsonb;
    v_drink_back_status jsonb;
    v_dashboard_delta jsonb;
    v_readiness record;
    v_status text := 'confirmed';
    v_message text;
    v_revision bigint := 0;
    v_checked_at timestamp with time zone := clock_timestamp();
begin
    if p_department_id <= 0 or
       v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' then
        raise exception 'invalid_drink_back_input';
    end if;

    v_request_body := jsonb_build_object(
        'operation_type', 'save_drink_back_adjustments_v2',
        'expected_business_day_id', p_expected_business_day_id,
        'expected_business_day_revision', p_expected_business_day_revision,
        'required_adjustments', v_required,
        'optional_adjustments', v_optional,
        'remove_cast_ids', v_remove);

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_business_day_state:%s', p_department_id), 0));

    delete from store.current_business_day_operation_results
     where created_at < now() - interval '30 days';

    select operation.request_body, operation.response
      into v_existing_request_body, v_response
      from store.current_business_day_operation_results operation
     where operation.department_id = p_department_id
       and operation.operation_id = v_operation_id;

    if found then
        if v_existing_request_body <> v_request_body then
            raise exception 'business_day_operation_id_reused';
        end if;

        return query
        select
            v_response->>'status',
            v_operation_id,
            v_response->>'message',
            v_response->'editor',
            v_response->'drink_back_status',
            v_response->'dashboard_delta',
            coalesce((v_response->>'business_day_revision')::bigint, 0);
        return;
    end if;

    if jsonb_typeof(v_required) <> 'array' or
       jsonb_typeof(v_optional) <> 'array' or
       jsonb_typeof(v_remove) <> 'array' or
       jsonb_array_length(v_required) > 500 or
       jsonb_array_length(v_optional) > 500 or
       jsonb_array_length(v_remove) > 500 then
        v_status := 'validation_error';
        v_message := 'invalid_drink_back_payload';
    elsif exists (
        select 1
          from jsonb_array_elements(v_required || v_optional) item
         where jsonb_typeof(item.value) <> 'object'
            or coalesce(item.value->>'cast_id', '') !~ '^[1-9][0-9]*$'
            or coalesce(item.value->>'adjustment_amount', '') !~ '^-?[0-9]+$'
    ) or exists (
        select 1
          from jsonb_array_elements(v_remove) item
         where coalesce(item.value#>>'{}', '') !~ '^[1-9][0-9]*$'
    ) then
        v_status := 'validation_error';
        v_message := 'invalid_drink_back_payload';
    elsif exists (
        select 1
          from jsonb_array_elements(v_required || v_optional) item
         where coalesce(item.value->>'adjustment_amount', '') ~ '^-?[0-9]+$'
           and abs((item.value->>'adjustment_amount')::numeric) > 999999999999
    ) then
        v_status := 'validation_error';
        v_message := 'invalid_drink_back_payload';
    elsif exists (
        select submitted.cast_id
          from (
              select (item.value->>'cast_id')::bigint as cast_id
                from jsonb_array_elements(v_required || v_optional) item
              union all
              select (item.value#>>'{}')::bigint
                from jsonb_array_elements(v_remove) item
          ) submitted
         group by submitted.cast_id
        having count(*) > 1
    ) then
        v_status := 'validation_error';
        v_message := 'duplicate_drink_back_cast';
    end if;

    select business_day.*
      into v_business_day
      from public.store_business_days business_day
     where business_day.department_id = p_department_id
       and business_day.status = 'open'
     order by business_day.opened_at desc
     limit 1
     for update;

    if v_business_day.business_day_id is null or
       p_expected_business_day_id is null or
       p_expected_business_day_revision is null or
       p_expected_business_day_id <> v_business_day.business_day_id or
       p_expected_business_day_revision <> v_business_day.business_ui_revision then
        v_status := 'conflict';
        v_message := 'business_day_revision_conflict';
    end if;

    if v_status = 'confirmed' and exists (
        with required_casts as (
            select attendance.cast_id
              from public.store_cast_attendance attendance
             where attendance.department_id = p_department_id
               and attendance.business_day_id = v_business_day.business_day_id
               and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')
             group by attendance.cast_id
        ), submitted_casts as (
            select (item.value->>'cast_id')::bigint as cast_id
              from jsonb_array_elements(v_required) item
        )
        select 1
          from (
              (select cast_id from required_casts except select cast_id from submitted_casts)
              union all
              (select cast_id from submitted_casts except select cast_id from required_casts)
          ) mismatch
    ) then
        v_status := 'validation_error';
        v_message := 'drink_back_required_casts_changed';
    end if;

    if v_status = 'confirmed' and exists (
        with required_casts as (
            select attendance.cast_id
              from public.store_cast_attendance attendance
             where attendance.department_id = p_department_id
               and attendance.business_day_id = v_business_day.business_day_id
               and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')
             group by attendance.cast_id
        )
        select 1
          from (
              select (item.value->>'cast_id')::bigint as cast_id
                from jsonb_array_elements(v_optional) item
              union all
              select (item.value#>>'{}')::bigint
                from jsonb_array_elements(v_remove) item
          ) optional_cast
          join required_casts required_cast using (cast_id)
    ) then
        v_status := 'validation_error';
        v_message := 'drink_back_optional_cast_is_attending';
    end if;

    if v_status = 'confirmed' and exists (
        select 1
          from (
              select (item.value->>'cast_id')::bigint as cast_id
                from jsonb_array_elements(v_required || v_optional) item
          ) submitted
          left join public.cast_master cast_member
            on cast_member.cast_id = submitted.cast_id
           and cast_member.department_id = p_department_id
           and cast_member.is_active = true
         where cast_member.cast_id is null
    ) then
        v_status := 'validation_error';
        v_message := 'drink_back_cast_not_active';
    end if;

    if v_status = 'confirmed' and exists (
        select 1
          from jsonb_array_elements(v_remove) item
          left join public.store_business_day_drink_back_adjustments adjustment
            on adjustment.department_id = p_department_id
           and adjustment.business_day_id = v_business_day.business_day_id
           and adjustment.cast_id = (item.value#>>'{}')::bigint
           and adjustment.status = 'active'
         where adjustment.business_day_drink_back_adjustment_id is null
    ) then
        v_status := 'validation_error';
        v_message := 'drink_back_remove_target_not_found';
    end if;

    if v_status = 'confirmed' then
        insert into public.store_business_day_drink_back_adjustments (
            business_day_id,
            business_date,
            company_id,
            department_id,
            cast_id,
            adjustment_amount,
            status,
            memo
        )
        select
            v_business_day.business_day_id,
            v_business_day.business_date,
            v_business_day.company_id,
            v_business_day.department_id,
            (item.value->>'cast_id')::bigint,
            (item.value->>'adjustment_amount')::numeric,
            'active',
            null
          from jsonb_array_elements(v_required || v_optional) item
        on conflict (business_day_id, cast_id) do update
            set business_date = excluded.business_date,
                company_id = excluded.company_id,
                department_id = excluded.department_id,
                adjustment_amount = excluded.adjustment_amount,
                status = 'active',
                memo = excluded.memo;

        update public.store_business_day_drink_back_adjustments adjustment
           set status = 'voided'
         where adjustment.department_id = p_department_id
           and adjustment.business_day_id = v_business_day.business_day_id
           and adjustment.status = 'active'
           and adjustment.cast_id in (
               select (item.value#>>'{}')::bigint
                 from jsonb_array_elements(v_remove) item
           );

        update public.store_business_days business_day
           set business_ui_revision = business_day.business_ui_revision + 1,
               updated_at = now()
         where business_day.department_id = p_department_id
           and business_day.business_day_id = v_business_day.business_day_id
           and business_day.status = 'open'
           and business_day.business_ui_revision = p_expected_business_day_revision
        returning business_day.* into v_business_day;

        if v_business_day.business_day_id is null then
            raise exception 'business_day_revision_conflict';
        end if;
    end if;

    if v_business_day.business_day_id is not null then
        v_editor := store.build_business_day_drink_back_editor(
            p_department_id,
            v_business_day.business_day_id);
        v_drink_back_status := v_editor->'status';
        v_revision := coalesce((v_editor->>'business_day_revision')::bigint, 0);
    else
        select current_editor.editor
          into v_editor
          from store.get_current_drink_back_editor(p_department_id) current_editor;
        v_drink_back_status := v_editor->'status';
    end if;

    if v_status = 'confirmed' then
        select readiness.*
          into v_readiness
          from store.get_business_day_closing_readiness_internal(
              p_department_id,
              v_business_day.business_day_id,
              'unprocessed') readiness;
        v_checked_at := coalesce(v_readiness.checked_at, clock_timestamp());
        v_dashboard_delta := jsonb_build_object(
            'department_id', p_department_id,
            'business_day_id', v_business_day.business_day_id,
            'business_day_revision', v_revision,
            'drink_back_required_cast_count', coalesce(v_readiness.drink_back_required_cast_count, 0),
            'drink_back_completed_cast_count', coalesce(v_readiness.drink_back_completed_cast_count, 0),
            'drink_back_missing_cast_count', coalesce(v_readiness.drink_back_missing_cast_count, 0),
            'drink_back_total_amount', coalesce(v_readiness.drink_back_total_amount, 0),
            'can_close', coalesce(v_readiness.can_close, false),
            'block_reasons', coalesce(v_readiness.block_reasons, '[]'::jsonb),
            'drink_back_editor', v_editor,
            'checked_at', v_checked_at);
    end if;

    v_response := jsonb_build_object(
        'status', v_status,
        'message', v_message,
        'editor', v_editor,
        'drink_back_status', v_drink_back_status,
        'dashboard_delta', v_dashboard_delta,
        'business_day_revision', v_revision);

    insert into store.current_business_day_operation_results (
        department_id,
        operation_id,
        operation_type,
        request_body,
        response
    ) values (
        p_department_id,
        v_operation_id,
        'save_drink_back_adjustments_v2',
        v_request_body,
        v_response
    );

    return query
    select
        v_status,
        v_operation_id,
        v_message,
        v_editor,
        v_drink_back_status,
        v_dashboard_delta,
        v_revision;
end;
$$;

revoke all on function store.save_drink_back_adjustments_v2(bigint, text, bigint, bigint, jsonb, jsonb, jsonb)
    from public, anon, authenticated, service_role;

commit;
