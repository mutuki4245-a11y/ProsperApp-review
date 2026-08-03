-- 会計伝票・営業日締めの固定値と、締め後データの不変条件です。
-- 08_checkout_ready.sql、09_business_home_snapshot.sql、10_business_home_flush.sql の後に適用します。

create or replace function store.capture_business_day_closing_snapshot(
    p_department_id bigint,
    p_business_day_id bigint,
    p_closed_at timestamp with time zone,
    p_backfilled boolean default false
)
returns bigint
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_business_home_data jsonb;
    v_daily_report jsonb;
    v_closing_data jsonb;
    v_snapshot_id bigint;
begin
    select b.*
      into v_business_day
      from public.store_business_days b
     where b.department_id = p_department_id
       and b.business_day_id = p_business_day_id
       and b.status in ('open', 'closed')
     for update;

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_found';
    end if;

    if p_closed_at is null or p_closed_at < v_business_day.opened_at then
        raise exception 'invalid_business_day_closed_at';
    end if;

    select cs.closing_snapshot_id
      into v_snapshot_id
      from public.store_business_day_closing_snapshots cs
     where cs.business_day_id = p_business_day_id
     limit 1;

    if v_snapshot_id is not null then
        return v_snapshot_id;
    end if;

    select s.snapshot
      into v_business_home_data
       from store.get_business_day_snapshot_at(
           p_department_id,
           p_business_day_id,
           p_closed_at
       ) s;

    if v_business_home_data is null then
        raise exception 'business_day_snapshot_not_found';
    end if;

    v_daily_report := store.build_business_day_daily_report(
        p_department_id,
        p_business_day_id,
        p_closed_at,
        'closed'
    );

    select jsonb_build_object(
        'schema_version', 'business-day-closing-v2',
        'accounting_snapshot_policy_version', 2,
        'captured_at', p_closed_at,
        'backfilled', coalesce(p_backfilled, false),
        'daily_report', v_daily_report,
        'business_day', jsonb_build_object(
            'business_day_id', v_business_day.business_day_id,
            'business_date', v_business_day.business_date,
            'company_id', v_business_day.company_id,
            'department_id', v_business_day.department_id,
            'department_name', d.department_name,
            'opened_at', v_business_day.opened_at,
            'closed_at', p_closed_at,
            'drink_delivery_amount', v_business_day.drink_delivery_amount,
            'memo', v_business_day.memo
        ),
        'totals', jsonb_build_object(
            'confirmed_checkout_count', (
                select count(*)
                  from public.store_checkouts c
                  join public.store_slips s on s.slip_id = c.slip_id
                 where s.business_day_id = p_business_day_id
                   and c.status = 'confirmed'
            ),
            'sales_total_amount', coalesce((
                select sum(c.total_amount)
                  from public.store_checkouts c
                  join public.store_slips s on s.slip_id = c.slip_id
                 where s.business_day_id = p_business_day_id
                   and c.status = 'confirmed'
            ), 0),
            'payment_total_amount', coalesce((
                select sum(p.amount)
                  from public.store_checkout_payments p
                  join public.store_slips s on s.slip_id = p.slip_id
                 where s.business_day_id = p_business_day_id
                   and p.status = 'confirmed'
            ), 0),
            'cast_sales_total_amount', coalesce((
                select sum(a.sales_amount)
                  from public.store_slip_cast_sales_adjustments a
                 where a.business_day_id = p_business_day_id
                   and a.status = 'confirmed'
            ), 0),
            'champagne_back_total_amount', coalesce((
                select sum(cb.back_amount)
                  from public.store_business_day_champagne_backs cb
                 where cb.business_day_id = p_business_day_id
                   and cb.status = 'active'
            ), 0),
            'drink_delivery_amount', v_business_day.drink_delivery_amount
        ),
        'payments', coalesce((
            select jsonb_agg(jsonb_build_object(
                'payment_method_id', grouped.payment_method_id,
                'payment_method_code', grouped.payment_method_code,
                'payment_method_name', grouped.payment_method_name,
                'amount', grouped.amount
            ) order by grouped.payment_method_code)
              from (
                select
                    p.payment_method_id,
                    p.payment_method_code,
                    p.payment_method_name,
                    sum(p.amount) as amount
                  from public.store_checkout_payments p
                  join public.store_slips s on s.slip_id = p.slip_id
                 where s.business_day_id = p_business_day_id
                   and p.status = 'confirmed'
                 group by p.payment_method_id, p.payment_method_code, p.payment_method_name
              ) grouped
        ), '[]'::jsonb),
        'checkouts', coalesce((
            select jsonb_agg(jsonb_build_object(
                'checkout_id', c.checkout_id,
                'slip_id', c.slip_id,
                'accounting_snapshot_version', ss.snapshot_version,
                'checkout_at', c.checkout_at,
                'subtotal_amount', c.subtotal_amount,
                'service_charge_amount', c.service_charge_amount,
                'total_amount', c.total_amount,
                'received_amount', c.received_amount,
                'change_amount', c.change_amount,
                'issuer', c.issuer_snapshot,
                'payments', coalesce((
                    select jsonb_agg(jsonb_build_object(
                        'payment_method_id', p.payment_method_id,
                        'payment_method_code', p.payment_method_code,
                        'payment_method_name', p.payment_method_name,
                        'amount', p.amount
                    ) order by p.checkout_payment_id)
                      from public.store_checkout_payments p
                     where p.checkout_id = c.checkout_id
                       and p.status = 'confirmed'
                ), '[]'::jsonb)
            ) order by c.checkout_at, c.checkout_id)
              from public.store_checkouts c
              join public.store_slips s on s.slip_id = c.slip_id
              left join public.store_slip_accounting_snapshots ss
                on ss.slip_id = c.slip_id
               and ss.status = 'active'
             where s.business_day_id = p_business_day_id
               and c.status = 'confirmed'
        ), '[]'::jsonb),
        'attendance', coalesce((
            select jsonb_agg(jsonb_build_object(
                'attendance_id', a.attendance_id,
                'cast_id', a.cast_id,
                'cast_display_name', c.display_name,
                'cast_department_id', c.department_id,
                'cast_department_name', cd.department_name,
                'attendance_status', a.attendance_status,
                'clock_in_at', a.clock_in_at,
                'clock_out_at', a.clock_out_at,
                'uses_send_service', a.uses_send_service,
                'source', a.source,
                'memo', a.memo
            ) order by a.attendance_id)
              from public.store_cast_attendance a
              join public.cast_master c on c.cast_id = a.cast_id
              left join public.department_master cd on cd.department_id = c.department_id
             where a.business_day_id = p_business_day_id
        ), '[]'::jsonb),
        'staff_attendance', coalesce((
            select jsonb_agg(jsonb_build_object(
                'staff_attendance_id', a.staff_attendance_id,
                'staff_id', a.staff_id,
                'staff_display_name', s.display_name,
                'employment_type', s.employment_type,
                'staff_department_id', s.department_id,
                'staff_department_name', sd.department_name,
                'attendance_status', a.attendance_status,
                'clock_in_at', a.clock_in_at,
                'clock_out_at', a.clock_out_at,
                'uses_send_service', a.uses_send_service,
                'source', a.source,
                'memo', a.memo
            ) order by a.staff_attendance_id)
              from public.store_staff_attendance a
              join public.store_staff_master s on s.staff_id = a.staff_id
              left join public.department_master sd on sd.department_id = s.department_id
             where a.business_day_id = p_business_day_id
        ), '[]'::jsonb),
        'cast_sales_adjustments', coalesce((
            select jsonb_agg(jsonb_build_object(
                'adjustment_id', a.adjustment_id,
                'slip_id', a.slip_id,
                'checkout_id', a.checkout_id,
                'slip_cast_id', a.slip_cast_id,
                'cast_id', a.cast_id,
                'cast_display_name', c.display_name,
                'source_amount_type', a.source_amount_type,
                'split_mode', a.split_mode,
                'base_amount', a.base_amount,
                'sales_amount', a.sales_amount,
                'status', a.status,
                'memo', a.memo
            ) order by a.adjustment_id)
              from public.store_slip_cast_sales_adjustments a
              join public.cast_master c on c.cast_id = a.cast_id
             where a.business_day_id = p_business_day_id
        ), '[]'::jsonb),
        'champagne_backs', coalesce((
            select jsonb_agg(jsonb_build_object(
                'business_day_champagne_back_id', cb.business_day_champagne_back_id,
                'cast_id', cb.cast_id,
                'cast_display_name', c.display_name,
                'back_type', cb.back_type,
                'quantity', cb.quantity,
                'back_unit_amount', cb.back_unit_amount,
                'back_amount', cb.back_amount,
                'status', cb.status,
                'memo', cb.memo
            ) order by cb.business_day_champagne_back_id)
              from public.store_business_day_champagne_backs cb
              join public.cast_master c on c.cast_id = cb.cast_id
             where cb.business_day_id = p_business_day_id
        ), '[]'::jsonb)
    )
      into v_closing_data
      from public.department_master d
     where d.department_id = p_department_id;

    if v_closing_data is null then
        raise exception 'store_department_not_found';
    end if;

    insert into public.store_business_day_closing_snapshots (
        business_day_id,
        business_date,
        company_id,
        department_id,
        snapshot_version,
        closed_at,
        backfilled,
        business_home_data,
        closing_data
    )
    values (
        v_business_day.business_day_id,
        v_business_day.business_date,
        v_business_day.company_id,
        v_business_day.department_id,
        2,
        p_closed_at,
        coalesce(p_backfilled, false),
        v_business_home_data,
        v_closing_data
    )
    returning closing_snapshot_id into v_snapshot_id;

    return v_snapshot_id;
