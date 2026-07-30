begin;

alter table public.cast_master
    add column if not exists joined_on date not null default ((now() at time zone 'Asia/Tokyo')::date);

drop function if exists store.get_context(bigint);

create or replace function store.get_context(p_department_id bigint)
returns table (
    company_id bigint,
    department_id bigint,
    department_name text,
    attendance_minute_step integer,
    cast_sales_amount_basis text,
    cast_sales_split_mode text
)
language sql
security definer
set search_path = public
as $$
    select
        d.company_id,
        d.department_id,
        d.department_name,
        case
            when d.attendance_minute_step in (5, 10, 15, 20, 30, 60) then d.attendance_minute_step
            else 15
        end as attendance_minute_step,
        case
            when d.cast_sales_amount_basis in ('subtotal', 'total') then d.cast_sales_amount_basis
            else 'total'
        end as cast_sales_amount_basis,
        case
            when d.cast_sales_split_mode in ('split', 'full') then d.cast_sales_split_mode
            else 'split'
        end as cast_sales_split_mode
    from public.department_master d
    where d.department_id = p_department_id
      and d.is_active = true
    limit 1;
$$;

create or replace function store.get_current_business_day(p_department_id bigint)
returns table (
    business_day_id bigint,
    company_id bigint,
    department_id bigint,
    business_date date,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    status text,
    memo text
)
language sql
security definer
set search_path = public
as $$
    select
        b.business_day_id,
        b.company_id,
        b.department_id,
        b.business_date,
        b.opened_at,
        b.closed_at,
        b.status,
        b.memo
    from public.store_business_days b
    where b.department_id = p_department_id
      and b.status = 'open'
    order by b.opened_at desc
    limit 1;
$$;

create or replace function store.open_business_day(
    p_department_id bigint,
    p_business_date date,
    p_memo text default null
)
returns table (
    business_day_id bigint,
    company_id bigint,
    department_id bigint,
    business_date date,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    status text,
    memo text
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
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

    if exists (
        select 1
        from public.store_business_days b
        where b.company_id = v_company_id
          and b.department_id = p_department_id
          and b.status = 'open'
    ) then
        raise exception 'business_day_already_open';
    end if;

    return query
    insert into public.store_business_days (
        company_id,
        department_id,
        business_date,
        opened_at,
        status,
        memo
    )
    values (
        v_company_id,
        p_department_id,
        p_business_date,
        now(),
        'open',
        nullif(trim(coalesce(p_memo, '')), '')
    )
    returning
        store_business_days.business_day_id,
        store_business_days.company_id,
        store_business_days.department_id,
        store_business_days.business_date,
        store_business_days.opened_at,
        store_business_days.closed_at,
        store_business_days.status,
        store_business_days.memo;
end;
$$;

drop function if exists store.open_business_day_with_attendance(bigint, date, bigint[], text);

create or replace function store.open_business_day_with_attendance(
    p_department_id bigint,
    p_business_date date,
    p_attendance_entries jsonb,
    p_memo text default null
)
returns table (
    business_day_id bigint,
    company_id bigint,
    department_id bigint,
    business_date date,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    status text,
    memo text
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day record;
    v_entry jsonb;
    v_cast_id bigint;
    v_clock_in_time time;
    v_inserted_count integer := 0;
begin
    select *
      into v_business_day
    from store.open_business_day(p_department_id, p_business_date, p_memo)
    limit 1;

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_opened';
    end if;

    for v_entry in
        select value
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_attendance_entries) = 'array' then p_attendance_entries
                else '[]'::jsonb
            end
        )
    loop
        v_cast_id := nullif(v_entry->>'cast_id', '')::bigint;

        if v_cast_id is null then
            raise exception 'attendance_cast_required';
        end if;

        begin
            v_clock_in_time := nullif(v_entry->>'clock_in_time', '')::time;
        exception when others then
            raise exception 'invalid_attendance_clock_in_time';
        end;

        if v_clock_in_time is null then
            raise exception 'invalid_attendance_clock_in_time';
        end if;

        if not exists (
            select 1
            from public.cast_master c
            join public.department_master d
              on d.department_id = c.department_id
            where c.cast_id = v_cast_id
              and c.company_id = v_business_day.company_id
              and c.is_active = true
              and c.status = 'active'
              and d.is_active = true
        ) then
            raise exception 'store_attendance_cast_not_found';
        end if;

        insert into public.store_cast_attendance (
            company_id,
            department_id,
            business_day_id,
            business_date,
            cast_id,
            attendance_status,
            clock_in_at,
            source
        )
        values (
            v_business_day.company_id,
            v_business_day.department_id,
            v_business_day.business_day_id,
            v_business_day.business_date,
            v_cast_id,
            'checked_in',
            (((v_business_day.business_date + case when v_clock_in_time < time '12:00' then 1 else 0 end)::timestamp + v_clock_in_time) at time zone 'Asia/Tokyo'),
            'opening'
        )
        on conflict on constraint uq_store_cast_attendance_day_cast
        do update set
            attendance_status = excluded.attendance_status,
            clock_in_at = excluded.clock_in_at,
            source = excluded.source,
            updated_at = now();

        v_inserted_count := v_inserted_count + 1;
    end loop;

    if v_inserted_count = 0 then
        raise exception 'attendance_required';
    end if;

    return query
    select
        v_business_day.business_day_id,
        v_business_day.company_id,
        v_business_day.department_id,
        v_business_day.business_date,
        v_business_day.opened_at,
        v_business_day.closed_at,
        v_business_day.status,
        v_business_day.memo;
