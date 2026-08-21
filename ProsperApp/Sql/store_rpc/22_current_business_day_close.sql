-- 営業日締めは、事前readなしで最終検証、close、post-close dashboardを1 RPCで返します。

begin;


drop function if exists store.close_current_business_day(bigint, bigint, text, text, boolean);
drop function if exists store.close_current_business_day(bigint, text, bigint, text, text, boolean);
drop function if exists store.close_current_business_day(bigint, text, bigint, bigint, text, text, boolean);
drop function if exists store.close_business_day_v2(bigint, text, bigint, bigint, text, boolean);

create or replace function store.close_business_day_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint default null,
    p_expected_business_day_revision bigint default null,
    p_memo text default null,
    p_ignore_closing_requirements boolean default false
)
returns table (
    operation_id text,
    status text,
    message text,
    closed_business_day_id bigint,
    business_date date,
    closed_at timestamp with time zone,
    report_business_day_id bigint,
    dashboard jsonb
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_closed_day public.store_business_days%rowtype;
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_request_body jsonb;
    v_existing_request_body jsonb;
    v_response jsonb;
    v_dashboard jsonb;
    v_readiness record;
    v_status text;
    v_message text;
begin
    if p_department_id <= 0 or
       v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' then
        raise exception 'invalid_business_day_operation';
    end if;

    v_request_body := jsonb_build_object(
        'operation_type', 'close_business_day_v2',
        'expected_business_day_id', p_expected_business_day_id,
        'expected_business_day_revision', p_expected_business_day_revision,
        'memo', nullif(trim(coalesce(p_memo, '')), ''),
        'ignore_closing_requirements', coalesce(p_ignore_closing_requirements, false)
    );

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_business_day_state:%s', p_department_id),
        0));

    select result.request_body, result.response
      into v_existing_request_body, v_response
      from store.current_business_day_operation_results result
     where result.department_id = p_department_id
       and result.operation_id = v_operation_id;

    if found then
        if v_existing_request_body <> v_request_body then
            raise exception 'business_day_operation_id_reused';
        end if;

        return query
        select
            v_response->>'operation_id',
            v_response->>'status',
            v_response->>'message',
            nullif(v_response->>'closed_business_day_id', '')::bigint,
            nullif(v_response->>'business_date', '')::date,
            nullif(v_response->>'closed_at', '')::timestamp with time zone,
            nullif(v_response->>'report_business_day_id', '')::bigint,
            v_response->'dashboard';
        return;
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
        select to_jsonb(current_dashboard)
          into v_dashboard
          from store.get_current_closing_dashboard(p_department_id, null) current_dashboard;
        v_status := 'conflict';
        v_message := '営業日情報が更新されました。最新の締め状況を確認してください。';
    else
        select readiness.*
          into v_readiness
          from store.get_business_day_closing_readiness_internal(
              p_department_id,
              v_business_day.business_day_id,
              null) readiness;

        if coalesce(p_ignore_closing_requirements, false) = false and
           coalesce(v_readiness.can_close, false) = false then
            select to_jsonb(current_dashboard)
              into v_dashboard
              from store.get_current_closing_dashboard(p_department_id, null) current_dashboard;
            v_status := 'validation_error';
            select string_agg(reason.value, ' ')
              into v_message
              from jsonb_array_elements_text(coalesce(v_dashboard->'block_reasons', '[]'::jsonb)) reason(value);
            v_message := coalesce(nullif(v_message, ''), '締め条件を満たしていません。');
        else
            perform 1
              from store.close_business_day_internal(
                  p_department_id,
                  v_business_day.business_day_id,
                  p_memo,
                  null,
                  true);

            update public.store_business_days business_day
               set business_ui_revision = business_day.business_ui_revision + 1
             where business_day.business_day_id = v_business_day.business_day_id
               and business_day.department_id = p_department_id
               and business_day.status = 'closed'
            returning business_day.* into v_closed_day;

            if v_closed_day.business_day_id is null then
                raise exception 'business_day_revision_conflict';
            end if;

            select to_jsonb(current_dashboard)
              into v_dashboard
              from store.get_current_closing_dashboard(p_department_id, null) current_dashboard;
            v_status := 'confirmed';
            v_message := format('営業日 %s を締めました。', v_closed_day.business_date);
        end if;
    end if;

    v_response := jsonb_build_object(
        'operation_id', v_operation_id,
        'status', v_status,
        'message', v_message,
        'closed_business_day_id', v_closed_day.business_day_id,
        'business_date', v_closed_day.business_date,
        'closed_at', v_closed_day.closed_at,
        'report_business_day_id', v_closed_day.business_day_id,
        'dashboard', coalesce(v_dashboard, '{}'::jsonb)
    );

    insert into store.current_business_day_operation_results (
        department_id,
        operation_id,
        operation_type,
        request_body,
        response
    ) values (
        p_department_id,
        v_operation_id,
        'close_business_day_v2',
        v_request_body,
        v_response
    );

    return query
    select
        v_operation_id,
        v_status,
        v_message,
        v_closed_day.business_day_id,
        v_closed_day.business_date,
        v_closed_day.closed_at,
        v_closed_day.business_day_id,
        v_dashboard;
end;
$$;

revoke all on function store.close_business_day_v2(bigint, text, bigint, bigint, text, boolean)
    from public, anon, authenticated, service_role;

commit;
