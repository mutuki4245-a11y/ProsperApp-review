create or replace function store.get_tables(p_department_id bigint)
returns table (
    table_id bigint,
    table_code text,
    table_name text
)
language sql
security definer
set search_path = public
as $$
    select
        t.table_id,
        t.table_code,
        t.table_name
    from public.store_table_master t
    where t.department_id = p_department_id
      and t.is_active = true
    order by t.sort_order asc, t.table_code asc;
$$;

drop function if exists store.get_casts(bigint);

create or replace function store.get_casts(p_department_id bigint)
returns table (
    cast_id bigint,
    cast_code text,
    display_name text,
    department_name text
)
language sql
security definer
set search_path = public
as $$
    select
        c.cast_id,
        c.cast_code,
        c.display_name,
        d.department_name
    from public.cast_master c
    join public.department_master d
      on d.department_id = c.department_id
    where c.is_active = true
      and c.status = 'active'
      and d.is_active = true
    order by
        case when c.department_id = p_department_id then 0 else 1 end,
        d.company_id asc,
        d.department_name asc,
        c.sort_order asc,
        c.display_name asc;
$$;

drop function if exists store.get_cast_admin_list(bigint);
drop function if exists store.get_casts_admin(bigint);

create or replace function store.get_casts_admin(p_department_id bigint)
returns table (
    cast_id bigint,
    display_name text,
    joined_on date
)
language sql
security definer
set search_path = public
as $$
    select
        c.cast_id,
        c.display_name,
        c.joined_on
    from public.cast_master c
    join public.department_master d
      on d.department_id = c.department_id
    where c.department_id = p_department_id
      and d.is_active = true
      and c.is_active = true
      and c.status = 'active'
    order by c.joined_on asc, c.cast_id asc;
$$;

drop function if exists store.create_cast(bigint, text);