end;
$$;

drop function if exists public.add_business_day_attendance(bigint, bigint, jsonb);

drop function if exists store.save_business_day_attendance(bigint, bigint, jsonb);

create or replace function store.save_business_day_attendance(
    p_department_id bigint,
    p_business_day_id bigint,
    p_attendance_entries jsonb
)
returns table (
    business_day_id bigint,
    company_id bigint,
    department_id bigint,
    business_date date,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    status text,
    memo text
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day record;
    v_entry jsonb;
    v_cast_id bigint;
    v_clock_in_time time;
    v_is_selected boolean;
    v_selected_count integer := 0;
begin
    select *
      into v_business_day
    from public.store_business_days b
    where b.department_id = p_department_id
      and b.business_day_id = p_business_day_id
      and b.status = 'open'
    limit 1;

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_open';
    end if;

    for v_entry in
        select value
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_attendance_entries) = 'array' then p_attendance_entries
                else '[]'::jsonb
            end
        )
    loop
        v_cast_id := nullif(v_entry->>'cast_id', '')::bigint;

        if v_cast_id is null then
            raise exception 'attendance_cast_required';
        end if;

        v_is_selected := coalesce((v_entry->>'is_selected')::boolean, true);

        if v_is_selected then
            begin
                v_clock_in_time := nullif(v_entry->>'clock_in_time', '')::time;
            exception when others then
                raise exception 'invalid_attendance_clock_in_time';
            end;

            if v_clock_in_time is null then
                raise exception 'invalid_attendance_clock_in_time';
            end if;

            if not exists (
                select 1
                from public.cast_master c
                join public.department_master d
                  on d.department_id = c.department_id
                where c.cast_id = v_cast_id
                  and c.company_id = v_business_day.company_id
                  and c.is_active = true
                  and c.status = 'active'
                  and d.is_active = true
            ) then
                raise exception 'store_attendance_cast_not_found';
            end if;

            insert into public.store_cast_attendance (
                company_id,
                department_id,
                business_day_id,
                business_date,
                cast_id,
                attendance_status,
                clock_in_at,
                source
            )
            values (
                v_business_day.company_id,
                v_business_day.department_id,
                v_business_day.business_day_id,
                v_business_day.business_date,
                v_cast_id,
                'checked_in',
                (((v_business_day.business_date + case when v_clock_in_time < time '12:00' then 1 else 0 end)::timestamp + v_clock_in_time) at time zone 'Asia/Tokyo'),
                'manual'
            )
            on conflict on constraint uq_store_cast_attendance_day_cast
            do update set
                attendance_status = excluded.attendance_status,
                clock_in_at = excluded.clock_in_at,
                source = excluded.source,
                updated_at = now();

            v_selected_count := v_selected_count + 1;
        else
            update public.store_cast_attendance a
               set attendance_status = 'cancelled',
                   clock_in_at = null,
                   clock_out_at = null,
                   source = 'manual',
                   updated_at = now()
             where a.department_id = p_department_id
               and a.business_day_id = p_business_day_id
               and a.cast_id = v_cast_id;
        end if;
    end loop;

    if v_selected_count = 0 then
        raise exception 'attendance_required';
    end if;

    return query
    select
        v_business_day.business_day_id,
        v_business_day.company_id,
        v_business_day.department_id,
        v_business_day.business_date,
        v_business_day.opened_at,
        v_business_day.closed_at,
        v_business_day.status,
        v_business_day.memo;
