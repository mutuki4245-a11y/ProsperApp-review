-- 営業中トップ用の全伝票スナップショットと、1操作単位の編集RPCです。
-- このファイルは 00_schema.sql ～ 05_checkout.sql の後、99_grants.sql の前に適用します。

drop function if exists store.get_business_day_snapshot(bigint, bigint);

create or replace function store.get_business_day_snapshot(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    business_day_revision bigint,
    snapshot jsonb
)
language sql
security definer
set search_path = public
as $$
    with target_day as (
        select
            b.business_day_id,
            b.business_date,
            b.business_ui_revision
        from public.store_business_days b
        where b.business_day_id = p_business_day_id
          and b.department_id = p_department_id
    ),
    target_slips as (
        select
            s.slip_id,
            s.slip_no,
            s.company_id,
            s.department_id,
            s.table_id,
            t.table_code,
            t.table_name,
            s.opened_at,
            s.status,
            s.customer_count,
            s.memo
        from public.store_slips s
        join target_day d
          on d.business_day_id = s.business_day_id
        left join public.store_table_master t
          on t.table_id = s.table_id
    ),
    customer_summary as (
        select
            c.slip_id,
            count(*) filter (where c.status = 'active')::integer as customer_count,
            string_agg(
                coalesce(nullif(c.customer_label, ''), '客' || c.line_no::text),
                '、' order by c.line_no
            ) filter (where c.status = 'active') as customer_names
        from public.store_slip_customers c
        join target_slips s on s.slip_id = c.slip_id
        group by c.slip_id
    ),
    cast_summary as (
        select
            rows.slip_id,
            string_agg(rows.display_name, '、' order by rows.started_at asc nulls last, rows.slip_cast_id asc) as cast_names
        from (
            select distinct on (sc.slip_id, sc.cast_id)
                sc.slip_id,
                sc.slip_cast_id,
                sc.started_at,
                cm.display_name
            from public.store_slip_casts sc
            join target_slips s on s.slip_id = sc.slip_id
            join public.cast_master cm on cm.cast_id = sc.cast_id
            where sc.status <> 'cancelled'
              and sc.nomination_type in ('nomination', 'in_store', 'companion')
            order by sc.slip_id, sc.cast_id, sc.started_at asc nulls last, sc.slip_cast_id asc
        ) rows
        group by rows.slip_id
    ),
    order_summary as (
        select
            l.slip_id,
            count(*) filter (where l.status = 'active')::integer as order_count,
            coalesce(sum(l.amount) filter (where l.status = 'active'), 0) as order_subtotal_amount,
            coalesce(sum(l.quantity) filter (where l.status = 'active' and coalesce(i.item_type, 'standard') = 'karaoke'), 0) as karaoke_quantity
        from public.store_order_lines l
        join target_slips s on s.slip_id = l.slip_id
        left join public.store_item_master i on i.item_id = l.item_id
        group by l.slip_id
    ),
    charge_summary as (
        select
            cl.slip_id,
            coalesce(sum(cl.amount) filter (where cl.charge_type = 'adjustment' and cl.status = 'active'), 0) as adjustment_amount
        from public.store_slip_charge_lines cl
        join target_slips s on s.slip_id = cl.slip_id
        group by cl.slip_id
    ),
    summaries as (
        select
            s.*,
            coalesce(cs.customer_count, s.customer_count) as customer_count_display,
            coalesce(cs.customer_names, '') as customer_names,
            coalesce(casts.cast_names, '') as cast_names,
            coalesce(os.order_count, 0)::integer as order_count,
            coalesce(os.order_subtotal_amount, 0) as order_subtotal_amount,
            coalesce(charges.adjustment_amount, 0) as adjustment_amount,
            greatest(
                coalesce(os.order_subtotal_amount, 0) +
                round(coalesce(os.order_subtotal_amount, 0) * 0.20, 0) +
                coalesce(charges.adjustment_amount, 0),
                0
            ) as accounting_amount,
            coalesce(os.karaoke_quantity, 0) as karaoke_quantity
        from target_slips s
        left join customer_summary cs on cs.slip_id = s.slip_id
        left join cast_summary casts on casts.slip_id = s.slip_id
        left join order_summary os on os.slip_id = s.slip_id
        left join charge_summary charges on charges.slip_id = s.slip_id
    ),
    slip_payloads as (
        select
            s.slip_id,
            s.opened_at,
            jsonb_build_object(
                'id', s.slip_id,
                'slipNo', s.slip_no,
                'tableDisplay', coalesce(nullif(concat_ws(' ', s.table_code, s.table_name), ''), '-'),
                'openedAt', s.opened_at,
                'openedTime', to_char(s.opened_at at time zone 'Asia/Tokyo', 'HH24:MI'),
                'status', s.status,
                'statusDisplay', case s.status
                    when 'open' then '在席'
                    when 'checkout_ready' then '会計準備中'
                    when 'checked_out' then '会計済み'
                    when 'cancelled' then '取消'
                    else s.status end,
                'statusBadgeClass', case s.status
                    when 'open' then 'text-bg-success'
                    when 'checkout_ready' then 'text-bg-warning'
                    when 'checked_out' then 'text-bg-secondary'
                    when 'cancelled' then 'text-bg-danger'
                    else 'text-bg-secondary' end,
                'customerCount', s.customer_count_display,
                'customerNames', coalesce(nullif(s.customer_names, ''), '客名なし'),
                'castNames', coalesce(nullif(s.cast_names, ''), '指名なし'),
                'orderCount', s.order_count,
                'orderSubtotalAmount', s.order_subtotal_amount,
                'adjustmentAmount', s.adjustment_amount,
                'memo', coalesce(nullif(s.memo, ''), '-'),
                'accountingAmount', s.accounting_amount,
                'karaokeQuantity', s.karaoke_quantity,
                'customers', coalesce((
                    select jsonb_agg(jsonb_build_object(
                        'id', c.slip_customer_id,
                        'lineNo', c.line_no,
                        'displayName', coalesce(nullif(c.customer_label, ''), '客' || c.line_no::text),
                        'customerLabel', c.customer_label,
                        'enteredAt', c.entered_at,
                        'enteredTime', to_char(c.entered_at at time zone 'Asia/Tokyo', 'HH24:MI'),
                        'leftAt', c.left_at,
                        'leftTime', case when c.left_at is null then null else to_char(c.left_at at time zone 'Asia/Tokyo', 'HH24:MI') end,
                        'status', c.status
                    ) order by c.line_no)
                    from public.store_slip_customers c
                    where c.slip_id = s.slip_id
                ), '[]'::jsonb),
                'nominations', coalesce((
                    select jsonb_agg(jsonb_build_object(
                        'id', sc.slip_cast_id,
                        'castId', sc.cast_id,
                        'displayName', cm.display_name,
                        'departmentName', d.department_name,
                        'nominationKind', sc.nomination_kind,
                        'nominationType', sc.nomination_type,
                        'nominationDisplayName', coalesce(nm.display_name, sc.nomination_kind, sc.nomination_type),
                        'nominationPrice', sc.nomination_price,
                        'startedAt', sc.started_at,
                        'startedTime', case when sc.started_at is null then null else to_char(sc.started_at at time zone 'Asia/Tokyo', 'HH24:MI') end,
                        'status', sc.status
                    ) order by sc.started_at asc nulls last, sc.slip_cast_id asc)
                    from public.store_slip_casts sc
                    join public.cast_master cm on cm.cast_id = sc.cast_id
                    left join public.department_master d on d.department_id = cm.department_id
                    left join public.store_nomination_back_master nm
                      on nm.company_id = cm.company_id
                     and nm.department_id = s.department_id
                     and nm.nomination_kind = sc.nomination_kind
                     and nm.back_type = 'nomination'
                    where sc.slip_id = s.slip_id
                ), '[]'::jsonb),
                'orders', coalesce((
                    select jsonb_agg(jsonb_build_object(
                        'id', ol.order_line_id,
                        'lineNo', ol.line_no,
                        'itemName', ol.item_name_snapshot,
                        'itemType', coalesce(i.item_type, case when ol.source_type = 'nomination_fee' then 'nomination_fee' else 'standard' end),
                        'quantity', ol.quantity,
                        'unitPrice', ol.unit_price,
                        'amount', ol.amount,
                        'orderedAt', ol.ordered_at,
                        'orderedTime', to_char(ol.ordered_at at time zone 'Asia/Tokyo', 'HH24:MI'),
                        'status', ol.status,
                        'sourceType', ol.source_type,
                        'sourceId', ol.source_id,
                        'backCastId', back.cast_id,
                        'backCastDisplayName', cm.display_name,
                        'backCastDepartmentName', d.department_name
                    ) order by ol.ordered_at asc, ol.line_no asc)
                    from public.store_order_lines ol
                    left join public.store_item_master i on i.item_id = ol.item_id
                    left join lateral (
                        select b.cast_id
                        from public.store_order_line_cast_backs b
                        where b.order_line_id = ol.order_line_id
                          and b.status = 'active'
                        order by b.order_line_cast_back_id asc
                        limit 1
                    ) back on true
                    left join public.cast_master cm on cm.cast_id = back.cast_id
                    left join public.department_master d on d.department_id = cm.department_id
                    where ol.slip_id = s.slip_id
                ), '[]'::jsonb),
                'adjustments', coalesce((
                    select jsonb_agg(jsonb_build_object(
                        'id', cl.charge_line_id,
                        'lineNo', cl.line_no,
                        'lineName', cl.line_name,
                        'amount', cl.amount,
                        'createdAt', cl.created_at,
                        'createdTime', to_char(cl.created_at at time zone 'Asia/Tokyo', 'HH24:MI'),
                        'status', cl.status
                    ) order by cl.line_no asc)
                    from public.store_slip_charge_lines cl
                    where cl.slip_id = s.slip_id
                      and cl.charge_type = 'adjustment'
                ), '[]'::jsonb)
            ) as slip
        from summaries s
    )
    select
        d.business_ui_revision,
        jsonb_build_object(
            'businessDayId', d.business_day_id,
            'businessDate', to_char(d.business_date, 'YYYY-MM-DD'),
            'businessDateDisplay', to_char(d.business_date, 'YYYY-MM-DD'),
            'businessDayRevision', d.business_ui_revision,
            'hasBusinessDay', true,
            'openSlipCount', coalesce((select count(*) from summaries where status in ('open', 'checkout_ready')), 0),
            'checkedOutSlipCount', coalesce((select count(*) from summaries where status = 'checked_out'), 0),
            'estimatedSalesAmount', coalesce((select sum(accounting_amount) from summaries where status <> 'cancelled'), 0),
            'slips', coalesce((select jsonb_agg(sp.slip order by sp.opened_at asc) from slip_payloads sp), '[]'::jsonb)
        )
    from target_day d;
