-- Checkout preparation is a DB state transition.  Printer progress remains on the terminal.

drop function if exists store.issue_checkout_statement(bigint, bigint, timestamp with time zone);
drop function if exists store.get_checkout_statement_print_data(bigint, bigint);
drop function if exists store.release_checkout_ready(bigint, bigint);
drop function if exists store.confirm_checkout(bigint, bigint, timestamp with time zone, jsonb, numeric, jsonb);
drop function if exists store.confirm_checkout(bigint, bigint, jsonb, numeric);
drop function if exists store.get_checkout_receipt_print_data(bigint, bigint);

create or replace function store.build_checkout_statement_data(
    p_department_id bigint,
    p_slip_id bigint
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip public.store_slips%rowtype;
    v_store_name text;
    v_table_display_name text;
    v_order_subtotal_amount numeric(12, 0);
    v_pricing_subtotal_amount numeric(12, 0);
    v_subtotal_amount numeric(12, 0);
    v_service_charge_amount numeric(12, 0);
    v_adjustment_amount numeric(12, 0);
    v_applied_adjustment_amount numeric(12, 0);
    v_total_amount numeric(12, 0);
begin
    select s.*
      into v_slip
      from public.store_slips s
     where s.department_id = p_department_id
       and s.slip_id = p_slip_id
       and s.status = 'checkout_ready';

    if v_slip.slip_id is null then
        raise exception 'checkout_ready_not_found';
    end if;

    select
        coalesce(nullif(trim(d.department_name), ''), nullif(trim(d.receipt_display_name), ''), '未設定'),
        coalesce(nullif(trim(concat_ws(' ', t.table_code, t.table_name)), ''), '未設定')
      into v_store_name, v_table_display_name
      from public.department_master d
      left join public.store_table_master t
        on t.table_id = v_slip.table_id
     where d.department_id = v_slip.department_id;

    select coalesce(sum(ol.amount), 0)
      into v_order_subtotal_amount
      from public.store_order_lines ol
     where ol.slip_id = p_slip_id
       and ol.status = 'active';

    select coalesce(sum(pl.amount), 0)
      into v_pricing_subtotal_amount
      from public.store_slip_pricing_lines pl
     where pl.slip_id = p_slip_id
       and pl.status = 'active'
       -- 移行前に確定した伝票だけは、対応するシステム商品行が無いため
       -- 料金計算の正本から補います。新規伝票は注文行に一度だけ計上されます。
       and not exists (
           select 1
           from public.store_order_lines ol
           where ol.source_type = 'automatic_pricing'
             and ol.source_id = pl.pricing_line_id
             and ol.status = 'active'
       );

    select coalesce(sum(cl.amount), 0)
      into v_adjustment_amount
      from public.store_slip_charge_lines cl
     where cl.slip_id = p_slip_id
       and cl.charge_type = 'adjustment'
       and cl.status = 'active';

    v_subtotal_amount := v_order_subtotal_amount + v_pricing_subtotal_amount;
    v_service_charge_amount := round(v_subtotal_amount * 0.20, 0);
    v_applied_adjustment_amount := greatest(v_adjustment_amount, -(v_subtotal_amount + v_service_charge_amount));
    v_total_amount := v_subtotal_amount + v_service_charge_amount + v_applied_adjustment_amount;

    return jsonb_build_object(
        'schema_version', 'checkout-statement-v1',
        'store_name', v_store_name,
        'table_display_name', v_table_display_name,
        'business_date', v_slip.business_date,
        'opened_at', v_slip.opened_at,
        'closed_at', v_slip.closed_at,
        'customer_count', (
            select count(*)::integer
            from public.store_slip_customers c
            where c.slip_id = p_slip_id
              and c.status <> 'cancelled'
        ),
        'orders', coalesce((
            select jsonb_agg(jsonb_build_object(
                'name', case
                    when ol.source_type = 'nomination_fee' and sc.nomination_type = 'companion'
                        then format('同伴(%s)', coalesce(nullif(trim(nomination_cast.display_name), ''), 'キャスト'))
                    when ol.source_type = 'nomination_fee'
                        then format('担当(%s)', coalesce(nullif(trim(nomination_cast.display_name), ''), 'キャスト'))
                    else ol.item_name_snapshot
                end,
                'quantity', ol.quantity,
                'unit_price', ol.unit_price,
                'amount', ol.amount,
                'source_type', coalesce(ol.source_type, ''),
                'back_cast_display_name', coalesce(back_cast.display_name, '')
            ) order by
                case when ol.source_type = 'nomination_fee' then 0 else 1 end,
                ol.line_no,
                ol.order_line_id)
            from public.store_order_lines ol
            left join public.store_slip_casts sc
              on sc.slip_cast_id = ol.source_id
             and sc.slip_id = ol.slip_id
            left join public.cast_master nomination_cast
              on nomination_cast.cast_id = sc.cast_id
            left join lateral (
                select nullif(trim(cm.display_name), '') as display_name
                from public.store_order_line_cast_backs ob
                join public.cast_master cm
                  on cm.cast_id = ob.cast_id
                where ob.order_line_id = ol.order_line_id
                  and ob.status = 'active'
                order by ob.order_line_cast_back_id asc
                limit 1
            ) back_cast on true
            where ol.slip_id = p_slip_id
              and ol.status = 'active'
        ), '[]'::jsonb),
        'pricing_lines', coalesce((
            select jsonb_agg(jsonb_build_object(
                'pricing_code', pl.pricing_code,
                'name', pl.line_name,
                'occurred_at', pl.occurred_at,
                'customer_count', pl.customer_count,
                'quantity', pl.quantity,
                'unit_price', pl.unit_price,
                'amount', pl.amount,
                'pricing_plan_version', pl.pricing_plan_version
            ) order by pl.occurred_at, pl.line_no, pl.pricing_line_id)
            from public.store_slip_pricing_lines pl
            where pl.slip_id = p_slip_id
              and pl.status = 'active'
              and not exists (
                  select 1
                  from public.store_order_lines ol
                  where ol.source_type = 'automatic_pricing'
                    and ol.source_id = pl.pricing_line_id
                    and ol.status = 'active'
              )
        ), '[]'::jsonb),
        'adjustments', coalesce((
            select jsonb_agg(jsonb_build_object(
                'name', cl.line_name,
                'amount', cl.amount
            ) order by cl.line_no, cl.charge_line_id)
            from public.store_slip_charge_lines cl
            where cl.slip_id = p_slip_id
              and cl.charge_type = 'adjustment'
              and cl.status = 'active'
              and v_adjustment_amount >= -(v_subtotal_amount + v_service_charge_amount)
        ), '[]'::jsonb),
        'order_subtotal_amount', v_order_subtotal_amount,
        'pricing_subtotal_amount', v_pricing_subtotal_amount,
        'subtotal_amount', v_subtotal_amount,
        'service_charge_amount', v_service_charge_amount,
        'consumption_tax_amount', round(v_total_amount * 10 / 110, 0),
        'total_amount', v_total_amount
    );
end;
$$;

create or replace function store.build_checkout_review_data(
    p_department_id bigint,
    p_slip_id bigint
)
returns jsonb
language sql
security definer
set search_path = public
as $$
    select jsonb_build_object(
        'orders', coalesce((
            select jsonb_agg(jsonb_build_object(
                'name', case
                    when ol.source_type = 'nomination_fee' and sc.nomination_type = 'companion'
                        then format('同伴(%s)', coalesce(nullif(trim(nomination_cast.display_name), ''), 'キャスト'))
                    when ol.source_type = 'nomination_fee'
                        then format('担当(%s)', coalesce(nullif(trim(nomination_cast.display_name), ''), 'キャスト'))
                    else ol.item_name_snapshot
                end,
                'quantity', ol.quantity,
                'unit_price', ol.unit_price,
                'amount', ol.amount,
                'source_type', coalesce(ol.source_type, ''),
                'back_cast_display_name', coalesce(back_cast.display_name, '')
            ) order by
                case when ol.source_type = 'nomination_fee' then 0 else 1 end,
                ol.line_no,
                ol.order_line_id)
            from public.store_order_lines ol
            join public.store_slips s
              on s.slip_id = ol.slip_id
            left join public.store_slip_casts sc
              on sc.slip_cast_id = ol.source_id
             and sc.slip_id = ol.slip_id
            left join public.cast_master nomination_cast
              on nomination_cast.cast_id = sc.cast_id
            left join lateral (
                select nullif(trim(cm.display_name), '') as display_name
                from public.store_order_line_cast_backs ob
                join public.cast_master cm
                  on cm.cast_id = ob.cast_id
                where ob.order_line_id = ol.order_line_id
                  and ob.status = 'active'
                order by ob.order_line_cast_back_id asc
                limit 1
            ) back_cast on true
            where ol.slip_id = p_slip_id
              and ol.status = 'active'
              and s.department_id = p_department_id
              and s.status = 'checkout_ready'
        ), '[]'::jsonb),
        'pricing_lines', coalesce((
            select jsonb_agg(jsonb_build_object(
                'pricing_code', pl.pricing_code,
                'name', pl.line_name,
                'occurred_at', pl.occurred_at,
                'customer_count', pl.customer_count,
                'quantity', pl.quantity,
                'unit_price', pl.unit_price,
                'amount', pl.amount
            ) order by pl.occurred_at, pl.line_no, pl.pricing_line_id)
            from public.store_slip_pricing_lines pl
            join public.store_slips s
              on s.slip_id = pl.slip_id
            where pl.slip_id = p_slip_id
              and pl.status = 'active'
              and s.department_id = p_department_id
              and s.status = 'checkout_ready'
              and not exists (
                  select 1
                  from public.store_order_lines ol
                  where ol.source_type = 'automatic_pricing'
                    and ol.source_id = pl.pricing_line_id
                    and ol.status = 'active'
              )
        ), '[]'::jsonb)
    );
$$;

create or replace function store.issue_checkout_statement(
    p_department_id bigint,
    p_slip_id bigint,
    p_closed_at timestamp with time zone
)
returns table (print_data jsonb, review_data jsonb)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip public.store_slips%rowtype;
begin
    select s.*
      into v_slip
      from public.store_slips s
     where s.department_id = p_department_id
       and s.slip_id = p_slip_id
       and s.status = 'open'
     for update;

    if v_slip.slip_id is null then
        raise exception 'checkout_statement_slip_not_found';
    end if;

    if p_closed_at is null or p_closed_at <= v_slip.opened_at or exists (
        select 1
        from public.store_slip_customers c
        where c.slip_id = p_slip_id
          and c.status <> 'cancelled'
          and p_closed_at <= c.entered_at
    ) then
        raise exception 'invalid_closed_at';
    end if;

    -- 会計伝票を出す時点で料金案を固定します。退店時刻は境界を含み、
    -- 各客の退店時刻は境界を含まないため、先に計算してから未退店客を退店にします。
    -- 料金計算の正本と対応する編集不可のシステム商品行は、同じトランザクションで作ります。
    update public.store_order_lines ol
       set status = 'voided',
           voided_at = coalesce(ol.voided_at, now()),
           updated_at = now()
     where ol.slip_id = p_slip_id
       and ol.source_type = 'automatic_pricing'
       and ol.status = 'active';

    update public.store_slip_pricing_lines pl
       set status = 'voided',
           updated_at = now()
     where pl.slip_id = p_slip_id
       and pl.status = 'active';

    with inserted_pricing as (
        insert into public.store_slip_pricing_lines (
            slip_id, business_day_id, business_date, company_id, department_id,
            pricing_plan_id, pricing_plan_version, pricing_mode, line_no,
            pricing_code, line_name, occurred_at, customer_count, quantity,
            unit_price, amount, slip_cast_id, status
        )
        select
            v_slip.slip_id,
            v_slip.business_day_id,
            v_slip.business_date,
            v_slip.company_id,
            v_slip.department_id,
            calculated.pricing_plan_id,
            calculated.pricing_plan_version,
            calculated.pricing_mode,
            coalesce((
                select max(existing.line_no)
                from public.store_slip_pricing_lines existing
                where existing.slip_id = p_slip_id
            ), 0) + row_number() over (order by calculated.occurred_at, calculated.pricing_code)::integer,
            calculated.pricing_code,
            calculated.line_name,
            calculated.occurred_at,
            calculated.customer_count,
            calculated.quantity,
            calculated.unit_price,
            calculated.amount,
            calculated.slip_cast_id,
            'active'
        from store.calculate_slip_pricing(p_department_id, p_slip_id, p_closed_at) calculated
        returning pricing_line_id, pricing_code, line_name, occurred_at, quantity, unit_price, amount
    ), pricing_items as (
        select *
        from store.ensure_pricing_system_items(p_department_id)
    )
    insert into public.store_order_lines (
        slip_id, line_no, item_id, item_name_snapshot, quantity, unit_price,
        amount, ordered_at, status, source_type, source_id
    )
    select
        p_slip_id,
        coalesce((
            select max(existing.line_no)
            from public.store_order_lines existing
            where existing.slip_id = p_slip_id
        ), 0) + row_number() over (order by inserted.occurred_at, inserted.pricing_line_id)::integer,
        item.item_id,
        inserted.line_name,
        inserted.quantity,
        inserted.unit_price,
        inserted.amount,
        inserted.occurred_at,
        'active',
        'automatic_pricing',
        inserted.pricing_line_id
    from inserted_pricing inserted
    join pricing_items item
      on item.pricing_code = inserted.pricing_code;

    update public.store_slip_customers c
       set left_at = p_closed_at,
           left_at_source = 'accounting_slip',
           status = 'left',
           updated_at = now()
     where c.slip_id = p_slip_id
       and c.status = 'active';

    update public.store_slips s
       set status = 'checkout_ready',
           closed_at = p_closed_at,
           customer_count = 0,
           updated_at = now()
     where s.slip_id = p_slip_id
       and s.department_id = p_department_id;

    return query select
        store.build_checkout_statement_data(p_department_id, p_slip_id),
        store.build_checkout_review_data(p_department_id, p_slip_id);
end;
$$;

create or replace function store.get_checkout_statement_print_data(
    p_department_id bigint,
    p_slip_id bigint
)
returns table (print_data jsonb, review_data jsonb)
language sql
security definer
set search_path = public
as $$
    select
        store.build_checkout_statement_data(p_department_id, p_slip_id),
        store.build_checkout_review_data(p_department_id, p_slip_id);
$$;

create or replace function store.release_checkout_ready(
    p_department_id bigint,
    p_slip_id bigint
)
returns table (slip_id bigint)
language plpgsql
security definer
set search_path = public
as $$
begin
    if not exists (
        select 1
        from public.store_slips s
        where s.department_id = p_department_id
          and s.slip_id = p_slip_id
          and s.status = 'checkout_ready'
    ) then
        raise exception 'checkout_ready_not_found';
    end if;

    -- 固定済みの自動料金は会計準備を解除した時点で無効にし、
    -- 次回の会計伝票出力でその時点の料金プラン・退店時刻から作り直します。
    update public.store_slip_pricing_lines pl
       set status = 'voided',
           updated_at = now()
     where pl.slip_id = p_slip_id
       and pl.status = 'active';

    update public.store_order_lines ol
       set status = 'voided',
           voided_at = coalesce(ol.voided_at, now()),
           updated_at = now()
     where ol.slip_id = p_slip_id
       and ol.source_type = 'automatic_pricing'
       and ol.status = 'active';

    update public.store_slip_customers c
       set left_at = null,
           left_at_source = null,
           status = 'active',
           updated_at = now()
     where c.slip_id = p_slip_id
       and c.status = 'left'
       and c.left_at_source = 'accounting_slip';

    return query
    update public.store_slips s
       set status = 'open',
           closed_at = null,
           customer_count = (
               select count(*)::integer
               from public.store_slip_customers c
               where c.slip_id = p_slip_id
                 and c.status = 'active'
           ),
           updated_at = now()
     where s.department_id = p_department_id
       and s.slip_id = p_slip_id
       and s.status = 'checkout_ready'
     returning s.slip_id;
end;
$$;

create or replace function store.build_checkout_receipt_data(
    p_department_id bigint,
    p_checkout_id bigint
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_checkout public.store_checkouts%rowtype;
begin
    select c.*
      into v_checkout
      from public.store_checkouts c
     where c.department_id = p_department_id
       and c.checkout_id = p_checkout_id
       and c.status = 'confirmed';

    if v_checkout.checkout_id is null then
        raise exception 'checkout_not_found';
    end if;

    return jsonb_build_object(
        'schema_version', 'checkout-receipt-v2',
        'issued_at', v_checkout.checkout_at,
        'particulars', 'ご飲食代として',
        'total_amount', v_checkout.total_amount,
        'taxable_amount_including_tax', v_checkout.total_amount,
        'consumption_tax_amount', round(v_checkout.total_amount * 10 / 110, 0),
        'payments', coalesce((
            select jsonb_agg(jsonb_build_object(
                'method_name', case when p.payment_method_code = 'cat' then 'クレジット' else p.payment_method_name end,
                'amount', p.amount
            ) order by p.checkout_payment_id)
            from public.store_checkout_payments p
            where p.checkout_id = v_checkout.checkout_id
              and p.status = 'confirmed'
        ), '[]'::jsonb),
        'issuer', coalesce(v_checkout.issuer_snapshot, '{}'::jsonb)
    );
end;
$$;

create or replace function store.confirm_checkout(
    p_department_id bigint,
    p_slip_id bigint,
    p_payments jsonb default '[]'::jsonb,
    p_received_amount numeric default null
)
returns table (
    checkout_id bigint,
    change_amount numeric,
    print_data jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_slip public.store_slips%rowtype;
    v_payment jsonb;
    v_method_code text;
    v_amount numeric(12, 0);
    v_payment_method public.payment_method_master%rowtype;
    v_order_subtotal_amount numeric(12, 0);
    v_pricing_subtotal_amount numeric(12, 0);
    v_subtotal_amount numeric(12, 0);
    v_service_charge_amount numeric(12, 0);
    v_adjustment_amount numeric(12, 0);
    v_applied_adjustment_amount numeric(12, 0);
    v_total_amount numeric(12, 0);
    v_payment_total numeric(12, 0) := 0;
    v_cash_amount numeric(12, 0) := 0;
    v_payment_count integer := 0;
    v_checkout_id bigint;
    v_single_payment_method_id bigint;
    v_change_amount numeric(12, 0) := 0;
    v_issuer_snapshot jsonb;
begin
    select s.*
      into v_slip
      from public.store_slips s
      join public.department_master d
        on d.department_id = s.department_id
       and d.is_active = true
     where s.department_id = p_department_id
       and s.slip_id = p_slip_id
       and s.status = 'checkout_ready'
     for update;

    if v_slip.slip_id is null or v_slip.closed_at is null then
        raise exception 'checkout_ready_not_found';
    end if;

    select d.company_id
      into v_company_id
      from public.department_master d
     where d.department_id = v_slip.department_id
       and d.is_active = true;

    if exists (
        select 1
        from public.store_checkouts c
        where c.slip_id = p_slip_id
          and c.status <> 'cancelled'
    ) then
        raise exception 'checkout_already_exists';
    end if;

    select coalesce(sum(ol.amount), 0)
      into v_order_subtotal_amount
      from public.store_order_lines ol
     where ol.slip_id = p_slip_id
       and ol.status = 'active';

    select coalesce(sum(pl.amount), 0)
      into v_pricing_subtotal_amount
      from public.store_slip_pricing_lines pl
     where pl.slip_id = p_slip_id
       and pl.status = 'active'
       and not exists (
           select 1
           from public.store_order_lines ol
           where ol.source_type = 'automatic_pricing'
             and ol.source_id = pl.pricing_line_id
             and ol.status = 'active'
       );

    select coalesce(sum(cl.amount), 0)
      into v_adjustment_amount
      from public.store_slip_charge_lines cl
     where cl.slip_id = p_slip_id
       and cl.charge_type = 'adjustment'
       and cl.status = 'active';

    v_subtotal_amount := v_order_subtotal_amount + v_pricing_subtotal_amount;
    v_service_charge_amount := round(v_subtotal_amount * 0.20, 0);
    v_applied_adjustment_amount := greatest(v_adjustment_amount, -(v_subtotal_amount + v_service_charge_amount));
    v_total_amount := v_subtotal_amount + v_service_charge_amount + v_applied_adjustment_amount;

    if jsonb_typeof(coalesce(p_payments, '[]'::jsonb)) <> 'array' then
        raise exception 'invalid_checkout_payment';
    end if;

    if exists (
        select 1
        from jsonb_array_elements(p_payments) payment(value)
        group by lower(nullif(trim(payment.value->>'method_code'), ''))
        having count(*) > 1
    ) then
        raise exception 'duplicate_checkout_payment_method';
    end if;

    for v_payment in select value from jsonb_array_elements(p_payments) loop
        v_method_code := lower(nullif(trim(v_payment->>'method_code'), ''));
        v_amount := nullif(v_payment->>'amount', '')::numeric;

        if v_method_code is null or coalesce(v_amount, 0) <= 0 then
            raise exception 'invalid_checkout_payment';
        end if;

        select pm.*
          into v_payment_method
          from public.payment_method_master pm
         where pm.company_id = v_company_id
           and pm.department_id = p_department_id
           and pm.payment_method_code = v_method_code
           and pm.is_active = true;

        if v_payment_method.payment_method_id is null then
            raise exception 'invalid_checkout_payment';
        end if;

        v_payment_count := v_payment_count + 1;
        v_payment_total := v_payment_total + v_amount;
        if v_method_code = 'cash' then
            v_cash_amount := v_cash_amount + v_amount;
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

    if v_cash_amount > 0 then
        if coalesce(p_received_amount, -1) < v_cash_amount then
            raise exception 'invalid_received_amount';
        end if;
        v_change_amount := p_received_amount - v_cash_amount;
    elsif p_received_amount is not null and p_received_amount <> 0 then
        raise exception 'invalid_received_amount';
    end if;

    select jsonb_build_object(
        'schema_version', 'issuer-v1',
        'company_name', coalesce(nullif(trim(c.company_name), ''), '未設定'),
        'invoice_registration_number', coalesce(nullif(trim(c.invoice_registration_number), ''), '未設定'),
        'store_name', coalesce(nullif(trim(d.receipt_display_name), ''), '未設定'),
        'address', coalesce(nullif(trim(d.receipt_address), ''), '未設定'),
        'phone', coalesce(nullif(trim(d.receipt_phone), ''), '未設定'),
        'logo', coalesce(d.receipt_logo, '')
    )
      into v_issuer_snapshot
      from public.company_master c
      join public.department_master d
        on d.company_id = c.company_id
     where d.department_id = p_department_id
       and c.company_id = v_company_id;

    insert into public.store_checkouts (
        slip_id, company_id, department_id, checkout_at,
        subtotal_amount, service_charge_amount, total_amount,
        payment_method_id, received_amount, change_amount, issuer_snapshot, status
    ) values (
        p_slip_id, v_company_id, p_department_id, now(),
        v_subtotal_amount, v_service_charge_amount, v_total_amount,
        v_single_payment_method_id,
        case when v_cash_amount > 0 then p_received_amount else null end,
        v_change_amount, coalesce(v_issuer_snapshot, '{}'::jsonb), 'confirmed'
    ) returning store_checkouts.checkout_id into v_checkout_id;

    for v_payment in select value from jsonb_array_elements(p_payments) loop
        v_method_code := lower(nullif(trim(v_payment->>'method_code'), ''));
        v_amount := nullif(v_payment->>'amount', '')::numeric;
        select pm.* into v_payment_method
        from public.payment_method_master pm
        where pm.company_id = v_company_id
          and pm.department_id = p_department_id
          and pm.payment_method_code = v_method_code
          and pm.is_active = true;

        insert into public.store_checkout_payments (
            checkout_id, slip_id, company_id, department_id, payment_method_id,
            payment_method_code, payment_method_name, amount, status
        ) values (
            v_checkout_id, p_slip_id, v_company_id, p_department_id, v_payment_method.payment_method_id,
            v_payment_method.payment_method_code, v_payment_method.payment_method_name, v_amount, 'confirmed'
        );
    end loop;

    update public.store_slips s
       set status = 'checked_out',
           customer_count = 0,
           updated_at = now()
     where s.department_id = p_department_id
       and s.slip_id = p_slip_id;

    return query select
        v_checkout_id,
        v_change_amount,
        store.build_checkout_receipt_data(p_department_id, v_checkout_id);
end;
$$;

create or replace function store.get_checkout_receipt_print_data(
    p_department_id bigint,
    p_slip_id bigint
)
returns table (
    checkout_id bigint,
    print_data jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_checkout_id bigint;
begin
    select c.checkout_id
      into v_checkout_id
      from public.store_checkouts c
      join public.store_slips s
        on s.slip_id = c.slip_id
     where c.department_id = p_department_id
       and c.slip_id = p_slip_id
       and c.status = 'confirmed'
       and s.status = 'checked_out'
     order by c.checkout_at desc, c.checkout_id desc
     limit 1;

    if v_checkout_id is null then
        raise exception 'checkout_not_found';
    end if;

    return query select
        v_checkout_id,
        store.build_checkout_receipt_data(p_department_id, v_checkout_id);
end;
$$;

revoke execute on function store.build_checkout_statement_data(bigint, bigint) from public, anon, authenticated, service_role;
revoke execute on function store.build_checkout_review_data(bigint, bigint) from public, anon, authenticated, service_role;
revoke execute on function store.build_checkout_receipt_data(bigint, bigint) from public, anon, authenticated, service_role;
