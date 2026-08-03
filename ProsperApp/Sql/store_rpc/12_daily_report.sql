begin;

-- 営業中の日報と締め時に固定する日報を、同じ集計規則で生成します。
-- 締め処理は p_state='closed' と締め時刻を渡し、返却JSONを closing_data.daily_report に保存します。
create or replace function store.build_business_day_daily_report(
    p_department_id bigint,
    p_business_day_id bigint,
    p_captured_at timestamp with time zone default clock_timestamp(),
    p_state text default 'provisional'
)
returns jsonb
language plpgsql
security definer
set search_path = pg_catalog, public, accounting
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_department_name text;
    v_business_home_data jsonb;
    v_payments jsonb := '[]'::jsonb;
    v_item_categories jsonb := '[]'::jsonb;
    v_visits jsonb := '[]'::jsonb;
    v_casts jsonb := '[]'::jsonb;
    v_staffs jsonb := '[]'::jsonb;
    v_expense_accounts jsonb := '[]'::jsonb;
    v_confirmed_checkout_count integer := 0;
    v_slip_count integer := 0;
    v_customer_count integer := 0;
    v_open_slip_count integer := 0;
    v_sales_total numeric := 0;
    v_payment_total numeric := 0;
    v_cash_total numeric := 0;
    v_expense_total numeric := 0;
    v_payment_difference numeric := 0;
    v_warnings jsonb := '[]'::jsonb;
