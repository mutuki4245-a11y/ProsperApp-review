drop function if exists store.get_slip_detail(bigint, bigint);

create or replace function store.get_slip_detail(
    p_department_id bigint,
    p_slip_id bigint
)
returns table (
    row_type text,
    slip_id bigint,
    slip_no text,
    business_date date,
    table_id bigint,
    table_code text,
    table_name text,
    opened_at timestamp with time zone,
    status text,
    customer_count integer,
    memo text,
    slip_customer_id bigint,
    line_no integer,
    customer_label text,
    entered_at timestamp with time zone,
    left_at timestamp with time zone,
    customer_status text,
    slip_cast_id bigint,
    cast_id bigint,
    cast_display_name text,
    cast_department_name text,
    nomination_type text,
    started_at timestamp with time zone,
    nomination_status text,
    nomination_price numeric,
    order_line_id bigint,
    item_name_snapshot text,
    item_type text,
    quantity numeric,
    unit_price numeric,
    amount numeric,
    ordered_at timestamp with time zone,
    order_status text,
    order_back_cast_id bigint,
    order_back_cast_display_name text,
    order_back_cast_department_name text,
    charge_line_id bigint,
    charge_type text,
    nomination_kind text,
    nomination_display_name text
)
language sql
security definer
set search_path = public
as $$
    select
        'slip'::text as row_type,
        s.slip_id,
        s.slip_no,
        s.business_date,
        s.table_id,
        t.table_code,
        t.table_name,
        s.opened_at,
        s.status,
        s.customer_count,
        s.memo,
        null::bigint as slip_customer_id,
        null::integer as line_no,
        null::text as customer_label,
        null::timestamp with time zone as entered_at,
        null::timestamp with time zone as left_at,
        null::text as customer_status,
        null::bigint as slip_cast_id,
        null::bigint as cast_id,
        null::text as cast_display_name,
        null::text as cast_department_name,
        null::text as nomination_type,
        null::timestamp with time zone as started_at,
        null::text as nomination_status,
        null::numeric as nomination_price,
        null::bigint as order_line_id,
        null::text as item_name_snapshot,
        null::text as item_type,
        null::numeric as quantity,
        null::numeric as unit_price,
        null::numeric as amount,
        null::timestamp with time zone as ordered_at,
        null::text as order_status,
        null::bigint as order_back_cast_id,
        null::text as order_back_cast_display_name,
        null::text as order_back_cast_department_name,
        null::bigint as charge_line_id,
        null::text as charge_type,
        null::text as nomination_kind,
        null::text as nomination_display_name
    from public.store_slips s
    left join public.store_table_master t
      on t.table_id = s.table_id
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id

    union all

    select
        'customer'::text,
        s.slip_id,
        s.slip_no,
        s.business_date,
        s.table_id,
        t.table_code,
        t.table_name,
        s.opened_at,
        s.status,
        s.customer_count,
        s.memo,
        c.slip_customer_id,
        c.line_no,
        c.customer_label,
        c.entered_at,
        c.left_at,
        c.status,
        null::bigint,
        null::bigint,
        null::text,
        null::text,
        null::text,
        null::timestamp with time zone,
        null::text,
        null::numeric,
        null::bigint,
        null::text,
        null::text,
        null::numeric,
        null::numeric,
        null::numeric,
        null::timestamp with time zone,
        null::text,
        null::bigint,
        null::text,
        null::text,
        null::bigint,
        null::text,
        null::text,
        null::text
    from public.store_slips s
    join public.store_slip_customers c
      on c.slip_id = s.slip_id
    left join public.store_table_master t
      on t.table_id = s.table_id
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id

    union all

    select
        'nomination'::text,
        s.slip_id,
        s.slip_no,
        s.business_date,
        s.table_id,
        t.table_code,
        t.table_name,
        s.opened_at,
        s.status,
        s.customer_count,
        s.memo,
        null::bigint,
        null::integer,
        null::text,
        null::timestamp with time zone,
        null::timestamp with time zone,
        null::text,
        sc.slip_cast_id,
        sc.cast_id,
        cm.display_name,
        d.department_name,
        sc.nomination_type,
        sc.started_at,
        sc.status,
        coalesce(sc.nomination_price, 0),
        null::bigint,
        null::text,
        null::text,
        null::numeric,
        null::numeric,
        null::numeric,
        null::timestamp with time zone,
        null::text,
        null::bigint,
        null::text,
        null::text,
        null::bigint,
        null::text,
        sc.nomination_kind,
        m.display_name
    from public.store_slips s
    join public.store_slip_casts sc
      on sc.slip_id = s.slip_id
    join public.cast_master cm
      on cm.cast_id = sc.cast_id
    left join public.department_master d
      on d.department_id = cm.department_id
    left join public.store_nomination_back_master m
      on m.company_id = s.company_id
     and m.department_id = s.department_id
     and m.nomination_kind = sc.nomination_kind
    left join public.store_table_master t
      on t.table_id = s.table_id
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id

    union all

    select
        'order'::text,
        s.slip_id,
        s.slip_no,
        s.business_date,
        s.table_id,
        t.table_code,
        t.table_name,
        s.opened_at,
        s.status,
        s.customer_count,
        s.memo,
        null::bigint,
        ol.line_no,
        null::text,
        null::timestamp with time zone,
        null::timestamp with time zone,
        null::text,
        null::bigint,
        null::bigint,
        null::text,
        null::text,
        null::text,
        null::timestamp with time zone,
        null::text,
        null::numeric,
        ol.order_line_id,
        ol.item_name_snapshot,
        coalesce(i.item_type, 'standard'),
        ol.quantity,
        ol.unit_price,
        ol.amount,
        ol.ordered_at,
        ol.status,
        ob.cast_id,
        ob.cast_display_name,
        ob.cast_department_name,
        null::bigint,
        null::text,
        null::text,
        null::text
    from public.store_slips s
    join public.store_order_lines ol
      on ol.slip_id = s.slip_id
    left join public.store_item_master i
      on i.item_id = ol.item_id
    left join lateral (
        select
            b.cast_id,
            cm.display_name as cast_display_name,
            d.department_name as cast_department_name
        from public.store_order_line_cast_backs b
        join public.cast_master cm
          on cm.cast_id = b.cast_id
        left join public.department_master d
          on d.department_id = cm.department_id
        where b.order_line_id = ol.order_line_id
          and b.status = 'active'
        order by b.order_line_cast_back_id desc
        limit 1
    ) ob on true
    left join public.store_table_master t
      on t.table_id = s.table_id
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id

    union all

    select
        'charge'::text,
        s.slip_id,
        s.slip_no,
        s.business_date,
        s.table_id,
        t.table_code,
        t.table_name,
        s.opened_at,
        s.status,
        s.customer_count,
        s.memo,
        null::bigint,
        cl.line_no,
        null::text,
        null::timestamp with time zone,
        null::timestamp with time zone,
        null::text,
        null::bigint,
        null::bigint,
        null::text,
        null::text,
        null::text,
        null::timestamp with time zone,
        null::text,
        null::numeric,
        null::bigint,
        cl.line_name,
        null::text,
        cl.quantity,
        cl.unit_price,
        cl.amount,
        cl.created_at,
        cl.status,
        null::bigint,
        null::text,
        null::text,
        cl.charge_line_id,
        cl.charge_type,
        null::text,
        null::text
    from public.store_slips s
    join public.store_slip_charge_lines cl
      on cl.slip_id = s.slip_id
    left join public.store_table_master t
      on t.table_id = s.table_id
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id
      and cl.charge_type = 'adjustment'
    order by row_type desc, line_no asc nulls first, ordered_at asc nulls first, started_at asc nulls first;