end;
$$;

-- 導入前に会計準備・会計済みだった伝票は、導入時点の値を監査基準として固定します。
do $$
declare
    v_slip record;
    v_print_data jsonb;
    v_review_data jsonb;
    v_day_snapshot jsonb;
    v_business_home_data jsonb;
    v_snapshot_version integer;
    v_checkout public.store_checkouts%rowtype;
    v_as_of timestamp with time zone;
begin
    for v_slip in
        select s.*
          from public.store_slips s
         where s.status in ('checkout_ready', 'checked_out')
           and not exists (
               select 1
                 from public.store_slip_accounting_snapshots ss
                where ss.slip_id = s.slip_id
                  and ss.status = 'active'
           )
         order by s.slip_id
    loop
        v_print_data := store.build_checkout_statement_data(v_slip.department_id, v_slip.slip_id);
        v_review_data := store.build_checkout_review_data(v_slip.department_id, v_slip.slip_id);
        v_as_of := coalesce(v_slip.closed_at, v_slip.updated_at, now());

        if v_slip.status = 'checked_out' then
            select c.*
              into v_checkout
              from public.store_checkouts c
             where c.slip_id = v_slip.slip_id
               and c.department_id = v_slip.department_id
               and c.status = 'confirmed'
             order by c.checkout_at desc, c.checkout_id desc
             limit 1;

            if v_checkout.checkout_id is null then
                raise exception 'checkout_backfill_source_not_found:%', v_slip.slip_id;
            end if;

            -- 移行済み会計は、再計算値ではなく確定済み会計行を金額の正本にします。
            v_print_data := v_print_data || jsonb_build_object(
                'subtotal_amount', v_checkout.subtotal_amount,
                'service_charge_amount', v_checkout.service_charge_amount,
                'consumption_tax_amount', round(v_checkout.total_amount * 10 / 110, 0),
                'total_amount', v_checkout.total_amount,
                'backfilled_from_checkout', true
            );
        end if;

        select s.snapshot
          into v_day_snapshot
          from store.get_business_day_snapshot_at(
              v_slip.department_id,
              v_slip.business_day_id,
              v_as_of
          ) s;

        select value
          into v_business_home_data
          from jsonb_array_elements(coalesce(v_day_snapshot->'slips', '[]'::jsonb))
         where nullif(value->>'id', '')::bigint = v_slip.slip_id
         limit 1;

        if v_business_home_data is null then
            raise exception 'checkout_business_snapshot_not_found:%', v_slip.slip_id;
        end if;

        if v_slip.status = 'checked_out' then
            v_business_home_data := v_business_home_data || jsonb_build_object(
                'accountingAmount', v_checkout.total_amount
            );
        end if;

        select coalesce(max(ss.snapshot_version), 0) + 1
          into v_snapshot_version
          from public.store_slip_accounting_snapshots ss
         where ss.slip_id = v_slip.slip_id;

        insert into public.store_slip_accounting_snapshots (
            slip_id,
            business_day_id,
            business_date,
            company_id,
            department_id,
            snapshot_version,
            status,
            checkout_ready_at,
            print_data,
            review_data,
            business_home_data,
            backfilled
        )
        values (
            v_slip.slip_id,
            v_slip.business_day_id,
            v_slip.business_date,
            v_slip.company_id,
            v_slip.department_id,
            v_snapshot_version,
            'active',
            coalesce(v_slip.closed_at, v_slip.updated_at, now()),
            v_print_data,
            v_review_data,
            v_business_home_data,
            true
        );
    end loop;
