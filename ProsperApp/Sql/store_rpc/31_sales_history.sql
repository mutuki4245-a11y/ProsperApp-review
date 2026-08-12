-- 営業履歴の一覧、締め済み営業日の補正、補正スナップショットを提供します。
-- 22_current_business_day_close.sql の後、99_grants.sql の前に適用します。

begin;

-- 旧実装の「営業日ごと1件」制約を外し、既存の (business_day_id, snapshot_version) を正とします。
drop index if exists public.ux_store_business_day_closing_snapshots_day;

create index if not exists idx_store_business_days_sales_history
    on public.store_business_days (department_id, business_date desc, business_day_id desc)
    where status in ('open', 'closed');

drop function if exists store.get_sales_history_page(bigint, date, date, date, bigint, integer, date);
create or replace function store.get_sales_history_page(
    p_department_id bigint,
    p_from_business_date date default null,
    p_to_business_date date default null,
    p_before_business_date date default null,
    p_before_business_day_id bigint default null,
    p_limit integer default 31,
    p_current_business_date date default null
)
returns table (
    business_day_id bigint,
    business_date date,
    status text,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    sales_amount numeric,
    customer_count integer,
    slip_count integer,
    memo text,
    latest_snapshot_version integer,
    report_available boolean,
    is_corrected boolean,
    can_edit boolean,
    edit_reason text
)
language sql
security definer
set search_path = pg_catalog, public
as $$
    with editable_rank as (
        select
            day.business_day_id,
            row_number() over (
                order by day.business_date desc, day.business_day_id desc
            ) as business_day_rank
        from public.store_business_days day
        where day.department_id = p_department_id
          and day.status in ('open', 'closed')
          and day.business_date <= coalesce(p_current_business_date, day.business_date)
    ), selected_days as (
        select day.*, rank.business_day_rank
        from public.store_business_days day
        left join editable_rank rank on rank.business_day_id = day.business_day_id
        where day.department_id = p_department_id
          and day.status in ('open', 'closed')
          and (p_from_business_date is null or day.business_date >= p_from_business_date)
          and (p_to_business_date is null or day.business_date <= p_to_business_date)
          and (
              p_before_business_date is null
              or (day.business_date, day.business_day_id)
                 < (p_before_business_date, coalesce(p_before_business_day_id, 9223372036854775807::bigint))
          )
        order by day.business_date desc, day.business_day_id desc
        limit greatest(1, least(coalesce(p_limit, 31), 101))
    )
    select
        day.business_day_id,
        day.business_date,
        day.status,
        day.opened_at,
        day.closed_at,
        coalesce(
            nullif(report.report#>>'{totals,salesAmount}', '')::numeric,
            nullif(snapshot.closing_data#>>'{totals,sales_total_amount}', '')::numeric,
            0
        ) as sales_amount,
        coalesce(
            nullif(report.report#>>'{totals,customerCount}', '')::integer,
            0
        ) as customer_count,
        coalesce(
            nullif(report.report#>>'{totals,slipCount}', '')::integer,
            jsonb_array_length(coalesce(snapshot.business_home_data->'slips', '[]'::jsonb)),
            0
        ) as slip_count,
        coalesce(report.report#>>'{businessDay,memo}', day.memo) as memo,
        snapshot.snapshot_version,
        day.status = 'open' or snapshot.closing_snapshot_id is not null as report_available,
        coalesce(snapshot.closing_data ? 'correction', false) as is_corrected,
        day.status = 'closed'
            and day.business_day_rank <= 3
            and snapshot.closing_data#>>'{daily_report,schemaVersion}' = 'daily-report-v3' as can_edit,
        case
            when day.status = 'open' then '営業中の編集は締め作業で行ってください。'
            when day.business_day_rank is null or day.business_day_rank > 3 then '直近3営業日を超えているため閲覧のみです。'
            when snapshot.closing_snapshot_id is null then '締めスナップショットがないため補正できません。'
            when snapshot.closing_data#>>'{daily_report,schemaVersion}' <> 'daily-report-v3' then '旧形式の日報は閲覧のみです。'
            else null
        end as edit_reason
    from selected_days day
    left join lateral (
        select closing_snapshot.*
        from public.store_business_day_closing_snapshots closing_snapshot
        where closing_snapshot.department_id = p_department_id
          and closing_snapshot.business_day_id = day.business_day_id
        order by closing_snapshot.snapshot_version desc
        limit 1
    ) snapshot on true
    left join lateral (
        select case
            when day.status = 'open' then store.build_business_day_daily_report(
                p_department_id,
                day.business_day_id,
                clock_timestamp(),
                'provisional')
            when jsonb_typeof(snapshot.closing_data->'daily_report') = 'object'
                then snapshot.closing_data->'daily_report'
            else null
        end as report
    ) report on true
    order by day.business_date desc, day.business_day_id desc;
$$;

drop function if exists store.get_sales_history_correction_editor(bigint, bigint, date);
create or replace function store.get_sales_history_correction_editor(
    p_department_id bigint,
    p_business_day_id bigint,
    p_current_business_date date
)
returns table (editor jsonb)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_snapshot public.store_business_day_closing_snapshots%rowtype;
    v_rank bigint;
    v_cast_entries jsonb := '[]'::jsonb;
    v_staff_entries jsonb := '[]'::jsonb;
    v_drink_back_adjustments jsonb := '[]'::jsonb;
    v_cast_sales_slips jsonb := '[]'::jsonb;
    v_source_amount_type text;
    v_split_mode text;
    v_can_edit boolean := false;
    v_edit_reason text;
begin
    if p_department_id <= 0 or p_business_day_id <= 0 or p_current_business_date is null then
        raise exception 'invalid_sales_history_editor_input';
    end if;

    perform pg_advisory_xact_lock_shared(hashtextextended(
        format('store_business_day_state:%s', p_department_id), 0));

    select day.*
      into v_business_day
      from public.store_business_days day
     where day.department_id = p_department_id
       and day.business_day_id = p_business_day_id
       and day.status in ('open', 'closed');

    if v_business_day.business_day_id is null then
        raise exception 'sales_history_business_day_not_found';
    end if;

    select ranked.business_day_rank
      into v_rank
      from (
          select day.business_day_id,
                 row_number() over (order by day.business_date desc, day.business_day_id desc) as business_day_rank
          from public.store_business_days day
          where day.department_id = p_department_id
            and day.status in ('open', 'closed')
            and day.business_date <= p_current_business_date
      ) ranked
     where ranked.business_day_id = p_business_day_id;

    select snapshot.*
      into v_snapshot
      from public.store_business_day_closing_snapshots snapshot
     where snapshot.department_id = p_department_id
       and snapshot.business_day_id = p_business_day_id
     order by snapshot.snapshot_version desc
     limit 1;

    v_can_edit := v_business_day.status = 'closed'
        and v_rank <= 3
        and v_snapshot.closing_data#>>'{daily_report,schemaVersion}' = 'daily-report-v3';
    v_edit_reason := case
        when v_business_day.status = 'open' then '営業中の編集は締め作業で行ってください。'
        when v_rank is null or v_rank > 3 then '直近3営業日を超えているため閲覧のみです。'
        when v_snapshot.closing_snapshot_id is null then '締めスナップショットがないため補正できません。'
        when v_snapshot.closing_data#>>'{daily_report,schemaVersion}' <> 'daily-report-v3' then '旧形式の日報は閲覧のみです。'
        else null
    end;

    select
        case when department.cast_sales_amount_basis in ('subtotal', 'total')
            then department.cast_sales_amount_basis else 'total' end,
        case when department.cast_sales_split_mode in ('split', 'full')
            then department.cast_sales_split_mode else 'split' end
      into v_source_amount_type, v_split_mode
      from public.department_master department
     where department.department_id = p_department_id;

    with candidates as (
        select cast_member.cast_id
          from public.cast_master cast_member
          join public.department_master department on department.department_id = cast_member.department_id
         where cast_member.company_id = v_business_day.company_id
           and cast_member.is_active = true
           and cast_member.status = 'active'
           and department.is_active = true
        union
        select attendance.cast_id
          from public.store_cast_attendance attendance
         where attendance.department_id = p_department_id
           and attendance.business_day_id = p_business_day_id
    )
    select coalesce(jsonb_agg(jsonb_build_object(
        'cast_id', cast_member.cast_id,
        'display_name', cast_member.display_name,
        'department_name', department.department_name,
        'is_active_candidate', cast_member.is_active and cast_member.status = 'active' and department.is_active,
        'is_selected', coalesce(attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out'), false),
        'clock_in_time', case when attendance.clock_in_at is null then null
            else to_char(attendance.clock_in_at at time zone 'Asia/Tokyo', 'HH24:MI') end,
        'clock_out_time', case when attendance.clock_out_at is null then null
            else to_char(attendance.clock_out_at at time zone 'Asia/Tokyo', 'HH24:MI') end,
        'uses_send_service', coalesce(attendance.uses_send_service, false),
        'version', case when attendance.updated_at is null then null
            else to_char(attendance.updated_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') end
    ) order by case when cast_member.department_id = p_department_id then 0 else 1 end,
               cast_member.sort_order, cast_member.display_name, cast_member.cast_id), '[]'::jsonb)
      into v_cast_entries
      from candidates candidate
      join public.cast_master cast_member on cast_member.cast_id = candidate.cast_id
      left join public.department_master department on department.department_id = cast_member.department_id
      left join public.store_cast_attendance attendance
        on attendance.department_id = p_department_id
       and attendance.business_day_id = p_business_day_id
       and attendance.cast_id = candidate.cast_id;

    with candidates as (
        select staff.staff_id
          from public.store_staff_master staff
         where staff.company_id = v_business_day.company_id
           and staff.department_id = p_department_id
           and staff.is_active = true
           and staff.status = 'active'
        union
        select attendance.staff_id
          from public.store_staff_attendance attendance
         where attendance.department_id = p_department_id
           and attendance.business_day_id = p_business_day_id
    )
    select coalesce(jsonb_agg(jsonb_build_object(
        'staff_id', staff.staff_id,
        'display_name', staff.display_name,
        'department_name', department.department_name,
        'employment_type', staff.employment_type,
        'is_active_candidate', staff.is_active and staff.status = 'active',
        'is_selected', coalesce(attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out'), false),
        'clock_in_time', case when attendance.clock_in_at is null then null
            else to_char(attendance.clock_in_at at time zone 'Asia/Tokyo', 'HH24:MI') end,
        'clock_out_time', case when attendance.clock_out_at is null then null
            else to_char(attendance.clock_out_at at time zone 'Asia/Tokyo', 'HH24:MI') end,
        'uses_send_service', coalesce(attendance.uses_send_service, false),
        'version', case when attendance.updated_at is null then null
            else to_char(attendance.updated_at at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') end
    ) order by staff.sort_order, staff.display_name, staff.staff_id), '[]'::jsonb)
      into v_staff_entries
      from candidates candidate
      join public.store_staff_master staff on staff.staff_id = candidate.staff_id
      left join public.department_master department on department.department_id = staff.department_id
      left join public.store_staff_attendance attendance
        on attendance.department_id = p_department_id
       and attendance.business_day_id = p_business_day_id
       and attendance.staff_id = candidate.staff_id;

    with candidates as (
        select cast_member.cast_id
          from public.cast_master cast_member
         where cast_member.department_id = p_department_id
           and cast_member.is_active = true
           and cast_member.status = 'active'
        union
        select attendance.cast_id
          from public.store_cast_attendance attendance
         where attendance.department_id = p_department_id
           and attendance.business_day_id = p_business_day_id
           and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')
        union
        select adjustment.cast_id
          from public.store_business_day_drink_back_adjustments adjustment
         where adjustment.department_id = p_department_id
           and adjustment.business_day_id = p_business_day_id
    )
    select coalesce(jsonb_agg(jsonb_build_object(
        'cast_id', cast_member.cast_id,
        'display_name', cast_member.display_name,
        'department_name', department.department_name,
        'is_required', coalesce(attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out'), false),
        'is_active', coalesce(adjustment.status = 'active', false),
        'adjustment_amount', case when adjustment.status = 'active' then adjustment.adjustment_amount else null end,
        'is_active_candidate', cast_member.is_active and cast_member.status = 'active'
    ) order by case when attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out') then 0 else 1 end,
               cast_member.sort_order, cast_member.display_name, cast_member.cast_id), '[]'::jsonb)
      into v_drink_back_adjustments
      from candidates candidate
      join public.cast_master cast_member on cast_member.cast_id = candidate.cast_id
      left join public.department_master department on department.department_id = cast_member.department_id
      left join public.store_cast_attendance attendance
        on attendance.department_id = p_department_id
       and attendance.business_day_id = p_business_day_id
       and attendance.cast_id = candidate.cast_id
      left join public.store_business_day_drink_back_adjustments adjustment
        on adjustment.department_id = p_department_id
       and adjustment.business_day_id = p_business_day_id
       and adjustment.cast_id = candidate.cast_id;

    select coalesce(jsonb_agg(
        to_jsonb(target_slip) || jsonb_build_object(
            'source_amount_type', history_settings.source_amount_type,
            'split_mode', history_settings.split_mode,
            'adjustments', coalesce((
                select jsonb_agg(jsonb_build_object(
                    'slip_cast_id', detail.slip_cast_id,
                    'cast_id', detail.cast_id,
                    'cast_display_name', detail.cast_display_name,
                    'sales_amount', detail.sales_amount,
                    'suggested_sales_amount', case history_settings.source_amount_type
                        when 'subtotal' then detail.suggested_subtotal_sales_amount
                        else detail.suggested_total_sales_amount end
                ) order by detail.started_at nulls last, detail.slip_cast_id)
                  from store.get_cast_sales_adjustment_detail(p_department_id, target_slip.slip_id) detail
            ), '[]'::jsonb)
        ) order by target_slip.checkout_at, target_slip.slip_id
    ), '[]'::jsonb)
      into v_cast_sales_slips
      from store.get_cast_sales_adjustment_slips(p_department_id, p_business_day_id) target_slip
      cross join lateral (
          select
              coalesce(max(adjustment.source_amount_type), v_source_amount_type) as source_amount_type,
              coalesce(max(adjustment.split_mode), v_split_mode) as split_mode
          from public.store_slip_cast_sales_adjustments adjustment
          where adjustment.department_id = p_department_id
            and adjustment.business_day_id = p_business_day_id
            and adjustment.slip_id = target_slip.slip_id
            and adjustment.status = 'confirmed'
      ) history_settings;

    return query select jsonb_build_object(
        'department_id', p_department_id,
        'business_day_id', v_business_day.business_day_id,
        'business_date', v_business_day.business_date,
        'status', v_business_day.status,
        'snapshot_version', v_snapshot.snapshot_version,
        'can_edit', v_can_edit,
        'edit_reason', v_edit_reason,
        'is_corrected', coalesce(v_snapshot.closing_data ? 'correction', false),
        'memo', v_business_day.memo,
        'drink_delivery_amount', v_business_day.drink_delivery_amount,
        'cast_entries', v_cast_entries,
        'staff_entries', v_staff_entries,
        'drink_back_adjustments', v_drink_back_adjustments,
        'cast_sales_slips', v_cast_sales_slips,
        'checked_at', clock_timestamp());
end;
$$;

create or replace function store.build_sales_history_correction_closing_data(
    p_department_id bigint,
    p_business_day_id bigint,
    p_captured_at timestamp with time zone,
    p_base_closing_data jsonb,
    p_correction jsonb
)
returns jsonb
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_daily_report jsonb;
    v_attendance jsonb;
    v_staff_attendance jsonb;
    v_cast_sales_adjustments jsonb;
    v_drink_back_adjustments jsonb;
begin
    select day.*
      into v_business_day
      from public.store_business_days day
     where day.department_id = p_department_id
       and day.business_day_id = p_business_day_id
       and day.status = 'closed';

    if v_business_day.business_day_id is null then
        raise exception 'sales_history_business_day_not_closed';
    end if;

    v_daily_report := store.build_business_day_daily_report(
        p_department_id, p_business_day_id, p_captured_at, 'closed');

    select coalesce(jsonb_agg(jsonb_build_object(
        'attendance_id', attendance.attendance_id,
        'cast_id', attendance.cast_id,
        'cast_display_name', cast_member.display_name,
        'cast_department_id', cast_member.department_id,
        'cast_department_name', department.department_name,
        'attendance_status', attendance.attendance_status,
        'clock_in_at', attendance.clock_in_at,
        'clock_out_at', attendance.clock_out_at,
        'uses_send_service', attendance.uses_send_service,
        'source', attendance.source,
        'memo', attendance.memo
    ) order by attendance.attendance_id), '[]'::jsonb)
      into v_attendance
      from public.store_cast_attendance attendance
      join public.cast_master cast_member on cast_member.cast_id = attendance.cast_id
      left join public.department_master department on department.department_id = cast_member.department_id
     where attendance.business_day_id = p_business_day_id;

    select coalesce(jsonb_agg(jsonb_build_object(
        'staff_attendance_id', attendance.staff_attendance_id,
        'staff_id', attendance.staff_id,
        'staff_display_name', staff.display_name,
        'employment_type', staff.employment_type,
        'staff_department_id', staff.department_id,
        'staff_department_name', department.department_name,
        'attendance_status', attendance.attendance_status,
        'clock_in_at', attendance.clock_in_at,
        'clock_out_at', attendance.clock_out_at,
        'uses_send_service', attendance.uses_send_service,
        'source', attendance.source,
        'memo', attendance.memo
    ) order by attendance.staff_attendance_id), '[]'::jsonb)
      into v_staff_attendance
      from public.store_staff_attendance attendance
      join public.store_staff_master staff on staff.staff_id = attendance.staff_id
      left join public.department_master department on department.department_id = staff.department_id
     where attendance.business_day_id = p_business_day_id;

    select coalesce(jsonb_agg(jsonb_build_object(
        'adjustment_id', adjustment.adjustment_id,
        'slip_id', adjustment.slip_id,
        'checkout_id', adjustment.checkout_id,
        'slip_cast_id', adjustment.slip_cast_id,
        'cast_id', adjustment.cast_id,
        'cast_display_name', cast_member.display_name,
        'source_amount_type', adjustment.source_amount_type,
        'split_mode', adjustment.split_mode,
        'base_amount', adjustment.base_amount,
        'sales_amount', adjustment.sales_amount,
        'status', adjustment.status,
        'memo', adjustment.memo
    ) order by adjustment.adjustment_id), '[]'::jsonb)
      into v_cast_sales_adjustments
      from public.store_slip_cast_sales_adjustments adjustment
      join public.cast_master cast_member on cast_member.cast_id = adjustment.cast_id
     where adjustment.business_day_id = p_business_day_id;

    select coalesce(jsonb_agg(jsonb_build_object(
        'business_day_drink_back_adjustment_id', adjustment.business_day_drink_back_adjustment_id,
        'cast_id', adjustment.cast_id,
        'cast_display_name', cast_member.display_name,
        'adjustment_amount', adjustment.adjustment_amount,
        'status', adjustment.status,
        'memo', adjustment.memo
    ) order by adjustment.business_day_drink_back_adjustment_id), '[]'::jsonb)
      into v_drink_back_adjustments
      from public.store_business_day_drink_back_adjustments adjustment
      join public.cast_master cast_member on cast_member.cast_id = adjustment.cast_id
     where adjustment.business_day_id = p_business_day_id;

    return coalesce(p_base_closing_data, '{}'::jsonb) || jsonb_build_object(
        'captured_at', p_captured_at,
        'daily_report', v_daily_report,
        'business_day', coalesce(p_base_closing_data->'business_day', '{}'::jsonb) || jsonb_build_object(
            'memo', v_business_day.memo,
            'drink_delivery_amount', v_business_day.drink_delivery_amount),
        'totals', coalesce(p_base_closing_data->'totals', '{}'::jsonb) || jsonb_build_object(
            'cast_sales_total_amount', coalesce((
                select sum(adjustment.sales_amount)
                  from public.store_slip_cast_sales_adjustments adjustment
                 where adjustment.business_day_id = p_business_day_id
                   and adjustment.status = 'confirmed'), 0),
            'drink_back_total_amount', coalesce((
                select sum(adjustment.adjustment_amount)
                  from public.store_business_day_drink_back_adjustments adjustment
                 where adjustment.business_day_id = p_business_day_id
                   and adjustment.status = 'active'), 0),
            'drink_delivery_amount', v_business_day.drink_delivery_amount),
        'attendance', v_attendance,
        'staff_attendance', v_staff_attendance,
        'cast_sales_adjustments', v_cast_sales_adjustments,
        'drink_back_adjustments', v_drink_back_adjustments,
        'correction', p_correction);
end;
$$;

drop function if exists store.save_sales_history_correction_v1(bigint, text, bigint, integer, date, text, text, jsonb);
create or replace function store.save_sales_history_correction_v1(
    p_department_id bigint,
    p_operation_id text,
    p_business_day_id bigint,
    p_expected_snapshot_version integer,
    p_current_business_date date,
    p_actor_email text,
    p_reason text,
    p_correction_payload jsonb
)
returns table (
    status text,
    operation_id text,
    message text,
    business_day_id bigint,
    snapshot_version integer,
    corrected_at timestamp with time zone,
    changed_sections jsonb
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_actor_email text := lower(trim(coalesce(p_actor_email, '')));
    v_reason text := trim(coalesce(p_reason, ''));
    v_payload jsonb := coalesce(p_correction_payload, '{}'::jsonb);
    v_request_body jsonb;
    v_existing_request_body jsonb;
    v_response jsonb;
    v_business_day public.store_business_days%rowtype;
    v_snapshot public.store_business_day_closing_snapshots%rowtype;
    v_business_day_rank bigint;
    v_cast_entries jsonb;
    v_staff_entries jsonb;
    v_drink_back_adjustments jsonb;
    v_cast_sales_slips jsonb;
    v_memo text;
    v_drink_delivery_amount numeric;
    v_changed_sections text[] := array[]::text[];
    v_basic_changed boolean := false;
    v_attendance_changed boolean := false;
    v_drink_back_changed boolean := false;
    v_cast_sales_changed boolean := false;
    v_entry jsonb;
    v_cast_attendance public.store_cast_attendance%rowtype;
    v_staff_attendance public.store_staff_attendance%rowtype;
    v_drink_back public.store_business_day_drink_back_adjustments%rowtype;
    v_is_selected boolean;
    v_is_active boolean;
    v_clock_in_time time;
    v_clock_out_time time;
    v_clock_in_at timestamp with time zone;
    v_clock_out_at timestamp with time zone;
    v_target_context_before text;
    v_new_closing_data jsonb;
    v_new_snapshot_version integer;
    v_corrected_at timestamp with time zone;
    v_status text := 'confirmed';
    v_message text := '営業日を補正しました。';
    v_error text;
begin
    if p_department_id <= 0
       or p_business_day_id <= 0
       or p_expected_snapshot_version is null
       or p_current_business_date is null
       or v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' then
        raise exception 'invalid_sales_history_correction_input';
    end if;

    v_request_body := jsonb_build_object(
        'operation_type', 'save_sales_history_correction_v1',
        'business_day_id', p_business_day_id,
        'expected_snapshot_version', p_expected_snapshot_version,
        'current_business_date', p_current_business_date,
        'actor_email', v_actor_email,
        'reason', v_reason,
        'correction_payload', v_payload);

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_business_day_state:%s', p_department_id), 0));

    delete from store.current_business_day_operation_results
     where created_at < now() - interval '30 days'
       and operation_type <> 'save_sales_history_correction_v1';

    select operation.request_body, operation.response
      into v_existing_request_body, v_response
      from store.current_business_day_operation_results operation
     where operation.department_id = p_department_id
       and operation.operation_id = v_operation_id;

    if found then
        if v_existing_request_body <> v_request_body then
            raise exception 'business_day_operation_id_reused';
        end if;

        return query select
            v_response->>'status',
            v_operation_id,
            v_response->>'message',
            nullif(v_response->>'business_day_id', '')::bigint,
            nullif(v_response->>'snapshot_version', '')::integer,
            nullif(v_response->>'corrected_at', '')::timestamp with time zone,
            coalesce(v_response->'changed_sections', '[]'::jsonb);
        return;
    end if;

    <<save_attempt>>
    begin
        if v_actor_email = '' or length(v_actor_email) > 320 or position('@' in v_actor_email) <= 1 then
            v_status := 'permission_denied';
            v_message := 'ログインユーザーを確認できません。再ログインしてください。';
            exit save_attempt;
        end if;

        if v_reason = '' or length(v_reason) > 500 then
            v_status := 'validation_error';
            v_message := '補正理由を1文字以上500文字以内で入力してください。';
            exit save_attempt;
        end if;

        if jsonb_typeof(v_payload) <> 'object' then
            v_status := 'validation_error';
            v_message := '補正内容を確認してください。';
            exit save_attempt;
        end if;

        select day.*
          into v_business_day
          from public.store_business_days day
         where day.department_id = p_department_id
           and day.business_day_id = p_business_day_id
         for update;

        if v_business_day.business_day_id is null then
            v_status := 'validation_error';
            v_message := '対象の営業日が見つかりません。';
            exit save_attempt;
        end if;

        select ranked.business_day_rank
          into v_business_day_rank
          from (
              select day.business_day_id,
                     row_number() over (order by day.business_date desc, day.business_day_id desc) as business_day_rank
              from public.store_business_days day
              where day.department_id = p_department_id
                and day.status in ('open', 'closed')
                and day.business_date <= p_current_business_date
          ) ranked
         where ranked.business_day_id = p_business_day_id;

        if v_business_day.status <> 'closed' or v_business_day_rank is null or v_business_day_rank > 3 then
            v_status := 'permission_denied';
            v_message := 'この営業日は補正対象外です。';
            exit save_attempt;
        end if;

        select snapshot.*
          into v_snapshot
          from public.store_business_day_closing_snapshots snapshot
         where snapshot.department_id = p_department_id
           and snapshot.business_day_id = p_business_day_id
         order by snapshot.snapshot_version desc
         limit 1
         for share;

        if v_snapshot.closing_snapshot_id is null
           or v_snapshot.closing_data#>>'{daily_report,schemaVersion}' <> 'daily-report-v3' then
            v_status := 'permission_denied';
            v_message := 'この営業日は旧形式または日報未作成のため補正できません。';
            exit save_attempt;
        end if;

        if v_snapshot.snapshot_version <> p_expected_snapshot_version then
            v_status := 'conflict';
            v_message := '別の補正が先に保存されています。最新の内容を確認してください。';
            exit save_attempt;
        end if;

        v_memo := nullif(trim(coalesce(v_payload->>'memo', '')), '');
        if length(coalesce(v_memo, '')) > 500
           or coalesce(v_payload->>'drink_delivery_amount', '') !~ '^[0-9]+$' then
            v_status := 'validation_error';
            v_message := '締めメモまたは酒代を確認してください。';
            exit save_attempt;
        end if;

        v_drink_delivery_amount := (v_payload->>'drink_delivery_amount')::numeric;
        if v_drink_delivery_amount > 999999999999 then
            v_status := 'validation_error';
            v_message := '酒代は999,999,999,999円以下で入力してください。';
            exit save_attempt;
        end if;

        v_cast_entries := v_payload->'cast_entries';
        v_staff_entries := v_payload->'staff_entries';
        v_drink_back_adjustments := v_payload->'drink_back_adjustments';
        v_cast_sales_slips := v_payload->'cast_sales_slips';

        if jsonb_typeof(v_cast_entries) <> 'array'
           or jsonb_typeof(v_staff_entries) <> 'array'
           or jsonb_typeof(v_drink_back_adjustments) <> 'array'
           or jsonb_typeof(v_cast_sales_slips) <> 'array'
           or jsonb_array_length(v_cast_entries) > 300
           or jsonb_array_length(v_staff_entries) > 300
           or jsonb_array_length(v_drink_back_adjustments) > 500
           or jsonb_array_length(v_cast_sales_slips) > 300 then
            v_status := 'validation_error';
            v_message := '補正内容の件数または形式を確認してください。';
            exit save_attempt;
        end if;

        if exists (
            select 1
              from jsonb_array_elements(v_cast_entries) item
             where jsonb_typeof(item.value) <> 'object'
                or coalesce(item.value->>'cast_id', '') !~ '^[1-9][0-9]*$'
                or jsonb_typeof(item.value->'is_selected') <> 'boolean'
                or (
                    (item.value->>'is_selected')::boolean
                    and (
                        coalesce(item.value->>'clock_in_time', '') !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'
                        or coalesce(item.value->>'clock_out_time', '') !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'
                        or jsonb_typeof(item.value->'uses_send_service') <> 'boolean'
                    )
                )
        ) or exists (
            select 1
              from jsonb_array_elements(v_staff_entries) item
             where jsonb_typeof(item.value) <> 'object'
                or coalesce(item.value->>'staff_id', '') !~ '^[1-9][0-9]*$'
                or jsonb_typeof(item.value->'is_selected') <> 'boolean'
                or (
                    (item.value->>'is_selected')::boolean
                    and (
                        coalesce(item.value->>'clock_in_time', '') !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'
                        or coalesce(item.value->>'clock_out_time', '') !~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'
                        or jsonb_typeof(item.value->'uses_send_service') <> 'boolean'
                    )
                )
        ) then
            v_status := 'validation_error';
            v_message := '勤怠の入力内容を確認してください。';
            exit save_attempt;
        end if;

        if exists (
            select item.value->>'cast_id'
              from jsonb_array_elements(v_cast_entries) item
             group by item.value->>'cast_id'
            having count(*) > 1
        ) or exists (
            select item.value->>'staff_id'
              from jsonb_array_elements(v_staff_entries) item
             group by item.value->>'staff_id'
            having count(*) > 1
        ) then
            v_status := 'validation_error';
            v_message := '同じ出勤者が重複しています。';
            exit save_attempt;
        end if;

        if exists (
            select 1
              from public.store_cast_attendance attendance
             where attendance.department_id = p_department_id
               and attendance.business_day_id = p_business_day_id
               and not exists (
                   select 1 from jsonb_array_elements(v_cast_entries) item
                    where (item.value->>'cast_id')::bigint = attendance.cast_id)
        ) or exists (
            select 1
              from public.store_staff_attendance attendance
             where attendance.department_id = p_department_id
               and attendance.business_day_id = p_business_day_id
               and not exists (
                   select 1 from jsonb_array_elements(v_staff_entries) item
                    where (item.value->>'staff_id')::bigint = attendance.staff_id)
        ) then
            v_status := 'validation_error';
            v_message := '既存の勤怠行が不足しています。画面を再読み込みしてください。';
            exit save_attempt;
        end if;

        if exists (
            select 1
              from jsonb_array_elements(v_cast_entries) item
              left join public.store_cast_attendance attendance
                on attendance.department_id = p_department_id
               and attendance.business_day_id = p_business_day_id
               and attendance.cast_id = (item.value->>'cast_id')::bigint
              left join public.cast_master cast_member
                on cast_member.cast_id = (item.value->>'cast_id')::bigint
               and cast_member.company_id = v_business_day.company_id
               and cast_member.is_active = true
               and cast_member.status = 'active'
              left join public.department_master department
                on department.department_id = cast_member.department_id
               and department.is_active = true
             where attendance.attendance_id is null and cast_member.cast_id is null
        ) or exists (
            select 1
              from jsonb_array_elements(v_staff_entries) item
              left join public.store_staff_attendance attendance
                on attendance.department_id = p_department_id
               and attendance.business_day_id = p_business_day_id
               and attendance.staff_id = (item.value->>'staff_id')::bigint
              left join public.store_staff_master staff
                on staff.staff_id = (item.value->>'staff_id')::bigint
               and staff.company_id = v_business_day.company_id
               and staff.department_id = p_department_id
               and staff.is_active = true
               and staff.status = 'active'
             where attendance.staff_attendance_id is null and staff.staff_id is null
        ) then
            v_status := 'validation_error';
            v_message := '追加できない出勤者が含まれています。';
            exit save_attempt;
        end if;

        if not exists (
            select 1 from jsonb_array_elements(v_cast_entries) item
             where (item.value->>'is_selected')::boolean
            union all
            select 1 from jsonb_array_elements(v_staff_entries) item
             where (item.value->>'is_selected')::boolean
        ) then
            v_status := 'validation_error';
            v_message := '出勤者を1名以上選択してください。';
            exit save_attempt;
        end if;

        if exists (
            select 1
              from jsonb_array_elements(v_drink_back_adjustments) item
             where jsonb_typeof(item.value) <> 'object'
                or coalesce(item.value->>'cast_id', '') !~ '^[1-9][0-9]*$'
                or jsonb_typeof(item.value->'is_active') <> 'boolean'
                or (
                    (item.value->>'is_active')::boolean
                    and (
                        coalesce(item.value->>'adjustment_amount', '') !~ '^-?[0-9]+$'
                        or abs((item.value->>'adjustment_amount')::numeric) > 999999999999
                    )
                )
        ) or exists (
            select item.value->>'cast_id'
              from jsonb_array_elements(v_drink_back_adjustments) item
             group by item.value->>'cast_id'
            having count(*) > 1
        ) then
            v_status := 'validation_error';
            v_message := 'ドリンクバック調整を確認してください。';
            exit save_attempt;
        end if;

        if exists (
            select 1
              from public.store_business_day_drink_back_adjustments adjustment
             where adjustment.department_id = p_department_id
               and adjustment.business_day_id = p_business_day_id
               and not exists (
                   select 1 from jsonb_array_elements(v_drink_back_adjustments) item
                    where (item.value->>'cast_id')::bigint = adjustment.cast_id)
        ) then
            v_status := 'validation_error';
            v_message := '既存のドリンクバック調整が不足しています。画面を再読み込みしてください。';
            exit save_attempt;
        end if;

        if exists (
            select 1
              from jsonb_array_elements(v_drink_back_adjustments) item
              left join public.store_business_day_drink_back_adjustments existing
                on existing.department_id = p_department_id
               and existing.business_day_id = p_business_day_id
               and existing.cast_id = (item.value->>'cast_id')::bigint
              left join public.store_cast_attendance attendance
                on attendance.department_id = p_department_id
               and attendance.business_day_id = p_business_day_id
               and attendance.cast_id = (item.value->>'cast_id')::bigint
              left join public.cast_master cast_member
                on cast_member.cast_id = (item.value->>'cast_id')::bigint
               and cast_member.department_id = p_department_id
               and cast_member.is_active = true
               and cast_member.status = 'active'
             where existing.business_day_drink_back_adjustment_id is null
               and attendance.attendance_id is null
               and cast_member.cast_id is null
        ) then
            v_status := 'validation_error';
            v_message := '追加できないドリンクバック対象が含まれています。';
            exit save_attempt;
        end if;

        if exists (
            select 1
              from jsonb_array_elements(v_cast_sales_slips) slip
             where jsonb_typeof(slip.value) <> 'object'
                or coalesce(slip.value->>'slip_id', '') !~ '^[1-9][0-9]*$'
                or slip.value->>'source_amount_type' not in ('subtotal', 'total')
                or slip.value->>'split_mode' not in ('split', 'full')
                or jsonb_typeof(slip.value->'adjustments') <> 'array'
                or jsonb_array_length(slip.value->'adjustments') > 100
        ) or exists (
            select slip.value->>'slip_id'
              from jsonb_array_elements(v_cast_sales_slips) slip
             group by slip.value->>'slip_id'
            having count(*) > 1
        ) then
            v_status := 'validation_error';
            v_message := 'キャスト売上額調整を確認してください。';
            exit save_attempt;
        end if;

        if exists (
            select 1
              from jsonb_array_elements(v_cast_sales_slips) submitted
             where exists (
                 select 1
                   from public.store_slip_cast_sales_adjustments existing
                  where existing.department_id = p_department_id
                    and existing.business_day_id = p_business_day_id
                    and existing.slip_id = (submitted.value->>'slip_id')::bigint
                    and existing.status = 'confirmed'
                    and (existing.source_amount_type <> submitted.value->>'source_amount_type'
                         or existing.split_mode <> submitted.value->>'split_mode'))
        ) then
            v_status := 'validation_error';
            v_message := '過去営業日の売上配分方式は変更できません。';
            exit save_attempt;
        end if;

        if exists (
            select 1
              from store.get_cast_sales_adjustment_slips(p_department_id, p_business_day_id) required_slip
             where not exists (
                 select 1 from jsonb_array_elements(v_cast_sales_slips) submitted
                  where (submitted.value->>'slip_id')::bigint = required_slip.slip_id)
        ) or exists (
            select 1
              from jsonb_array_elements(v_cast_sales_slips) submitted
             where not exists (
                 select 1 from store.get_cast_sales_adjustment_slips(p_department_id, p_business_day_id) required_slip
                  where required_slip.slip_id = (submitted.value->>'slip_id')::bigint)
        ) then
            v_status := 'validation_error';
            v_message := '会計済み伝票の構成が変わっています。画面を再読み込みしてください。';
            exit save_attempt;
        end if;

        for v_entry in select value from jsonb_array_elements(v_cast_sales_slips)
        loop
            if exists (
                select 1
                  from jsonb_array_elements(v_entry->'adjustments') item
                 where jsonb_typeof(item.value) <> 'object'
                    or coalesce(item.value->>'slip_cast_id', '') !~ '^[1-9][0-9]*$'
                    or (
                        item.value->>'sales_amount' is not null
                        and (
                            item.value->>'sales_amount' !~ '^[0-9]+$'
                            or (item.value->>'sales_amount')::numeric > 999999999999
                        )
                    )
            ) or (
                select count(*) from jsonb_array_elements(v_entry->'adjustments')
            ) <> (
                select count(*)
                  from public.store_slip_casts slip_cast
                 where slip_cast.slip_id = (v_entry->>'slip_id')::bigint
                   and slip_cast.status = 'active'
                   and slip_cast.nomination_type in ('nomination', 'in_store', 'companion')
            ) or exists (
                select item.value->>'slip_cast_id'
                  from jsonb_array_elements(v_entry->'adjustments') item
                 group by item.value->>'slip_cast_id'
                having count(*) > 1
            ) or exists (
                select 1
                  from jsonb_array_elements(v_entry->'adjustments') item
                 where not exists (
                     select 1
                       from public.store_slip_casts slip_cast
                      where slip_cast.slip_id = (v_entry->>'slip_id')::bigint
                        and slip_cast.slip_cast_id = (item.value->>'slip_cast_id')::bigint
                        and slip_cast.status = 'active'
                        and slip_cast.nomination_type in ('nomination', 'in_store', 'companion'))
            ) then
                v_status := 'validation_error';
                v_message := 'キャスト売上額の配分を確認してください。';
                exit save_attempt;
            end if;

            if exists (
                select 1
                  from jsonb_array_elements(v_entry->'adjustments') item
                having count(*) filter (where item.value->>'sales_amount' is null) > 0
                   and (
                       count(*) filter (where item.value->>'sales_amount' is not null) > 0
                       or exists (
                           select 1
                             from public.store_slip_cast_sales_adjustments existing
                            where existing.department_id = p_department_id
                              and existing.business_day_id = p_business_day_id
                              and existing.slip_id = (v_entry->>'slip_id')::bigint
                              and existing.status = 'confirmed'
                       )
                   )
            ) then
                v_status := 'validation_error';
                v_message := 'キャスト売上額は伝票内の全員分を入力してください。';
                exit save_attempt;
            end if;
        end loop;

        v_basic_changed := v_business_day.memo is distinct from v_memo
            or v_business_day.drink_delivery_amount is distinct from v_drink_delivery_amount;

        for v_entry in select value from jsonb_array_elements(v_cast_entries)
        loop
            v_is_selected := (v_entry->>'is_selected')::boolean;
            if v_is_selected then
                v_clock_in_time := (v_entry->>'clock_in_time')::time;
                v_clock_out_time := (v_entry->>'clock_out_time')::time;
                v_clock_in_at := (((v_business_day.business_date
                    + case when v_clock_in_time < time '12:00' then 1 else 0 end)::timestamp
                    + v_clock_in_time) at time zone 'Asia/Tokyo');
                v_clock_out_at := (((v_business_day.business_date
                    + case when v_clock_out_time < time '12:00' then 1 else 0 end)::timestamp
                    + v_clock_out_time) at time zone 'Asia/Tokyo');
                if v_clock_out_at <= v_clock_in_at then
                    v_status := 'validation_error';
                    v_message := 'キャストの退勤時刻は出勤時刻より後にしてください。';
                    exit save_attempt;
                end if;
            else
                v_clock_in_at := null;
                v_clock_out_at := null;
            end if;

            select attendance.*
              into v_cast_attendance
              from public.store_cast_attendance attendance
             where attendance.department_id = p_department_id
               and attendance.business_day_id = p_business_day_id
               and attendance.cast_id = (v_entry->>'cast_id')::bigint;

            if (v_cast_attendance.attendance_id is null and v_is_selected)
               or (v_cast_attendance.attendance_id is not null and (
                   (v_cast_attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')) is distinct from v_is_selected
                   or v_cast_attendance.clock_in_at is distinct from v_clock_in_at
                   or v_cast_attendance.clock_out_at is distinct from v_clock_out_at
                   or v_cast_attendance.uses_send_service is distinct from
                      case when v_is_selected then (v_entry->>'uses_send_service')::boolean else false end)) then
                v_attendance_changed := true;
            end if;
        end loop;

        for v_entry in select value from jsonb_array_elements(v_staff_entries)
        loop
            v_is_selected := (v_entry->>'is_selected')::boolean;
            if v_is_selected then
                v_clock_in_time := (v_entry->>'clock_in_time')::time;
                v_clock_out_time := (v_entry->>'clock_out_time')::time;
                v_clock_in_at := (((v_business_day.business_date
                    + case when v_clock_in_time < time '12:00' then 1 else 0 end)::timestamp
                    + v_clock_in_time) at time zone 'Asia/Tokyo');
                v_clock_out_at := (((v_business_day.business_date
                    + case when v_clock_out_time < time '12:00' then 1 else 0 end)::timestamp
                    + v_clock_out_time) at time zone 'Asia/Tokyo');
                if v_clock_out_at <= v_clock_in_at then
                    v_status := 'validation_error';
                    v_message := 'スタッフの退勤時刻は出勤時刻より後にしてください。';
                    exit save_attempt;
                end if;
            else
                v_clock_in_at := null;
                v_clock_out_at := null;
            end if;

            select attendance.*
              into v_staff_attendance
              from public.store_staff_attendance attendance
             where attendance.department_id = p_department_id
               and attendance.business_day_id = p_business_day_id
               and attendance.staff_id = (v_entry->>'staff_id')::bigint;

            if (v_staff_attendance.staff_attendance_id is null and v_is_selected)
               or (v_staff_attendance.staff_attendance_id is not null and (
                   (v_staff_attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out')) is distinct from v_is_selected
                   or v_staff_attendance.clock_in_at is distinct from v_clock_in_at
                   or v_staff_attendance.clock_out_at is distinct from v_clock_out_at
                   or v_staff_attendance.uses_send_service is distinct from
                      case when v_is_selected then (v_entry->>'uses_send_service')::boolean else false end)) then
                v_attendance_changed := true;
            end if;
        end loop;

        for v_entry in select value from jsonb_array_elements(v_drink_back_adjustments)
        loop
            v_is_active := (v_entry->>'is_active')::boolean;
            select adjustment.*
              into v_drink_back
              from public.store_business_day_drink_back_adjustments adjustment
             where adjustment.department_id = p_department_id
               and adjustment.business_day_id = p_business_day_id
               and adjustment.cast_id = (v_entry->>'cast_id')::bigint;
            if (v_drink_back.business_day_drink_back_adjustment_id is null and v_is_active)
               or (v_drink_back.business_day_drink_back_adjustment_id is not null and (
                   (v_drink_back.status = 'active') is distinct from v_is_active
                   or (v_is_active and v_drink_back.adjustment_amount is distinct from
                       (v_entry->>'adjustment_amount')::numeric))) then
                v_drink_back_changed := true;
            end if;
        end loop;

        for v_entry in select value from jsonb_array_elements(v_cast_sales_slips)
        loop
            if exists (
                (select
                    (item.value->>'slip_cast_id')::bigint,
                    (item.value->>'sales_amount')::numeric,
                    v_entry->>'source_amount_type',
                    v_entry->>'split_mode'
                 from jsonb_array_elements(v_entry->'adjustments') item
                 where item.value->>'sales_amount' is not null
                 except
                 select adjustment.slip_cast_id, adjustment.sales_amount,
                        adjustment.source_amount_type, adjustment.split_mode
                   from public.store_slip_cast_sales_adjustments adjustment
                  where adjustment.department_id = p_department_id
                    and adjustment.business_day_id = p_business_day_id
                    and adjustment.slip_id = (v_entry->>'slip_id')::bigint
                    and adjustment.status = 'confirmed')
                union all
                (select adjustment.slip_cast_id, adjustment.sales_amount,
                        adjustment.source_amount_type, adjustment.split_mode
                   from public.store_slip_cast_sales_adjustments adjustment
                  where adjustment.department_id = p_department_id
                    and adjustment.business_day_id = p_business_day_id
                    and adjustment.slip_id = (v_entry->>'slip_id')::bigint
                    and adjustment.status = 'confirmed'
                 except
                 select
                    (item.value->>'slip_cast_id')::bigint,
                    (item.value->>'sales_amount')::numeric,
                    v_entry->>'source_amount_type',
                    v_entry->>'split_mode'
                 from jsonb_array_elements(v_entry->'adjustments') item
                 where item.value->>'sales_amount' is not null)
            ) then
                v_cast_sales_changed := true;
            end if;
        end loop;

        if (v_attendance_changed or v_drink_back_changed) and exists (
            select 1
              from jsonb_array_elements(v_cast_entries) attendance
             where (attendance.value->>'is_selected')::boolean
               and not exists (
                   select 1
                     from jsonb_array_elements(v_drink_back_adjustments) adjustment
                    where (adjustment.value->>'cast_id')::bigint = (attendance.value->>'cast_id')::bigint
                      and (adjustment.value->>'is_active')::boolean)
        ) then
            v_status := 'validation_error';
            v_message := '勤怠またはドリンクバックを補正する場合は、出勤キャスト全員の金額を入力してください。0円も保存が必要です。';
            exit save_attempt;
        end if;

        if v_basic_changed then v_changed_sections := array_append(v_changed_sections, 'business_day'); end if;
        if v_attendance_changed then v_changed_sections := array_append(v_changed_sections, 'attendance'); end if;
        if v_drink_back_changed then v_changed_sections := array_append(v_changed_sections, 'drink_back'); end if;
        if v_cast_sales_changed then v_changed_sections := array_append(v_changed_sections, 'cast_sales'); end if;

        if cardinality(v_changed_sections) = 0 then
            v_status := 'unchanged';
            v_message := '変更はありませんでした。';
            v_new_snapshot_version := v_snapshot.snapshot_version;
            exit save_attempt;
        end if;

        v_target_context_before := current_setting('store.sales_history_correction_business_day_id', true);
        perform set_config(
            'store.sales_history_correction_business_day_id',
            p_business_day_id::text,
            true);

        begin
            if v_basic_changed then
                update public.store_business_days day
                   set memo = v_memo,
                       drink_delivery_amount = v_drink_delivery_amount,
                       drink_delivery_amount_entered = true,
                       updated_at = now()
                 where day.department_id = p_department_id
                   and day.business_day_id = p_business_day_id
                   and day.status = 'closed';
            end if;

            if v_attendance_changed then
                for v_entry in select value from jsonb_array_elements(v_cast_entries)
                loop
                    v_is_selected := (v_entry->>'is_selected')::boolean;
                    if v_is_selected then
                        v_clock_in_time := (v_entry->>'clock_in_time')::time;
                        v_clock_out_time := (v_entry->>'clock_out_time')::time;
                        v_clock_in_at := (((v_business_day.business_date
                            + case when v_clock_in_time < time '12:00' then 1 else 0 end)::timestamp
                            + v_clock_in_time) at time zone 'Asia/Tokyo');
                        v_clock_out_at := (((v_business_day.business_date
                            + case when v_clock_out_time < time '12:00' then 1 else 0 end)::timestamp
                            + v_clock_out_time) at time zone 'Asia/Tokyo');
                        insert into public.store_cast_attendance (
                            company_id, department_id, business_day_id, business_date, cast_id,
                            attendance_status, clock_in_at, clock_out_at, uses_send_service, source)
                        values (
                            v_business_day.company_id, p_department_id, p_business_day_id,
                            v_business_day.business_date, (v_entry->>'cast_id')::bigint,
                            'checked_out', v_clock_in_at, v_clock_out_at,
                            (v_entry->>'uses_send_service')::boolean, 'manual')
                        on conflict on constraint uq_store_cast_attendance_day_cast do update
                            set attendance_status = 'checked_out',
                                clock_in_at = excluded.clock_in_at,
                                clock_out_at = excluded.clock_out_at,
                                uses_send_service = excluded.uses_send_service,
                                source = 'manual',
                                updated_at = now();
                    else
                        update public.store_cast_attendance attendance
                           set attendance_status = 'cancelled',
                               clock_in_at = null,
                               clock_out_at = null,
                               uses_send_service = false,
                               source = 'manual',
                               updated_at = now()
                         where attendance.department_id = p_department_id
                           and attendance.business_day_id = p_business_day_id
                           and attendance.cast_id = (v_entry->>'cast_id')::bigint;
                    end if;
                end loop;

                for v_entry in select value from jsonb_array_elements(v_staff_entries)
                loop
                    v_is_selected := (v_entry->>'is_selected')::boolean;
                    if v_is_selected then
                        v_clock_in_time := (v_entry->>'clock_in_time')::time;
                        v_clock_out_time := (v_entry->>'clock_out_time')::time;
                        v_clock_in_at := (((v_business_day.business_date
                            + case when v_clock_in_time < time '12:00' then 1 else 0 end)::timestamp
                            + v_clock_in_time) at time zone 'Asia/Tokyo');
                        v_clock_out_at := (((v_business_day.business_date
                            + case when v_clock_out_time < time '12:00' then 1 else 0 end)::timestamp
                            + v_clock_out_time) at time zone 'Asia/Tokyo');
                        insert into public.store_staff_attendance (
                            company_id, department_id, business_day_id, business_date, staff_id,
                            attendance_status, clock_in_at, clock_out_at, uses_send_service, source)
                        values (
                            v_business_day.company_id, p_department_id, p_business_day_id,
                            v_business_day.business_date, (v_entry->>'staff_id')::bigint,
                            'checked_out', v_clock_in_at, v_clock_out_at,
                            (v_entry->>'uses_send_service')::boolean, 'manual')
                        on conflict on constraint uq_store_staff_attendance_day_staff do update
                            set attendance_status = 'checked_out',
                                clock_in_at = excluded.clock_in_at,
                                clock_out_at = excluded.clock_out_at,
                                uses_send_service = excluded.uses_send_service,
                                source = 'manual',
                                updated_at = now();
                    else
                        update public.store_staff_attendance attendance
                           set attendance_status = 'cancelled',
                               clock_in_at = null,
                               clock_out_at = null,
                               uses_send_service = false,
                               source = 'manual',
                               updated_at = now()
                         where attendance.department_id = p_department_id
                           and attendance.business_day_id = p_business_day_id
                           and attendance.staff_id = (v_entry->>'staff_id')::bigint;
                    end if;
                end loop;
            end if;

            if v_drink_back_changed then
                for v_entry in select value from jsonb_array_elements(v_drink_back_adjustments)
                loop
                    v_is_active := (v_entry->>'is_active')::boolean;
                    if v_is_active then
                        insert into public.store_business_day_drink_back_adjustments (
                            business_day_id, business_date, company_id, department_id,
                            cast_id, adjustment_amount, status, memo)
                        values (
                            p_business_day_id, v_business_day.business_date,
                            v_business_day.company_id, p_department_id,
                            (v_entry->>'cast_id')::bigint,
                            (v_entry->>'adjustment_amount')::numeric,
                            'active', null)
                        on conflict on constraint uq_store_business_day_drink_back_adjustments_day_cast do update
                            set adjustment_amount = excluded.adjustment_amount,
                                status = 'active',
                                updated_at = now();
                    else
                        update public.store_business_day_drink_back_adjustments adjustment
                           set status = 'voided',
                               updated_at = now()
                         where adjustment.department_id = p_department_id
                           and adjustment.business_day_id = p_business_day_id
                           and adjustment.cast_id = (v_entry->>'cast_id')::bigint
                           and adjustment.status <> 'voided';
                    end if;
                end loop;
            end if;

            if v_cast_sales_changed then
                for v_entry in select value from jsonb_array_elements(v_cast_sales_slips)
                loop
                    if exists (
                        (select
                            (item.value->>'slip_cast_id')::bigint,
                            (item.value->>'sales_amount')::numeric,
                            v_entry->>'source_amount_type',
                            v_entry->>'split_mode'
                         from jsonb_array_elements(v_entry->'adjustments') item
                         where item.value->>'sales_amount' is not null
                         except
                         select adjustment.slip_cast_id, adjustment.sales_amount,
                                adjustment.source_amount_type, adjustment.split_mode
                           from public.store_slip_cast_sales_adjustments adjustment
                          where adjustment.department_id = p_department_id
                            and adjustment.business_day_id = p_business_day_id
                            and adjustment.slip_id = (v_entry->>'slip_id')::bigint
                            and adjustment.status = 'confirmed')
                        union all
                        (select adjustment.slip_cast_id, adjustment.sales_amount,
                                adjustment.source_amount_type, adjustment.split_mode
                           from public.store_slip_cast_sales_adjustments adjustment
                          where adjustment.department_id = p_department_id
                            and adjustment.business_day_id = p_business_day_id
                            and adjustment.slip_id = (v_entry->>'slip_id')::bigint
                            and adjustment.status = 'confirmed'
                         except
                         select
                            (item.value->>'slip_cast_id')::bigint,
                            (item.value->>'sales_amount')::numeric,
                            v_entry->>'source_amount_type',
                            v_entry->>'split_mode'
                         from jsonb_array_elements(v_entry->'adjustments') item
                         where item.value->>'sales_amount' is not null)
                    ) then
                        perform store.save_cast_sales_adjustment_internal(
                            p_department_id,
                            (v_entry->>'slip_id')::bigint,
                            v_entry->'adjustments',
                            v_entry->>'source_amount_type',
                            v_entry->>'split_mode');
                    end if;
                end loop;
            end if;

            update public.store_business_days day
               set business_ui_revision = day.business_ui_revision + 1,
                   updated_at = now()
             where day.department_id = p_department_id
               and day.business_day_id = p_business_day_id
               and day.status = 'closed'
            returning day.* into v_business_day;

            v_corrected_at := clock_timestamp();
            v_new_snapshot_version := v_snapshot.snapshot_version + 1;
            v_new_closing_data := store.build_sales_history_correction_closing_data(
                p_department_id,
                p_business_day_id,
                v_corrected_at,
                v_snapshot.closing_data,
                jsonb_build_object(
                    'operation_id', v_operation_id,
                    'actor_email', v_actor_email,
                    'reason', v_reason,
                    'corrected_at', v_corrected_at,
                    'changed_sections', to_jsonb(v_changed_sections),
                    'source_snapshot_version', v_snapshot.snapshot_version));

            insert into public.store_business_day_closing_snapshots (
                business_day_id, business_date, company_id, department_id,
                snapshot_version, closed_at, backfilled, business_home_data, closing_data)
            values (
                v_business_day.business_day_id,
                v_business_day.business_date,
                v_business_day.company_id,
                v_business_day.department_id,
                v_new_snapshot_version,
                v_business_day.closed_at,
                v_snapshot.backfilled,
                v_snapshot.business_home_data,
                v_new_closing_data);

            perform set_config(
                'store.sales_history_correction_business_day_id',
                coalesce(v_target_context_before, ''),
                true);
        exception when others then
            v_error := sqlerrm;
            perform set_config(
                'store.sales_history_correction_business_day_id',
                coalesce(v_target_context_before, ''),
                true);
            if v_error like 'invalid_%'
               or v_error like 'cast_sales_%'
               or v_error like 'store_slip_%' then
                v_status := 'validation_error';
                v_message := '補正内容を保存できません。最新の内容を確認してください。';
                exit save_attempt;
            end if;
            raise;
        end;
    end save_attempt;

    v_response := jsonb_build_object(
        'status', v_status,
        'operation_id', v_operation_id,
        'message', v_message,
        'business_day_id', p_business_day_id,
        'snapshot_version', coalesce(v_new_snapshot_version, v_snapshot.snapshot_version),
        'corrected_at', v_corrected_at,
        'changed_sections', to_jsonb(v_changed_sections));

    if v_status in ('confirmed', 'unchanged') then
        insert into store.current_business_day_operation_results (
            department_id, operation_id, operation_type, request_body, response)
        values (
            p_department_id,
            v_operation_id,
            'save_sales_history_correction_v1',
            v_request_body,
            v_response);
    end if;

    return query select
        v_status,
        v_operation_id,
        v_message,
        p_business_day_id,
        coalesce(v_new_snapshot_version, v_snapshot.snapshot_version),
        v_corrected_at,
        to_jsonb(v_changed_sections);
end;
$$;

revoke all on function store.get_sales_history_page(bigint, date, date, date, bigint, integer, date)
    from public, anon, authenticated, service_role;
revoke all on function store.get_sales_history_correction_editor(bigint, bigint, date)
    from public, anon, authenticated, service_role;
revoke all on function store.build_sales_history_correction_closing_data(bigint, bigint, timestamp with time zone, jsonb, jsonb)
    from public, anon, authenticated, service_role;
revoke all on function store.save_sales_history_correction_v1(bigint, text, bigint, integer, date, text, text, jsonb)
    from public, anon, authenticated, service_role;

commit;
