begin;

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

drop function if exists store.submit_current_order_entry_v2(bigint, text, bigint, bigint, jsonb);

create or replace function store.submit_current_order_entry_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint,
    p_expected_business_day_revision bigint,
    p_lines jsonb default '[]'::jsonb
)
returns table (
    status text,
    operation_id text,
    inserted_count integer,
    inserted_lines jsonb,
    business_day_id bigint,
    business_day_revision bigint,
    message text,
    recovery_candidates jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_business_day public.store_business_days%rowtype;
    v_line jsonb;
    v_client_line_id text;
    v_slip_id bigint;
    v_before_order_line_id bigint;
    v_order_line_id bigint;
    v_inserted_count integer;
    v_inserted_lines jsonb := '[]'::jsonb;
    v_business_day_id bigint;
    v_revision bigint := 0;
    v_message text := '注文を登録しました。';
    v_status text := 'confirmed';
    v_recovery_candidates jsonb;
    v_request_body jsonb;
    v_existing_request jsonb;
    v_response jsonb;
    v_error_message text;
begin
    if p_department_id <= 0 or p_expected_business_day_id <= 0 or p_expected_business_day_revision is null or
       v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' or
       jsonb_typeof(p_lines) <> 'array' or jsonb_array_length(p_lines) not between 1 and 200 then
        raise exception 'invalid_order_entry_batch';
    end if;

    if exists (
        select 1
          from jsonb_array_elements(p_lines) line
         where jsonb_typeof(line.value) <> 'object' or
               coalesce(line.value->>'client_line_id', '') !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' or
               coalesce(line.value->>'slip_id', '') !~ '^[1-9][0-9]*$' or
               coalesce(line.value->>'item_id', '') !~ '^[1-9][0-9]*$' or
               coalesce(line.value->>'quantity', '') !~ '^[1-9][0-9]*$'
    ) or (
        select count(*) <> count(distinct line.value->>'client_line_id')
          from jsonb_array_elements(p_lines) line
    ) then
        raise exception 'invalid_order_entry_lines';
    end if;

    v_request_body := jsonb_build_object(
        'expected_business_day_id', p_expected_business_day_id,
        'expected_business_day_revision', p_expected_business_day_revision,
        'lines', p_lines
    );

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_business_day_state:%s', p_department_id),
        0));

    delete from store.current_business_day_operation_results
     where created_at < now() - interval '30 days';

    select operation.request_body, operation.response
      into v_existing_request, v_response
      from store.current_business_day_operation_results operation
     where operation.department_id = p_department_id
       and operation.operation_id = v_operation_id;
    if found then
        if v_existing_request <> v_request_body then
            raise exception 'business_day_operation_id_reused';
        end if;
        return query select
            v_response->>'status',
            v_operation_id,
            coalesce(nullif(v_response->>'inserted_count', '')::integer, 0),
            coalesce(v_response->'inserted_lines', '[]'::jsonb),
            nullif(v_response->>'business_day_id', '')::bigint,
            coalesce(nullif(v_response->>'business_day_revision', '')::bigint, 0),
            v_response->>'message',
            v_response->'recovery_candidates';
        return;
    end if;

    -- 全行をsubtransactionで確定し、1行でも失敗すれば全行をrollbackする。
    begin
        select current_day.*
          into v_business_day
          from store.get_current_business_day_internal(p_department_id) current_day;
        if v_business_day.business_day_id is null or
           v_business_day.business_day_id <> p_expected_business_day_id or
           v_business_day.business_ui_revision <> p_expected_business_day_revision then
            raise exception 'business_day_revision_conflict';
        end if;

        perform 1
          from public.store_slips slip
         where slip.department_id = p_department_id
           and slip.business_day_id = v_business_day.business_day_id
           and slip.status = 'open'
           and slip.slip_id in (
               select (line.value->>'slip_id')::bigint
                 from jsonb_array_elements(p_lines) line
           )
         order by slip.slip_id
         for update;

        if (select count(distinct (line.value->>'slip_id')::bigint) from jsonb_array_elements(p_lines) line) <>
           (select count(distinct slip.slip_id)
              from public.store_slips slip
             where slip.department_id = p_department_id
               and slip.business_day_id = v_business_day.business_day_id
               and slip.status = 'open'
               and slip.slip_id in (select (line.value->>'slip_id')::bigint from jsonb_array_elements(p_lines) line)) then
            raise exception 'store_order_slip_not_found';
        end if;

        for v_line in
            select line.value
              from jsonb_array_elements(p_lines) line
             order by (line.value->>'slip_id')::bigint, line.value->>'client_line_id'
        loop
            v_client_line_id := lower(v_line->>'client_line_id');
            v_slip_id := (v_line->>'slip_id')::bigint;
            select coalesce(max(order_line.order_line_id), 0)
              into v_before_order_line_id
              from public.store_order_lines order_line
             where order_line.slip_id = v_slip_id;

            select added.inserted_count
              into v_inserted_count
              from store.add_order_lines_internal(
                  p_department_id,
                  v_slip_id,
                  jsonb_build_array(jsonb_build_object(
                      'slip_id', v_slip_id,
                      'item_id', (v_line->>'item_id')::bigint,
                      'quantity', (v_line->>'quantity')::integer,
                      'cast_back_cast_id', nullif(v_line->>'cast_back_cast_id', '')::bigint
                  ))) added;
            if coalesce(v_inserted_count, 0) <> 1 then
                raise exception 'order_entry_insert_failed';
            end if;

            select max(order_line.order_line_id)
              into v_order_line_id
              from public.store_order_lines order_line
             where order_line.slip_id = v_slip_id
               and order_line.order_line_id > v_before_order_line_id;
            if v_order_line_id is null then
                raise exception 'order_entry_insert_result_missing';
            end if;

            v_inserted_lines := v_inserted_lines || jsonb_build_array(jsonb_build_object(
                'client_line_id', v_client_line_id,
                'order_line_id', v_order_line_id,
                'slip_id', v_slip_id
            ));
        end loop;

        select business_day.business_day_id, business_day.business_ui_revision
          into v_business_day_id, v_revision
          from public.store_business_days business_day
         where business_day.department_id = p_department_id
           and business_day.business_day_id = v_business_day.business_day_id;
        v_inserted_count := jsonb_array_length(v_inserted_lines);
        v_message := format('注文を登録しました。登録行数: %s', v_inserted_count);
    exception when others then
        get stacked diagnostics v_error_message = message_text;
        v_status := case
            when v_error_message like '%revision_conflict%' or
                 v_error_message like '%slip_not_found%' or
                 v_error_message like '%not_open%'
                then 'conflict'
            else 'validation_error'
        end;
        v_inserted_count := 0;
        v_inserted_lines := '[]'::jsonb;
        v_message := coalesce(v_error_message, 'order_entry_submit_failed');

        select current_day.business_day_id, current_day.business_ui_revision
          into v_business_day_id, v_revision
          from store.get_current_business_day_internal(p_department_id) current_day;
        select to_jsonb(candidate_row)
          into v_recovery_candidates
          from store.get_current_order_entry_candidates(p_department_id) candidate_row;
    end;

    v_response := jsonb_build_object(
        'status', v_status,
        'inserted_count', v_inserted_count,
        'inserted_lines', v_inserted_lines,
        'business_day_id', v_business_day_id,
        'business_day_revision', v_revision,
        'message', v_message,
        'recovery_candidates', v_recovery_candidates
    );

    insert into store.current_business_day_operation_results (
        department_id, operation_id, operation_type, request_body, response
    ) values (
        p_department_id, v_operation_id, 'submit_current_order_entry_v2', v_request_body, v_response
    );

    return query select
        v_status,
        v_operation_id,
        v_inserted_count,
        v_inserted_lines,
        v_business_day_id,
        v_revision,
        v_message,
        v_recovery_candidates;
end;
$$;

revoke all on function store.submit_current_order_entry_v2(bigint, text, bigint, bigint, jsonb)
    from public, anon, authenticated, service_role;

commit;