$$;

drop function if exists store.add_slip_customers(bigint, bigint, text[]);

create or replace function store.add_slip_customers(
    p_department_id bigint,
    p_slip_id bigint,
    p_customer_labels text[] default array[]::text[],
    p_entered_at timestamp with time zone default null
)
returns table (
    inserted_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip public.store_slips%rowtype;
    v_line_no integer;
    v_label text;
    v_index integer;
    v_inserted_count integer := 0;
begin
    select *
      into v_slip
    from public.store_slips s
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id
      and s.status = 'open'
    limit 1;

    if v_slip.slip_id is null then
        raise exception 'store_slip_not_found';
    end if;

    if coalesce(array_length(p_customer_labels, 1), 0) <= 0 then
        raise exception 'invalid_customer_count';
    end if;

    v_line_no := coalesce((
        select max(c.line_no)
        from public.store_slip_customers c
        where c.slip_id = p_slip_id
    ), 0);

    for v_index in 1..array_length(p_customer_labels, 1) loop
        v_label := nullif(trim(coalesce(p_customer_labels[v_index], '')), '');
        v_line_no := v_line_no + 1;

        insert into public.store_slip_customers (
            slip_id,
            line_no,
            customer_label,
            entered_at,
            status
        )
        values (
            p_slip_id,
            v_line_no,
            v_label,
            coalesce(p_entered_at, now()),
            'active'
        );

        v_inserted_count := v_inserted_count + 1;
    end loop;

    update public.store_slips s
       set customer_count = (
               select count(*)::integer
               from public.store_slip_customers c
               where c.slip_id = p_slip_id
                 and c.status = 'active'
           ),
           updated_at = now()
     where s.slip_id = p_slip_id;

    return query select v_inserted_count;
end;
$$;

create or replace function store.add_slip_nominations(
    p_department_id bigint,
    p_slip_id bigint,
    p_cast_nominations jsonb default '[]'::jsonb
)
returns table (
    inserted_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_slip public.store_slips%rowtype;
    v_nomination jsonb;
    v_cast_id bigint;
    v_nomination_kind text;
    v_nomination_type text;
    v_nomination_price numeric(12, 0);
    v_companion_time time;
    v_started_at timestamp with time zone;
    v_slip_cast_id bigint;
    v_nomination_fee_item public.store_item_master%rowtype;
    v_line_no integer;
    v_inserted_count integer := 0;
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
        raise exception 'store_slip_not_found';
    end if;

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
                then (((v_slip.business_date + case when v_companion_time < time '12:00' then 1 else 0 end)::timestamp + v_companion_time) at time zone 'Asia/Tokyo')
            else now()
        end;

        if not exists (
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
            raise exception 'store_cast_not_found';
        end if;

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
            p_slip_id,
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
            p_slip_id,
            v_slip.business_day_id,
            v_slip.business_date,
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
          into v_line_no
        from public.store_order_lines ol
        where ol.slip_id = p_slip_id;

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
            p_slip_id,
            v_line_no,
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

        v_inserted_count := v_inserted_count + 1;
    end loop;

    return query select v_inserted_count;
end;
$$;

create or replace function store.leave_slip_customer(
    p_department_id bigint,
    p_slip_customer_id bigint,
    p_left_at timestamp with time zone
)
returns table (
    slip_customer_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_customer public.store_slip_customers%rowtype;
    v_slip_id bigint;
begin
    select c.*
      into v_customer
    from public.store_slip_customers c
    join public.store_slips s
      on s.slip_id = c.slip_id
    where c.slip_customer_id = p_slip_customer_id
      and s.department_id = p_department_id
      and s.status = 'open'
      and c.status = 'active'
    limit 1;

    if v_customer.slip_customer_id is null then
        raise exception 'store_slip_customer_not_found';
    end if;

    if p_left_at < v_customer.entered_at then
        raise exception 'invalid_left_at';
    end if;

    v_slip_id := v_customer.slip_id;

    update public.store_slip_customers c
       set left_at = p_left_at,
           status = 'left',
           updated_at = now()
     where c.slip_customer_id = p_slip_customer_id;

    update public.store_slips s
       set customer_count = (
               select count(*)::integer
               from public.store_slip_customers c
               where c.slip_id = v_slip_id
                 and c.status = 'active'
           ),
           updated_at = now()
     where s.slip_id = v_slip_id;

    return query select p_slip_customer_id;
end;
$$;

create or replace function store.save_slip_adjustments(
    p_department_id bigint,
    p_slip_id bigint,
    p_adjustment_lines jsonb default '[]'::jsonb
)
returns table (
    saved_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip public.store_slips%rowtype;
    v_line jsonb;
    v_line_name text;
    v_amount numeric(12, 0);
    v_line_no integer;
    v_saved_count integer := 0;
begin
    select *
      into v_slip
    from public.store_slips s
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id
      and s.status = 'open'
    limit 1;

    if v_slip.slip_id is null then
        raise exception 'store_slip_not_found';
    end if;

    update public.store_slip_charge_lines cl
       set status = 'voided',
           updated_at = now()
     where cl.slip_id = p_slip_id
       and cl.charge_type = 'adjustment'
       and cl.status = 'active';

    v_line_no := coalesce((
        select max(cl.line_no)
        from public.store_slip_charge_lines cl
        where cl.slip_id = p_slip_id
    ), 0);

    for v_line in
        select value
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_adjustment_lines) = 'array' then p_adjustment_lines
                else '[]'::jsonb
            end
        )
    loop
        v_line_name := nullif(trim(coalesce(v_line->>'line_name', '')), '');
        v_amount := nullif(v_line->>'amount', '')::numeric;

        if v_line_name is null or length(v_line_name) > 160 then
            raise exception 'invalid_adjustment_name';
        end if;

        if v_amount is null or v_amount <> trunc(v_amount) or abs(v_amount) > 99999999 then
            raise exception 'invalid_adjustment_amount';
        end if;

        v_line_no := v_line_no + 1;

        insert into public.store_slip_charge_lines (
            slip_id,
            business_day_id,
            business_date,
            company_id,
            department_id,
            line_no,
            charge_type,
            line_name,
            quantity,
            unit_price,
            amount,
            status
        )
        values (
            p_slip_id,
            v_slip.business_day_id,
            v_slip.business_date,
            v_slip.company_id,
            v_slip.department_id,
            v_line_no,
            'adjustment',
            v_line_name,
            1,
            v_amount,
            v_amount,
            'active'
        );

        v_saved_count := v_saved_count + 1;
    end loop;

    return query select v_saved_count;
end;
$$;

create or replace function store.add_slip_adjustment(
    p_department_id bigint,
    p_slip_id bigint,
    p_line_name text,
    p_amount numeric
)
returns table (
    inserted_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip public.store_slips%rowtype;
    v_line_name text;
    v_amount numeric(12, 0);
    v_line_no integer;
begin
    select *
      into v_slip
    from public.store_slips s
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id
      and s.status = 'open'
    limit 1;

    if v_slip.slip_id is null then
        raise exception 'store_slip_not_found';
    end if;

    v_line_name := nullif(trim(coalesce(p_line_name, '')), '');
    v_amount := p_amount;

    if v_line_name is null or length(v_line_name) > 160 then
        raise exception 'invalid_adjustment_name';
    end if;

    if v_amount is null or v_amount <> trunc(v_amount) or abs(v_amount) > 99999999 then
        raise exception 'invalid_adjustment_amount';
    end if;

    select coalesce(max(cl.line_no), 0) + 1
      into v_line_no
    from public.store_slip_charge_lines cl
    where cl.slip_id = p_slip_id;

    insert into public.store_slip_charge_lines (
        slip_id,
        business_day_id,
        business_date,
        company_id,
        department_id,
        line_no,
        charge_type,
        line_name,
        quantity,
        unit_price,
        amount,
        status
    )
    values (
        p_slip_id,
        v_slip.business_day_id,
        v_slip.business_date,
        v_slip.company_id,
        v_slip.department_id,
        v_line_no,
        'adjustment',
        v_line_name,
        1,
        v_amount,
        v_amount,
        'active'
    );

    return query select 1;
end;
$$;

create or replace function store.save_karaoke_lines(
    p_department_id bigint,
    p_business_day_id bigint,
    p_karaoke_lines jsonb default '[]'::jsonb
)
returns table (
    saved_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_line jsonb;
    v_slip public.store_slips%rowtype;
    v_karaoke_item public.store_item_master%rowtype;
    v_slip_id bigint;
    v_quantity numeric(10, 2);
    v_line_no integer;
    v_order_line_id bigint;
    v_entered_at timestamp with time zone;
    v_saved_count integer := 0;
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

    if not exists (
        select 1
        from public.store_business_days b
        where b.business_day_id = p_business_day_id
          and b.department_id = p_department_id
          and b.status = 'open'
    ) then
        raise exception 'business_day_not_open';
    end if;

    select *
      into v_karaoke_item
    from public.store_item_master i
    where i.company_id = v_company_id
      and i.department_id = p_department_id
      and i.item_type = 'karaoke'
      and i.is_active = true
    order by i.sort_order asc, i.item_id asc
    limit 1;

    if v_karaoke_item.item_id is null then
        raise exception 'store_karaoke_item_not_found';
    end if;

    for v_line in
        select value
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_karaoke_lines) = 'array' then p_karaoke_lines
                else '[]'::jsonb
            end
        )
    loop
        v_slip_id := nullif(v_line->>'slip_id', '')::bigint;
        v_quantity := coalesce(nullif(v_line->>'quantity', '')::numeric, 0);

        if v_slip_id is null or v_quantity < 0 or v_quantity <> trunc(v_quantity) or v_quantity > 999 then
            raise exception 'invalid_karaoke_quantity';
        end if;

        select *
          into v_slip
        from public.store_slips s
        where s.slip_id = v_slip_id
          and s.department_id = p_department_id
          and s.business_day_id = p_business_day_id
          and s.status = 'open'
        limit 1;

        if v_slip.slip_id is null then
            raise exception 'store_slip_not_found';
        end if;

        select coalesce(min(c.entered_at), v_slip.opened_at)
          into v_entered_at
        from public.store_slip_customers c
        where c.slip_id = v_slip_id
          and c.status <> 'cancelled';

        select ol.order_line_id
          into v_order_line_id
        from public.store_order_lines ol
        join public.store_item_master i
          on i.item_id = ol.item_id
        where ol.slip_id = v_slip_id
          and ol.status = 'active'
          and i.item_type = 'karaoke'
        order by ol.line_no asc
        limit 1;

        update public.store_order_lines ol
           set status = 'voided',
               voided_at = coalesce(ol.voided_at, now()),
               updated_at = now()
        from public.store_item_master i
        where i.item_id = ol.item_id
          and ol.slip_id = v_slip_id
          and ol.status = 'active'
          and i.item_type = 'karaoke'
          and (v_order_line_id is null or ol.order_line_id <> v_order_line_id);

        if v_quantity <= 0 then
            if v_order_line_id is not null then
                update public.store_order_lines ol
                   set status = 'voided',
                       voided_at = coalesce(ol.voided_at, now()),
                       updated_at = now()
                 where ol.order_line_id = v_order_line_id;
            end if;
        elsif v_order_line_id is null then
            v_line_no := coalesce((
                select max(ol.line_no)
                from public.store_order_lines ol
                where ol.slip_id = v_slip_id
            ), 0) + 1;

            insert into public.store_order_lines (
                slip_id,
                line_no,
                item_id,
                item_name_snapshot,
                quantity,
                unit_price,
                amount,
                ordered_at,
                status
            )
            values (
                v_slip_id,
                v_line_no,
                v_karaoke_item.item_id,
                v_karaoke_item.item_name,
                v_quantity,
                v_karaoke_item.default_price,
                v_karaoke_item.default_price * v_quantity,
                v_entered_at,
                'active'
            );
        else
            update public.store_order_lines ol
               set item_id = v_karaoke_item.item_id,
                   item_name_snapshot = v_karaoke_item.item_name,
                   quantity = v_quantity,
                   unit_price = v_karaoke_item.default_price,
                   amount = v_karaoke_item.default_price * v_quantity,
                   ordered_at = v_entered_at,
                   status = 'active',
                   voided_at = null,
                   updated_at = now()
             where ol.order_line_id = v_order_line_id;
        end if;

        update public.store_slip_charge_lines cl
           set status = 'voided',
               updated_at = now()
         where cl.slip_id = v_slip_id
           and cl.charge_type = 'karaoke'
           and cl.status = 'active';

        v_saved_count := v_saved_count + 1;
    end loop;

    return query select v_saved_count;
end;
$$;

drop function if exists store.save_order_line_quantities(bigint, bigint, jsonb);

create or replace function store.save_order_line_quantities(
    p_department_id bigint,
    p_slip_id bigint,
    p_order_lines jsonb default '[]'::jsonb
)
returns table (
    saved_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_slip public.store_slips%rowtype;
    v_line jsonb;
    v_order_line public.store_order_lines%rowtype;
    v_order_line_id_text text;
    v_quantity_text text;
    v_order_line_id bigint;
    v_quantity numeric(10, 0);
    v_saved_count integer := 0;
begin
    select *
      into v_slip
    from public.store_slips s
    where s.slip_id = p_slip_id
      and s.department_id = p_department_id
      and s.status = 'open'
    limit 1;

    if v_slip.slip_id is null then
        raise exception 'store_slip_not_found';
    end if;

    for v_line in
        select value
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_order_lines) = 'array' then p_order_lines
                else '[]'::jsonb
            end
        )
    loop
        v_order_line_id_text := nullif(trim(coalesce(v_line->>'order_line_id', '')), '');
        v_quantity_text := nullif(trim(coalesce(v_line->>'quantity', '')), '');

        if v_order_line_id_text is null or v_order_line_id_text !~ '^[0-9]+$' then
            raise exception 'store_order_line_not_found';
        end if;

        if v_quantity_text is null or v_quantity_text !~ '^[0-9]+(\.0+)?$' then
            raise exception 'invalid_order_quantity';
        end if;

        v_order_line_id := v_order_line_id_text::bigint;
        v_quantity := v_quantity_text::numeric;

        if v_quantity < 0 or v_quantity > 999 or v_quantity <> trunc(v_quantity) then
            raise exception 'invalid_order_quantity';
        end if;

        select ol.*
          into v_order_line
        from public.store_order_lines ol
        left join public.store_item_master i
          on i.item_id = ol.item_id
        where ol.order_line_id = v_order_line_id
          and ol.slip_id = p_slip_id
          and ol.status = 'active'
          and coalesce(i.item_type, 'standard') = 'standard'
        limit 1;

        if not found then
            raise exception 'store_order_line_not_found';
        end if;

        if v_quantity <= 0 then
            update public.store_order_lines ol
               set status = 'voided',
                   voided_at = coalesce(ol.voided_at, now()),
                   updated_at = now()
             where ol.order_line_id = v_order_line_id;

            update public.store_order_line_cast_backs b
               set status = 'voided',
                   updated_at = now()
             where b.order_line_id = v_order_line_id
               and b.status = 'active';
        else
            update public.store_order_lines ol
               set quantity = v_quantity,
                   amount = ol.unit_price * v_quantity,
                   updated_at = now()
             where ol.order_line_id = v_order_line_id;

            update public.store_order_line_cast_backs b
               set quantity = v_quantity,
                   back_amount = b.back_unit_amount * v_quantity,
                   updated_at = now()
             where b.order_line_id = v_order_line_id
               and b.status = 'active';
        end if;

        v_saved_count := v_saved_count + 1;
    end loop;

    return query select v_saved_count;
end;
$$;

create or replace function store.update_slip_customer_label(
    p_department_id bigint,
    p_slip_customer_id bigint,
    p_customer_label text default null
)
returns table (
    slip_customer_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
begin
    return query
    update public.store_slip_customers c
       set customer_label = nullif(trim(coalesce(p_customer_label, '')), ''),
           updated_at = now()
      from public.store_slips s
     where s.slip_id = c.slip_id
       and c.slip_customer_id = p_slip_customer_id
       and s.department_id = p_department_id
       and s.status in ('open', 'checked_out')
       and c.status <> 'cancelled'
    returning c.slip_customer_id;

    if not found then
        raise exception 'store_slip_customer_not_found';
    end if;
end;
$$;

create or replace function store.void_order_line(
    p_department_id bigint,
    p_order_line_id bigint
)
returns table (
    order_line_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_order_line public.store_order_lines%rowtype;
begin
    select ol.*
      into v_order_line
    from public.store_order_lines ol
    join public.store_slips s
      on s.slip_id = ol.slip_id
    left join public.store_item_master i
      on i.item_id = ol.item_id
    where ol.order_line_id = p_order_line_id
      and s.department_id = p_department_id
      and s.status = 'open'
      and ol.status = 'active'
      and coalesce(i.item_type, 'standard') = 'standard'
    limit 1;

    if v_order_line.order_line_id is null then
        raise exception 'store_order_line_not_found';
    end if;

    update public.store_order_lines ol
       set status = 'voided',
           updated_at = now()
     where ol.order_line_id = p_order_line_id;

    update public.store_order_line_cast_backs b
       set status = 'voided',
           updated_at = now()
     where b.order_line_id = p_order_line_id
       and b.status = 'active';

    return query select p_order_line_id;
end;
$$;