end;
$$;

-- 先行適用済みの旧backfillを、確定済み会計金額と新しい時点計算規則へ一度だけ移行します。
do $$
begin
    perform set_config('store.allow_closed_day_mutation', 'on', true);

    update public.store_slip_accounting_snapshots ss
       set backfilled = true
      from public.store_business_day_closing_snapshots cs
     where cs.business_day_id = ss.business_day_id
       and cs.backfilled = true
       and ss.status = 'active'
       and ss.backfilled = false;

    update public.store_slip_accounting_snapshots ss
       set print_data = ss.print_data || jsonb_build_object(
               'subtotal_amount', c.subtotal_amount,
               'service_charge_amount', c.service_charge_amount,
               'consumption_tax_amount', round(c.total_amount * 10 / 110, 0),
               'total_amount', c.total_amount,
               'backfilled_from_checkout', true
           ),
           business_home_data = ss.business_home_data || jsonb_build_object(
               'accountingAmount', c.total_amount
           )
      from public.store_slips s
      join public.store_checkouts c
        on c.slip_id = s.slip_id
       and c.status = 'confirmed'
     where ss.slip_id = s.slip_id
       and ss.status = 'active'
       and ss.backfilled = true
       and s.status = 'checked_out'
       and (
           nullif(ss.print_data->>'subtotal_amount', '')::numeric is distinct from c.subtotal_amount or
           nullif(ss.print_data->>'service_charge_amount', '')::numeric is distinct from c.service_charge_amount or
           nullif(ss.print_data->>'total_amount', '')::numeric is distinct from c.total_amount or
           nullif(ss.business_home_data->>'accountingAmount', '')::numeric is distinct from c.total_amount or
           ss.print_data->>'backfilled_from_checkout' is distinct from 'true'
       );

    delete from public.store_business_day_closing_snapshots cs
     where cs.backfilled = true
       and coalesce(
           nullif(cs.closing_data->>'accounting_snapshot_policy_version', '')::integer,
           0
       ) < 2;

    perform set_config('store.allow_closed_day_mutation', 'off', true);
