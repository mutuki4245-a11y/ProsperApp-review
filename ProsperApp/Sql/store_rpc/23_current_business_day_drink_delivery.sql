-- 納品額editorの復旧readと、営業日解決を含むidempotent mutationです。

begin;


drop function if exists store.get_current_drink_delivery_editor(bigint);

create or replace function store.get_current_drink_delivery_editor(
    p_department_id bigint
)
returns table (
    department_id bigint,
    has_business_day boolean,
    business_day_id bigint,
    business_day_revision bigint,
    business_date date,
    drink_delivery_amount numeric,
    is_entered boolean,
    checked_at timestamp with time zone
)
language sql
security definer
set search_path = pg_catalog, public
as $$
    select
        p_department_id,
        business_day.business_day_id is not null,
        business_day.business_day_id,
        coalesce(business_day.business_ui_revision, 0),
        business_day.business_date,
        coalesce(business_day.drink_delivery_amount, 0),
        coalesce(business_day.drink_delivery_amount_entered, false),
        clock_timestamp()
    from (values (1)) seed(value)
    left join lateral (
        select current_day.*
        from public.store_business_days current_day
        where current_day.department_id = p_department_id
          and current_day.status = 'open'
        order by current_day.opened_at desc
        limit 1
    ) business_day on true;
$$;

revoke all on function store.get_current_drink_delivery_editor(bigint)
    from public, anon, authenticated, service_role;

drop function if exists store.save_current_business_day_drink_delivery_amount_v2(bigint, bigint, date, numeric);
drop function if exists store.save_current_business_day_drink_delivery_amount_v2(bigint, text, bigint, date, numeric);
drop function if exists store.save_current_business_day_drink_delivery_amount_v2(bigint, text, bigint, bigint, date, numeric);

create or replace function store.save_current_business_day_drink_delivery_amount_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint default null,
    p_expected_business_day_revision bigint default null,
    p_business_date date default null,
    p_drink_delivery_amount numeric default null
)
returns table (
    status text,
    operation_id text,
    message text,
    editor jsonb,
    dashboard_delta jsonb,
    business_day jsonb
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_request_body jsonb;
    v_existing_request_body jsonb;
    v_response jsonb;
    v_editor jsonb;
    v_dashboard_delta jsonb;
    v_business_day_json jsonb;
    v_status text := 'confirmed';
    v_message text;
    v_checked_at timestamp with time zone;
begin
    if p_department_id <= 0 or
       v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' then
        raise exception 'invalid_drink_delivery_input';
    end if;

    v_request_body := jsonb_build_object(
        'operation_type', 'save_current_business_day_drink_delivery_amount_v2',
        'expected_business_day_id', p_expected_business_day_id,
        'expected_business_day_revision', p_expected_business_day_revision,
        'business_date', p_business_date,
        'drink_delivery_amount', p_drink_delivery_amount
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

        return query
        select
            v_response->>'status',
            v_operation_id,
            v_response->>'message',
            v_response->'editor',
            v_response->'dashboard_delta',
            v_response->'business_day';
        return;
    end if;

    if p_business_date is null or
       p_drink_delivery_amount is null or
       p_drink_delivery_amount < 0 or
       p_drink_delivery_amount > 999999999999 or
       p_drink_delivery_amount <> trunc(p_drink_delivery_amount) then
        v_status := 'validation_error';
        v_message := 'invalid_drink_delivery_amount';
    else
        select current_day.*
          into v_business_day
          from public.store_business_days current_day
         where current_day.department_id = p_department_id
           and current_day.status = 'open'
         order by current_day.opened_at desc
         limit 1
         for update;

        if v_business_day.business_day_id is null then
            if p_expected_business_day_id is not null or
               p_expected_business_day_revision is not null then
                v_status := 'conflict';
                v_message := 'business_day_revision_conflict';
            else
                perform 1
                  from store.open_business_day_internal(p_department_id, p_business_date, null);

                select opened_day.*
                  into v_business_day
                  from public.store_business_days opened_day
                 where opened_day.department_id = p_department_id
                   and opened_day.status = 'open'
                 order by opened_day.opened_at desc
                 limit 1
                 for update;
            end if;
        elsif p_expected_business_day_id is null or
              p_expected_business_day_revision is null or
              p_expected_business_day_id <> v_business_day.business_day_id or
              p_expected_business_day_revision <> v_business_day.business_ui_revision then
            v_status := 'conflict';
            v_message := 'business_day_revision_conflict';
        end if;

        if v_status = 'confirmed' then
            update public.store_business_days target_day
               set drink_delivery_amount = p_drink_delivery_amount,
                   drink_delivery_amount_entered = true,
                   business_ui_revision = target_day.business_ui_revision + 1,
                   updated_at = now()
             where target_day.business_day_id = v_business_day.business_day_id
               and target_day.department_id = p_department_id
               and target_day.status = 'open'
            returning target_day.* into v_business_day;

            if v_business_day.business_day_id is null then
                v_status := 'conflict';
                v_message := 'business_day_revision_conflict';
            else
                v_business_day_json := to_jsonb(v_business_day);
            end if;
        end if;
    end if;

    select to_jsonb(editor_state), editor_state.checked_at
      into v_editor, v_checked_at
      from store.get_current_drink_delivery_editor(p_department_id) editor_state;

    if v_status = 'confirmed' then
        v_dashboard_delta := jsonb_build_object(
            'department_id', p_department_id,
            'business_day_id', v_business_day.business_day_id,
            'business_day_revision', v_business_day.business_ui_revision,
            'drink_delivery_amount', v_business_day.drink_delivery_amount,
            'is_drink_delivery_amount_entered', true,
            'checked_at', v_checked_at
        );
    end if;

    v_response := jsonb_build_object(
        'status', v_status,
        'message', v_message,
        'editor', v_editor,
        'dashboard_delta', v_dashboard_delta,
        'business_day', v_business_day_json
    );

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
        'save_current_business_day_drink_delivery_amount_v2',
        v_request_body,
        v_response
    );

    return query
    select
        v_status,
        v_operation_id,
        v_message,
        v_editor,
        v_dashboard_delta,
        v_business_day_json;
end;
$$;

revoke all on function store.save_current_business_day_drink_delivery_amount_v2(bigint, text, bigint, bigint, date, numeric)
    from public, anon, authenticated, service_role;

commit;
