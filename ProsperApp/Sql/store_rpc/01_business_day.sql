begin;

alter table public.cast_master
    add column if not exists joined_on date not null default ((now() at time zone 'Asia/Tokyo')::date);

create or replace function public.get_store_context(p_department_id bigint)
returns table (
    company_id bigint,
    department_id bigint,
    department_name text
)
language sql
security definer
set search_path = public
as $$
    select
        d.company_id,
        d.department_id,
        d.department_name
    from public.department_master d
    where d.department_id = p_department_id
      and d.is_active = true
    limit 1;
$$;

create or replace function public.get_current_business_day(p_department_id bigint)
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

create or replace function public.open_business_day(
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

create or replace function public.open_business_day_with_attendance(
    p_department_id bigint,
    p_business_date date,
    p_attending_cast_ids bigint[] default array[]::bigint[],
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
    v_cast_id bigint;
begin
    select *
      into v_business_day
    from public.open_business_day(p_department_id, p_business_date, p_memo)
    limit 1;

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_opened';
    end if;

    for v_cast_id in
        select distinct cast_id
        from unnest(coalesce(p_attending_cast_ids, array[]::bigint[])) as selected_casts(cast_id)
        where cast_id is not null
    loop
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
            source
        )
        values (
            v_business_day.company_id,
            v_business_day.department_id,
            v_business_day.business_day_id,
            v_business_day.business_date,
            v_cast_id,
            'scheduled',
            'opening'
        )
        on conflict on constraint uq_store_cast_attendance_day_cast
        do update set
            attendance_status = excluded.attendance_status,
            source = excluded.source,
            updated_at = now();
    end loop;

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

create or replace function public.open_business_day_with_attendance(
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
    from public.open_business_day(p_department_id, p_business_date, p_memo)
    limit 1;

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_opened';
    end if;

    for v_entry in
        select value from jsonb_array_elements(coalesce(p_attendance_entries, '[]'::jsonb))
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

create or replace function public.add_business_day_attendance(
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
    v_inserted_count integer := 0;
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
        select value from jsonb_array_elements(coalesce(p_attendance_entries, '[]'::jsonb))
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
            'manual'
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

drop function if exists public.save_business_day_attendance(bigint, bigint, jsonb);

create or replace function public.save_business_day_attendance(
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
        select value from jsonb_array_elements(coalesce(p_attendance_entries, '[]'::jsonb))
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

create or replace function public.get_open_slip_count(
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
      and s.status = 'open';
$$;

create or replace function public.close_business_day(
    p_department_id bigint,
    p_business_day_id bigint,
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
    v_open_slip_count integer;
begin
    select public.get_open_slip_count(p_department_id, p_business_day_id)
      into v_open_slip_count;

    if coalesce(v_open_slip_count, 0) > 0 then
        raise exception 'open_slips_exist:%', v_open_slip_count;
    end if;

    return query
    update public.store_business_days b
       set status = 'closed',
           closed_at = now(),
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