end;
$$;

-- 既存締め日は当時の値を復元できないため、導入時点を以後の不変基準として保存します。
do $$
declare
    v_business_day record;
begin
    for v_business_day in
        select b.*
          from public.store_business_days b
         where b.status = 'closed'
           and not exists (
               select 1
                 from public.store_business_day_closing_snapshots cs
                where cs.business_day_id = b.business_day_id
           )
         order by b.business_day_id
    loop
        perform store.capture_business_day_closing_snapshot(
            v_business_day.department_id,
            v_business_day.business_day_id,
            coalesce(v_business_day.closed_at, v_business_day.updated_at, now()),
            true
        );
    end loop;
end;
$$;

create or replace function store.guard_closed_business_day_mutation()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
    v_row_data jsonb;
    v_business_day_id bigint;
    v_slip_id bigint;
    v_slip_business_day_id bigint;
    v_business_day_status text;
begin
    if current_setting('store.allow_closed_day_mutation', true) = 'on' then
        if tg_op = 'DELETE' then
            return old;
        end if;
        return new;
    end if;

    if tg_op <> 'INSERT' then
        v_row_data := to_jsonb(old);
        v_business_day_id := nullif(v_row_data->>'business_day_id', '')::bigint;
        v_slip_id := nullif(v_row_data->>'slip_id', '')::bigint;
        v_slip_business_day_id := null;
        v_business_day_status := null;

        if tg_table_name = 'store_slips' then
            v_slip_business_day_id := v_business_day_id;
        elsif v_slip_id is not null then
            select s.business_day_id
              into v_slip_business_day_id
              from public.store_slips s
              where s.slip_id = v_slip_id;
        end if;

        if v_business_day_id is not null
           and v_slip_business_day_id is not null
           and v_business_day_id <> v_slip_business_day_id then
            raise exception 'business_day_slip_mismatch';
        end if;

        v_business_day_id := coalesce(v_business_day_id, v_slip_business_day_id);

        select b.status
          into v_business_day_status
          from public.store_business_days b
         where b.business_day_id = v_business_day_id
         for key share;

        if v_business_day_status = 'closed' then
            raise exception 'business_day_closed';
        end if;
    end if;

    if tg_op <> 'DELETE' then
        v_row_data := to_jsonb(new);
        v_business_day_id := nullif(v_row_data->>'business_day_id', '')::bigint;
        v_slip_id := nullif(v_row_data->>'slip_id', '')::bigint;
        v_slip_business_day_id := null;
        v_business_day_status := null;

        if tg_table_name = 'store_slips' then
            v_slip_business_day_id := v_business_day_id;
        elsif v_slip_id is not null then
            select s.business_day_id
              into v_slip_business_day_id
              from public.store_slips s
              where s.slip_id = v_slip_id;
        end if;

        if v_business_day_id is not null
           and v_slip_business_day_id is not null
           and v_business_day_id <> v_slip_business_day_id then
            raise exception 'business_day_slip_mismatch';
        end if;

        v_business_day_id := coalesce(v_business_day_id, v_slip_business_day_id);

        select b.status
          into v_business_day_status
          from public.store_business_days b
         where b.business_day_id = v_business_day_id
         for key share;

        if v_business_day_status = 'closed' then
            raise exception 'business_day_closed';
        end if;
    end if;

    if tg_op = 'DELETE' then
        return old;
    end if;

    return new;
