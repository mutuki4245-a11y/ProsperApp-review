begin;

create schema if not exists store;

drop function if exists public.get_store_departments();
drop function if exists store.get_departments();
drop function if exists store.delete_non_master_records(bigint, text);

create or replace function store.get_departments()
returns table (
    department_id bigint,
    company_id bigint,
    department_code text,
    department_name text,
    is_active boolean
)
language sql
security definer
set search_path = public
as $$
    select
        d.department_id,
        d.company_id,
        d.department_code,
        d.department_name,
        d.is_active
    from public.department_master d
    where d.is_active = true
    order by d.company_id asc, d.department_code asc, d.department_id asc;
$$;

revoke execute on function store.get_departments() from public, anon, authenticated, service_role;

create or replace function store.delete_non_master_records(
    p_department_id bigint,
    p_confirmation text
)
returns table (
    table_name text,
    deleted_count integer
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_department_exists boolean;
    v_business_day_ids bigint[] := array[]::bigint[];
    v_slip_ids bigint[] := array[]::bigint[];
    v_checkout_ids bigint[] := array[]::bigint[];
    v_order_line_ids bigint[] := array[]::bigint[];
    v_slip_cast_ids bigint[] := array[]::bigint[];
    v_deleted integer;
begin
    if p_confirmation is distinct from 'DELETE_NON_MASTER_RECORDS' then
        raise exception 'debug_delete_confirmation_required';
    end if;

    if p_department_id is null or p_department_id <= 0 then
        raise exception 'store_department_not_found';
    end if;

    select exists (
        select 1
        from public.department_master d
        where d.department_id = p_department_id
          and d.is_active = true
    )
    into v_department_exists;

    if not v_department_exists then
        raise exception 'store_department_not_found';
    end if;

    select coalesce(array_agg(b.business_day_id), array[]::bigint[])
    into v_business_day_ids
    from public.store_business_days b
    where b.department_id = p_department_id;

    select coalesce(array_agg(s.slip_id), array[]::bigint[])
    into v_slip_ids
    from public.store_slips s
    where s.department_id = p_department_id
       or s.business_day_id = any(v_business_day_ids);

    select coalesce(array_agg(c.checkout_id), array[]::bigint[])
    into v_checkout_ids
    from public.store_checkouts c
    where c.department_id = p_department_id
       or c.slip_id = any(v_slip_ids);

    select coalesce(array_agg(ol.order_line_id), array[]::bigint[])
    into v_order_line_ids
    from public.store_order_lines ol
    where ol.slip_id = any(v_slip_ids);

    select coalesce(array_agg(sc.slip_cast_id), array[]::bigint[])
      into v_slip_cast_ids
    from public.store_slip_casts sc
    where sc.slip_id = any(v_slip_ids);

    -- 明示確認済みの全消去だけは、締め後不変トリガーをこのトランザクション内で解除します。
    perform set_config('store.allow_closed_day_mutation', 'on', true);

    delete from public.store_business_day_closing_snapshots cs
    where cs.department_id = p_department_id
       or cs.business_day_id = any(v_business_day_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_business_day_closing_snapshots';
    deleted_count := v_deleted;
    return next;

    delete from public.store_slip_accounting_snapshots ss
    where ss.department_id = p_department_id
       or ss.business_day_id = any(v_business_day_ids)
       or ss.slip_id = any(v_slip_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_slip_accounting_snapshots';
    deleted_count := v_deleted;
    return next;

    delete from public.store_business_home_flush_batches b
    where b.department_id = p_department_id
       or b.business_day_id = any(v_business_day_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_business_home_flush_batches';
    deleted_count := v_deleted;
    return next;

    delete from public.store_slip_pricing_lines pl
    where pl.department_id = p_department_id
       or pl.business_day_id = any(v_business_day_ids)
       or pl.slip_id = any(v_slip_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_slip_pricing_lines';
    deleted_count := v_deleted;
    return next;

    delete from public.store_checkout_payments p
    where p.department_id = p_department_id
       or p.checkout_id = any(v_checkout_ids)
       or p.slip_id = any(v_slip_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_checkout_payments';
    deleted_count := v_deleted;
    return next;

    delete from public.store_slip_cast_sales_adjustments a
    where a.department_id = p_department_id
       or a.business_day_id = any(v_business_day_ids)
       or a.slip_id = any(v_slip_ids)
       or a.checkout_id = any(v_checkout_ids)
       or a.slip_cast_id = any(v_slip_cast_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_slip_cast_sales_adjustments';
    deleted_count := v_deleted;
    return next;

    delete from public.store_order_line_cast_backs b
    where b.department_id = p_department_id
       or b.business_day_id = any(v_business_day_ids)
       or b.slip_id = any(v_slip_ids)
       or b.order_line_id = any(v_order_line_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_order_line_cast_backs';
    deleted_count := v_deleted;
    return next;

    delete from public.store_slip_cast_backs b
    where b.department_id = p_department_id
       or b.business_day_id = any(v_business_day_ids)
       or b.slip_id = any(v_slip_ids)
       or b.slip_cast_id = any(v_slip_cast_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_slip_cast_backs';
    deleted_count := v_deleted;
    return next;

    delete from public.store_slip_charge_lines l
    where l.department_id = p_department_id
       or l.business_day_id = any(v_business_day_ids)
       or l.slip_id = any(v_slip_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_slip_charge_lines';
    deleted_count := v_deleted;
    return next;

    delete from public.store_order_lines ol
    where ol.slip_id = any(v_slip_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_order_lines';
    deleted_count := v_deleted;
    return next;

    delete from public.store_slip_casts sc
    where sc.slip_id = any(v_slip_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_slip_casts';
    deleted_count := v_deleted;
    return next;

    delete from public.store_slip_customers c
    where c.slip_id = any(v_slip_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_slip_customers';
    deleted_count := v_deleted;
    return next;

    delete from public.store_checkouts c
    where c.department_id = p_department_id
       or c.slip_id = any(v_slip_ids)
       or c.checkout_id = any(v_checkout_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_checkouts';
    deleted_count := v_deleted;
    return next;

    delete from public.store_slips s
    where s.department_id = p_department_id
       or s.business_day_id = any(v_business_day_ids)
       or s.slip_id = any(v_slip_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_slips';
    deleted_count := v_deleted;
    return next;

    delete from public.store_cast_attendance a
    where a.department_id = p_department_id
       or a.business_day_id = any(v_business_day_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_cast_attendance';
    deleted_count := v_deleted;
    return next;

    delete from public.store_business_days b
    where b.department_id = p_department_id
       or b.business_day_id = any(v_business_day_ids);
    get diagnostics v_deleted = row_count;
    table_name := 'store_business_days';
    deleted_count := v_deleted;
    return next;
end;
$$;

revoke execute on function store.delete_non_master_records(bigint, text) from public, anon, authenticated, service_role;

commit;