end;
$$;

create or replace function store.get_business_day_closing_attendance(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    attendance_id bigint,
    cast_id bigint,
    cast_display_name text,
    cast_department_name text,
    attendance_status text,
    clock_in_at timestamp with time zone,
    clock_out_at timestamp with time zone,
    uses_send_service boolean
)
language sql
security definer
set search_path = public
as $$
    select
        a.attendance_id,
        a.cast_id,
        c.display_name as cast_display_name,
        d.department_name as cast_department_name,
        a.attendance_status,
        a.clock_in_at,
        a.clock_out_at,
        coalesce(a.uses_send_service, false) as uses_send_service
    from public.store_cast_attendance a
    join public.store_business_days b
      on b.business_day_id = a.business_day_id
     and b.department_id = p_department_id
    join public.cast_master c
      on c.cast_id = a.cast_id
    left join public.department_master d
      on d.department_id = c.department_id
    where a.department_id = p_department_id
      and a.business_day_id = p_business_day_id
      and a.attendance_status in ('scheduled', 'checked_in', 'checked_out')
    order by
        case when c.department_id = p_department_id then 0 else 1 end,
        c.sort_order asc,
        c.display_name asc,
        a.attendance_id asc;
$$;

create or replace function store.save_business_day_closing_attendance(
    p_department_id bigint,
    p_business_day_id bigint,
    p_attendance_entries jsonb
)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day record;
    v_entry jsonb;
    v_attendance record;
    v_attendance_id bigint;
    v_clock_in_time time;
    v_clock_in_at timestamp with time zone;
    v_clock_out_time time;
    v_clock_out_at timestamp with time zone;
    v_uses_send_service boolean;
    v_updated_count integer := 0;
