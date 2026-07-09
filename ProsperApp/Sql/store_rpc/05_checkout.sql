alter table public.store_checkouts
    drop constraint if exists uq_store_checkouts_slip;

create unique index if not exists ux_store_checkouts_active_slip
    on public.store_checkouts(slip_id)
    where status <> 'cancelled';

create or replace function store.confirm_checkout(
    p_department_id bigint,
    p_slip_id bigint,
    p_closed_at timestamp with time zone,
    p_payments jsonb default '[]'::jsonb,
    p_received_amount numeric default null
)
returns table (
    checkout_id bigint,
    change_amount numeric
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_slip public.store_slips%rowtype;
    v_payment jsonb;
    v_payment_method public.payment_method_master%rowtype;
    v_method_code text;
    v_amount numeric(12, 0);
    v_subtotal_amount numeric(12, 0);
    v_service_tax_amount numeric(12, 0);
    v_nomination_amount numeric(12, 0);
    v_charge_amount numeric(12, 0);
    v_total_amount numeric(12, 0);
    v_payment_total numeric(12, 0) := 0;
    v_cash_amount numeric(12, 0) := 0;
    v_payment_count integer := 0;
    v_checkout_id bigint;
    v_single_payment_method_id bigint := null;
    v_change_amount numeric(12, 0) := 0;
begin
    select d.company_id
      into v_company_id
    from public.department_master d
    where d.department_id = p_department_id
      and d.is_active = true
    limit 1;

    if v_company_id is null then
        raise exception 'store_department_not_found';
    end if;

    select *
      into v_slip
    from public.store_slips s
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id
      and s.status = 'open'
    limit 1;

    if v_slip.slip_id is null then
        raise exception 'store_checkout_slip_not_found';
    end if;

    if p_closed_at < v_slip.opened_at then
        raise exception 'invalid_closed_at';
    end if;

    if exists (
        select 1
        from public.store_checkouts c
        where c.slip_id = p_slip_id
          and c.status <> 'cancelled'
    ) then
        raise exception 'checkout_already_exists';
    end if;

    insert into public.payment_method_master (
        company_id,
        department_id,
        payment_method_code,
        payment_method_name,
        requires_received_amount,
        sort_order,
        is_active
    )
    values
        (v_company_id, p_department_id, 'cash', '現金', true, 10, true),
        (v_company_id, p_department_id, 'cat', 'CAT', false, 20, true),
        (v_company_id, p_department_id, 'paypay', 'PAYPAY', false, 30, true)
    on conflict on constraint uq_payment_method_master_code
    do update set
        payment_method_name = excluded.payment_method_name,
        requires_received_amount = excluded.requires_received_amount,
        sort_order = excluded.sort_order,
        is_active = true,
        updated_at = now();

    select coalesce(sum(ol.amount), 0)
      into v_subtotal_amount
    from public.store_order_lines ol
    left join public.store_item_master i
      on i.item_id = ol.item_id
    where ol.slip_id = p_slip_id
      and ol.status = 'active'
      and coalesce(i.item_type, 'standard') <> 'nomination_fee';

    select coalesce(sum(ol.amount), 0)
      into v_nomination_amount
    from public.store_order_lines ol
    join public.store_item_master i
      on i.item_id = ol.item_id
    where ol.slip_id = p_slip_id
      and ol.status = 'active'
      and i.item_type = 'nomination_fee';

    select coalesce(sum(cl.amount), 0)
      into v_charge_amount
    from public.store_slip_charge_lines cl
    where cl.slip_id = p_slip_id
      and cl.charge_type = 'adjustment'
      and cl.status = 'active';

    v_service_tax_amount := round(v_subtotal_amount * 0.20, 0);
    v_total_amount := v_subtotal_amount + v_service_tax_amount + v_nomination_amount + v_charge_amount;

    if v_total_amount < 0 then
        raise exception 'invalid_checkout_total';
    end if;

    for v_payment in
        select value
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_payments) = 'array' then p_payments
                else '[]'::jsonb
            end
        )
    loop
        v_method_code := lower(nullif(trim(coalesce(v_payment->>'method_code', '')), ''));
        v_amount := nullif(v_payment->>'amount', '')::numeric;

        if v_method_code not in ('cash', 'cat', 'paypay') or coalesce(v_amount, 0) <= 0 then
            raise exception 'invalid_checkout_payment';
        end if;

        select *
          into v_payment_method
        from public.payment_method_master pm
        where pm.company_id = v_company_id
          and pm.department_id = p_department_id
          and pm.payment_method_code = v_method_code
          and pm.is_active = true
        limit 1;

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

    if v_payment_count = 0 then
        raise exception 'invalid_checkout_payment';
    end if;

    if v_payment_total <> v_total_amount then
        raise exception 'invalid_checkout_total';
    end if;

    if v_cash_amount > 0 then
        if coalesce(p_received_amount, -1) < v_cash_amount then
            raise exception 'invalid_received_amount';
        end if;

        v_change_amount := coalesce(p_received_amount, 0) - v_cash_amount;
    elsif p_received_amount is not null and p_received_amount <> 0 then
        raise exception 'invalid_received_amount';
    end if;

    insert into public.store_checkouts (
        slip_id,
        company_id,
        department_id,
        checkout_at,
        subtotal_amount,
        service_tax_amount,
        total_amount,
        payment_method_id,
        received_amount,
        change_amount,
        status
    )
    values (
        p_slip_id,
        v_company_id,
        p_department_id,
        p_closed_at,
        v_subtotal_amount,
        v_service_tax_amount,
        v_total_amount,
        v_single_payment_method_id,
        case when v_cash_amount > 0 then p_received_amount else null end,
        v_change_amount,
        'confirmed'
    )
    returning store_checkouts.checkout_id into v_checkout_id;

    for v_payment in
        select value
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_payments) = 'array' then p_payments
                else '[]'::jsonb
            end
        )
    loop
        v_method_code := lower(nullif(trim(coalesce(v_payment->>'method_code', '')), ''));
        v_amount := nullif(v_payment->>'amount', '')::numeric;

        select *
          into v_payment_method
        from public.payment_method_master pm
        where pm.company_id = v_company_id
          and pm.department_id = p_department_id
          and pm.payment_method_code = v_method_code
        limit 1;

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
        )
        values (
            v_checkout_id,
            p_slip_id,
            v_company_id,
            p_department_id,
            v_payment_method.payment_method_id,
            v_payment_method.payment_method_code,
            v_payment_method.payment_method_name,
            v_amount,
            'confirmed'
        );
    end loop;

    update public.store_slip_customers c
       set left_at = coalesce(c.left_at, p_closed_at),
           status = 'left',
           updated_at = now()
     where c.slip_id = p_slip_id
       and c.status = 'active';

    update public.store_slips s
       set closed_at = p_closed_at,
           status = 'checked_out',
           customer_count = 0,
           updated_at = now()
     where s.slip_id = p_slip_id;

    return query select v_checkout_id, v_change_amount;
