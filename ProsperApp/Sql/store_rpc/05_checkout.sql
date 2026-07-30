alter table public.store_checkouts
    drop constraint if exists uq_store_checkouts_slip;

create unique index if not exists ux_store_checkouts_active_slip
    on public.store_checkouts(slip_id)
    where status <> 'cancelled';

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
    v_cancelled_checkout_id bigint;
    v_reopened_slip_id bigint;
begin
    select s.*
      into v_slip
      from public.store_slips s
     where s.slip_id = p_slip_id
       and s.department_id = p_department_id
       and s.status = 'checked_out'
     for update;

    if v_slip.slip_id is null then
        raise exception 'store_checkout_not_found';
    end if;

    select b.status
      into v_business_day_status
    from public.store_business_days b
    where b.business_day_id = v_slip.business_day_id
    for key share;

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
    limit 1
    for update;

    if v_checkout.checkout_id is null then
        raise exception 'store_checkout_not_found';
    end if;

    if store.invalidate_slip_accounting_snapshot(
        p_department_id,
        p_slip_id,
        'checkout_cancelled'
    ) <> 1 then
        raise exception 'checkout_snapshot_not_found';
    end if;

    -- 会計準備で固定した料金は取消時に無効化します。
    -- 再オープン後は現在の料金プランから見積り、次の伝票発行で新しい版を作ります。
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

    update public.store_checkout_payments p
       set status = 'cancelled',
           updated_at = now()
     where p.checkout_id = v_checkout.checkout_id
       and p.status = 'confirmed';

    update public.store_slip_cast_sales_adjustments a
       set status = 'cancelled',
           updated_at = now()
     where a.department_id = p_department_id
       and a.slip_id = p_slip_id
       and a.checkout_id = v_checkout.checkout_id
       and a.status = 'confirmed';

    update public.store_checkouts c
       set status = 'cancelled',
           updated_at = now()
     where c.checkout_id = v_checkout.checkout_id
       and c.status = 'confirmed'
     returning c.checkout_id
      into v_cancelled_checkout_id;

    if v_cancelled_checkout_id is null then
        raise exception 'store_checkout_not_found';
    end if;

    update public.store_slip_customers c
       set left_at = null,
           left_at_source = null,
           status = 'active',
           updated_at = now()
     where c.slip_id = p_slip_id
       and c.status = 'left'
       and c.left_at_source = 'accounting_slip';

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
     where s.slip_id = p_slip_id
       and s.department_id = p_department_id
       and s.status = 'checked_out'
     returning s.slip_id
      into v_reopened_slip_id;

    if v_reopened_slip_id is null then
        raise exception 'store_checkout_not_found';
    end if;

    return query select v_cancelled_checkout_id;
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
