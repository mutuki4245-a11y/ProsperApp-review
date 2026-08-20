-- キャスト売上額調整のcurrent overviewと、version検証付きidempotent mutationです。

begin;


drop function if exists store.get_business_day_cast_sales_adjustment_overview(bigint, bigint);
drop function if exists store.save_business_day_cast_sales_adjustments(bigint, bigint, jsonb);
drop function if exists store.save_cast_sales_adjustment(bigint, bigint, jsonb, text, text);

drop function if exists store.get_current_cast_sales_adjustment_overview(bigint);

create or replace function store.get_current_cast_sales_adjustment_overview(
    p_department_id bigint
)
returns table (
    department_id bigint,
    has_business_day boolean,
    business_day_id bigint,
    business_day_revision bigint,
    business_date date,
    source_amount_type text,
    split_mode text,
    status jsonb,
    slips jsonb,
    details jsonb,
    checked_at timestamp with time zone
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_source_amount_type text;
    v_split_mode text;
    v_status jsonb := jsonb_build_object(
        'required_slip_count', 0,
        'completed_slip_count', 0,
        'missing_slip_count', 0);
    v_slips jsonb := '[]'::jsonb;
    v_details jsonb := '[]'::jsonb;
    v_checked_at timestamp with time zone := clock_timestamp();
begin
    if p_department_id <= 0 then
        raise exception 'invalid_cast_sales_adjustment_overview_input';
    end if;

    select
        case when department.cast_sales_amount_basis in ('subtotal', 'total')
            then department.cast_sales_amount_basis else 'total' end,
        case when department.cast_sales_split_mode in ('split', 'full')
            then department.cast_sales_split_mode else 'split' end
      into v_source_amount_type, v_split_mode
      from public.department_master department
     where department.department_id = p_department_id;

    if not found then
        raise exception 'invalid_cast_sales_adjustment_department';
    end if;

    select current_day.*
      into v_business_day
      from public.store_business_days current_day
     where current_day.department_id = p_department_id
       and current_day.status = 'open'
     order by current_day.opened_at desc
     limit 1;

    if v_business_day.business_day_id is not null then
        select to_jsonb(current_status)
          into v_status
          from store.get_business_day_cast_sales_adjustment_status_internal(
              p_department_id,
              v_business_day.business_day_id) current_status;

        select coalesce(
            jsonb_agg(
                to_jsonb(target_slip)
                || jsonb_build_object(
                    'slip_version', slip.updated_at,
                    'checkout_version', checkout.updated_at)
                order by target_slip.checkout_at, target_slip.slip_id),
            '[]'::jsonb)
          into v_slips
          from store.get_cast_sales_adjustment_slips(
              p_department_id,
              v_business_day.business_day_id) target_slip
          join public.store_slips slip
            on slip.slip_id = target_slip.slip_id
          join public.store_checkouts checkout
            on checkout.checkout_id = target_slip.checkout_id;

        select coalesce(
            jsonb_agg(
                to_jsonb(detail_row)
                || jsonb_build_object(
                    'slip_version', slip.updated_at,
                    'checkout_version', checkout.updated_at)
                order by detail_row.checkout_at,
                         detail_row.slip_id,
                         detail_row.started_at nulls last,
                         detail_row.slip_cast_id),
            '[]'::jsonb)
          into v_details
          from store.get_cast_sales_adjustment_slips(
              p_department_id,
              v_business_day.business_day_id) target_slip
          cross join lateral store.get_cast_sales_adjustment_detail(
              p_department_id,
              target_slip.slip_id) detail_row
          join public.store_slips slip
            on slip.slip_id = detail_row.slip_id
          join public.store_checkouts checkout
            on checkout.checkout_id = detail_row.checkout_id;
    end if;

    return query
    select
        p_department_id,
        v_business_day.business_day_id is not null,
        v_business_day.business_day_id,
        coalesce(v_business_day.business_ui_revision, 0),
        v_business_day.business_date,
        v_source_amount_type,
        v_split_mode,
        coalesce(v_status, jsonb_build_object(
            'required_slip_count', 0,
            'completed_slip_count', 0,
            'missing_slip_count', 0)),
        coalesce(v_slips, '[]'::jsonb),
        coalesce(v_details, '[]'::jsonb),
        v_checked_at;
end;
$$;

revoke all on function store.get_current_cast_sales_adjustment_overview(bigint)
    from public, anon, authenticated, service_role;

drop function if exists store.save_current_cast_sales_adjustment_v2(
    bigint, text, bigint, bigint, bigint, timestamp with time zone,
    bigint, timestamp with time zone, jsonb, text, text);

create or replace function store.save_current_cast_sales_adjustment_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint,
    p_expected_business_day_revision bigint,
    p_slip_id bigint,
    p_expected_slip_version timestamp with time zone,
    p_expected_checkout_id bigint,
    p_expected_checkout_version timestamp with time zone,
    p_adjustments jsonb default '[]'::jsonb,
    p_source_amount_type text default 'total',
    p_split_mode text default 'split'
)
returns table (
    status text,
    operation_id text,
    message text,
    business_day_id bigint,
    business_day_revision bigint,
    saved_adjustments jsonb,
    detail jsonb,
    overview_status jsonb,
    dashboard_delta jsonb,
    latest_overview jsonb
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_request_body jsonb;
    v_existing_request_body jsonb;
    v_response jsonb;
    v_business_day public.store_business_days%rowtype;
    v_slip public.store_slips%rowtype;
    v_checkout public.store_checkouts%rowtype;
    v_master_source_amount_type text;
    v_master_split_mode text;
    v_overview jsonb;
    v_saved_adjustments jsonb := '[]'::jsonb;
    v_detail jsonb := '[]'::jsonb;
    v_overview_status jsonb;
    v_dashboard_delta jsonb;
    v_latest_overview jsonb;
    v_status text := 'confirmed';
    v_message text;
    v_error text;
begin
    if p_department_id <= 0 or
       v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' or
       p_expected_business_day_id <= 0 or
       p_expected_business_day_revision < 0 or
       p_slip_id <= 0 or
       p_expected_slip_version is null or
       p_expected_checkout_id <= 0 or
       p_expected_checkout_version is null then
        raise exception 'invalid_cast_sales_adjustment_v2_input';
    end if;

    v_request_body := jsonb_build_object(
        'operation_type', 'save_current_cast_sales_adjustment_v2',
        'expected_business_day_id', p_expected_business_day_id,
        'expected_business_day_revision', p_expected_business_day_revision,
        'slip_id', p_slip_id,
        'expected_slip_version', p_expected_slip_version,
        'expected_checkout_id', p_expected_checkout_id,
        'expected_checkout_version', p_expected_checkout_version,
        'adjustments', p_adjustments,
        'source_amount_type', p_source_amount_type,
        'split_mode', p_split_mode);

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_business_day_state:%s', p_department_id), 0));

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
            nullif(v_response->>'business_day_id', '')::bigint,
            coalesce(nullif(v_response->>'business_day_revision', '')::bigint, 0),
            coalesce(v_response->'saved_adjustments', '[]'::jsonb),
            coalesce(v_response->'detail', '[]'::jsonb),
            v_response->'overview_status',
            v_response->'dashboard_delta',
            v_response->'latest_overview';
        return;
    end if;

    select current_day.*
      into v_business_day
      from public.store_business_days current_day
     where current_day.department_id = p_department_id
       and current_day.status = 'open'
     order by current_day.opened_at desc
     limit 1
     for update;

    if v_business_day.business_day_id is null then
        v_status := 'conflict';
        v_message := 'business_day_not_found';
    elsif v_business_day.business_day_id <> p_expected_business_day_id or
          v_business_day.business_ui_revision <> p_expected_business_day_revision then
        v_status := 'conflict';
        v_message := 'business_day_revision_conflict';
    else
        select
            case when department.cast_sales_amount_basis in ('subtotal', 'total')
                then department.cast_sales_amount_basis else 'total' end,
            case when department.cast_sales_split_mode in ('split', 'full')
                then department.cast_sales_split_mode else 'split' end
          into v_master_source_amount_type, v_master_split_mode
          from public.department_master department
         where department.department_id = p_department_id;

        select slip.*
          into v_slip
          from public.store_slips slip
         where slip.department_id = p_department_id
           and slip.business_day_id = v_business_day.business_day_id
           and slip.slip_id = p_slip_id
           and slip.status = 'checked_out'
         for update;

        select checkout.*
          into v_checkout
          from public.store_checkouts checkout
         where checkout.department_id = p_department_id
           and checkout.slip_id = p_slip_id
           and checkout.status = 'confirmed'
         order by checkout.checkout_id desc
         limit 1
         for update;

        if v_slip.slip_id is null or
           v_checkout.checkout_id is null or
           v_slip.updated_at is distinct from p_expected_slip_version or
           v_checkout.checkout_id <> p_expected_checkout_id or
           v_checkout.updated_at is distinct from p_expected_checkout_version then
            v_status := 'conflict';
            v_message := 'cast_sales_target_conflict';
        elsif p_source_amount_type is distinct from v_master_source_amount_type or
              p_split_mode is distinct from v_master_split_mode then
            v_status := 'conflict';
            v_message := 'cast_sales_settings_conflict';
        else
            begin
                perform store.save_cast_sales_adjustment_internal(
                    p_department_id,
                    p_slip_id,
                    p_adjustments,
                    p_source_amount_type,
                    p_split_mode);
            exception when others then
                get stacked diagnostics v_error = message_text;
                if v_error in ('store_slip_not_checked_out', 'cast_sales_checkout_not_found') then
                    v_status := 'conflict';
                    v_message := v_error;
                elsif v_error in (
                    'invalid_cast_sales_adjustment_settings',
                    'invalid_cast_sales_adjustment_payload',
                    'cast_sales_adjustment_not_required'
                ) then
                    v_status := 'validation_error';
                    v_message := v_error;
                else
                    raise;
                end if;
            end;

            if v_status = 'confirmed' then
                update public.store_business_days business_day
                   set business_ui_revision = business_day.business_ui_revision + 1,
                       updated_at = now()
                 where business_day.business_day_id = v_business_day.business_day_id
                   and business_day.status = 'open'
                   and business_day.business_ui_revision = p_expected_business_day_revision
                returning business_day.* into v_business_day;

                if not found then
                    raise exception 'business_day_revision_conflict';
                end if;
            end if;
        end if;
    end if;

    select to_jsonb(current_overview)
      into v_overview
      from store.get_current_cast_sales_adjustment_overview(p_department_id) current_overview;

    if v_status = 'confirmed' then
        select coalesce(jsonb_agg(to_jsonb(adjustment) order by adjustment.slip_cast_id), '[]'::jsonb)
          into v_saved_adjustments
          from public.store_slip_cast_sales_adjustments adjustment
         where adjustment.department_id = p_department_id
           and adjustment.slip_id = p_slip_id
           and adjustment.checkout_id = p_expected_checkout_id
           and adjustment.status = 'confirmed';
    else
        v_latest_overview := v_overview;
    end if;

    select coalesce(jsonb_agg(row_value.value order by (row_value.value->>'slip_cast_id')::bigint), '[]'::jsonb)
      into v_detail
      from jsonb_array_elements(coalesce(v_overview->'details', '[]'::jsonb)) row_value(value)
     where nullif(row_value.value->>'slip_id', '')::bigint = p_slip_id;

    v_overview_status := v_overview->'status';
    if v_status = 'confirmed' then
        v_dashboard_delta := jsonb_build_object(
            'department_id', p_department_id,
            'business_day_id', v_business_day.business_day_id,
            'business_day_revision', v_business_day.business_ui_revision,
            'cast_sales_required_slip_count', coalesce((v_overview_status->>'required_slip_count')::integer, 0),
            'cast_sales_completed_slip_count', coalesce((v_overview_status->>'completed_slip_count')::integer, 0),
            'cast_sales_missing_slip_count', coalesce((v_overview_status->>'missing_slip_count')::integer, 0),
            'checked_at', v_overview->'checked_at');
    end if;

    v_response := jsonb_build_object(
        'status', v_status,
        'message', v_message,
        'business_day_id', v_overview->'business_day_id',
        'business_day_revision', coalesce((v_overview->>'business_day_revision')::bigint, 0),
        'saved_adjustments', v_saved_adjustments,
        'detail', v_detail,
        'overview_status', v_overview_status,
        'dashboard_delta', v_dashboard_delta,
        'latest_overview', v_latest_overview);

    insert into store.current_business_day_operation_results (
        department_id, operation_id, operation_type, request_body, response)
    values (
        p_department_id,
        v_operation_id,
        'save_current_cast_sales_adjustment_v2',
        v_request_body,
        v_response);

    return query
    select
        v_status,
        v_operation_id,
        v_message,
        nullif(v_overview->>'business_day_id', '')::bigint,
        coalesce((v_overview->>'business_day_revision')::bigint, 0),
        v_saved_adjustments,
        v_detail,
        v_overview_status,
        v_dashboard_delta,
        v_latest_overview;
end;
$$;

revoke all on function store.save_current_cast_sales_adjustment_v2(
    bigint, text, bigint, bigint, bigint, timestamp with time zone,
    bigint, timestamp with time zone, jsonb, text, text)
    from public, anon, authenticated, service_role;

drop function if exists store.confirm_current_cast_sales_adjustments_v2(bigint, text, bigint, bigint, jsonb);

create or replace function store.confirm_current_cast_sales_adjustments_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint,
    p_expected_business_day_revision bigint,
    p_slips jsonb default '[]'::jsonb
)
returns table (
    status text,
    operation_id text,
    message text,
    business_day_id bigint,
    business_day_revision bigint,
    saved_adjustments jsonb,
    detail jsonb,
    overview_status jsonb,
    dashboard_delta jsonb,
    latest_overview jsonb
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_request_body jsonb;
    v_existing_request_body jsonb;
    v_response jsonb;
    v_business_day public.store_business_days%rowtype;
    v_slip public.store_slips%rowtype;
    v_checkout public.store_checkouts%rowtype;
    v_slip_payload jsonb;
    v_slip_id bigint;
    v_expected_slip_version timestamp with time zone;
    v_expected_checkout_id bigint;
    v_expected_checkout_version timestamp with time zone;
    v_source_amount_type text;
    v_split_mode text;
    v_master_source_amount_type text;
    v_master_split_mode text;
    v_overview jsonb;
    v_saved_adjustments jsonb := '[]'::jsonb;
    v_overview_status jsonb;
    v_dashboard_delta jsonb;
    v_latest_overview jsonb;
    v_status text := 'confirmed';
    v_message text;
    v_error text;
    v_payload_count integer;
    v_target_count integer;
begin
    if p_department_id <= 0 or
       v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' or
       p_expected_business_day_id <= 0 or
       p_expected_business_day_revision < 0 or
       jsonb_typeof(p_slips) <> 'array' or
       jsonb_array_length(p_slips) not between 1 and 100 then
        raise exception 'invalid_cast_sales_adjustment_batch';
    end if;

    if exists (
        select 1
          from jsonb_array_elements(p_slips) payload(value)
         where jsonb_typeof(payload.value) <> 'object'
            or coalesce(payload.value->>'slip_id', '') !~ '^[1-9][0-9]*$'
            or coalesce(payload.value->>'expected_checkout_id', '') !~ '^[1-9][0-9]*$'
            or jsonb_typeof(payload.value->'adjustments') <> 'array'
    ) then
        raise exception 'invalid_cast_sales_adjustment_batch';
    end if;

    select count(*), count(distinct payload.value->>'slip_id')
      into v_payload_count, v_target_count
      from jsonb_array_elements(p_slips) payload(value);
    if v_payload_count <> v_target_count then
        raise exception 'duplicate_cast_sales_adjustment_slip';
    end if;

    v_request_body := jsonb_build_object(
        'operation_type', 'confirm_current_cast_sales_adjustments_v2',
        'expected_business_day_id', p_expected_business_day_id,
        'expected_business_day_revision', p_expected_business_day_revision,
        'slips', p_slips);

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_business_day_state:%s', p_department_id), 0));

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
            nullif(v_response->>'business_day_id', '')::bigint,
            coalesce(nullif(v_response->>'business_day_revision', '')::bigint, 0),
            coalesce(v_response->'saved_adjustments', '[]'::jsonb),
            coalesce(v_response->'detail', '[]'::jsonb),
            v_response->'overview_status',
            v_response->'dashboard_delta',
            v_response->'latest_overview';
        return;
    end if;

    select current_day.*
      into v_business_day
      from public.store_business_days current_day
     where current_day.department_id = p_department_id
       and current_day.status = 'open'
     order by current_day.opened_at desc
     limit 1
     for update;

    if v_business_day.business_day_id is null then
        v_status := 'conflict';
        v_message := 'business_day_not_found';
    elsif v_business_day.business_day_id <> p_expected_business_day_id or
          v_business_day.business_ui_revision <> p_expected_business_day_revision then
        v_status := 'conflict';
        v_message := 'business_day_revision_conflict';
    else
        select
            case when department.cast_sales_amount_basis in ('subtotal', 'total')
                then department.cast_sales_amount_basis else 'total' end,
            case when department.cast_sales_split_mode in ('split', 'full')
                then department.cast_sales_split_mode else 'split' end
          into v_master_source_amount_type, v_master_split_mode
          from public.department_master department
         where department.department_id = p_department_id;

        select count(*)::integer
          into v_target_count
          from store.get_cast_sales_adjustment_slips(
              p_department_id,
              v_business_day.business_day_id) target_slip;

        if v_target_count <> v_payload_count or exists (
            select 1
              from store.get_cast_sales_adjustment_slips(
                  p_department_id,
                  v_business_day.business_day_id) target_slip
             where not exists (
                 select 1
                   from jsonb_array_elements(p_slips) payload(value)
                  where (payload.value->>'slip_id')::bigint = target_slip.slip_id)
        ) then
            v_status := 'conflict';
            v_message := 'cast_sales_target_conflict';
        else
            begin
                for v_slip_payload in
                    select payload.value
                      from jsonb_array_elements(p_slips) payload(value)
                     order by (payload.value->>'slip_id')::bigint
                loop
                    v_slip_id := (v_slip_payload->>'slip_id')::bigint;
                    v_expected_slip_version := (v_slip_payload->>'expected_slip_version')::timestamp with time zone;
                    v_expected_checkout_id := (v_slip_payload->>'expected_checkout_id')::bigint;
                    v_expected_checkout_version := (v_slip_payload->>'expected_checkout_version')::timestamp with time zone;
                    v_source_amount_type := v_slip_payload->>'source_amount_type';
                    v_split_mode := v_slip_payload->>'split_mode';

                    select slip.*
                      into v_slip
                      from public.store_slips slip
                     where slip.department_id = p_department_id
                       and slip.business_day_id = v_business_day.business_day_id
                       and slip.slip_id = v_slip_id
                       and slip.status = 'checked_out'
                     for update;

                    select checkout.*
                      into v_checkout
                      from public.store_checkouts checkout
                     where checkout.department_id = p_department_id
                       and checkout.slip_id = v_slip_id
                       and checkout.status = 'confirmed'
                     order by checkout.checkout_id desc
                     limit 1
                     for update;

                    if v_slip.slip_id is null or
                       v_checkout.checkout_id is null or
                       v_slip.updated_at is distinct from v_expected_slip_version or
                       v_checkout.checkout_id <> v_expected_checkout_id or
                       v_checkout.updated_at is distinct from v_expected_checkout_version then
                        raise exception 'cast_sales_target_conflict';
                    end if;

                    if v_source_amount_type is distinct from v_master_source_amount_type or
                       v_split_mode is distinct from v_master_split_mode then
                        raise exception 'cast_sales_settings_conflict';
                    end if;

                    perform store.save_cast_sales_adjustment_internal(
                        p_department_id,
                        v_slip_id,
                        coalesce(v_slip_payload->'adjustments', '[]'::jsonb),
                        v_source_amount_type,
                        v_split_mode);
                end loop;
            exception when others then
                get stacked diagnostics v_error = message_text;
                if v_error in ('cast_sales_target_conflict', 'cast_sales_settings_conflict') then
                    v_status := 'conflict';
                    v_message := v_error;
                elsif v_error in (
                    'invalid_cast_sales_adjustment_settings',
                    'invalid_cast_sales_adjustment_payload',
                    'cast_sales_adjustment_not_required'
                ) then
                    v_status := 'validation_error';
                    v_message := v_error;
                else
                    raise;
                end if;
            end;

            if v_status = 'confirmed' then
                update public.store_business_days business_day
                   set business_ui_revision = business_day.business_ui_revision + 1,
                       updated_at = now()
                 where business_day.business_day_id = v_business_day.business_day_id
                   and business_day.status = 'open'
                   and business_day.business_ui_revision = p_expected_business_day_revision
                returning business_day.* into v_business_day;

                if not found then
                    raise exception 'business_day_revision_conflict';
                end if;
            end if;
        end if;
    end if;

    select to_jsonb(current_overview)
      into v_overview
      from store.get_current_cast_sales_adjustment_overview(p_department_id) current_overview;

    if v_status = 'confirmed' then
        select coalesce(jsonb_agg(to_jsonb(adjustment) order by adjustment.slip_id, adjustment.slip_cast_id), '[]'::jsonb)
          into v_saved_adjustments
          from public.store_slip_cast_sales_adjustments adjustment
         where adjustment.department_id = p_department_id
           and adjustment.business_day_id = p_expected_business_day_id
           and adjustment.status = 'confirmed'
           and exists (
               select 1
                 from jsonb_array_elements(p_slips) payload(value)
                where (payload.value->>'slip_id')::bigint = adjustment.slip_id);
    else
        v_latest_overview := v_overview;
    end if;

    v_overview_status := v_overview->'status';
    if v_status = 'confirmed' then
        v_dashboard_delta := jsonb_build_object(
            'department_id', p_department_id,
            'business_day_id', v_business_day.business_day_id,
            'business_day_revision', v_business_day.business_ui_revision,
            'cast_sales_required_slip_count', coalesce((v_overview_status->>'required_slip_count')::integer, 0),
            'cast_sales_completed_slip_count', coalesce((v_overview_status->>'completed_slip_count')::integer, 0),
            'cast_sales_missing_slip_count', coalesce((v_overview_status->>'missing_slip_count')::integer, 0),
            'checked_at', v_overview->'checked_at');
    end if;

    v_response := jsonb_build_object(
        'status', v_status,
        'message', v_message,
        'business_day_id', v_overview->'business_day_id',
        'business_day_revision', coalesce((v_overview->>'business_day_revision')::bigint, 0),
        'saved_adjustments', v_saved_adjustments,
        'detail', '[]'::jsonb,
        'overview_status', v_overview_status,
        'dashboard_delta', v_dashboard_delta,
        'latest_overview', v_latest_overview);

    insert into store.current_business_day_operation_results (
        department_id, operation_id, operation_type, request_body, response)
    values (
        p_department_id,
        v_operation_id,
        'confirm_current_cast_sales_adjustments_v2',
        v_request_body,
        v_response);

    return query
    select
        v_status,
        v_operation_id,
        v_message,
        nullif(v_overview->>'business_day_id', '')::bigint,
        coalesce((v_overview->>'business_day_revision')::bigint, 0),
        v_saved_adjustments,
        '[]'::jsonb,
        v_overview_status,
        v_dashboard_delta,
        v_latest_overview;
end;
$$;

revoke all on function store.confirm_current_cast_sales_adjustments_v2(bigint, text, bigint, bigint, jsonb)
    from public, anon, authenticated, service_role;

commit;
