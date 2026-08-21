begin;


create index if not exists current_business_day_operation_results_created_at_idx
    on store.current_business_day_operation_results (created_at);

revoke all on table store.current_business_day_operation_results from public, anon, authenticated, service_role;

drop function if exists store.issue_checkout_statement_v2(bigint, text, bigint, bigint, bigint, timestamp with time zone);
drop function if exists store.release_checkout_ready_v2(bigint, text, bigint, bigint, bigint);
drop function if exists store.confirm_checkout_v2(bigint, text, bigint, bigint, bigint, jsonb, numeric);
drop function if exists store.cancel_checkout_v2(bigint, text, bigint, bigint, bigint);
drop function if exists store.apply_checkout_mutation_v2(text, bigint, text, bigint, bigint, bigint, timestamp with time zone, jsonb, numeric);
drop function if exists store.confirm_checkout_draft_v2(bigint, bigint, jsonb, numeric);
drop function if exists store.checkout_mutation_result_v2(text, text, text, bigint, bigint, bigint, bigint, text, jsonb, jsonb, jsonb, numeric, jsonb);
drop function if exists store.remember_checkout_mutation_v2(bigint, text, text, jsonb, jsonb);

create or replace function store.checkout_mutation_result_v2(
    p_operation_id text,
    p_status text,
    p_error_code text,
    p_slip_id bigint,
    p_checkout_id bigint,
    p_business_day_id bigint,
    p_business_day_revision bigint,
    p_current_slip_status text,
    p_statement_print_data jsonb,
    p_statement_review_data jsonb,
    p_receipt_print_data jsonb,
    p_change_amount numeric,
    p_business_snapshot jsonb
)
returns jsonb
language sql
immutable
set search_path = public
as $$
    select jsonb_build_object(
        'operation_id', p_operation_id,
        'status', p_status,
        'error_code', p_error_code,
        'slip_id', p_slip_id,
        'checkout_id', p_checkout_id,
        'business_day_id', p_business_day_id,
        'business_day_revision', coalesce(p_business_day_revision, 0),
        'current_slip_status', p_current_slip_status,
        'statement_print_data', p_statement_print_data,
        'statement_review_data', p_statement_review_data,
        'receipt_print_data', p_receipt_print_data,
        'change_amount', coalesce(p_change_amount, 0),
        'business_snapshot', p_business_snapshot
    );
$$;