begin
    select *
      into v_business_day
    from public.store_business_days b
    where b.department_id = p_department_id
      and b.business_day_id = p_business_day_id
      and b.status = 'open'
    limit 1;

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_open';
    end if;

    for v_entry in
        select value
        from jsonb_array_elements(
            case
                when jsonb_typeof(p_attendance_entries) = 'array' then p_attendance_entries
                else '[]'::jsonb
            end
        )
    loop
        v_attendance_id := nullif(v_entry->>'attendance_id', '')::bigint;

        if v_attendance_id is null then
            raise exception 'attendance_required';
        end if;

        begin
            v_clock_in_time := nullif(v_entry->>'clock_in_time', '')::time;
        exception when others then
            raise exception 'invalid_attendance_clock_in_time';
        end;

        if v_clock_in_time is null then
            raise exception 'invalid_attendance_clock_in_time';
        end if;

        begin
            v_clock_out_time := nullif(v_entry->>'clock_out_time', '')::time;
        exception when others then
            raise exception 'invalid_attendance_clock_out_time';
        end;

        if v_clock_out_time is null then
            raise exception 'invalid_attendance_clock_out_time';
        end if;

        v_uses_send_service := coalesce((v_entry->>'uses_send_service')::boolean, false);
        v_clock_in_at := (((v_business_day.business_date + case when v_clock_in_time < time '12:00' then 1 else 0 end)::timestamp + v_clock_in_time) at time zone 'Asia/Tokyo');
        v_clock_out_at := (((v_business_day.business_date + case when v_clock_out_time < time '12:00' then 1 else 0 end)::timestamp + v_clock_out_time) at time zone 'Asia/Tokyo');

        select *
          into v_attendance
        from public.store_cast_attendance a
        where a.attendance_id = v_attendance_id
          and a.department_id = p_department_id
          and a.business_day_id = p_business_day_id
          and a.attendance_status in ('scheduled', 'checked_in', 'checked_out')
        for update;

        if v_attendance.attendance_id is null then
            raise exception 'attendance_not_found';
        end if;

        if v_clock_out_at <= v_clock_in_at then
            raise exception 'invalid_attendance_clock_out_time';
        end if;

        update public.store_cast_attendance a
           set attendance_status = 'checked_out',
               clock_in_at = v_clock_in_at,
               clock_out_at = v_clock_out_at,
               uses_send_service = v_uses_send_service,
               updated_at = now()
         where a.attendance_id = v_attendance_id
           and a.department_id = p_department_id
           and a.business_day_id = p_business_day_id;

        v_updated_count := v_updated_count + 1;
    end loop;

    if v_updated_count = 0 then
        raise exception 'attendance_required';
    end if;

    return v_updated_count;
end;
$$;

create or replace function store.get_open_slip_count(
    p_department_id bigint,
    p_business_day_id bigint
)
returns integer
language sql
security definer
set search_path = public
as $$
    select count(*)::integer
    from public.store_slips s
    where s.department_id = p_department_id
      and s.business_day_id = p_business_day_id
      and s.status in ('open', 'checkout_ready');
$$;

create or replace function store.get_business_day_drink_delivery_status(
    p_department_id bigint,
    p_business_day_id bigint
)
returns table (
    drink_delivery_amount numeric,
    is_entered boolean
)
language sql
security definer
set search_path = public
as $$
    select
        coalesce(b.drink_delivery_amount, 0) as drink_delivery_amount,
        coalesce(b.drink_delivery_amount_entered, false) as is_entered
    from public.store_business_days b
    where b.department_id = p_department_id
      and b.business_day_id = p_business_day_id
    limit 1;
$$;

drop function if exists public.get_business_day_drink_delivery_amount(bigint, bigint);

create or replace function store.save_business_day_drink_delivery_amount(
    p_department_id bigint,
    p_business_day_id bigint,
    p_drink_delivery_amount numeric
)
returns numeric
language plpgsql
security definer
set search_path = public
as $$
declare
    v_drink_delivery_amount numeric(12, 0);
begin
    if p_drink_delivery_amount is null or
       p_drink_delivery_amount < 0 or
       p_drink_delivery_amount <> trunc(p_drink_delivery_amount) then
        raise exception 'invalid_drink_delivery_amount';
    end if;

    update public.store_business_days b
       set drink_delivery_amount = p_drink_delivery_amount,
           drink_delivery_amount_entered = true,
           updated_at = now()
     where b.business_day_id = p_business_day_id
       and b.department_id = p_department_id
       and b.status = 'open'
     returning b.drink_delivery_amount
      into v_drink_delivery_amount;

    if v_drink_delivery_amount is null then
        raise exception 'business_day_not_open';
    end if;

    return v_drink_delivery_amount;
end;
$$;