begin
    if lower(coalesce(nullif(trim(p_state), ''), 'provisional')) not in ('provisional', 'closed') then
        raise exception 'invalid_daily_report_state';
    end if;

    select bd.*
      into v_business_day
      from public.store_business_days bd
     where bd.business_day_id = p_business_day_id
       and bd.department_id = p_department_id
       and bd.status in ('open', 'closed');

    if v_business_day.business_day_id is null then
        raise exception 'business_day_not_found';
    end if;

    select d.department_name
      into v_department_name
      from public.department_master d
     where d.department_id = p_department_id;

    select snapshot.snapshot
      into v_business_home_data
      from store.get_business_day_snapshot_at(
          p_department_id,
          p_business_day_id,
          coalesce(p_captured_at, clock_timestamp())
      ) snapshot;

    if v_business_home_data is null then
        raise exception 'business_day_snapshot_not_found';
    end if;

    select
        count(*)::integer,
        coalesce(sum(checkout.total_amount), 0),
        coalesce(sum(slip.customer_count), 0)
      into
        v_confirmed_checkout_count,
        v_sales_total,
        v_customer_count
      from public.store_checkouts checkout
      join public.store_slips slip
        on slip.slip_id = checkout.slip_id
     where slip.business_day_id = p_business_day_id
       and checkout.status = 'confirmed';

    select
        count(*) filter (where slip.status <> 'cancelled')::integer,
        count(*) filter (where slip.status in ('open', 'checkout_ready'))::integer
      into v_slip_count, v_open_slip_count
      from public.store_slips slip
     where slip.business_day_id = p_business_day_id;

    select
        coalesce(sum(payment.amount), 0),
        coalesce(sum(payment.amount) filter (
            where lower(payment.payment_method_code) = 'cash'
               or payment.payment_method_name = '現金'
        ), 0)
      into v_payment_total, v_cash_total
      from public.store_checkout_payments payment
      join public.store_slips slip
        on slip.slip_id = payment.slip_id
     where slip.business_day_id = p_business_day_id
       and payment.status = 'confirmed';

    with payment_totals as (
        select
            payment.payment_method_code,
            payment.payment_method_name,
            sum(payment.amount) as amount
          from public.store_checkout_payments payment
          join public.store_slips slip
            on slip.slip_id = payment.slip_id
         where slip.business_day_id = p_business_day_id
           and payment.status = 'confirmed'
         group by payment.payment_method_code, payment.payment_method_name
    ),
    payment_rows as (
        select
            master.payment_method_code,
            master.payment_method_name,
            master.sort_order,
            coalesce(sum(payment_totals.amount), 0) as amount
          from public.payment_method_master master
          left join payment_totals
            on payment_totals.payment_method_code = master.payment_method_code
         where master.department_id = p_department_id
           and master.is_active = true
         group by
            master.payment_method_code,
            master.payment_method_name,
            master.sort_order
        union all
        select
            payment_totals.payment_method_code,
            payment_totals.payment_method_name,
            9999,
            payment_totals.amount
          from payment_totals
         where not exists (
             select 1
               from public.payment_method_master master
              where master.department_id = p_department_id
                and master.payment_method_code = payment_totals.payment_method_code
         )
    )
    select coalesce(jsonb_agg(jsonb_build_object(
        'code', payment_rows.payment_method_code,
        'name', payment_rows.payment_method_name,
        'amount', payment_rows.amount
    ) order by payment_rows.sort_order, payment_rows.payment_method_code), '[]'::jsonb)
      into v_payments
      from payment_rows;

    select coalesce(jsonb_agg(jsonb_build_object(
        'code', grouped.category_code,
        'name', grouped.category_name,
        'quantity', grouped.quantity,
        'amount', grouped.amount
    ) order by grouped.category_name, grouped.category_code), '[]'::jsonb)
      into v_item_categories
      from (
        select
            coalesce(nullif(line.item_category_code_snapshot, ''), 'uncategorized') as category_code,
            coalesce(nullif(line.item_category_name_snapshot, ''), '未分類') as category_name,
            sum(line.quantity) as quantity,
            sum(line.amount) as amount
          from public.store_order_lines line
          join public.store_slips slip
            on slip.slip_id = line.slip_id
         where slip.business_day_id = p_business_day_id
           and line.status = 'active'
           and exists (
               select 1
                 from public.store_checkouts checkout
                where checkout.slip_id = slip.slip_id
                  and checkout.status = 'confirmed'
           )
         group by
            coalesce(nullif(line.item_category_code_snapshot, ''), 'uncategorized'),
            coalesce(nullif(line.item_category_name_snapshot, ''), '未分類')
      ) grouped;

    select coalesce(jsonb_agg(jsonb_build_object(
        'slipId', nullif(visit.value->>'id', '')::bigint,
        'slipNo', visit.value->>'slipNo',
        'entryTime', visit.value->>'openedTime',
        'tableDisplay', visit.value->>'tableDisplay',
        'customerCount', coalesce(nullif(visit.value->>'customerCount', '')::integer, 0),
        'customerNames', visit.value->>'customerNames',
        'castNames', visit.value->>'castNames',
        'amount', case
            when checkout.checkout_id is not null then checkout.total_amount
            else coalesce(nullif(visit.value->>'accountingAmount', '')::numeric, 0)
        end,
        'payments', coalesce(checkout.payments, '[]'::jsonb),
        'memo', visit.value->>'memo',
        'status', visit.value->>'status',
        'statusDisplay', visit.value->>'statusDisplay'
    ) order by visit.ordinality), '[]'::jsonb)
      into v_visits
      from jsonb_array_elements(coalesce(v_business_home_data->'slips', '[]'::jsonb))
           with ordinality visit(value, ordinality)
      left join lateral (
          select
              c.checkout_id,
              c.total_amount,
              coalesce((
                  select jsonb_agg(jsonb_build_object(
                      'code', p.payment_method_code,
                      'name', p.payment_method_name,
                      'amount', p.amount
                  ) order by p.checkout_payment_id)
                    from public.store_checkout_payments p
                   where p.checkout_id = c.checkout_id
                     and p.status = 'confirmed'
              ), '[]'::jsonb) as payments
            from public.store_checkouts c
           where c.slip_id = nullif(visit.value->>'id', '')::bigint
             and c.status = 'confirmed'
           order by c.checkout_at desc, c.checkout_id desc
           limit 1
      ) checkout on true;

    select coalesce(jsonb_agg(jsonb_build_object(
        'castId', attendance.cast_id,
        'displayName', cast_member.display_name,
        'clockInAt', attendance.clock_in_at,
        'clockOutAt', attendance.clock_out_at,
        'usesSendService', attendance.uses_send_service,
        'drinkBackAmount', coalesce((
            select sum(cast_back.back_amount)
              from public.store_order_line_cast_backs cast_back
             where cast_back.business_day_id = p_business_day_id
               and cast_back.cast_id = attendance.cast_id
               and cast_back.back_type = 'drink'
               and cast_back.status = 'active'
        ), 0) + coalesce((
            select sum(back_amount.back_amount)
              from public.store_business_day_champagne_backs back_amount
             where back_amount.business_day_id = p_business_day_id
               and back_amount.cast_id = attendance.cast_id
               and back_amount.status = 'active'
        ), 0),
        'assignmentBackAmount', coalesce((
            select sum(cast_back.back_amount)
              from public.store_slip_cast_backs cast_back
             where cast_back.business_day_id = p_business_day_id
               and cast_back.cast_id = attendance.cast_id
               and cast_back.back_type = 'nomination'
               and cast_back.status = 'active'
        ), 0),
        'castSalesAmount', coalesce((
            select sum(adjustment.sales_amount)
              from public.store_slip_cast_sales_adjustments adjustment
             where adjustment.business_day_id = p_business_day_id
               and adjustment.cast_id = attendance.cast_id
               and adjustment.status = 'confirmed'
        ), 0),
        'advanceAmount', coalesce((
            select sum(advance.amount)
              from public.store_business_day_cast_advances advance
             where advance.business_day_id = p_business_day_id
               and advance.cast_id = attendance.cast_id
        ), 0)
    ) order by cast_member.sort_order, cast_member.cast_id), '[]'::jsonb)
      into v_casts
      from public.store_cast_attendance attendance
      join public.cast_master cast_member
        on cast_member.cast_id = attendance.cast_id
     where attendance.business_day_id = p_business_day_id
       and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out');

    select coalesce(jsonb_agg(jsonb_build_object(
        'staffId', attendance.staff_id,
        'displayName', staff_member.display_name,
        'employmentType', staff_member.employment_type,
        'clockInAt', attendance.clock_in_at,
        'clockOutAt', attendance.clock_out_at,
        'usesSendService', attendance.uses_send_service
    ) order by staff_member.sort_order, staff_member.staff_id), '[]'::jsonb)
      into v_staffs
      from public.store_staff_attendance attendance
      join public.store_staff_master staff_member
        on staff_member.staff_id = attendance.staff_id
     where attendance.business_day_id = p_business_day_id
       and attendance.attendance_status in ('scheduled', 'checked_in', 'checked_out');

    select
        coalesce(jsonb_agg(jsonb_build_object(
            'accountCode', grouped.account_code,
            'accountName', grouped.account_name,
            'amount', grouped.amount
        ) order by grouped.sort_order, grouped.account_code), '[]'::jsonb),
        coalesce(sum(grouped.amount), 0)
      into v_expense_accounts, v_expense_total
      from (
        select
            line.account_code,
            coalesce(
                nullif(max(line.account_name_snapshot), ''),
                nullif(max(account.account_name), ''),
                line.account_code
            ) as account_name,
            min(coalesce(account.sort_order, 9999)) as sort_order,
            sum(line.amount) as amount
          from accounting.journal_entry_lines line
          join accounting.journal_entries entry
            on entry.journal_entry_id = line.journal_entry_id
          left join accounting.account_master account
            on account.account_code = line.account_code
         where entry.entry_date = v_business_day.business_date
           and entry.status in ('confirmed', 'ready', 'exported')
           and line.department_id = p_department_id
           and upper(line.dc_flag) in ('D', 'DEBIT')
           and exists (
               select 1
                 from accounting.document_journal_links document_link
                where document_link.journal_entry_id = entry.journal_entry_id
           )
         group by line.account_code
      ) grouped;

    v_payment_difference := v_sales_total - v_payment_total;
    if v_open_slip_count > 0 then
        v_warnings := v_warnings || jsonb_build_array(
            format('未会計伝票が%s件あります。確定売上には含めていません。', v_open_slip_count)
        );
    end if;
    if v_payment_difference <> 0 then
        v_warnings := v_warnings || jsonb_build_array(
            format('確定売上と支払内訳に%s円の差額があります。', v_payment_difference)
        );
    end if;

    return jsonb_build_object(
        'schemaVersion', 'daily-report-v3',
        'state', lower(coalesce(nullif(trim(p_state), ''), 'provisional')),
        'capturedAt', coalesce(p_captured_at, clock_timestamp()),
        'legacyUnavailableSections', '[]'::jsonb,
        'businessDay', jsonb_build_object(
            'businessDayId', v_business_day.business_day_id,
            'businessDate', v_business_day.business_date,
            'departmentName', v_department_name,
            'openedAt', v_business_day.opened_at,
            'closedAt', case
                when lower(coalesce(nullif(trim(p_state), ''), 'provisional')) = 'closed'
                    then coalesce(v_business_day.closed_at, p_captured_at)
                else v_business_day.closed_at
            end,
            'memo', v_business_day.memo
        ),
        'totals', jsonb_build_object(
            'salesAmount', v_sales_total,
            'cashAmount', v_cash_total,
            'expenseAmount', v_expense_total,
            'cashBalanceAmount', v_cash_total - v_expense_total,
            'drinkDeliveryAmount', v_business_day.drink_delivery_amount,
            'confirmedCheckoutCount', v_confirmed_checkout_count,
            'slipCount', v_slip_count,
            'customerCount', v_customer_count,
            'openSlipCount', v_open_slip_count,
            'paymentDifferenceAmount', v_payment_difference
        ),
        'payments', v_payments,
        'itemCategories', v_item_categories,
        'visits', v_visits,
        'casts', v_casts,
        'staffs', v_staffs,
        'expenseAccounts', v_expense_accounts,
        'warnings', v_warnings
    );