create or replace function store.remember_checkout_mutation_v2(
    p_department_id bigint,
    p_operation_id text,
    p_operation_type text,
    p_request_body jsonb,
    p_response jsonb
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
begin
    insert into store.current_business_day_operation_results (
        department_id,
        operation_id,
        operation_type,
        request_body,
        response
    ) values (
        p_department_id,
        p_operation_id,
        p_operation_type,
        p_request_body,
        p_response
    );

    return p_response;
end;
$$;

create or replace function store.confirm_checkout_draft_v2(
    p_department_id bigint,
    p_slip_id bigint,
    p_payments jsonb default '[]'::jsonb,
    p_received_amount numeric default null
)
returns table (
    checkout_id bigint,
    change_amount numeric,
    receipt_print_data jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip public.store_slips%rowtype;
    v_checkout_id bigint;
    v_payment jsonb;
    v_method_code text;
    v_amount numeric(12, 0);
    v_payment_method public.payment_method_master%rowtype;
    v_statement_data jsonb;
    v_order_subtotal_amount numeric(12, 0);
    v_subtotal_amount numeric(12, 0);
    v_service_charge_amount numeric(12, 0);
    v_total_amount numeric(12, 0);
    v_payment_total numeric(12, 0) := 0;
    v_received_required_amount numeric(12, 0) := 0;
    v_received_required_method_count integer := 0;
    v_payment_count integer := 0;
    v_single_payment_method_id bigint;
    v_change_amount numeric(12, 0) := 0;
    v_issuer_snapshot jsonb;
    v_updated_slip_id bigint;
begin
    select slip.*
      into v_slip
      from public.store_slips slip
     where slip.department_id = p_department_id
       and slip.slip_id = p_slip_id
       and slip.status = 'checkout_ready'
     for update;

    if v_slip.slip_id is null or v_slip.closed_at is null then
        raise exception 'checkout_ready_not_found';
    end if;

    select snapshot.print_data
      into v_statement_data
      from public.store_slip_accounting_snapshots snapshot
     where snapshot.department_id = p_department_id
       and snapshot.slip_id = p_slip_id
       and snapshot.status = 'active'
     order by snapshot.snapshot_version desc
     limit 1;

    if v_statement_data is null then
        raise exception 'checkout_snapshot_not_found';
    end if;

    v_order_subtotal_amount := coalesce(nullif(v_statement_data->>'order_subtotal_amount', '')::numeric, 0);
    v_subtotal_amount := coalesce(nullif(v_statement_data->>'subtotal_amount', '')::numeric, 0);
    v_service_charge_amount := coalesce(nullif(v_statement_data->>'service_charge_amount', '')::numeric, 0);
    v_total_amount := coalesce(nullif(v_statement_data->>'total_amount', '')::numeric, 0);

    if v_order_subtotal_amount < 0 or
       v_subtotal_amount < 0 or
       v_service_charge_amount < 0 or
       v_total_amount < 0 then
        raise exception 'invalid_checkout_snapshot';
    end if;

    if jsonb_typeof(coalesce(p_payments, '[]'::jsonb)) <> 'array' then
        raise exception 'invalid_checkout_payment';
    end if;

    if jsonb_array_length(coalesce(p_payments, '[]'::jsonb)) > 20 or exists (
        select 1
          from jsonb_array_elements(coalesce(p_payments, '[]'::jsonb)) payment(value)
         where jsonb_typeof(payment.value) <> 'object' or
               nullif(trim(payment.value->>'method_code'), '') is null or
               coalesce(payment.value->>'amount', '') !~ '^[0-9]+([.][0-9]+)?$'
    ) then
        raise exception 'invalid_checkout_payment';
    end if;

    if exists (
        select 1
          from jsonb_array_elements(coalesce(p_payments, '[]'::jsonb)) payment(value)
         where (payment.value->>'amount')::numeric <= 0 or
               (payment.value->>'amount')::numeric > 99999999
    ) then
        raise exception 'invalid_checkout_payment';
    end if;

    if exists (
        select 1
          from jsonb_array_elements(coalesce(p_payments, '[]'::jsonb)) payment(value)
         group by lower(nullif(trim(payment.value->>'method_code'), ''))
        having count(*) > 1
    ) then
        raise exception 'duplicate_checkout_payment_method';
    end if;

    for v_payment in
        select value from jsonb_array_elements(coalesce(p_payments, '[]'::jsonb))
    loop
        v_method_code := lower(nullif(trim(v_payment->>'method_code'), ''));
        v_amount := nullif(v_payment->>'amount', '')::numeric;

        if v_method_code is null or coalesce(v_amount, 0) <= 0 then
            raise exception 'invalid_checkout_payment';
        end if;

        select method.*
          into v_payment_method
          from public.payment_method_master method
         where method.company_id = v_slip.company_id
           and method.department_id = p_department_id
           and method.payment_method_code = v_method_code
           and method.is_active = true;

        if v_payment_method.payment_method_id is null then
            raise exception 'invalid_checkout_payment';
        end if;

        v_payment_count := v_payment_count + 1;
        v_payment_total := v_payment_total + v_amount;
        if coalesce(v_payment_method.requires_received_amount, false) then
            v_received_required_amount := v_received_required_amount + v_amount;
            v_received_required_method_count := v_received_required_method_count + 1;
        end if;
        if v_payment_count = 1 then
            v_single_payment_method_id := v_payment_method.payment_method_id;
        else
            v_single_payment_method_id := null;
        end if;
    end loop;

    if (v_total_amount = 0 and v_payment_count <> 0) or
       (v_total_amount > 0 and v_payment_count = 0) or
       v_payment_total <> v_total_amount then
        raise exception 'invalid_checkout_total';
    end if;

    if v_received_required_method_count > 1 then
        raise exception 'multiple_received_amount_payment_methods';
    end if;

    if p_received_amount is not null and
       (p_received_amount < 0 or p_received_amount > 99999999) then
        raise exception 'invalid_received_amount';
    end if;

    if v_received_required_amount > 0 then
        if coalesce(p_received_amount, -1) < v_received_required_amount then
            raise exception 'invalid_received_amount';
        end if;
        v_change_amount := p_received_amount - v_received_required_amount;
    elsif p_received_amount is not null and p_received_amount <> 0 then
        raise exception 'invalid_received_amount';
    end if;

    select checkout.checkout_id
      into v_checkout_id
      from public.store_checkouts checkout
     where checkout.department_id = p_department_id
       and checkout.slip_id = p_slip_id
       and checkout.status = 'draft'
     order by checkout.checkout_id desc
     limit 1
     for update;

    if v_checkout_id is null then
        insert into public.store_checkouts (
            slip_id,
            company_id,
            department_id,
            checkout_at,
            subtotal_amount,
            service_charge_amount,
            total_amount,
            status
        ) values (
            p_slip_id,
            v_slip.company_id,
            p_department_id,
            now(),
            v_subtotal_amount,
            v_service_charge_amount,
            v_total_amount,
            'draft'
        )
        returning store_checkouts.checkout_id into v_checkout_id;
    end if;

    select jsonb_build_object(
        'schema_version', 'issuer-v1',
        'company_name', coalesce(nullif(trim(company.company_name), ''), '未設定'),
        'invoice_registration_number', coalesce(nullif(trim(company.invoice_registration_number), ''), '未設定'),
        'store_name', coalesce(nullif(trim(department.receipt_display_name), ''), '未設定'),
        'address', coalesce(nullif(trim(department.receipt_address), ''), '未設定'),
        'phone', coalesce(nullif(trim(department.receipt_phone), ''), '未設定'),
        'logo', coalesce(department.receipt_logo, '')
    )
      into v_issuer_snapshot
      from public.company_master company
      join public.department_master department
        on department.company_id = company.company_id
     where department.department_id = p_department_id
       and company.company_id = v_slip.company_id;

    update public.store_checkouts checkout
       set checkout_at = now(),
           subtotal_amount = v_subtotal_amount,
           service_charge_amount = v_service_charge_amount,
           total_amount = v_total_amount,
           payment_method_id = v_single_payment_method_id,
           received_amount = case when v_received_required_amount > 0 then p_received_amount else null end,
           change_amount = v_change_amount,
           issuer_snapshot = coalesce(v_issuer_snapshot, '{}'::jsonb),
           status = 'confirmed',
           updated_at = now()
     where checkout.checkout_id = v_checkout_id
       and checkout.status = 'draft';

    if not found then
        raise exception 'checkout_state_conflict';
    end if;

    for v_payment in
        select value from jsonb_array_elements(coalesce(p_payments, '[]'::jsonb))
    loop
        v_method_code := lower(nullif(trim(v_payment->>'method_code'), ''));
        v_amount := nullif(v_payment->>'amount', '')::numeric;
        select method.*
          into v_payment_method
          from public.payment_method_master method
         where method.company_id = v_slip.company_id
           and method.department_id = p_department_id
           and method.payment_method_code = v_method_code
           and method.is_active = true;

        insert into public.store_checkout_payments (
            checkout_id,
            slip_id,
            company_id,
            department_id,
            payment_method_id,
            payment_method_code,
            payment_method_name,
            amount,
            status
        ) values (
            v_checkout_id,
            p_slip_id,
            v_slip.company_id,
            p_department_id,
            v_payment_method.payment_method_id,
            v_payment_method.payment_method_code,
            v_payment_method.payment_method_name,
            v_amount,
            'confirmed'
        );
    end loop;

    update public.store_slips slip
       set status = 'checked_out',
           customer_count = 0,
           updated_at = now()
     where slip.department_id = p_department_id
       and slip.slip_id = p_slip_id
       and slip.status = 'checkout_ready'
    returning slip.slip_id into v_updated_slip_id;

    if v_updated_slip_id is null then
        raise exception 'checkout_state_conflict';
    end if;

    return query select
        v_checkout_id,
        v_change_amount,
        store.build_checkout_receipt_data(p_department_id, v_checkout_id);
end;
$$;

create or replace function store.apply_checkout_mutation_v2(
    p_operation_type text,
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint,
    p_expected_business_day_revision bigint,
    p_slip_id bigint,
    p_closed_at timestamp with time zone default null,
    p_payments jsonb default '[]'::jsonb,
    p_received_amount numeric default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_request_body jsonb;
    v_existing_operation_type text;
    v_existing_request jsonb;
    v_response jsonb;
    v_business_day public.store_business_days%rowtype;
    v_slip public.store_slips%rowtype;
    v_business_day_revision bigint := 0;
    v_business_snapshot jsonb;
    v_checkout_id bigint;
    v_change_amount numeric := 0;
    v_statement_print_data jsonb;
    v_statement_review_data jsonb;
    v_receipt_print_data jsonb;
    v_error_code text;
    v_result_slip_id bigint;
    v_current_slip_status text;
begin
    if p_operation_type is null or p_operation_type not in (
        'issue_checkout_statement_v2',
        'release_checkout_ready_v2',
        'confirm_checkout_v2',
        'cancel_checkout_v2'
    ) then
        raise exception 'invalid_checkout_operation_type';
    end if;

    if coalesce(p_department_id, 0) <= 0 or
       v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' then
        raise exception 'invalid_checkout_mutation_identity';
    end if;

    v_request_body := jsonb_build_object(
        'expected_business_day_id', p_expected_business_day_id,
        'expected_business_day_revision', p_expected_business_day_revision,
        'slip_id', p_slip_id,
        'closed_at', p_closed_at,
        'payments', coalesce(p_payments, '[]'::jsonb),
        'received_amount', p_received_amount
    );

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_business_day_state:%s', p_department_id),
        0));

    select operation.operation_type, operation.request_body, operation.response
      into v_existing_operation_type, v_existing_request, v_response
      from store.current_business_day_operation_results operation
     where operation.department_id = p_department_id
       and operation.operation_id = v_operation_id;

    if found then
        if v_existing_operation_type <> p_operation_type or v_existing_request <> v_request_body then
            return store.checkout_mutation_result_v2(
                v_operation_id,
                'conflict',
                'business_day_operation_id_reused',
                p_slip_id,
                null,
                null,
                0,
                null,
                null,
                null,
                null,
                0,
                null
            );
        end if;
        return v_response;
    end if;

    if p_expected_business_day_id is null or p_expected_business_day_id <= 0 or
       p_expected_business_day_revision is null or p_expected_business_day_revision < 0 or
       p_slip_id is null or p_slip_id <= 0 or
       (p_operation_type = 'issue_checkout_statement_v2' and p_closed_at is null) or
       (p_operation_type = 'confirm_checkout_v2' and jsonb_typeof(coalesce(p_payments, '[]'::jsonb)) <> 'array') then
        v_response := store.checkout_mutation_result_v2(
            v_operation_id,
            'validation_error',
            'invalid_checkout_mutation_input',
            p_slip_id,
            null,
            p_expected_business_day_id,
            coalesce(p_expected_business_day_revision, 0),
            null,
            null,
            null,
            null,
            0,
            null
        );
        return store.remember_checkout_mutation_v2(
            p_department_id,
            v_operation_id,
            p_operation_type,
            v_request_body,
            v_response
        );
    end if;

    select business_day.*
      into v_business_day
      from public.store_business_days business_day
     where business_day.department_id = p_department_id
       and business_day.status = 'open'
     order by business_day.opened_at desc, business_day.business_day_id desc
     limit 1
     for update;

    select slip.*
      into v_slip
      from public.store_slips slip
     where slip.department_id = p_department_id
       and slip.slip_id = p_slip_id
     for update;

    if v_business_day.business_day_id is null then
        v_response := store.checkout_mutation_result_v2(
            v_operation_id,
            'conflict',
            'business_day_closed',
            p_slip_id,
            null,
            coalesce(v_slip.business_day_id, p_expected_business_day_id),
            0,
            v_slip.status,
            null,
            null,
            null,
            0,
            jsonb_build_object(
                'businessDayId', null,
                'businessDayRevision', 0,
                'hasBusinessDay', false,
                'openSlipCount', 0,
                'checkedOutSlipCount', 0,
                'estimatedSalesAmount', 0,
                'slips', '[]'::jsonb
            )
        );
        return store.remember_checkout_mutation_v2(
            p_department_id,
            v_operation_id,
            p_operation_type,
            v_request_body,
            v_response
        );
    end if;

    select snapshot.business_day_revision, snapshot.snapshot
      into v_business_day_revision, v_business_snapshot
      from store.get_business_day_snapshot_internal(
          p_department_id,
          v_business_day.business_day_id
      ) snapshot;

    if v_business_day.business_day_id <> p_expected_business_day_id then
        v_response := store.checkout_mutation_result_v2(
            v_operation_id,
            'conflict',
            'business_day_revision_conflict',
            p_slip_id,
            null,
            v_business_day.business_day_id,
            v_business_day_revision,
            v_slip.status,
            null,
            null,
            null,
            0,
            v_business_snapshot
        );
        return store.remember_checkout_mutation_v2(
            p_department_id,
            v_operation_id,
            p_operation_type,
            v_request_body,
            v_response
        );
    end if;

    if v_slip.slip_id is null then
        v_response := store.checkout_mutation_result_v2(
            v_operation_id,
            'conflict',
            'checkout_target_not_found',
            p_slip_id,
            null,
            v_business_day.business_day_id,
            v_business_day_revision,
            null,
            null,
            null,
            null,
            0,
            v_business_snapshot
        );
        return store.remember_checkout_mutation_v2(
            p_department_id,
            v_operation_id,
            p_operation_type,
            v_request_body,
            v_response
        );
    end if;

    if v_slip.business_day_id <> v_business_day.business_day_id then
        v_response := store.checkout_mutation_result_v2(
            v_operation_id,
            'conflict',
            'checkout_business_day_conflict',
            p_slip_id,
            null,
            v_business_day.business_day_id,
            v_business_day_revision,
            v_slip.status,
            null,
            null,
            null,
            0,
            v_business_snapshot
        );
        return store.remember_checkout_mutation_v2(
            p_department_id,
            v_operation_id,
            p_operation_type,
            v_request_body,
            v_response
        );
    end if;

    if p_operation_type = 'issue_checkout_statement_v2' and v_slip.status <> 'open' then
        select checkout.checkout_id
          into v_checkout_id
          from public.store_checkouts checkout
         where checkout.department_id = p_department_id
           and checkout.slip_id = p_slip_id
           and checkout.status <> 'cancelled'
         order by checkout.checkout_id desc
         limit 1;
        v_error_code := case
            when v_slip.status = 'checkout_ready' then 'checkout_statement_already_issued'
            when v_slip.status = 'checked_out' then 'checkout_already_confirmed'
            else 'checkout_state_conflict'
        end;
    elsif p_operation_type = 'release_checkout_ready_v2' and v_slip.status <> 'checkout_ready' then
        v_error_code := case
            when v_slip.status = 'open' then 'checkout_ready_already_released'
            when v_slip.status = 'checked_out' then 'checkout_already_confirmed'
            else 'checkout_state_conflict'
        end;
    elsif p_operation_type = 'confirm_checkout_v2' and v_slip.status <> 'checkout_ready' then
        if v_slip.status = 'checked_out' then
            select checkout.checkout_id
              into v_checkout_id
              from public.store_checkouts checkout
             where checkout.department_id = p_department_id
               and checkout.slip_id = p_slip_id
               and checkout.status = 'confirmed'
             order by checkout.checkout_id desc
             limit 1;
        end if;
        v_error_code := case
            when v_slip.status = 'checked_out' then 'checkout_already_confirmed'
            when v_slip.status = 'open' then 'checkout_not_ready'
            else 'checkout_state_conflict'
        end;
    elsif p_operation_type = 'cancel_checkout_v2' and v_slip.status <> 'checked_out' then
        if v_slip.status = 'open' then
            select checkout.checkout_id
              into v_checkout_id
              from public.store_checkouts checkout
             where checkout.department_id = p_department_id
               and checkout.slip_id = p_slip_id
               and checkout.status = 'cancelled'
             order by checkout.checkout_id desc
             limit 1;
        end if;
        v_error_code := case
            when v_slip.status = 'open' and v_checkout_id is not null then 'checkout_already_cancelled'
            when v_slip.status = 'checkout_ready' then 'checkout_not_confirmed'
            else 'checkout_state_conflict'
        end;
    end if;

    if v_error_code is not null then
        v_response := store.checkout_mutation_result_v2(
            v_operation_id,
            'conflict',
            v_error_code,
            p_slip_id,
            v_checkout_id,
            v_business_day.business_day_id,
            v_business_day_revision,
            v_slip.status,
            null,
            null,
            null,
            0,
            v_business_snapshot
        );
        return store.remember_checkout_mutation_v2(
            p_department_id,
            v_operation_id,
            p_operation_type,
            v_request_body,
            v_response
        );
    end if;

    if v_business_day_revision <> p_expected_business_day_revision then
        v_response := store.checkout_mutation_result_v2(
            v_operation_id,
            'conflict',
            case
                when p_operation_type = 'issue_checkout_statement_v2' then 'checkout_unflushed_changes'
                else 'business_day_revision_conflict'
            end,
            p_slip_id,
            null,
            v_business_day.business_day_id,
            v_business_day_revision,
            v_slip.status,
            null,
            null,
            null,
            0,
            v_business_snapshot
        );
        return store.remember_checkout_mutation_v2(
            p_department_id,
            v_operation_id,
            p_operation_type,
            v_request_body,
            v_response
        );
    end if;

    if p_operation_type = 'issue_checkout_statement_v2' then
        if p_closed_at <= v_slip.opened_at or exists (
            select 1
              from public.store_slip_customers customer
             where customer.slip_id = p_slip_id
               and customer.status <> 'cancelled'
               and p_closed_at <= customer.entered_at
        ) then
            v_response := store.checkout_mutation_result_v2(
                v_operation_id,
                'validation_error',
                'invalid_closed_at',
                p_slip_id,
                null,
                v_business_day.business_day_id,
                v_business_day_revision,
                v_slip.status,
                null,
                null,
                null,
                0,
                v_business_snapshot
            );
            return store.remember_checkout_mutation_v2(
                p_department_id,
                v_operation_id,
                p_operation_type,
                v_request_body,
                v_response
            );
        end if;

        if exists (
            select 1
              from public.store_checkouts checkout
             where checkout.slip_id = p_slip_id
               and checkout.status <> 'cancelled'
        ) then
            v_response := store.checkout_mutation_result_v2(
                v_operation_id,
                'conflict',
                'checkout_already_exists',
                p_slip_id,
                null,
                v_business_day.business_day_id,
                v_business_day_revision,
                v_slip.status,
                null,
                null,
                null,
                0,
                v_business_snapshot
            );
            return store.remember_checkout_mutation_v2(
                p_department_id,
                v_operation_id,
                p_operation_type,
                v_request_body,
                v_response
            );
        end if;

        select issued.print_data, issued.review_data
          into v_statement_print_data, v_statement_review_data
          from store.issue_checkout_statement_internal(
              p_department_id,
              p_slip_id,
              p_closed_at
          ) issued;

        if v_statement_print_data is null or v_statement_review_data is null then
            raise exception 'checkout_statement_result_missing';
        end if;

        insert into public.store_checkouts (
            slip_id,
            company_id,
            department_id,
            checkout_at,
            subtotal_amount,
            service_charge_amount,
            total_amount,
            status
        ) values (
            p_slip_id,
            v_slip.company_id,
            p_department_id,
            now(),
            coalesce(nullif(v_statement_print_data->>'subtotal_amount', '')::numeric, 0),
            coalesce(nullif(v_statement_print_data->>'service_charge_amount', '')::numeric, 0),
            coalesce(nullif(v_statement_print_data->>'total_amount', '')::numeric, 0),
            'draft'
        )
        returning store_checkouts.checkout_id into v_checkout_id;
    elsif p_operation_type = 'release_checkout_ready_v2' then
        select checkout.checkout_id
          into v_checkout_id
          from public.store_checkouts checkout
         where checkout.department_id = p_department_id
           and checkout.slip_id = p_slip_id
           and checkout.status = 'draft'
         order by checkout.checkout_id desc
         limit 1
         for update;

        select released.slip_id
          into v_result_slip_id
          from store.release_checkout_ready_internal(
              p_department_id,
              p_slip_id
          ) released;

        if v_result_slip_id is null then
            raise exception 'checkout_release_result_missing';
        end if;

        if v_checkout_id is not null then
            update public.store_checkouts checkout
               set status = 'cancelled',
                   updated_at = now()
             where checkout.checkout_id = v_checkout_id
               and checkout.status = 'draft';
        end if;
    elsif p_operation_type = 'confirm_checkout_v2' then
        begin
            select confirmed.checkout_id, confirmed.change_amount, confirmed.receipt_print_data
              into v_checkout_id, v_change_amount, v_receipt_print_data
              from store.confirm_checkout_draft_v2(
                  p_department_id,
                  p_slip_id,
                  coalesce(p_payments, '[]'::jsonb),
                  p_received_amount
              ) confirmed;
        exception
            when others then
                if sqlerrm in (
                    'invalid_checkout_snapshot',
                    'invalid_checkout_payment',
                    'duplicate_checkout_payment_method',
                    'invalid_checkout_total',
                    'multiple_received_amount_payment_methods',
                    'invalid_received_amount'
                ) then
                    v_response := store.checkout_mutation_result_v2(
                        v_operation_id,
                        'validation_error',
                        sqlerrm,
                        p_slip_id,
                        null,
                        v_business_day.business_day_id,
                        v_business_day_revision,
                        v_slip.status,
                        null,
                        null,
                        null,
                        0,
                        v_business_snapshot
                    );
                    return store.remember_checkout_mutation_v2(
                        p_department_id,
                        v_operation_id,
                        p_operation_type,
                        v_request_body,
                        v_response
                    );
                end if;
                raise;
        end;

        if v_checkout_id is null or v_receipt_print_data is null then
            raise exception 'checkout_confirmation_result_missing';
        end if;
    elsif p_operation_type = 'cancel_checkout_v2' then
        select cancelled.checkout_id
          into v_checkout_id
          from store.cancel_checkout_internal(
              p_department_id,
              p_slip_id
          ) cancelled;

        if v_checkout_id is null then
            raise exception 'checkout_cancellation_result_missing';
        end if;
    end if;

    select snapshot.business_day_revision, snapshot.snapshot
      into v_business_day_revision, v_business_snapshot
      from store.get_business_day_snapshot_internal(
          p_department_id,
          v_business_day.business_day_id
      ) snapshot;

    select slip.status
      into v_current_slip_status
      from public.store_slips slip
     where slip.department_id = p_department_id
       and slip.slip_id = p_slip_id;

    v_response := store.checkout_mutation_result_v2(
        v_operation_id,
        'confirmed',
        null,
        p_slip_id,
        v_checkout_id,
        v_business_day.business_day_id,
        v_business_day_revision,
        v_current_slip_status,
        v_statement_print_data,
        v_statement_review_data,
        v_receipt_print_data,
        v_change_amount,
        v_business_snapshot
    );

    return store.remember_checkout_mutation_v2(
        p_department_id,
        v_operation_id,
        p_operation_type,
        v_request_body,
        v_response
    );
end;
$$;

create or replace function store.issue_checkout_statement_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint,
    p_expected_business_day_revision bigint,
    p_slip_id bigint,
    p_closed_at timestamp with time zone
)
returns table (
    operation_id text,
    status text,
    error_code text,
    slip_id bigint,
    checkout_id bigint,
    business_day_id bigint,
    business_day_revision bigint,
    current_slip_status text,
    statement_print_data jsonb,
    statement_review_data jsonb,
    receipt_print_data jsonb,
    change_amount numeric,
    business_snapshot jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_result jsonb;
begin
    v_result := store.apply_checkout_mutation_v2(
        'issue_checkout_statement_v2',
        p_department_id,
        p_operation_id,
        p_expected_business_day_id,
        p_expected_business_day_revision,
        p_slip_id,
        p_closed_at,
        '[]'::jsonb,
        null
    );

    return query select
        v_result->>'operation_id',
        v_result->>'status',
        v_result->>'error_code',
        nullif(v_result->>'slip_id', '')::bigint,
        nullif(v_result->>'checkout_id', '')::bigint,
        nullif(v_result->>'business_day_id', '')::bigint,
        coalesce(nullif(v_result->>'business_day_revision', '')::bigint, 0),
        v_result->>'current_slip_status',
        v_result->'statement_print_data',
        v_result->'statement_review_data',
        v_result->'receipt_print_data',
        coalesce(nullif(v_result->>'change_amount', '')::numeric, 0),
        v_result->'business_snapshot';
end;
$$;

create or replace function store.release_checkout_ready_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint,
    p_expected_business_day_revision bigint,
    p_slip_id bigint
)
returns table (
    operation_id text,
    status text,
    error_code text,
    slip_id bigint,
    checkout_id bigint,
    business_day_id bigint,
    business_day_revision bigint,
    current_slip_status text,
    statement_print_data jsonb,
    statement_review_data jsonb,
    receipt_print_data jsonb,
    change_amount numeric,
    business_snapshot jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_result jsonb;
begin
    v_result := store.apply_checkout_mutation_v2(
        'release_checkout_ready_v2',
        p_department_id,
        p_operation_id,
        p_expected_business_day_id,
        p_expected_business_day_revision,
        p_slip_id,
        null,
        '[]'::jsonb,
        null
    );

    return query select
        v_result->>'operation_id',
        v_result->>'status',
        v_result->>'error_code',
        nullif(v_result->>'slip_id', '')::bigint,
        nullif(v_result->>'checkout_id', '')::bigint,
        nullif(v_result->>'business_day_id', '')::bigint,
        coalesce(nullif(v_result->>'business_day_revision', '')::bigint, 0),
        v_result->>'current_slip_status',
        v_result->'statement_print_data',
        v_result->'statement_review_data',
        v_result->'receipt_print_data',
        coalesce(nullif(v_result->>'change_amount', '')::numeric, 0),
        v_result->'business_snapshot';
end;
$$;

create or replace function store.confirm_checkout_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint,
    p_expected_business_day_revision bigint,
    p_slip_id bigint,
    p_payments jsonb default '[]'::jsonb,
    p_received_amount numeric default null
)
returns table (
    operation_id text,
    status text,
    error_code text,
    slip_id bigint,
    checkout_id bigint,
    business_day_id bigint,
    business_day_revision bigint,
    current_slip_status text,
    statement_print_data jsonb,
    statement_review_data jsonb,
    receipt_print_data jsonb,
    change_amount numeric,
    business_snapshot jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_result jsonb;
begin
    v_result := store.apply_checkout_mutation_v2(
        'confirm_checkout_v2',
        p_department_id,
        p_operation_id,
        p_expected_business_day_id,
        p_expected_business_day_revision,
        p_slip_id,
        null,
        coalesce(p_payments, '[]'::jsonb),
        p_received_amount
    );

    return query select
        v_result->>'operation_id',
        v_result->>'status',
        v_result->>'error_code',
        nullif(v_result->>'slip_id', '')::bigint,
        nullif(v_result->>'checkout_id', '')::bigint,
        nullif(v_result->>'business_day_id', '')::bigint,
        coalesce(nullif(v_result->>'business_day_revision', '')::bigint, 0),
        v_result->>'current_slip_status',
        v_result->'statement_print_data',
        v_result->'statement_review_data',
        v_result->'receipt_print_data',
        coalesce(nullif(v_result->>'change_amount', '')::numeric, 0),
        v_result->'business_snapshot';
end;
$$;

create or replace function store.cancel_checkout_v2(
    p_department_id bigint,
    p_operation_id text,
    p_expected_business_day_id bigint,
    p_expected_business_day_revision bigint,
    p_slip_id bigint
)
returns table (
    operation_id text,
    status text,
    error_code text,
    slip_id bigint,
    checkout_id bigint,
    business_day_id bigint,
    business_day_revision bigint,
    current_slip_status text,
    statement_print_data jsonb,
    statement_review_data jsonb,
    receipt_print_data jsonb,
    change_amount numeric,
    business_snapshot jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_result jsonb;
begin
    v_result := store.apply_checkout_mutation_v2(
        'cancel_checkout_v2',
        p_department_id,
        p_operation_id,
        p_expected_business_day_id,
        p_expected_business_day_revision,
        p_slip_id,
        null,
        '[]'::jsonb,
        null
    );

    return query select
        v_result->>'operation_id',
        v_result->>'status',
        v_result->>'error_code',
        nullif(v_result->>'slip_id', '')::bigint,
        nullif(v_result->>'checkout_id', '')::bigint,
        nullif(v_result->>'business_day_id', '')::bigint,
        coalesce(nullif(v_result->>'business_day_revision', '')::bigint, 0),
        v_result->>'current_slip_status',
        v_result->'statement_print_data',
        v_result->'statement_review_data',
        v_result->'receipt_print_data',
        coalesce(nullif(v_result->>'change_amount', '')::numeric, 0),
        v_result->'business_snapshot';
end;
$$;

revoke all on function store.checkout_mutation_result_v2(text, text, text, bigint, bigint, bigint, bigint, text, jsonb, jsonb, jsonb, numeric, jsonb)
    from public, anon, authenticated, service_role;
revoke all on function store.remember_checkout_mutation_v2(bigint, text, text, jsonb, jsonb)
    from public, anon, authenticated, service_role;
revoke all on function store.confirm_checkout_draft_v2(bigint, bigint, jsonb, numeric)
    from public, anon, authenticated, service_role;
revoke all on function store.apply_checkout_mutation_v2(text, bigint, text, bigint, bigint, bigint, timestamp with time zone, jsonb, numeric)
    from public, anon, authenticated, service_role;
revoke all on function store.issue_checkout_statement_v2(bigint, text, bigint, bigint, bigint, timestamp with time zone)
    from public, anon, authenticated, service_role;
revoke all on function store.release_checkout_ready_v2(bigint, text, bigint, bigint, bigint)
    from public, anon, authenticated, service_role;
revoke all on function store.confirm_checkout_v2(bigint, text, bigint, bigint, bigint, jsonb, numeric)
    from public, anon, authenticated, service_role;
revoke all on function store.cancel_checkout_v2(bigint, text, bigint, bigint, bigint)
    from public, anon, authenticated, service_role;

commit;