create or replace function store.get_business_day_closing_readiness(
    p_department_id bigint,
    p_business_day_id bigint,
    p_pending_receipt_status text default 'unprocessed'
)
returns table (
    business_day_id bigint,
    open_slip_count integer,
    drink_delivery_amount numeric,
    is_drink_delivery_amount_entered boolean,
    attendance_count integer,
    missing_clock_out_count integer,
    cast_sales_required_slip_count integer,
    cast_sales_completed_slip_count integer,
    cast_sales_missing_slip_count integer,
    pending_receipt_count integer,
    can_close boolean,
    block_reasons jsonb,
    checked_at timestamp with time zone
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_open_slip_count integer := 0;
    v_attendance_count integer := 0;
    v_missing_clock_out_count integer := 0;
    v_cast_sales_required_slip_count integer := 0;
    v_cast_sales_completed_slip_count integer := 0;
    v_cast_sales_missing_slip_count integer := 0;
    v_pending_receipt_count integer := 0;
    v_can_close boolean;
    v_block_reasons jsonb := '[]'::jsonb;
begin
    select b.*
      into v_business_day
    from public.store_business_days b
    where b.department_id = p_department_id
      and b.business_day_id = p_business_day_id
      and b.status = 'open'
    limit 1;

    if v_business_day.business_day_id is null then
        return;
    end if;

    select store.get_open_slip_count(p_department_id, p_business_day_id)
      into v_open_slip_count;

    select
        count(*)::integer,
        count(*) filter (where a.clock_out_at is null)::integer
      into v_attendance_count,
           v_missing_clock_out_count
    from public.store_cast_attendance a
    where a.business_day_id = p_business_day_id
      and a.department_id = p_department_id
      and a.attendance_status in ('scheduled', 'checked_in', 'checked_out');

    select
        coalesce(s.required_slip_count, 0),
        coalesce(s.completed_slip_count, 0),
        coalesce(s.missing_slip_count, 0)
      into v_cast_sales_required_slip_count,
           v_cast_sales_completed_slip_count,
           v_cast_sales_missing_slip_count
    from store.get_business_day_cast_sales_adjustment_status(
        p_department_id,
        p_business_day_id
    ) s;

    if nullif(trim(coalesce(p_pending_receipt_status, '')), '') is not null then
        select count(*)::integer
          into v_pending_receipt_count
        from public.documents d
        where d.department_id = p_department_id
          and d.status = p_pending_receipt_status;
    end if;

    if coalesce(v_open_slip_count, 0) > 0 then
        v_block_reasons := v_block_reasons ||
            jsonb_build_array(format('未会計伝票が %s 件あります。', v_open_slip_count));
    end if;

    if coalesce(v_business_day.drink_delivery_amount_entered, false) = false then
        v_block_reasons := v_block_reasons ||
            jsonb_build_array('酒代が未入力です。酒代がない場合も0円で保存してください。');
    end if;

    if coalesce(v_attendance_count, 0) = 0 then
        v_block_reasons := v_block_reasons ||
            jsonb_build_array('勤怠入力に出勤キャストが登録されていません。');
    elsif coalesce(v_missing_clock_out_count, 0) > 0 then
        v_block_reasons := v_block_reasons ||
            jsonb_build_array(format('退勤時刻が未入力のキャストが %s 名います。', v_missing_clock_out_count));
    end if;

    if coalesce(v_cast_sales_missing_slip_count, 0) > 0 then
        v_block_reasons := v_block_reasons ||
            jsonb_build_array('キャスト売上額調整が未完了です。');
    end if;

    if coalesce(v_pending_receipt_count, 0) > 0 then
        v_block_reasons := v_block_reasons ||
            jsonb_build_array(format('未入力領収書が %s 件あります。', v_pending_receipt_count));
    end if;

    v_can_close :=
        coalesce(v_open_slip_count, 0) = 0 and
        coalesce(v_business_day.drink_delivery_amount_entered, false) and
        coalesce(v_attendance_count, 0) > 0 and
        coalesce(v_missing_clock_out_count, 0) = 0 and
        coalesce(v_cast_sales_missing_slip_count, 0) = 0 and
        coalesce(v_pending_receipt_count, 0) = 0;

    return query
    select
        v_business_day.business_day_id,
        coalesce(v_open_slip_count, 0),
        coalesce(v_business_day.drink_delivery_amount, 0),
        coalesce(v_business_day.drink_delivery_amount_entered, false),
        coalesce(v_attendance_count, 0),
        coalesce(v_missing_clock_out_count, 0),
        coalesce(v_cast_sales_required_slip_count, 0),
        coalesce(v_cast_sales_completed_slip_count, 0),
        coalesce(v_cast_sales_missing_slip_count, 0),
        coalesce(v_pending_receipt_count, 0),
        v_can_close,
        v_block_reasons,
        clock_timestamp();
end;
$$;

revoke all on function store.get_business_day_closing_readiness(bigint, bigint, text)
    from public, anon, authenticated, service_role;

drop function if exists store.close_business_day(bigint, bigint, text);
drop function if exists store.close_business_day(bigint, bigint, text, text);
drop function if exists store.close_business_day(bigint, bigint, text, text, boolean);

create or replace function store.close_business_day(
    p_department_id bigint,
    p_business_day_id bigint,
    p_memo text default null,
    p_pending_receipt_status text default 'unprocessed',
    p_ignore_closing_requirements boolean default false
)
returns table (
    business_day_id bigint,
    company_id bigint,
    department_id bigint,
    business_date date,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    status text,
    memo text
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_readiness record;
    v_closed_at timestamp with time zone;
begin
    select *
      into v_business_day
    from public.store_business_days b
    where b.business_day_id = p_business_day_id
      and b.department_id = p_department_id
      and b.status = 'open'
    for update;

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_open';
    end if;

    if coalesce(p_ignore_closing_requirements, false) then
        raise exception 'closing_override_disabled';
    end if;

    select *
      into v_readiness
    from store.get_business_day_closing_readiness(
        p_department_id,
        p_business_day_id,
        p_pending_receipt_status
    );

    if coalesce(v_readiness.open_slip_count, 0) > 0 then
        raise exception 'open_slips_exist:%', v_readiness.open_slip_count;
    end if;

    if coalesce(v_readiness.is_drink_delivery_amount_entered, false) = false then
        raise exception 'drink_delivery_required';
    end if;

    if coalesce(v_readiness.attendance_count, 0) = 0 then
        raise exception 'attendance_required';
    end if;

    if coalesce(v_readiness.missing_clock_out_count, 0) > 0 then
        raise exception 'attendance_clock_out_required:%', v_readiness.missing_clock_out_count;
    end if;

    if coalesce(v_readiness.cast_sales_missing_slip_count, 0) > 0 then
        raise exception 'cast_sales_adjustment_required:%', v_readiness.cast_sales_missing_slip_count;
    end if;

    if coalesce(v_readiness.pending_receipt_count, 0) > 0 then
        raise exception 'pending_receipts_exist:%', v_readiness.pending_receipt_count;
    end if;

    v_closed_at := clock_timestamp();
    perform store.capture_business_day_closing_snapshot(
        p_department_id,
        p_business_day_id,
        v_closed_at,
        false
    );

    return query
    update public.store_business_days b
       set status = 'closed',
           closed_at = v_closed_at,
           memo = coalesce(nullif(trim(coalesce(p_memo, '')), ''), b.memo)
     where b.business_day_id = p_business_day_id
       and b.department_id = p_department_id
       and b.status = 'open'
    returning
        b.business_day_id,
        b.company_id,
        b.department_id,
        b.business_date,
        b.opened_at,
        b.closed_at,
        b.status,
        b.memo;
end;
$$;

commit;
