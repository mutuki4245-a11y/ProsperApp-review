-- 締めトップの全パネル、ドリンクバックeditor fragment、cast master差分を1 readで返します。

begin;

drop function if exists store.get_current_closing_dashboard(bigint);
drop function if exists store.get_current_closing_dashboard(bigint, text);

create or replace function store.get_current_closing_dashboard(
    p_department_id bigint,
    p_known_cast_master_revision text default null
)
returns table (
    department_id bigint,
    has_business_day boolean,
    business_day_id bigint,
    business_day_revision bigint,
    business_date date,
    memo text,
    open_slip_count integer,
    drink_delivery_amount numeric,
    is_drink_delivery_amount_entered boolean,
    attendance_count integer,
    missing_clock_out_count integer,
    cast_sales_required_slip_count integer,
    cast_sales_completed_slip_count integer,
    cast_sales_missing_slip_count integer,
    drink_back_required_cast_count integer,
    drink_back_completed_cast_count integer,
    drink_back_missing_cast_count integer,
    drink_back_total_amount numeric,
    drink_back_editor jsonb,
    cast_master_revision text,
    active_casts jsonb,
    pending_receipt_count integer,
    can_close boolean,
    block_reasons jsonb,
    checked_at timestamp with time zone
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_readiness record;
    v_pending_receipt_count integer := 0;
    v_checked_at timestamp with time zone := clock_timestamp();
    v_drink_back_editor jsonb;
    v_active_casts jsonb;
    v_cast_master_revision text;
begin
    if p_department_id <= 0 then
        raise exception 'invalid_closing_dashboard_input';
    end if;

    select coalesce(jsonb_agg(jsonb_build_object(
        'cast_id', cast_member.cast_id,
        'display_name', cast_member.display_name,
        'department_name', department.department_name
    ) order by cast_member.sort_order, cast_member.cast_id), '[]'::jsonb)
      into v_active_casts
      from public.cast_master cast_member
      left join public.department_master department
        on department.department_id = cast_member.department_id
     where cast_member.department_id = p_department_id
       and cast_member.is_active = true;
    v_cast_master_revision := md5(v_active_casts::text);

    select business_day.*
      into v_business_day
      from public.store_business_days business_day
     where business_day.department_id = p_department_id
       and business_day.status = 'open'
     order by business_day.opened_at desc
     limit 1;

    if v_business_day.business_day_id is null then
        select count(*)::integer
          into v_pending_receipt_count
          from store.get_pending_receipts_internal(p_department_id, 'unprocessed');
        v_drink_back_editor := jsonb_build_object(
            'department_id', p_department_id,
            'has_business_day', false,
            'business_day_id', null,
            'business_day_revision', 0,
            'business_date', null,
            'required_rows', '[]'::jsonb,
            'optional_rows', '[]'::jsonb,
            'status', jsonb_build_object(
                'required_cast_count', 0,
                'completed_cast_count', 0,
                'missing_cast_count', 0,
                'total_amount', 0),
            'optional_adjustment_count', 0,
            'checked_at', v_checked_at);

        return query
        select
            p_department_id,
            false,
            null::bigint,
            0::bigint,
            null::date,
            null::text,
            0,
            0::numeric,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0::numeric,
            v_drink_back_editor,
            v_cast_master_revision,
            case when p_known_cast_master_revision = v_cast_master_revision then null else v_active_casts end,
            coalesce(v_pending_receipt_count, 0),
            false,
            jsonb_build_array('営業中の営業日がありません。最初の業務入力で営業日が自動作成されます。'),
            v_checked_at;
        return;
    end if;

    select readiness.*
      into v_readiness
      from store.get_business_day_closing_readiness_internal(
          p_department_id,
          v_business_day.business_day_id,
          'unprocessed') readiness;
    v_drink_back_editor := store.build_business_day_drink_back_editor(
        p_department_id,
        v_business_day.business_day_id);

    return query
    select
        p_department_id,
        true,
        v_business_day.business_day_id,
        v_business_day.business_ui_revision,
        v_business_day.business_date,
        v_business_day.memo,
        coalesce(v_readiness.open_slip_count, 0),
        coalesce(v_readiness.drink_delivery_amount, 0),
        coalesce(v_readiness.is_drink_delivery_amount_entered, false),
        coalesce(v_readiness.attendance_count, 0),
        coalesce(v_readiness.missing_clock_out_count, 0),
        coalesce(v_readiness.cast_sales_required_slip_count, 0),
        coalesce(v_readiness.cast_sales_completed_slip_count, 0),
        coalesce(v_readiness.cast_sales_missing_slip_count, 0),
        coalesce(v_readiness.drink_back_required_cast_count, 0),
        coalesce(v_readiness.drink_back_completed_cast_count, 0),
        coalesce(v_readiness.drink_back_missing_cast_count, 0),
        coalesce(v_readiness.drink_back_total_amount, 0),
        v_drink_back_editor,
        v_cast_master_revision,
        case when p_known_cast_master_revision = v_cast_master_revision then null else v_active_casts end,
        coalesce(v_readiness.pending_receipt_count, 0),
        coalesce(v_readiness.can_close, false),
        coalesce(v_readiness.block_reasons, '[]'::jsonb),
        coalesce(v_readiness.checked_at, v_checked_at);
end;
$$;

revoke all on function store.get_current_closing_dashboard(bigint, text)
    from public, anon, authenticated, service_role;

commit;