$$;

drop function if exists store.apply_business_slip_editor_operation(bigint, bigint, bigint, text, text, jsonb);

create or replace function store.apply_business_slip_editor_operation(
    p_department_id bigint,
    p_business_day_id bigint,
    p_slip_id bigint,
    p_operation_type text,
    p_operation_id text,
    p_payload jsonb default '{}'::jsonb
)
returns table (
    operation_id text,
    business_day_revision bigint,
    snapshot jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip public.store_slips%rowtype;
    v_customer_id bigint;
    v_customer_label text;
    v_time_text text;
    v_operation_at timestamp with time zone;
    v_nomination jsonb;
    v_adjustment_name text;
    v_adjustment_amount numeric(12, 0);
    v_slip_cast_id bigint;
    v_charge_line_id bigint;
    v_order_line_id bigint;
    v_order_item_id bigint;
    v_order_quantity integer;
    v_order_cast_back_cast_id bigint;
    v_revision bigint;
    v_snapshot jsonb;
begin
    if p_operation_type not in (
        'add_customer', 'update_customer', 'leave_customer',
        'add_nomination', 'cancel_nomination',
        'add_adjustment', 'void_adjustment',
        'add_order', 'void_order') then
        raise exception 'invalid_business_editor_operation';
    end if;

    if jsonb_typeof(p_payload) <> 'object' then
        raise exception 'invalid_business_editor_payload';
    end if;

    select *
      into v_slip
    from public.store_slips s
    join public.store_business_days b
      on b.business_day_id = s.business_day_id
     and b.department_id = p_department_id
     and b.status = 'open'
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id
      and s.business_day_id = p_business_day_id
      and s.status = 'open'
    for update;

    if v_slip.slip_id is null then
        raise exception 'store_slip_not_found';
    end if;

    if p_operation_type = 'add_customer' then
        v_customer_label := nullif(trim(coalesce(p_payload->>'customer_label', '')), '');
        v_time_text := nullif(trim(coalesce(p_payload->>'entered_time', '')), '');
        if v_customer_label is not null and char_length(v_customer_label) > 100 then
            raise exception 'invalid_customer_label';
        end if;
        if v_time_text is null or v_time_text !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$' then
            raise exception 'invalid_customer_time';
        end if;
        if mod(extract(minute from v_time_text::time)::integer, 5) <> 0 then
            raise exception 'invalid_customer_time';
        end if;
        v_operation_at := ((v_slip.business_date + case when v_time_text::time < time '12:00' then 1 else 0 end)::timestamp + v_time_text::time) at time zone 'Asia/Tokyo';
        if v_operation_at < v_slip.opened_at or v_operation_at > now() + interval '5 minutes' then
            raise exception 'invalid_customer_time';
        end if;
        perform store.add_slip_customers(p_department_id, p_slip_id, array[v_customer_label], v_operation_at);
    elsif p_operation_type = 'update_customer' then
        v_customer_id := nullif(p_payload->>'slip_customer_id', '')::bigint;
        v_customer_label := nullif(trim(coalesce(p_payload->>'customer_label', '')), '');
        if v_customer_id is null or (v_customer_label is not null and char_length(v_customer_label) > 100) or not exists (
            select 1 from public.store_slip_customers c
            where c.slip_customer_id = v_customer_id and c.slip_id = p_slip_id and c.status <> 'cancelled'
        ) then
            raise exception 'store_slip_customer_not_found';
        end if;
        perform store.update_slip_customer_label(p_department_id, v_customer_id, v_customer_label);
    elsif p_operation_type = 'leave_customer' then
        v_customer_id := nullif(p_payload->>'slip_customer_id', '')::bigint;
        v_time_text := nullif(trim(coalesce(p_payload->>'left_time', '')), '');
        if v_customer_id is null or v_time_text is null or v_time_text !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$' then
            raise exception 'invalid_customer_time';
        end if;
        if mod(extract(minute from v_time_text::time)::integer, 5) <> 0 then
            raise exception 'invalid_customer_time';
        end if;
        v_operation_at := ((v_slip.business_date + case when v_time_text::time < time '12:00' then 1 else 0 end)::timestamp + v_time_text::time) at time zone 'Asia/Tokyo';
        if v_operation_at > now() + interval '5 minutes' or not exists (
            select 1 from public.store_slip_customers c
            where c.slip_customer_id = v_customer_id and c.slip_id = p_slip_id and c.status = 'active'
        ) then
            raise exception 'invalid_left_at';
        end if;
        perform store.leave_slip_customer(p_department_id, v_customer_id, v_operation_at);
    elsif p_operation_type = 'add_nomination' then
        v_nomination := jsonb_build_array(jsonb_build_object(
            'cast_id', p_payload->>'cast_id',
            'nomination_kind', p_payload->>'nomination_kind',
            'nomination_price', p_payload->>'nomination_price'
        ));
        perform store.add_slip_nominations(p_department_id, p_slip_id, v_nomination);
    elsif p_operation_type = 'cancel_nomination' then
        if coalesce(p_payload->>'slip_cast_id', '') !~ '^[1-9][0-9]*$' then
            raise exception 'store_slip_nomination_not_found';
        end if;
        v_slip_cast_id := (p_payload->>'slip_cast_id')::bigint;
        perform store.cancel_slip_nomination(p_department_id, v_slip_cast_id);
    elsif p_operation_type = 'add_adjustment' then
        v_adjustment_name := nullif(trim(coalesce(p_payload->>'line_name', '')), '');
        v_adjustment_amount := nullif(p_payload->>'amount', '')::numeric;
        if v_adjustment_name is null or char_length(v_adjustment_name) > 160 then
            raise exception 'invalid_adjustment_name';
        end if;
        if v_adjustment_amount is null or v_adjustment_amount <> trunc(v_adjustment_amount) or abs(v_adjustment_amount) > 99999999 then
            raise exception 'invalid_adjustment_amount';
        end if;
        perform store.add_slip_adjustment(p_department_id, p_slip_id, v_adjustment_name, v_adjustment_amount);
    elsif p_operation_type = 'void_adjustment' then
        if coalesce(p_payload->>'charge_line_id', '') !~ '^[1-9][0-9]*$' then
            raise exception 'store_slip_adjustment_not_found';
        end if;
        v_charge_line_id := (p_payload->>'charge_line_id')::bigint;
        perform store.void_slip_adjustment(p_department_id, v_charge_line_id);
    elsif p_operation_type = 'add_order' then
        if coalesce(p_payload->>'item_id', '') !~ '^[1-9][0-9]*$' or
           coalesce(p_payload->>'quantity', '') !~ '^[1-9][0-9]*$' or
           (nullif(p_payload->>'cast_back_cast_id', '') is not null and p_payload->>'cast_back_cast_id' !~ '^[1-9][0-9]*$') then
            raise exception 'invalid_order_quantity';
        end if;
        v_order_item_id := (p_payload->>'item_id')::bigint;
        v_order_quantity := (p_payload->>'quantity')::integer;
        if v_order_quantity > 999 then
            raise exception 'invalid_order_quantity';
        end if;
        v_order_cast_back_cast_id := nullif(p_payload->>'cast_back_cast_id', '')::bigint;
        perform store.add_order_lines(
            p_department_id,
            p_slip_id,
            jsonb_build_array(jsonb_strip_nulls(jsonb_build_object(
                'item_id', v_order_item_id,
                'quantity', v_order_quantity,
                'cast_back_cast_id', v_order_cast_back_cast_id
            )))
        );
    else
        if coalesce(p_payload->>'order_line_id', '') !~ '^[1-9][0-9]*$' then
            raise exception 'store_order_line_not_found';
        end if;
        v_order_line_id := (p_payload->>'order_line_id')::bigint;
        perform store.void_order_line(p_department_id, v_order_line_id);
    end if;

    select s.business_day_revision, s.snapshot
      into v_revision, v_snapshot
    from store.get_business_day_snapshot(p_department_id, p_business_day_id) s;

    return query select p_operation_id, v_revision, v_snapshot;
end;
$$;
