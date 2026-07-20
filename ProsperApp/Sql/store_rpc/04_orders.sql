drop function if exists store.get_order_attending_casts(bigint, bigint);

create or replace function store.get_order_attending_casts(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    cast_id bigint,
    display_name text,
    drink_memo text,
    department_name text,
    clock_in_time text
)
language sql
security definer
set search_path = public
as $$
    select
        c.cast_id,
        c.display_name,
        c.drink_memo,
        d.department_name,
        case
            when a.clock_in_at is null then null
            else to_char(a.clock_in_at at time zone 'Asia/Tokyo', 'HH24:MI')
        end as clock_in_time
    from public.store_cast_attendance a
    join public.cast_master c
      on c.cast_id = a.cast_id
    join public.department_master d
      on d.department_id = c.department_id
    where a.department_id = p_department_id
      and a.business_day_id = p_business_day_id
      and a.attendance_status in ('scheduled', 'checked_in', 'checked_out')
      and c.is_active = true
      and c.status = 'active'
      and d.is_active = true
    order by d.department_name asc, c.sort_order asc, c.display_name asc;
$$;

create or replace function store.add_order_lines(
    p_department_id bigint,
    p_slip_id bigint,
    p_order_lines jsonb default '[]'::jsonb
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
    v_order_line jsonb;
    v_item public.store_item_master%rowtype;
    v_line_slip_id bigint;
    v_item_id bigint;
    v_quantity numeric(10, 2);
    v_cast_back_cast_id bigint;
    v_order_line_id bigint;
    v_back_unit_amount numeric(12, 0);
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

    for v_order_line in
        select value
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_order_lines) = 'array' then p_order_lines
                else '[]'::jsonb
            end
        )
    loop
        v_line_slip_id := coalesce(nullif(v_order_line->>'slip_id', '')::bigint, p_slip_id);
        v_item_id := nullif(v_order_line->>'item_id', '')::bigint;
        v_quantity := nullif(v_order_line->>'quantity', '')::numeric;
        v_cast_back_cast_id := nullif(v_order_line->>'cast_back_cast_id', '')::bigint;

        if v_line_slip_id is null or v_item_id is null or coalesce(v_quantity, 0) <= 0 then
            raise exception 'invalid_order_quantity';
        end if;

        select *
          into v_slip
        from public.store_slips s
        where s.slip_id = v_line_slip_id
          and s.department_id = p_department_id
          and s.status = 'open'
        limit 1;

        if v_slip.slip_id is null then
            raise exception 'store_order_slip_not_found';
        end if;

        select *
          into v_item
        from public.store_item_master i
        where i.item_id = v_item_id
          and i.company_id = v_company_id
          and i.department_id = p_department_id
          and i.is_active = true
        limit 1;

        if v_item.item_id is null then
            raise exception 'store_order_item_not_found';
        end if;

        if v_item.item_type <> 'standard' then
            raise exception 'store_order_item_not_orderable';
        end if;

        if v_cast_back_cast_id is not null and v_item.is_cast_back_target and not exists (
            select 1
            from public.store_cast_attendance a
            join public.cast_master c
              on c.cast_id = a.cast_id
            where a.business_day_id = v_slip.business_day_id
              and a.department_id = p_department_id
              and a.cast_id = v_cast_back_cast_id
              and a.attendance_status in ('scheduled', 'checked_in', 'checked_out')
              and c.company_id = v_company_id
              and c.is_active = true
              and c.status = 'active'
        ) then
            raise exception 'store_order_attendance_cast_not_found';
        end if;

        v_line_no := coalesce((
            select max(l.line_no)
            from public.store_order_lines l
            where l.slip_id = v_line_slip_id
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
            v_line_slip_id,
            v_line_no,
            v_item.item_id,
            v_item.item_name,
            v_quantity,
            v_item.default_price,
            v_item.default_price * v_quantity,
            now(),
            'active'
        )
        returning store_order_lines.order_line_id into v_order_line_id;

        if v_item.is_cast_back_target and v_cast_back_cast_id is not null then
            v_back_unit_amount := case
                when exists (
                    select 1
                    from public.store_slip_casts sc
                    where sc.slip_id = v_line_slip_id
                      and sc.cast_id = v_cast_back_cast_id
                      and sc.status = 'active'
                )
                    then v_item.cast_back_nomination_unit_amount
                else v_item.cast_back_regular_unit_amount
            end;

            insert into public.store_order_line_cast_backs (
                order_line_id,
                slip_id,
                business_day_id,
                business_date,
                company_id,
                department_id,
                cast_id,
                back_type,
                quantity,
                back_unit_amount,
                back_amount,
                status
            )
            values (
                v_order_line_id,
                v_line_slip_id,
                v_slip.business_day_id,
                v_slip.business_date,
                v_company_id,
                p_department_id,
                v_cast_back_cast_id,
                v_item.cast_back_type,
                v_quantity,
                v_back_unit_amount,
                v_back_unit_amount * v_quantity,
                'active'
            );
        end if;

        v_inserted_count := v_inserted_count + 1;
    end loop;

    return query select v_inserted_count;
end;
$$;