end;
$$;

create or replace function store.guard_closed_business_day_row()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
    if current_setting('store.allow_closed_day_mutation', true) = 'on' then
        if tg_op = 'DELETE' then
            return old;
        end if;
        return new;
    end if;

    if old.status = 'closed' then
        raise exception 'business_day_closed';
    end if;

    if tg_op = 'UPDATE'
       and new.status = 'closed'
       and not exists (
           select 1
             from public.store_business_day_closing_snapshots cs
            where cs.business_day_id = old.business_day_id
       ) then
        raise exception 'business_day_closing_snapshot_required';
    end if;

    if tg_op = 'DELETE' then
        return old;
    end if;

    return new;
end;
$$;

create or replace function store.guard_immutable_closing_snapshot()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
    if current_setting('store.allow_closed_day_mutation', true) = 'on' then
        if tg_op = 'DELETE' then
            return old;
        end if;
        return new;
    end if;

    raise exception 'business_day_closing_snapshot_immutable';
end;
$$;

create or replace function store.guard_immutable_master_identity()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
    v_old jsonb;
    v_new jsonb;
begin
    if tg_op = 'DELETE' then
        raise exception 'master_identity_immutable';
    end if;

    v_old := to_jsonb(old);
    v_new := to_jsonb(new);

    if v_old->>'company_id' is distinct from v_new->>'company_id' or
       v_old->>'department_id' is distinct from v_new->>'department_id' or
       v_old->>'cast_id' is distinct from v_new->>'cast_id' or
       v_old->>'staff_id' is distinct from v_new->>'staff_id' or
       v_old->>'table_id' is distinct from v_new->>'table_id' then
        raise exception 'master_identity_immutable';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_store_business_days_closed_guard on public.store_business_days;