end;
$$;

-- 営業中の営業日を優先し、なければ直近の締め済み営業日だけを返します。
-- v1スナップショットでは当時保存されていない欄を現在値で補完しません。
create or replace function store.get_business_day_daily_report(
    p_department_id bigint,
    p_business_day_id bigint default null
)
returns table (
    report jsonb
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_snapshot public.store_business_day_closing_snapshots%rowtype;
    v_payments jsonb := '[]'::jsonb;
    v_visits jsonb := '[]'::jsonb;
    v_cash_total numeric := 0;
begin
    if p_business_day_id is not null then
        select bd.*
          into v_business_day
          from public.store_business_days bd
         where bd.department_id = p_department_id
           and bd.business_day_id = p_business_day_id
           and bd.status in ('open', 'closed');
    else
        select bd.*
          into v_business_day
          from public.store_business_days bd
         where bd.department_id = p_department_id
           and bd.status = 'open'
         order by bd.business_date desc, bd.business_day_id desc
         limit 1;

        if v_business_day.business_day_id is null then
            select bd.*
              into v_business_day
              from public.store_business_days bd
              join public.store_business_day_closing_snapshots snapshot
                on snapshot.business_day_id = bd.business_day_id
             where bd.department_id = p_department_id
               and bd.status = 'closed'
             order by bd.business_date desc, bd.business_day_id desc
             limit 1;
        end if;
    end if;

    if v_business_day.business_day_id is null then
        raise exception 'daily_report_business_day_not_found';
    end if;

    if v_business_day.status = 'open' then
        return query
        select store.build_business_day_daily_report(
            p_department_id,
            v_business_day.business_day_id,
            clock_timestamp(),
            'provisional'
        );
        return;
    end if;

    select snapshot.*
      into v_snapshot
      from public.store_business_day_closing_snapshots snapshot
     where snapshot.department_id = p_department_id
       and snapshot.business_day_id = v_business_day.business_day_id
     order by snapshot.snapshot_version desc
     limit 1;

    if v_snapshot.closing_snapshot_id is null then
        raise exception 'daily_report_closing_snapshot_not_found';
    end if;

    if jsonb_typeof(v_snapshot.closing_data->'daily_report') = 'object' then
        return query
        select jsonb_set(
            jsonb_set(
                v_snapshot.closing_data->'daily_report',
                '{visits}',
                coalesce((
                    select jsonb_agg(
                        visit.value || jsonb_build_object(
                            'castNames', coalesce(
                                snapshot_visit.value->>'castNames',
                                visit.value->>'castNames',
                                ''
                            )
                        )
                        order by visit.ordinality
                    )
                      from jsonb_array_elements(
                          coalesce(v_snapshot.closing_data->'daily_report'->'visits', '[]'::jsonb)
                      ) with ordinality visit(value, ordinality)
                      left join lateral (
                          select home_visit.value
                            from jsonb_array_elements(
                                coalesce(v_snapshot.business_home_data->'slips', '[]'::jsonb)
                            ) home_visit(value)
                           where nullif(home_visit.value->>'id', '')::bigint =
                                 nullif(visit.value->>'slipId', '')::bigint
                           limit 1
                      ) snapshot_visit on true
                ), '[]'::jsonb),
                true
            ),
            '{casts}',
            coalesce((
                select jsonb_agg(
                    (cast_row.value - 'champagneBackAmount') || jsonb_build_object(
                        'drinkBackAmount',
                        coalesce(nullif(cast_row.value->>'drinkBackAmount', '')::numeric, 0) +
                        coalesce(nullif(cast_row.value->>'champagneBackAmount', '')::numeric, 0)
                    )
                    order by cast_row.ordinality
                )
                  from jsonb_array_elements(
                      coalesce(v_snapshot.closing_data->'daily_report'->'casts', '[]'::jsonb)
                  ) with ordinality cast_row(value, ordinality)
            ), '[]'::jsonb),
            true
        );
        return;
    end if;

    select coalesce(jsonb_agg(jsonb_build_object(
        'code', payment.value->>'payment_method_code',
        'name', payment.value->>'payment_method_name',
        'amount', coalesce(nullif(payment.value->>'amount', '')::numeric, 0)
    ) order by payment.ordinality), '[]'::jsonb)
      into v_payments
      from jsonb_array_elements(coalesce(v_snapshot.closing_data->'payments', '[]'::jsonb))
           with ordinality payment(value, ordinality);

    select coalesce(sum(nullif(payment.value->>'amount', '')::numeric), 0)
      into v_cash_total
      from jsonb_array_elements(coalesce(v_snapshot.closing_data->'payments', '[]'::jsonb)) payment(value)
     where lower(coalesce(payment.value->>'payment_method_code', '')) = 'cash'
        or payment.value->>'payment_method_name' = '現金';

    select coalesce(jsonb_agg(jsonb_build_object(
        'slipId', nullif(visit.value->>'id', '')::bigint,
        'slipNo', visit.value->>'slipNo',
        'entryTime', visit.value->>'openedTime',
        'tableDisplay', visit.value->>'tableDisplay',
        'customerCount', coalesce(nullif(visit.value->>'customerCount', '')::integer, 0),
        'customerNames', visit.value->>'customerNames',
        'castNames', visit.value->>'castNames',
        'amount', coalesce(nullif(visit.value->>'accountingAmount', '')::numeric, 0),
        'payments', '[]'::jsonb,
        'memo', visit.value->>'memo',
        'status', visit.value->>'status',
        'statusDisplay', visit.value->>'statusDisplay'
    ) order by visit.ordinality), '[]'::jsonb)
      into v_visits
      from jsonb_array_elements(coalesce(v_snapshot.business_home_data->'slips', '[]'::jsonb))
           with ordinality visit(value, ordinality);

    return query
    select jsonb_build_object(
        'schemaVersion', 'daily-report-legacy-v1',
        'state', 'legacy',
        'capturedAt', v_snapshot.closed_at,
        'legacyUnavailableSections', jsonb_build_array('expenses', 'castAdvances', 'itemCategories'),
        'businessDay', jsonb_build_object(
            'businessDayId', v_business_day.business_day_id,
            'businessDate', v_business_day.business_date,
            'departmentName', v_snapshot.closing_data#>>'{business_day,department_name}',
            'openedAt', v_snapshot.closing_data#>>'{business_day,opened_at}',
            'closedAt', v_snapshot.closed_at,
            'memo', v_snapshot.closing_data#>>'{business_day,memo}'
        ),
        'totals', jsonb_build_object(
            'salesAmount', coalesce(nullif(v_snapshot.closing_data#>>'{totals,sales_total_amount}', '')::numeric, 0),
            'cashAmount', v_cash_total,
            'expenseAmount', null,
            'cashBalanceAmount', null,
            'drinkDeliveryAmount', coalesce(nullif(v_snapshot.closing_data#>>'{totals,drink_delivery_amount}', '')::numeric, 0),
            'confirmedCheckoutCount', coalesce(nullif(v_snapshot.closing_data#>>'{totals,confirmed_checkout_count}', '')::integer, 0),
            'slipCount', jsonb_array_length(coalesce(v_snapshot.business_home_data->'slips', '[]'::jsonb)),
            'customerCount', (
                select coalesce(sum(coalesce(nullif(visit.value->>'customerCount', '')::integer, 0)), 0)
                  from jsonb_array_elements(coalesce(v_snapshot.business_home_data->'slips', '[]'::jsonb)) visit(value)
                 where visit.value->>'status' <> 'cancelled'
            ),
            'openSlipCount', 0,
            'paymentDifferenceAmount', coalesce(nullif(v_snapshot.closing_data#>>'{totals,sales_total_amount}', '')::numeric, 0)
                - coalesce(nullif(v_snapshot.closing_data#>>'{totals,payment_total_amount}', '')::numeric, 0)
        ),
        'payments', v_payments,
        'itemCategories', '[]'::jsonb,
        'visits', v_visits,
        'casts', coalesce((
            select jsonb_agg(jsonb_build_object(
                'castId', nullif(attendance.value->>'cast_id', '')::bigint,
                'displayName', attendance.value->>'cast_display_name',
                'clockInAt', attendance.value->>'clock_in_at',
                'clockOutAt', attendance.value->>'clock_out_at',
                'usesSendService', coalesce((attendance.value->>'uses_send_service')::boolean, false),
                'drinkBackAmount', coalesce((
                    select sum(coalesce(nullif(back_amount.value->>'back_amount', '')::numeric, 0))
                      from jsonb_array_elements(coalesce(v_snapshot.closing_data->'champagne_backs', '[]'::jsonb)) back_amount(value)
                     where back_amount.value->>'cast_id' = attendance.value->>'cast_id'
                       and back_amount.value->>'status' = 'active'
                ), 0),
                'assignmentBackAmount', null,
                'castSalesAmount', coalesce((
                    select sum(coalesce(nullif(adjustment.value->>'sales_amount', '')::numeric, 0))
                      from jsonb_array_elements(coalesce(v_snapshot.closing_data->'cast_sales_adjustments', '[]'::jsonb)) adjustment(value)
                     where adjustment.value->>'cast_id' = attendance.value->>'cast_id'
                       and adjustment.value->>'status' = 'confirmed'
                ), 0),
                'advanceAmount', null
            ) order by attendance.ordinality)
              from jsonb_array_elements(coalesce(v_snapshot.closing_data->'attendance', '[]'::jsonb))
                   with ordinality attendance(value, ordinality)
             where attendance.value->>'attendance_status' in ('scheduled', 'checked_in', 'checked_out')
        ), '[]'::jsonb),
        'staffs', coalesce((
            select jsonb_agg(jsonb_build_object(
                'staffId', nullif(attendance.value->>'staff_id', '')::bigint,
                'displayName', attendance.value->>'staff_display_name',
                'employmentType', coalesce(nullif(attendance.value->>'employment_type', ''), 'employee'),
                'clockInAt', attendance.value->>'clock_in_at',
                'clockOutAt', attendance.value->>'clock_out_at',
                'usesSendService', coalesce((attendance.value->>'uses_send_service')::boolean, false)
            ) order by attendance.ordinality)
              from jsonb_array_elements(coalesce(v_snapshot.closing_data->'staff_attendance', '[]'::jsonb))
                   with ordinality attendance(value, ordinality)
             where attendance.value->>'attendance_status' in ('scheduled', 'checked_in', 'checked_out')
        ), '[]'::jsonb),
        'expenseAccounts', '[]'::jsonb,
        'warnings', jsonb_build_array(
            'この営業日は旧形式で締められたため、経費・キャスト前渡金・商品カテゴリ内訳は保存されていません。'
        )
    );
end;
$$;

revoke execute on function store.build_business_day_daily_report(bigint, bigint, timestamp with time zone, text)
    from public, anon, authenticated, service_role;
revoke execute on function store.get_business_day_daily_report(bigint, bigint)
    from public, anon, authenticated, service_role;

commit;