create or replace function store.create_cast(
    p_department_id bigint,
    p_display_name text
)
returns table (
    cast_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_display_name text;
    v_sort_order integer;
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

    v_display_name := nullif(trim(coalesce(p_display_name, '')), '');
    if v_display_name is null then
        raise exception 'invalid_store_cast';
    end if;

    select coalesce(max(c.sort_order), 0) + 10
      into v_sort_order
    from public.cast_master c
    where c.company_id = v_company_id
      and c.department_id = p_department_id;

    return query
    insert into public.cast_master (
        company_id,
        department_id,
        display_name,
        joined_on,
        status,
        sort_order,
        is_active
    )
    values (
        v_company_id,
        p_department_id,
        v_display_name,
        (now() at time zone 'Asia/Tokyo')::date,
        'active',
        v_sort_order,
        true
    )
    returning cast_master.cast_id;
end;
$$;

drop function if exists store.delete_cast(bigint, bigint);

create or replace function store.delete_cast(
    p_department_id bigint,
    p_cast_id bigint
)
returns table (
    cast_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_deleted_cast_id bigint;
begin
    update public.cast_master c
       set status = 'inactive',
           is_active = false,
           updated_at = now()
     where c.department_id = p_department_id
       and c.cast_id = p_cast_id
       and c.is_active = true
    returning c.cast_id
      into v_deleted_cast_id;

    if v_deleted_cast_id is null then
        raise exception 'store_cast_not_found';
    end if;

    return query
    select v_deleted_cast_id;
end;
$$;

drop function if exists store.get_business_day_slips(bigint, bigint);

create or replace function store.get_business_day_slips(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    slip_id bigint,
    slip_no text,
    table_id bigint,
    table_code text,
    table_name text,
    opened_at timestamp with time zone,
    status text,
    customer_count integer,
    customer_names text,
    cast_names text,
    accounting_amount numeric,
    karaoke_quantity numeric,
    memo text
)
language sql
security definer
set search_path = public
as $$
    with target_slips as (
        select
            s.slip_id,
            s.slip_no,
            s.table_id,
            t.table_code,
            t.table_name,
            s.opened_at,
            s.status,
            s.customer_count,
            s.memo
        from public.store_slips s
        left join public.store_table_master t
          on t.table_id = s.table_id
        where s.department_id = p_department_id
          and s.business_day_id = p_business_day_id
    ),
    customer_summary as (
        select
            c.slip_id,
            count(*) filter (where c.status <> 'cancelled')::integer as customer_count,
            string_agg(
                coalesce(nullif(c.customer_label, ''), '客' || c.line_no::text),
                '、'
                order by c.line_no
            ) filter (where c.status <> 'cancelled') as customer_names
        from target_slips s
        join public.store_slip_customers c
          on c.slip_id = s.slip_id
        group by c.slip_id
    ),
    cast_rows as (
        select
            sc.slip_id,
            sc.cast_id,
            cm.display_name as cast_display_name,
            min(sc.started_at) as first_started_at,
            min(sc.slip_cast_id) as first_slip_cast_id
        from target_slips s
        join public.store_slip_casts sc
          on sc.slip_id = s.slip_id
        join public.cast_master cm
          on cm.cast_id = sc.cast_id
        where sc.status <> 'cancelled'
          and sc.nomination_type in ('nomination', 'in_store', 'companion')
        group by sc.slip_id, sc.cast_id, cm.display_name
    ),
    cast_summary as (
        select
            cr.slip_id,
            string_agg(
                cr.cast_display_name,
                '、'
                order by cr.first_started_at asc nulls last, cr.first_slip_cast_id asc
            ) as cast_names
        from cast_rows cr
        group by cr.slip_id
    ),
    order_summary as (
        select
            l.slip_id,
            coalesce(sum(l.amount) filter (where l.status = 'active'), 0) as order_subtotal_amount,
            coalesce(sum(l.quantity) filter (where l.status = 'active' and i.item_type = 'karaoke'), 0) as karaoke_quantity
        from target_slips s
        join public.store_order_lines l
          on l.slip_id = s.slip_id
        left join public.store_item_master i
          on i.item_id = l.item_id
        group by l.slip_id
    ),
    charge_summary as (
        select
            cl.slip_id,
            coalesce(sum(cl.amount) filter (where cl.charge_type = 'adjustment' and cl.status = 'active'), 0) as charge_amount
        from target_slips s
        join public.store_slip_charge_lines cl
          on cl.slip_id = s.slip_id
        group by cl.slip_id
    )
    select
        ts.slip_id,
        ts.slip_no,
        ts.table_id,
        ts.table_code,
        ts.table_name,
        ts.opened_at,
        ts.status,
        coalesce(cs.customer_count, ts.customer_count) as customer_count,
        coalesce(cs.customer_names, '') as customer_names,
        coalesce(casts.cast_names, '') as cast_names,
        greatest(
            coalesce(os.order_subtotal_amount, 0) +
            round(coalesce(os.order_subtotal_amount, 0) * 0.20, 0) +
            coalesce(charges.charge_amount, 0),
            0
        ) as accounting_amount,
        coalesce(os.karaoke_quantity, 0) as karaoke_quantity,
        ts.memo
    from target_slips ts
    left join customer_summary cs
      on cs.slip_id = ts.slip_id
    left join cast_summary casts
      on casts.slip_id = ts.slip_id
    left join order_summary os
      on os.slip_id = ts.slip_id
    left join charge_summary charges
      on charges.slip_id = ts.slip_id
    order by ts.opened_at asc;
$$;

drop function if exists store.get_order_entry_slips(bigint, bigint);

create or replace function store.get_order_entry_slips(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    slip_id bigint,
    table_id bigint,
    table_code text,
    table_name text,
    opened_at timestamp with time zone,
    customer_count integer,
    customer_names text,
    memo text
)
language sql
security definer
set search_path = public
as $$
    with target_slips as (
        select
            s.slip_id,
            s.table_id,
            t.table_code,
            t.table_name,
            s.opened_at,
            s.customer_count,
            s.memo,
            t.sort_order
        from public.store_slips s
        left join public.store_table_master t
          on t.table_id = s.table_id
        where s.department_id = p_department_id
          and s.business_day_id = p_business_day_id
          and s.status = 'open'
    ),
    customer_summary as (
        select
            c.slip_id,
            count(*) filter (where c.status <> 'cancelled')::integer as customer_count,
            string_agg(
                coalesce(nullif(c.customer_label, ''), '客' || c.line_no::text),
                '、'
                order by c.line_no
            ) filter (where c.status <> 'cancelled') as customer_names
        from target_slips s
        join public.store_slip_customers c
          on c.slip_id = s.slip_id
        group by c.slip_id
    )
    select
        ts.slip_id,
        ts.table_id,
        ts.table_code,
        ts.table_name,
        ts.opened_at,
        coalesce(cs.customer_count, ts.customer_count) as customer_count,
        coalesce(cs.customer_names, '') as customer_names,
        ts.memo
    from target_slips ts
    left join customer_summary cs
      on cs.slip_id = ts.slip_id
    order by ts.sort_order asc nulls last, ts.table_code asc nulls last, ts.opened_at asc;
$$;

drop function if exists store.get_order_items(bigint);

create or replace function store.get_order_items(p_department_id bigint)
returns table (
    item_id bigint,
    item_name text,
    item_type text,
    default_price numeric,
    category_code text,
    category_name text,
    is_cast_back_target boolean,
    cast_back_regular_unit_amount numeric,
    cast_back_nomination_unit_amount numeric,
    cast_back_type text
)
language sql
security definer
set search_path = public
as $$
    select
        i.item_id,
        i.item_name,
        i.item_type,
        i.default_price,
        c.category_code,
        coalesce(c.category_name, '未分類') as category_name,
        i.is_cast_back_target,
        i.cast_back_regular_unit_amount,
        i.cast_back_nomination_unit_amount,
        i.cast_back_type
    from public.store_item_master i
    join public.store_item_category_master c
      on c.item_category_id = i.item_category_id
     and c.department_id = i.department_id
     and c.is_active = true
    where i.department_id = p_department_id
      and i.is_active = true
      and i.item_type = 'standard'
    order by c.sort_order asc nulls last, c.category_name asc nulls last, i.sort_order asc, i.item_name asc;
$$;

drop function if exists store.get_item_admin_catalog(bigint);

create or replace function store.get_item_admin_catalog(p_department_id bigint)
returns table (
    row_type text,
    item_category_id bigint,
    category_code text,
    category_name text,
    item_id bigint,
    item_name text,
    item_type text,
    default_price numeric,
    sort_order integer,
    is_active boolean,
    is_cast_back_target boolean,
    cast_back_regular_unit_amount numeric,
    cast_back_nomination_unit_amount numeric,
    cast_back_type text
)
language sql
security definer
set search_path = public
as $$
    select *
    from (
        select
            'category'::text as row_type,
            c.item_category_id,
            c.category_code,
            c.category_name,
            null::bigint as item_id,
            null::text as item_name,
            null::text as item_type,
            null::numeric as default_price,
            c.sort_order,
            c.is_active,
            null::boolean as is_cast_back_target,
            null::numeric as cast_back_regular_unit_amount,
            null::numeric as cast_back_nomination_unit_amount,
            null::text as cast_back_type
        from public.store_item_category_master c
        where c.department_id = p_department_id

        union all

        select
            'item'::text as row_type,
            c.item_category_id,
            c.category_code,
            c.category_name,
            i.item_id,
            i.item_name,
            i.item_type,
            i.default_price,
            i.sort_order,
            i.is_active,
            i.is_cast_back_target,
            i.cast_back_regular_unit_amount,
            i.cast_back_nomination_unit_amount,
            i.cast_back_type
        from public.store_item_master i
        left join public.store_item_category_master c
          on c.item_category_id = i.item_category_id
         and c.department_id = i.department_id
        where i.department_id = p_department_id
    ) rows
    order by
        case rows.row_type when 'category' then 0 else 1 end,
        rows.category_name asc nulls last,
        rows.sort_order asc,
        rows.item_name asc nulls last;
$$;

drop function if exists store.get_nomination_back_master(bigint);

create or replace function store.get_nomination_back_master(p_department_id bigint)
returns table (
    nomination_kind text,
    nomination_type text,
    display_name text,
    companion_time text,
    back_type text,
    back_unit_amount numeric,
    sort_order integer,
    is_active boolean
)
language sql
security definer
set search_path = public
as $$
    with target_department as (
        select
            d.company_id,
            d.department_id
        from public.department_master d
        where d.department_id = p_department_id
          and d.is_active = true
        limit 1
    )
    select
        m.nomination_kind,
        m.nomination_type,
        m.display_name,
        to_char(m.companion_time, 'HH24:MI') as companion_time,
        m.back_type,
        m.back_unit_amount,
        m.sort_order,
        m.is_active
    from target_department d
    join public.store_nomination_back_master m
      on m.company_id = d.company_id
     and m.department_id = d.department_id
    order by m.sort_order, m.display_name;
$$;

drop function if exists store.save_nomination_back_master(bigint, jsonb);

create or replace function store.save_nomination_back_master(
    p_department_id bigint,
    p_settings jsonb default '[]'::jsonb
)
returns table (
    updated_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_payload_count integer;
    v_distinct_count integer;
    v_invalid_count integer;
    v_updated_count integer;
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

    if p_settings is null or jsonb_typeof(p_settings) <> 'array' then
        raise exception 'invalid_nomination_back_master';
    end if;

    with payload as (
        select
            nullif(trim(coalesce(x.nomination_kind, '')), '') as nomination_kind,
            nullif(trim(coalesce(x.nomination_type, '')), '') as nomination_type,
            nullif(trim(coalesce(x.display_name, '')), '') as display_name,
            case
                when nullif(trim(coalesce(x.companion_time, '')), '') is null then null::time
                when trim(x.companion_time) ~ '^[0-9]{2}:[0-9]{2}(:[0-9]{2})?$' then trim(x.companion_time)::time
                else null::time
            end as companion_time,
            coalesce(x.back_unit_amount, 0) as back_unit_amount,
            coalesce(x.is_active, true) as is_active,
            coalesce(x.sort_order, 0) as sort_order
        from jsonb_to_recordset(p_settings) as x(
            nomination_kind text,
            nomination_type text,
            display_name text,
            companion_time text,
            back_unit_amount numeric,
            is_active boolean,
            sort_order integer
        )
    )
    select
        count(*)::integer,
        count(distinct nomination_kind)::integer,
        count(*) filter (
            where nomination_kind is null
               or display_name is null
               or nomination_type not in ('nomination', 'in_store', 'companion')
               or (nomination_type = 'companion' and companion_time is null)
               or (nomination_type <> 'companion' and companion_time is not null)
               or back_unit_amount < 0
               or sort_order < 0
        )::integer
      into v_payload_count, v_distinct_count, v_invalid_count
    from payload;

    if coalesce(v_payload_count, 0) = 0 or
       v_payload_count <> v_distinct_count or
       coalesce(v_invalid_count, 0) > 0 then
        raise exception 'invalid_nomination_back_master';
    end if;

    with payload as (
        select
            nullif(trim(coalesce(x.nomination_kind, '')), '') as nomination_kind,
            nullif(trim(coalesce(x.nomination_type, '')), '') as nomination_type,
            nullif(trim(coalesce(x.display_name, '')), '') as display_name,
            case
                when nullif(trim(coalesce(x.companion_time, '')), '') is null then null::time
                when trim(x.companion_time) ~ '^[0-9]{2}:[0-9]{2}(:[0-9]{2})?$' then trim(x.companion_time)::time
                else null::time
            end as companion_time,
            coalesce(x.back_unit_amount, 0) as back_unit_amount,
            coalesce(x.is_active, true) as is_active,
            coalesce(x.sort_order, 0) as sort_order
        from jsonb_to_recordset(p_settings) as x(
            nomination_kind text,
            nomination_type text,
            display_name text,
            companion_time text,
            back_unit_amount numeric,
            is_active boolean,
            sort_order integer
        )
    ),
    upserted as (
        insert into public.store_nomination_back_master (
            company_id,
            department_id,
            nomination_kind,
            nomination_type,
            display_name,
            companion_time,
            back_type,
            back_unit_amount,
            sort_order,
            is_active
        )
        select
            v_company_id,
            p_department_id,
            p.nomination_kind,
            p.nomination_type,
            p.display_name,
            p.companion_time,
            'nomination',
            p.back_unit_amount,
            p.sort_order,
            p.is_active
        from payload p
        on conflict (company_id, department_id, nomination_kind)
        do update
           set nomination_type = excluded.nomination_type,
               display_name = excluded.display_name,
               companion_time = excluded.companion_time,
               back_type = 'nomination',
               back_unit_amount = excluded.back_unit_amount,
               sort_order = excluded.sort_order,
               is_active = excluded.is_active,
               updated_at = now()
        returning nomination_back_id
    )
    select count(*)::integer
      into v_updated_count
    from upserted;

    return query select coalesce(v_updated_count, 0);
end;
$$;

drop function if exists store.upsert_item_category(bigint, bigint, text, text, integer, boolean);

create or replace function store.upsert_item_category(
    p_department_id bigint,
    p_item_category_id bigint default null,
    p_category_code text default null,
    p_category_name text default null,
    p_sort_order integer default 0,
    p_is_active boolean default true
)
returns table (
    item_category_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_category_code text;
    v_category_name text;
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

    v_category_code := nullif(trim(coalesce(p_category_code, '')), '');
    v_category_name := nullif(trim(coalesce(p_category_name, '')), '');

    if v_category_code is null or v_category_name is null then
        raise exception 'invalid_store_item_category';
    end if;

    if coalesce(p_item_category_id, 0) > 0 then
        return query
        update public.store_item_category_master c
           set category_code = v_category_code,
               category_name = v_category_name,
               sort_order = coalesce(p_sort_order, 0),
               is_active = coalesce(p_is_active, true),
               updated_at = now()
         where c.item_category_id = p_item_category_id
           and c.company_id = v_company_id
           and c.department_id = p_department_id
        returning c.item_category_id;

        if not found then
            raise exception 'store_item_category_not_found';
        end if;

        return;
    end if;

    return query
    insert into public.store_item_category_master (
        company_id,
        department_id,
        category_code,
        category_name,
        sort_order,
        is_active
    )
    values (
        v_company_id,
        p_department_id,
        v_category_code,
        v_category_name,
        coalesce(p_sort_order, 0),
        coalesce(p_is_active, true)
    )
    returning store_item_category_master.item_category_id;
end;
$$;

drop function if exists store.upsert_item(bigint, bigint, bigint, text, text, numeric, integer, boolean, boolean, numeric, text);
drop function if exists store.upsert_item(bigint, bigint, bigint, text, text, numeric, integer, boolean, boolean, numeric, numeric, text);
drop function if exists store.upsert_item(bigint, bigint, bigint, text, numeric, boolean, boolean, numeric, numeric, text);

create or replace function store.upsert_item(
    p_department_id bigint,
    p_item_id bigint default null,
    p_item_category_id bigint default null,
    p_item_name text default null,
    p_default_price numeric default 0,
    p_is_active boolean default true,
    p_is_cast_back_target boolean default false,
    p_cast_back_regular_unit_amount numeric default 0,
    p_cast_back_nomination_unit_amount numeric default 0,
    p_cast_back_type text default 'drink'
)
returns table (
    item_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_item_name text;
    v_cast_back_type text;
    v_cast_back_regular_unit_amount numeric;
    v_cast_back_nomination_unit_amount numeric;
    v_sort_order integer;
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

    if coalesce(p_item_id, 0) > 0 and exists (
        select 1
        from public.store_item_master i
        where i.item_id = p_item_id
          and i.company_id = v_company_id
          and i.department_id = p_department_id
          and i.item_type <> 'standard'
    ) then
        raise exception 'store_item_locked';
    end if;

    if not exists (
        select 1
        from public.store_item_category_master c
        where c.item_category_id = p_item_category_id
          and c.company_id = v_company_id
          and c.department_id = p_department_id
    ) then
        raise exception 'store_item_category_not_found';
    end if;

    v_item_name := nullif(trim(coalesce(p_item_name, '')), '');
    v_cast_back_type := coalesce(nullif(trim(coalesce(p_cast_back_type, '')), ''), 'drink');
    v_cast_back_regular_unit_amount := case
        when coalesce(p_is_cast_back_target, false) then coalesce(p_cast_back_regular_unit_amount, 0)
        else 0
    end;
    v_cast_back_nomination_unit_amount := case
        when coalesce(p_is_cast_back_target, false) then coalesce(p_cast_back_nomination_unit_amount, 0)
        else 0
    end;

    if v_item_name is null
       or coalesce(p_default_price, 0) < 0
       or v_cast_back_regular_unit_amount < 0
       or v_cast_back_nomination_unit_amount < 0
       or v_cast_back_type not in ('drink', 'sales', 'other') then
        raise exception 'invalid_store_item';
    end if;

    if coalesce(p_item_id, 0) > 0 then
        return query
        update public.store_item_master i
           set item_category_id = p_item_category_id,
               item_name = v_item_name,
               default_price = coalesce(p_default_price, 0),
               is_active = coalesce(p_is_active, true),
               is_cast_back_target = coalesce(p_is_cast_back_target, false),
               cast_back_unit_amount = v_cast_back_regular_unit_amount,
               cast_back_regular_unit_amount = v_cast_back_regular_unit_amount,
               cast_back_nomination_unit_amount = v_cast_back_nomination_unit_amount,
               cast_back_type = v_cast_back_type,
               updated_at = now()
         where i.item_id = p_item_id
           and i.company_id = v_company_id
           and i.department_id = p_department_id
        returning i.item_id;

        if not found then
            raise exception 'store_item_not_found';
        end if;

        return;
    end if;

    select coalesce(max(i.sort_order), 0) + 10
      into v_sort_order
    from public.store_item_master i
    where i.department_id = p_department_id
      and i.item_category_id = p_item_category_id;

    return query
    insert into public.store_item_master (
        company_id,
        department_id,
        item_category_id,
        item_name,
        default_price,
        is_active,
        is_cast_back_target,
        cast_back_unit_amount,
        cast_back_regular_unit_amount,
        cast_back_nomination_unit_amount,
        cast_back_type,
        sort_order
    )
    values (
        v_company_id,
        p_department_id,
        p_item_category_id,
        v_item_name,
        coalesce(p_default_price, 0),
        coalesce(p_is_active, true),
        coalesce(p_is_cast_back_target, false),
        v_cast_back_regular_unit_amount,
        v_cast_back_regular_unit_amount,
        v_cast_back_nomination_unit_amount,
        v_cast_back_type,
        coalesce(v_sort_order, 10)
    )
    returning store_item_master.item_id;
end;
$$;

drop function if exists store.delete_item(bigint, bigint);

create or replace function store.delete_item(
    p_department_id bigint,
    p_item_id bigint
)
returns table (
    item_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_deleted_item_id bigint;
begin
    if exists (
        select 1
        from public.store_item_master i
        where i.department_id = p_department_id
          and i.item_id = p_item_id
          and i.item_type <> 'standard'
    ) then
        raise exception 'store_item_locked';
    end if;

    if not exists (
        select 1
        from public.store_item_master i
        where i.department_id = p_department_id
          and i.item_id = p_item_id
    ) then
        raise exception 'store_item_not_found';
    end if;

    update public.store_order_lines ol
       set item_id = null,
           updated_at = now()
     where ol.item_id = p_item_id;

    delete from public.store_item_master i
     where i.department_id = p_department_id
       and i.item_id = p_item_id
    returning i.item_id
      into v_deleted_item_id;

    if v_deleted_item_id is null then
        raise exception 'store_item_not_found';
    end if;

    return query
    select v_deleted_item_id;
end;
$$;

drop function if exists store.reorder_items(bigint, jsonb);

create or replace function store.reorder_items(
    p_department_id bigint,
    p_items jsonb default '[]'::jsonb
)
returns table (
    updated_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_payload_count integer;
    v_existing_count integer;
    v_updated_count integer;
begin
    if not exists (
        select 1
        from public.department_master d
        where d.department_id = p_department_id
          and d.is_active = true
    ) then
        raise exception 'store_department_not_found';
    end if;

    if p_items is null or jsonb_typeof(p_items) <> 'array' then
        raise exception 'invalid_store_item_order';
    end if;

    with payload as (
        select distinct on (x.item_id)
            x.item_id,
            coalesce(x.sort_order, 0) as sort_order
        from jsonb_to_recordset(p_items) as x(item_id bigint, sort_order integer)
        where coalesce(x.item_id, 0) > 0
          and coalesce(x.sort_order, 0) >= 0
        order by x.item_id
    )
    select count(*)
      into v_payload_count
    from payload;

    if coalesce(v_payload_count, 0) = 0 then
        raise exception 'invalid_store_item_order';
    end if;

    with payload as (
        select distinct on (x.item_id)
            x.item_id,
            coalesce(x.sort_order, 0) as sort_order
        from jsonb_to_recordset(p_items) as x(item_id bigint, sort_order integer)
        where coalesce(x.item_id, 0) > 0
          and coalesce(x.sort_order, 0) >= 0
        order by x.item_id
    )
    select count(*)
      into v_existing_count
    from payload p
    join public.store_item_master i
      on i.item_id = p.item_id
     and i.department_id = p_department_id;

    if v_existing_count <> v_payload_count then
        raise exception 'store_item_not_found';
    end if;

    with payload as (
        select distinct on (x.item_id)
            x.item_id,
            coalesce(x.sort_order, 0) as sort_order
        from jsonb_to_recordset(p_items) as x(item_id bigint, sort_order integer)
        where coalesce(x.item_id, 0) > 0
          and coalesce(x.sort_order, 0) >= 0
        order by x.item_id
    ),
    updated as (
        update public.store_item_master i
           set sort_order = p.sort_order,
               updated_at = now()
          from payload p
         where i.item_id = p.item_id
           and i.department_id = p_department_id
        returning i.item_id
    )
    select count(*)
      into v_updated_count
    from updated;

    return query
    select coalesce(v_updated_count, 0);
end;
$$;