create trigger trg_store_business_days_closed_guard
before update or delete on public.store_business_days
for each row execute function store.guard_closed_business_day_row();

drop trigger if exists trg_store_business_day_closing_snapshots_immutable on public.store_business_day_closing_snapshots;
create trigger trg_store_business_day_closing_snapshots_immutable
before update or delete on public.store_business_day_closing_snapshots
for each row execute function store.guard_immutable_closing_snapshot();

drop trigger if exists trg_department_master_identity_immutable on public.department_master;
create trigger trg_department_master_identity_immutable
before update or delete on public.department_master
for each row execute function store.guard_immutable_master_identity();

drop trigger if exists trg_cast_master_identity_immutable on public.cast_master;
create trigger trg_cast_master_identity_immutable
before update or delete on public.cast_master
for each row execute function store.guard_immutable_master_identity();

drop trigger if exists trg_store_staff_master_identity_immutable on public.store_staff_master;
create trigger trg_store_staff_master_identity_immutable
before update or delete on public.store_staff_master
for each row execute function store.guard_immutable_master_identity();

drop trigger if exists trg_store_table_master_identity_immutable on public.store_table_master;
create trigger trg_store_table_master_identity_immutable
before update on public.store_table_master
for each row execute function store.guard_immutable_master_identity();

do $$
declare
    v_table_name text;
begin
    foreach v_table_name in array array[
        'store_cast_attendance',
        'store_staff_attendance',
        'store_slips',
        'store_slip_customers',
        'store_slip_casts',
        'store_order_lines',
        'store_order_line_cast_backs',
        'store_slip_cast_backs',
        'store_business_day_champagne_backs',
        'store_business_day_cast_advances',
        'store_slip_charge_lines',
        'store_checkouts',
        'store_checkout_payments',
        'store_slip_cast_sales_adjustments',
        'store_slip_pricing_lines',
        'store_slip_accounting_snapshots'
    ]
    loop
        execute format(
            'drop trigger if exists %I on public.%I',
            'trg_' || v_table_name || '_closed_day_guard',
            v_table_name
        );
        execute format(
            'create trigger %I before insert or update or delete on public.%I ' ||
            'for each row execute function store.guard_closed_business_day_mutation()',
            'trg_' || v_table_name || '_closed_day_guard',
            v_table_name
        );
    end loop;
end;
$$;

revoke all on table public.store_slip_accounting_snapshots from public, anon, authenticated, service_role;
revoke all on table public.store_business_day_closing_snapshots from public, anon, authenticated, service_role;
revoke all on table public.store_business_day_champagne_backs from public, anon, authenticated, service_role;
revoke all on table public.store_business_day_cast_advances from public, anon, authenticated, service_role;
revoke all on table public.store_staff_attendance from public, anon, authenticated, service_role;
revoke all on table public.store_staff_master from public, anon, authenticated, service_role;
revoke execute on function store.get_business_day_snapshot_at(bigint, bigint, timestamp with time zone) from public, anon, authenticated, service_role;
revoke execute on function store.capture_business_day_closing_snapshot(bigint, bigint, timestamp with time zone, boolean) from public, anon, authenticated, service_role;
revoke execute on function store.guard_closed_business_day_mutation() from public, anon, authenticated, service_role;
revoke execute on function store.guard_closed_business_day_row() from public, anon, authenticated, service_role;
revoke execute on function store.guard_immutable_closing_snapshot() from public, anon, authenticated, service_role;
revoke execute on function store.guard_immutable_master_identity() from public, anon, authenticated, service_role;