end;
$$;

drop function if exists store.cancel_checkout(bigint, bigint);

create or replace function store.cancel_checkout(
    p_department_id bigint,
    p_slip_id bigint
)
returns table (
    checkout_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip public.store_slips%rowtype;
    v_business_day_status text;
    v_checkout public.store_checkouts%rowtype;
begin
    select s.*
      into v_slip
    from public.store_slips s
    join public.store_business_days b
      on b.business_day_id = s.business_day_id
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id
      and s.status = 'checked_out'
    limit 1;

    if v_slip.slip_id is null then
        raise exception 'store_checkout_not_found';
    end if;

    select b.status
      into v_business_day_status
    from public.store_business_days b
    where b.business_day_id = v_slip.business_day_id
    limit 1;

    if v_business_day_status <> 'open' then
        raise exception 'business_day_not_open';
    end if;

    select *
      into v_checkout
    from public.store_checkouts c
    where c.slip_id = p_slip_id
      and c.department_id = p_department_id
      and c.status = 'confirmed'
    order by c.checkout_at desc, c.checkout_id desc
    limit 1;

    if v_checkout.checkout_id is null then
        raise exception 'store_checkout_not_found';
    end if;

    update public.store_checkout_payments p
       set status = 'cancelled',
           updated_at = now()
     where p.checkout_id = v_checkout.checkout_id
       and p.status = 'confirmed';

    delete from public.store_slip_cast_sales_adjustments a
     where a.department_id = p_department_id
       and a.slip_id = p_slip_id;

    update public.store_checkouts c
       set status = 'cancelled',
           updated_at = now()
     where c.checkout_id = v_checkout.checkout_id;

    update public.store_slips s
       set closed_at = null,
           status = 'open',
           customer_count = (
               select count(*)::integer
               from public.store_slip_customers c
               where c.slip_id = p_slip_id
                 and c.status = 'active'
           ),
           updated_at = now()
     where s.slip_id = p_slip_id;

    return query select v_checkout.checkout_id;
end;
$$;

drop function if exists store.create_slip(bigint, bigint, timestamp with time zone, text[], bigint[], text);

create or replace function store.create_slip(
    p_department_id bigint,
    p_table_id bigint,
    p_opened_at timestamp with time zone,
    p_customer_labels text[] default array[]::text[],
    p_cast_nominations jsonb default '[]'::jsonb,
    p_memo text default null
)
returns table (
    slip_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_business_day public.store_business_days%rowtype;
    v_slip_id bigint;
    v_line_no integer;
    v_label text;
    v_cast_id bigint;
    v_nomination jsonb;
    v_nomination_kind text;
    v_nomination_type text;
    v_nomination_price numeric(12, 0);
    v_companion_time time;
    v_started_at timestamp with time zone;
    v_slip_cast_id bigint;
    v_nomination_fee_item public.store_item_master%rowtype;
    v_order_line_no integer;
    v_customer_count integer;
    v_slip_no text;
begin
    select d.company_id
      into v_company_id
    from public.department_master d
    where d.department_id = p_department_id
      and d.is_active = true
    limit 1;

    if v_company_id is null then
        raise exception 'store_department_not_found';
    end if;

    select *
      into v_business_day
    from public.store_business_days b
    where b.department_id = p_department_id
      and b.status = 'open'
    order by b.opened_at desc
    limit 1;

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_open';
    end if;

    if not exists (
        select 1
        from public.store_table_master t
        where t.table_id = p_table_id
          and t.department_id = p_department_id
          and t.is_active = true
    ) then
        raise exception 'store_table_not_found';
    end if;

    v_customer_count := greatest(coalesce(array_length(p_customer_labels, 1), 0), 1);
    v_slip_no := 'S' || to_char(p_opened_at at time zone 'Asia/Tokyo', 'YYMMDDHH24MISS') ||
                 '-T' || p_table_id::text ||
                 '-' || floor(random() * 900 + 100)::integer::text;

    insert into public.store_slips (
        company_id,
        department_id,
        business_day_id,
        business_date,
        table_id,
        slip_no,
        opened_at,
        status,
        customer_count,
        memo
    )
    values (
        v_company_id,
        p_department_id,
        v_business_day.business_day_id,
        v_business_day.business_date,
        p_table_id,
        v_slip_no,
        p_opened_at,
        'open',
        v_customer_count,
        nullif(trim(coalesce(p_memo, '')), '')
    )
    returning store_slips.slip_id into v_slip_id;

    for v_line_no in 1..v_customer_count loop
        v_label := nullif(trim(coalesce(p_customer_labels[v_line_no], '')), '');
        insert into public.store_slip_customers (
            slip_id,
            line_no,
            customer_label,
            entered_at,
            status
        )
        values (
            v_slip_id,
            v_line_no,
            v_label,
            p_opened_at,
            'active'
        );
    end loop;

    for v_nomination in
        select value
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_cast_nominations) = 'array' then p_cast_nominations
                else '[]'::jsonb
            end
        )
    loop
        v_cast_id := nullif(v_nomination->>'cast_id', '')::bigint;
        v_nomination_kind := nullif(trim(coalesce(v_nomination->>'nomination_kind', '')), '');
        v_nomination_price := nullif(v_nomination->>'nomination_price', '')::numeric;

        if v_cast_id is null then
            raise exception 'cast_not_selected';
        end if;

        if v_nomination_price is null or
           v_nomination_price < 1000 or
           v_nomination_price > 20000 or
           mod(v_nomination_price, 1000) <> 0 then
            raise exception 'invalid_nomination_price';
        end if;

        select
            m.nomination_type,
            m.companion_time
          into v_nomination_type, v_companion_time
        from public.store_nomination_back_master m
        where m.company_id = v_company_id
          and m.department_id = p_department_id
          and m.nomination_kind = v_nomination_kind
          and m.back_type = 'nomination'
          and m.is_active = true
        limit 1;

        if v_nomination_type is null then
            raise exception 'invalid_nomination_type';
        end if;

        if v_nomination_type = 'companion' and v_companion_time is null then
            raise exception 'invalid_companion_time';
        end if;

        v_started_at := case
            when v_nomination_type = 'companion'
                then (((v_business_day.business_date + case when v_companion_time < time '12:00' then 1 else 0 end)::timestamp + v_companion_time) at time zone 'Asia/Tokyo')
            else p_opened_at
        end;

        if exists (
            select 1
            from public.cast_master c
            join public.department_master d
              on d.department_id = c.department_id
            where c.cast_id = v_cast_id
              and c.company_id = v_company_id
              and c.is_active = true
              and c.status = 'active'
              and d.is_active = true
        ) then
            insert into public.store_slip_casts (
                slip_id,
                cast_id,
                nomination_kind,
                nomination_type,
                nomination_price,
                started_at,
                status
            )
            values (
                v_slip_id,
                v_cast_id,
                v_nomination_kind,
                v_nomination_type,
                v_nomination_price,
                v_started_at,
                'active'
            )
            returning store_slip_casts.slip_cast_id into v_slip_cast_id;

            insert into public.store_slip_cast_backs (
                slip_cast_id,
                slip_id,
                business_day_id,
                business_date,
                company_id,
                department_id,
                cast_id,
                nomination_kind,
                nomination_type,
                back_type,
                quantity,
                back_unit_amount,
                back_amount,
                status
            )
            select
                v_slip_cast_id,
                v_slip_id,
                v_business_day.business_day_id,
                v_business_day.business_date,
                v_company_id,
                p_department_id,
                v_cast_id,
                v_nomination_kind,
                v_nomination_type,
                'nomination',
                1,
                m.back_unit_amount,
                m.back_unit_amount,
                'active'
            from public.store_nomination_back_master m
            where m.company_id = v_company_id
              and m.department_id = p_department_id
              and m.nomination_kind = v_nomination_kind
              and m.back_type = 'nomination'
              and m.is_active = true
              and m.back_unit_amount > 0;

            if v_nomination_fee_item.item_id is null then
                select *
                  into v_nomination_fee_item
                from public.store_item_master i
                where i.company_id = v_company_id
                  and i.department_id = p_department_id
                  and i.item_type = 'nomination_fee'
                  and i.is_active = true
                order by i.sort_order asc, i.item_id asc
                limit 1;

                if v_nomination_fee_item.item_id is null then
                    raise exception 'store_nomination_fee_item_not_found';
                end if;
            end if;

            select coalesce(max(ol.line_no), 0) + 1
              into v_order_line_no
            from public.store_order_lines ol
            where ol.slip_id = v_slip_id;

            insert into public.store_order_lines (
                slip_id,
                line_no,
                item_id,
                item_name_snapshot,
                quantity,
                unit_price,
                amount,
                ordered_at,
                status,
                source_type,
                source_id
            )
            values (
                v_slip_id,
                v_order_line_no,
                v_nomination_fee_item.item_id,
                v_nomination_fee_item.item_name,
                1,
                v_nomination_price,
                v_nomination_price,
                v_started_at,
                'active',
                'nomination_fee',
                v_slip_cast_id
            );
        else
            raise exception 'store_cast_not_found';
        end if;
    end loop;

    return query select v_slip_id;
end;
$$;
