-- ProsperApp schema reference for Codex agents.
-- Purpose:
--   This file is a read-only reference for agents that need table/column/RPC information.
--   Status 2026-07-20: checkout-related definitions below have been reconciled with
--   the source implementation, but this remains a compact reference, not an
--   executable schema. For the authoritative checkout contract, read
--   Sql/store_order_accounting_tables.sql, Sql/store_rpc/08_checkout_ready.sql,
--   Infrastructure/Supabase/SupabaseCheckoutRepository.cs, and the prosper-rpc allowlist.
--   Do not execute this file as a migration. Execute the dedicated files instead:
--     - Sql/store_order_accounting_tables.sql
--     - Sql/store_settings_functions.sql
--     - Sql/store_rpc/00_schema.sql
--     - Sql/store_rpc/01_business_day.sql
--     - Sql/store_rpc/02_store_masters.sql
--     - Sql/store_rpc/03_slips.sql
--     - Sql/store_rpc/04_orders.sql
--     - Sql/store_rpc/05_checkout.sql
--     - Sql/store_rpc/06_receipts.sql
--     - Sql/store_rpc/07_cast_sales_adjustments.sql
--     - Sql/store_rpc/08_checkout_ready.sql
--     - Sql/store_rpc/99_grants.sql
--     - Sql/quick_entry_account_master_updates.sql
--   Sql/store_rpc_functions.sql is a non-executable index for the split RPC files.
--
-- Current DB access policy:
--   Application DB operations should go through Supabase RPC functions.
--   Tables have RLS enabled where added by this project; policies are intentionally not defined here.

-- -----------------------------------------------------------------------------
-- Existing master / receipt / journal tables provided by Supabase schema snapshot
-- -----------------------------------------------------------------------------

-- accounting.account_master
--   account_code text
--   account_name text
--   category text
--   dc_type text
--   requires_subaccount boolean
--   is_active boolean
--   sort_order integer

-- accounting.account_subaccount_map
--   company_id bigint
--   account_code text
--   subaccount_id bigint
--   is_default boolean
--   sort_order integer
--   created_at timestamp with time zone

-- company_master
--   company_id bigint
--   company_code text
--   company_name text
--   is_active boolean
--   created_at timestamp with time zone
--   company_short_code text
--   invoice_registration_number text

-- department_master
--   department_id bigint
--   company_id bigint
--   department_code text
--   department_name text
--   is_active boolean
--   attendance_minute_step integer
--   cast_sales_amount_basis text
--   cast_sales_split_mode text
--   receipt_display_name text
--   receipt_address text
--   receipt_phone text
--   receipt_logo text

-- document_file_no_sequences
--   company_id bigint
--   seq_date date
--   last_no integer

-- document_files
--   file_id bigint
--   company_id bigint
--   file_no text
--   file_name text
--   original_file_name text
--   file_ext text
--   storage_path text
--   file_hash text
--   uploaded_at timestamp with time zone
--   uploaded_by text
--   drive_file_id text
--   drive_url text

-- documents
--   document_id bigint
--   company_id bigint
--   file_id bigint
--   document_type text
--   document_date date
--   amount numeric
--   counterparty text
--   title text
--   memo text
--   status text
--   created_at timestamp with time zone
--   updated_at timestamp with time zone
--   upload_source_id bigint
--   department_id bigint

-- document_journal_links
--   link_id bigint
--   document_id bigint
--   journal_entry_id bigint
--   relation_type text
--   created_at timestamp with time zone

-- journal_entries
--   journal_entry_id bigint
--   company_id bigint
--   entry_date date
--   slip_no text
--   description text
--   created_at timestamp with time zone
--   created_by text
--   approved_at timestamp with time zone
--   approved_by text

-- journal_entry_lines
--   line_id bigint
--   journal_entry_id bigint
--   line_no integer
--   account_code text
--   subaccount_id bigint
--   dc_flag text
--   amount numeric
--   tax_code text
--   line_memo text
--   account_name_snapshot text
--   subaccount_name_snapshot text
--   created_at timestamp with time zone

-- accounting.subaccount_master
--   subaccount_id bigint
--   company_id bigint
--   subaccount_code text
--   subaccount_name text
--   subaccount_type text
--   is_active boolean
--   sort_order integer
--   created_at timestamp with time zone
--   updated_at timestamp with time zone

-- upload_source_master
--   upload_source_id bigint
--   company_id bigint
--   upload_source_code text
--   upload_source_name text
--   source_type text
--   default_department_id bigint
--   default_offset_account_code text
--   default_offset_subaccount_id bigint
--   drive_source_folder_id text
--   is_active boolean

-- -----------------------------------------------------------------------------
-- Store operation tables created by Sql/store_order_accounting_tables.sql
-- -----------------------------------------------------------------------------

-- store_table_master: store-specific table/seat master.
create table if not exists public.store_table_master (
    table_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    table_code text not null,
    table_name text,
    table_category_no smallint not null default 0,
    sort_order integer not null default 0,
    is_active boolean not null default true,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint uq_store_table_master_code unique (company_id, department_id, table_code),
    constraint chk_store_table_master_category_no check (table_category_no between 0 and 9)
);

-- cast_master: cast display names and store affiliation. Do not put high-risk HR data here.
create table if not exists public.cast_master (
    cast_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    cast_code text,
    display_name text not null,
    drink_memo text,
    joined_on date not null default ((now() at time zone 'Asia/Tokyo')::date),
    status text not null default 'active', -- active / inactive / left
    sort_order integer not null default 0,
    is_active boolean not null default true,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_cast_master_status check (status in ('active', 'inactive', 'left')),
    constraint chk_cast_master_drink_memo_length check (char_length(drink_memo) <= 30),
    constraint uq_cast_master_code unique (company_id, department_id, cast_code)
);

-- store_staff_master: non-cast staff display names and store affiliation.
-- Do not put high-risk HR data here.
create table if not exists public.store_staff_master (
    staff_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    staff_code text,
    display_name text not null,
    employment_type text not null default 'employee',
    joined_on date not null default ((now() at time zone 'Asia/Tokyo')::date),
    status text not null default 'active', -- active / inactive / left
    sort_order integer not null default 0,
    is_active boolean not null default true,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_staff_master_status check (status in ('active', 'inactive', 'left')),
    constraint chk_store_staff_master_employment_type check (employment_type in ('employee', 'part_time')),
    constraint uq_store_staff_master_code unique (company_id, department_id, staff_code)
);

-- store_item_category_master: order item category master.
create table if not exists public.store_item_category_master (
    item_category_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    category_code text not null,
    category_name text not null,
    sort_order integer not null default 0,
    is_active boolean not null default true,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint uq_store_item_category_master_code unique (company_id, department_id, category_code)
);

-- store_item_master: order item master. Prices are tax-included integer yen.
create table if not exists public.store_item_master (
    item_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    item_category_id bigint references public.store_item_category_master(item_category_id),
    item_name text not null,
    item_type text not null default 'standard', -- standard / karaoke / nomination_fee
    default_price numeric(12, 0) not null default 0,
    is_cast_back_target boolean not null default false,
    cast_back_unit_amount numeric(12, 0) not null default 0,
    cast_back_regular_unit_amount numeric(12, 0) not null default 0,
    cast_back_nomination_unit_amount numeric(12, 0) not null default 0,
    cast_back_type text not null default 'drink', -- drink / sales / other
    sort_order integer not null default 0,
    is_active boolean not null default true,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_item_master_type check (item_type in ('standard', 'karaoke', 'nomination_fee')),
    constraint chk_store_item_master_default_price check (default_price >= 0),
    constraint chk_store_item_master_cast_back_unit_amount check (cast_back_unit_amount >= 0),
    constraint chk_store_item_master_cast_back_regular_unit_amount check (cast_back_regular_unit_amount >= 0),
    constraint chk_store_item_master_cast_back_nomination_unit_amount check (cast_back_nomination_unit_amount >= 0),
    constraint chk_store_item_master_cast_back_type check (cast_back_type in ('drink', 'sales', 'other')),
    constraint uq_store_item_master_name unique (company_id, department_id, item_name)
);

-- store_nomination_back_master: per-store nomination option and cast back amount.
create table if not exists public.store_nomination_back_master (
    nomination_back_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    nomination_kind text not null, -- selectable key, e.g. nomination / companion_until_1929
    nomination_type text not null, -- base type: nomination / in_store / companion
    display_name text not null,
    companion_time time,
    back_type text not null default 'nomination',
    back_unit_amount numeric(12, 0) not null default 0,
    sort_order integer not null default 0,
    is_active boolean not null default true,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_nomination_back_master_type check (nomination_type in ('nomination', 'in_store', 'companion')),
    constraint chk_store_nomination_back_master_kind check (length(trim(nomination_kind)) > 0),
    constraint chk_store_nomination_back_master_display check (length(trim(display_name)) > 0),
    constraint chk_store_nomination_back_master_companion_time check (
        (nomination_type = 'companion' and companion_time is not null) or
        (nomination_type <> 'companion' and companion_time is null)
    ),
    constraint chk_store_nomination_back_master_back_type check (back_type = 'nomination'),
    constraint chk_store_nomination_back_master_unit_amount check (back_unit_amount >= 0),
    constraint uq_store_nomination_back_master_kind unique (company_id, department_id, nomination_kind)
);

-- payment_method_master: checkout payment method master.
create table if not exists public.payment_method_master (
    payment_method_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    payment_method_code text not null,
    payment_method_name text not null,
    requires_received_amount boolean not null default false,
    sort_order integer not null default 0,
    is_active boolean not null default true,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint uq_payment_method_master_code unique (company_id, department_id, payment_method_code)
);

-- store_business_days: store business day opened/closed by opening and closing flows.
create table if not exists public.store_business_days (
    business_day_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    business_date date not null,
    opened_at timestamp with time zone not null default now(),
    closed_at timestamp with time zone,
    status text not null default 'open', -- open / closed / cancelled
    opened_by text,
    closed_by text,
    memo text,
    source_type text, -- nomination_fee for system rows generated from store_slip_casts
    source_id bigint,
    drink_delivery_amount numeric(12, 0) not null default 0,
    drink_delivery_amount_entered boolean not null default false,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_business_days_status check (status in ('open', 'closed', 'cancelled')),
    constraint chk_store_business_days_drink_delivery_amount check (drink_delivery_amount >= 0),
    constraint chk_store_business_days_closed_at check (closed_at is null or closed_at >= opened_at)
);

-- store_cast_attendance: per-business-day cast attendance.
-- Opening saves selected casts here with clock_in_at as checked_in.
create table if not exists public.store_cast_attendance (
    attendance_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    business_day_id bigint not null references public.store_business_days(business_day_id) on delete cascade,
    business_date date not null,
    cast_id bigint not null references public.cast_master(cast_id),
    attendance_status text not null default 'scheduled', -- scheduled / checked_in / checked_out / absent / cancelled
    clock_in_at timestamp with time zone,
    clock_out_at timestamp with time zone,
    uses_send_service boolean not null default false,
    source text not null default 'opening', -- opening / manual / import
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_cast_attendance_status check (attendance_status in ('scheduled', 'checked_in', 'checked_out', 'absent', 'cancelled')),
    constraint chk_store_cast_attendance_source check (source in ('opening', 'manual', 'import')),
    constraint chk_store_cast_attendance_clock_out check (clock_out_at is null or clock_in_at is null or clock_out_at >= clock_in_at),
    constraint uq_store_cast_attendance_day_cast unique (business_day_id, cast_id)
);

-- store_staff_attendance: per-business-day non-cast staff attendance.
create table if not exists public.store_staff_attendance (
    staff_attendance_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    business_day_id bigint not null references public.store_business_days(business_day_id) on delete cascade,
    business_date date not null,
    staff_id bigint not null references public.store_staff_master(staff_id),
    attendance_status text not null default 'scheduled',
    clock_in_at timestamp with time zone,
    clock_out_at timestamp with time zone,
    uses_send_service boolean not null default false,
    source text not null default 'opening',
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_staff_attendance_status check (attendance_status in ('scheduled', 'checked_in', 'checked_out', 'absent', 'cancelled')),
    constraint chk_store_staff_attendance_source check (source in ('opening', 'manual', 'import')),
    constraint chk_store_staff_attendance_clock_out check (clock_out_at is null or clock_in_at is null or clock_out_at >= clock_in_at),
    constraint uq_store_staff_attendance_day_staff unique (business_day_id, staff_id)
);

-- store_slips: one slip per table/session. Business day is set by current open business day.
create table if not exists public.store_slips (
    slip_id bigint generated by default as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    business_day_id bigint references public.store_business_days(business_day_id),
    business_date date,
    -- Deletable table master: preserve the historical ID and display snapshot without an FK.
    table_id bigint,
    table_code_snapshot text,
    table_name_snapshot text,
    slip_no text,
    opened_at timestamp with time zone not null,
    closed_at timestamp with time zone,
    status text not null default 'open', -- open / checkout_ready / checked_out / cancelled
    customer_count integer not null default 0,
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_slips_status check (status in ('open', 'checkout_ready', 'checked_out', 'cancelled')),
    constraint chk_store_slips_customer_count check (customer_count >= 0),
    constraint chk_store_slips_closed_at check (closed_at is null or closed_at >= opened_at),
    constraint uq_store_slips_slip_no unique (company_id, department_id, slip_no)
);

-- store_slip_customers: customer rows inside a slip. Row count is customer count.
create table if not exists public.store_slip_customers (
    slip_customer_id bigint generated by default as identity primary key,
    slip_id bigint not null references public.store_slips(slip_id) on delete cascade,
    line_no integer not null,
    customer_label text,
    entered_at timestamp with time zone not null,
    left_at timestamp with time zone,
    left_at_source text, -- null / manual / accounting_slip
    status text not null default 'active', -- active / left / cancelled
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_slip_customers_status check (status in ('active', 'left', 'cancelled')),
    constraint chk_store_slip_customers_left_at check (left_at is null or left_at >= entered_at),
    constraint chk_store_slip_customers_left_at_source check (
        (left_at is null and left_at_source is null)
        or (left_at is not null and left_at_source in ('manual', 'accounting_slip'))
    ),
    constraint uq_store_slip_customers_line unique (slip_id, line_no)
);

-- store_slip_casts: nominated casts on a slip.
create table if not exists public.store_slip_casts (
    slip_cast_id bigint generated by default as identity primary key,
    slip_id bigint not null references public.store_slips(slip_id) on delete cascade,
    cast_id bigint not null references public.cast_master(cast_id),
    nomination_kind text not null default 'nomination',
    nomination_type text not null default 'nomination', -- nomination / in_store / companion / help / other
    nomination_price numeric(12, 0) not null default 0,
    started_at timestamp with time zone,
    ended_at timestamp with time zone,
    status text not null default 'active', -- active / ended / cancelled
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_slip_casts_nomination_type check (nomination_type in ('nomination', 'in_store', 'companion', 'help', 'other')),
    constraint chk_store_slip_casts_nomination_price check (nomination_price >= 0),
    constraint chk_store_slip_casts_status check (status in ('active', 'ended', 'cancelled')),
    constraint chk_store_slip_casts_ended_at check (started_at is null or ended_at is null or ended_at >= started_at)
);

-- store_order_lines: order lines on a slip. Amounts are tax-included integer yen.
create table if not exists public.store_order_lines (
    order_line_id bigint generated by default as identity primary key,
    slip_id bigint not null references public.store_slips(slip_id) on delete cascade,
    line_no integer not null,
    -- Deletable item/category masters: preserve historical IDs and line snapshots without FKs.
    item_id bigint,
    item_name_snapshot text not null,
    item_category_id_snapshot bigint,
    item_category_code_snapshot text,
    item_category_name_snapshot text,
    quantity numeric(10, 2) not null default 1,
    unit_price numeric(12, 0) not null default 0,
    amount numeric(12, 0) not null default 0,
    ordered_at timestamp with time zone not null default now(),
    status text not null default 'active', -- active / voided
    voided_at timestamp with time zone,
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_order_lines_quantity check (quantity > 0),
    constraint chk_store_order_lines_unit_price check (unit_price >= 0),
    constraint chk_store_order_lines_amount check (amount >= 0),
    constraint chk_store_order_lines_status check (status in ('active', 'voided')),
    constraint chk_store_order_lines_source_type check (source_type is null or source_type in ('nomination_fee')),
    constraint uq_store_order_lines_line unique (slip_id, line_no)
);

-- store_order_line_cast_backs: cast-specific back amounts generated from order lines.
create table if not exists public.store_order_line_cast_backs (
    order_line_cast_back_id bigint generated by default as identity primary key,
    order_line_id bigint not null references public.store_order_lines(order_line_id) on delete cascade,
    slip_id bigint not null references public.store_slips(slip_id) on delete cascade,
    business_day_id bigint references public.store_business_days(business_day_id),
    business_date date,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    cast_id bigint not null references public.cast_master(cast_id),
    back_type text not null default 'drink', -- drink / sales / other
    quantity numeric(10, 2) not null default 1,
    back_unit_amount numeric(12, 0) not null default 0,
    back_amount numeric(12, 0) not null default 0,
    status text not null default 'active', -- active / voided / cancelled
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_order_line_cast_backs_type check (back_type in ('drink', 'sales', 'other')),
    constraint chk_store_order_line_cast_backs_quantity check (quantity > 0),
    constraint chk_store_order_line_cast_backs_unit_amount check (back_unit_amount >= 0),
    constraint chk_store_order_line_cast_backs_amount check (back_amount >= 0),
    constraint chk_store_order_line_cast_backs_status check (status in ('active', 'voided', 'cancelled'))
);

-- store_slip_cast_backs: cast back amounts generated from nomination rows.
create table if not exists public.store_slip_cast_backs (
    slip_cast_back_id bigint generated by default as identity primary key,
    slip_cast_id bigint not null references public.store_slip_casts(slip_cast_id) on delete cascade,
    slip_id bigint not null references public.store_slips(slip_id) on delete cascade,
    business_day_id bigint references public.store_business_days(business_day_id),
    business_date date,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    cast_id bigint not null references public.cast_master(cast_id),
    nomination_kind text not null default 'nomination',
    nomination_type text not null, -- nomination / in_store / companion
    back_type text not null default 'nomination',
    quantity numeric(10, 2) not null default 1,
    back_unit_amount numeric(12, 0) not null default 0,
    back_amount numeric(12, 0) not null default 0,
    status text not null default 'active', -- active / voided / cancelled
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_slip_cast_backs_nomination_type check (nomination_type in ('nomination', 'in_store', 'companion')),
    constraint chk_store_slip_cast_backs_back_type check (back_type = 'nomination'),
    constraint chk_store_slip_cast_backs_quantity check (quantity > 0),
    constraint chk_store_slip_cast_backs_unit_amount check (back_unit_amount >= 0),
    constraint chk_store_slip_cast_backs_amount check (back_amount >= 0),
    constraint chk_store_slip_cast_backs_status check (status in ('active', 'voided', 'cancelled')),
    constraint uq_store_slip_cast_backs_slip_cast unique (slip_cast_id)
);

-- store_slip_charge_lines: free adjustment lines outside product order rows.
-- Current karaoke is not stored here; karaoke is a store_item_master item_type='karaoke' order line.
create table if not exists public.store_slip_charge_lines (
    charge_line_id bigint generated by default as identity primary key,
    slip_id bigint not null references public.store_slips(slip_id) on delete cascade,
    business_day_id bigint references public.store_business_days(business_day_id),
    business_date date,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    line_no integer not null,
    charge_type text not null,
    line_name text not null,
    quantity numeric(10, 2) not null default 1,
    unit_price numeric(12, 0) not null default 0,
    amount numeric(12, 0) not null default 0,
    status text not null default 'active', -- active / voided / cancelled
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_slip_charge_lines_type check (charge_type in ('adjustment', 'karaoke')),
    constraint chk_store_slip_charge_lines_name check (length(trim(line_name)) > 0),
    constraint chk_store_slip_charge_lines_quantity check (quantity > 0),
    constraint chk_store_slip_charge_lines_adjustment check (charge_type <> 'adjustment' or quantity = 1),
    constraint chk_store_slip_charge_lines_karaoke check (charge_type <> 'karaoke' or (unit_price = 200 and amount >= 0)),
    constraint chk_store_slip_charge_lines_status check (status in ('active', 'voided', 'cancelled')),
    constraint uq_store_slip_charge_lines_line unique (slip_id, line_no)
);

-- store_checkouts: active checkout snapshots. Cancelled rows are retained for audit/correction history.
create table if not exists public.store_checkouts (
    checkout_id bigint generated by default as identity primary key,
    slip_id bigint not null references public.store_slips(slip_id) on delete cascade,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    checkout_at timestamp with time zone not null,
    subtotal_amount numeric(12, 0) not null default 0,
    service_charge_amount numeric(12, 0) not null default 0,
    total_amount numeric(12, 0) not null default 0,
    payment_method_id bigint references public.payment_method_master(payment_method_id),
    received_amount numeric(12, 0),
    change_amount numeric(12, 0),
    issuer_snapshot jsonb,
    status text not null default 'draft', -- draft / confirmed / cancelled
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_checkouts_total_amount check (total_amount >= 0),
    constraint chk_store_checkouts_received_amount check (received_amount is null or received_amount >= 0),
    constraint chk_store_checkouts_change_amount check (change_amount is null or change_amount >= 0),
    constraint chk_store_checkouts_status check (status in ('draft', 'confirmed', 'cancelled'))
);

-- store_checkout_payments: split payment rows for a checkout.
create table if not exists public.store_checkout_payments (
    checkout_payment_id bigint generated by default as identity primary key,
    checkout_id bigint not null references public.store_checkouts(checkout_id) on delete cascade,
    slip_id bigint not null references public.store_slips(slip_id) on delete cascade,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    payment_method_id bigint references public.payment_method_master(payment_method_id),
    payment_method_code text not null,
    payment_method_name text not null,
    amount numeric(12, 0) not null default 0,
    status text not null default 'confirmed', -- confirmed / cancelled
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_checkout_payments_amount check (amount >= 0),
    constraint chk_store_checkout_payments_status check (status in ('confirmed', 'cancelled'))
);

-- store_slip_cast_sales_adjustments: closing-time sales amount allocation by slip nomination.
create table if not exists public.store_slip_cast_sales_adjustments (
    adjustment_id bigint generated by default as identity primary key,
    slip_id bigint not null references public.store_slips(slip_id) on delete cascade,
    checkout_id bigint not null references public.store_checkouts(checkout_id) on delete cascade,
    business_day_id bigint references public.store_business_days(business_day_id),
    business_date date,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    slip_cast_id bigint not null references public.store_slip_casts(slip_cast_id) on delete cascade,
    cast_id bigint not null references public.cast_master(cast_id),
    source_amount_type text not null default 'total', -- subtotal / total
    split_mode text not null default 'split', -- split / full
    base_amount numeric(12, 0) not null default 0,
    sales_amount numeric(12, 0) not null default 0,
    status text not null default 'confirmed', -- confirmed / cancelled
    memo text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint chk_store_slip_cast_sales_adjustments_source check (source_amount_type in ('subtotal', 'total')),
    constraint chk_store_slip_cast_sales_adjustments_split check (split_mode in ('split', 'full')),
    constraint chk_store_slip_cast_sales_adjustments_base_amount check (base_amount >= 0),
    constraint chk_store_slip_cast_sales_adjustments_sales_amount check (sales_amount >= 0),
    constraint chk_store_slip_cast_sales_adjustments_status check (status in ('confirmed', 'cancelled'))
);

-- store_slip_accounting_snapshots: versioned checkout-ready statement and business-home payloads.
create table if not exists public.store_slip_accounting_snapshots (
    accounting_snapshot_id bigint generated by default as identity primary key,
    slip_id bigint not null references public.store_slips(slip_id) on delete cascade,
    business_day_id bigint not null references public.store_business_days(business_day_id),
    business_date date not null,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    snapshot_version integer not null,
    status text not null default 'active', -- active / released / cancelled
    invalidation_reason text,
    checkout_ready_at timestamp with time zone not null,
    invalidated_at timestamp with time zone,
    print_data jsonb not null,
    review_data jsonb not null,
    business_home_data jsonb not null,
    backfilled boolean not null default false,
    created_at timestamp with time zone not null default now(),
    constraint uq_store_slip_accounting_snapshot_version unique (slip_id, snapshot_version)
);

-- store_business_day_cast_advances: receipt journal linkage for one attending cast.
create table if not exists public.store_business_day_cast_advances (
    cast_advance_id bigint generated by default as identity primary key,
    business_day_id bigint not null references public.store_business_days(business_day_id) on delete cascade,
    business_date date not null,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    cast_id bigint not null references public.cast_master(cast_id),
    journal_entry_id uuid not null references accounting.journal_entries(journal_entry_id),
    amount numeric(12, 0) not null,
    memo text,
    created_at timestamp with time zone not null default now(),
    constraint uq_store_business_day_cast_advances_journal unique (journal_entry_id)
);

-- store_business_day_closing_snapshots: immutable daily accounting record captured by close_business_day.
create table if not exists public.store_business_day_closing_snapshots (
    closing_snapshot_id bigint generated by default as identity primary key,
    business_day_id bigint not null references public.store_business_days(business_day_id),
    business_date date not null,
    company_id bigint not null references public.company_master(company_id),
    department_id bigint not null references public.department_master(department_id),
    snapshot_version integer not null default 1,
    closed_at timestamp with time zone not null,
    backfilled boolean not null default false,
    business_home_data jsonb not null,
    closing_data jsonb not null,
    created_at timestamp with time zone not null default now(),
    constraint uq_store_business_day_closing_snapshot_version unique (business_day_id, snapshot_version)
);

-- Important indexes from the executable migration:
--   ux_store_item_master_karaoke_active: unique (company_id, department_id) where item_type = 'karaoke' and is_active = true
--   ux_store_business_days_one_open: unique (company_id, department_id) where status = 'open'
--   idx_store_business_days_lookup: (company_id, department_id, business_date, status)
--   idx_store_cast_attendance_day_status: (company_id, department_id, business_day_id, attendance_status)
--   idx_store_cast_attendance_cast: (cast_id, business_date)
--   idx_store_slips_open: (company_id, department_id, status, opened_at)
--   idx_store_slips_business_day: (business_day_id, status)
--   idx_store_slip_customers_slip_status: (slip_id, status)
--   idx_store_nomination_back_master_department: (department_id, is_active, sort_order)
--   idx_store_order_lines_slip_status: (slip_id, status)
--   idx_store_order_line_cast_backs_cast_date: (company_id, department_id, cast_id, business_date, status)
--   idx_store_order_line_cast_backs_line: (order_line_id, status)
--   idx_store_slip_cast_backs_cast_date: (company_id, department_id, cast_id, business_date, status)
--   idx_store_slip_cast_backs_slip: (slip_id, status)
--   idx_store_slip_cast_backs_slip_cast: (slip_cast_id, status)
--   idx_store_slip_charge_lines_slip_status: (slip_id, charge_type, status)
--   idx_store_slip_charge_lines_day: (company_id, department_id, business_day_id, status)
--   idx_store_checkouts_date: (company_id, department_id, checkout_at)
--   ux_store_checkouts_active_slip: unique (slip_id) where status <> 'cancelled'
--   idx_store_checkout_payments_checkout: (checkout_id, status)
--   ux_store_slip_cast_sales_adjustments_confirmed: unique (slip_id, slip_cast_id) where status = 'confirmed'
--   idx_store_slip_cast_sales_adjustments_day: (company_id, department_id, business_day_id, status)
--   idx_store_slip_cast_sales_adjustments_slip: (slip_id, status)
--   idx_store_slip_cast_sales_adjustments_cast_date: (company_id, department_id, cast_id, business_date, status)

-- -----------------------------------------------------------------------------
-- Current RPC functions used by the application (2026-08-07 v2)
-- -----------------------------------------------------------------------------

-- Settings:
--   store.get_departments
--   store.delete_non_master_records
-- Business home and orders:
--   store.get_business_home_bootstrap_v2
--   store.get_current_business_home_snapshot
--   store.sync_business_home_changes_v2
--   store.get_current_order_entry_candidates
--   store.submit_current_order_entry_v2
-- Checkout:
--   store.issue_checkout_statement_v2
--   store.release_checkout_ready_v2
--   store.confirm_checkout_v2
--   store.cancel_checkout_v2
--   store.get_checkout_statement_print_data
--   store.get_checkout_receipt_print_data
-- Attendance and closing:
--   store.get_attendance_editor_bootstrap_v2
--   store.get_current_attendance_editor_snapshot
--   store.save_current_business_day_attendance_v2
--   store.get_current_closing_dashboard
--   store.close_business_day_v2
--   store.get_current_drink_delivery_editor
--   store.save_current_business_day_drink_delivery_amount_v2
--   store.get_current_cast_sales_adjustment_overview
--   store.save_current_cast_sales_adjustment_v2
--   store.confirm_current_cast_sales_adjustments_v2
--   store.get_current_drink_back_editor
--   store.save_drink_back_adjustments_v2
-- Receipts and reports:
--   store.get_current_receipt_work_queue
--   store.advance_receipt_work_queue_v2
--   store.is_pending_receipt_drive_file_allowed
--   store.get_business_day_daily_report
-- Management:
--   store.get_management_master_snapshot
--   store.save_management_master_v2
--
-- The exact list is enforced by Tests/rpc-contract.test.mjs against C# callers,
-- the Edge allowlist, and SQL definitions. Low-level SQL functions are internal
-- only and are not application RPCs.

-- -----------------------------------------------------------------------------
-- Historical pre-v2 RPC reference (not callable by the current application)
-- The names below are retained only as schema history. 00_legacy_rpc_cutover.sql
-- drops these application-facing functions; do not add them to the Edge allowlist.
-- -----------------------------------------------------------------------------

-- Store settings:
--   store.get_departments()
--     returns department_id, company_id, department_code, department_name, is_active

-- Store context and business day:
--   store.get_context(p_department_id bigint)
--     returns company_id, department_id, department_name,
--       attendance_minute_step, cast_sales_amount_basis, cast_sales_split_mode
--   store.get_current_business_day(p_department_id bigint)
--     returns business_day_id, company_id, department_id, business_date, opened_at, closed_at, status, memo
--   store.get_store_bootstrap(p_department_id bigint)
--     returns store context, current business day, all application master lists, attendance casts,
--     and the initial business-day snapshot in one read. Periodic refresh continues to use store.get_business_day_snapshot.
--   store.open_business_day(p_department_id bigint, p_business_date date, p_memo text)
--     returns business_day_id, company_id, department_id, business_date, opened_at, closed_at, status, memo
--   store.open_business_day_with_attendance(p_department_id bigint, p_business_date date, p_attendance_entries jsonb, p_memo text)
--     returns business_day_id, company_id, department_id, business_date, opened_at, closed_at, status, memo
--     p_attendance_entries supports { cast_id, clock_in_time } and saves checked_in rows with clock_in_at.
--   store.get_open_slip_count(p_department_id bigint, p_business_day_id bigint)
--     returns integer
--   store.get_business_day_drink_delivery_status(p_department_id bigint, p_business_day_id bigint)
--     returns drink_delivery_amount and is_entered so 0 yen can be distinguished from not entered.
--   store.save_business_day_drink_delivery_amount(p_department_id bigint, p_business_day_id bigint, p_drink_delivery_amount numeric)
--     updates store_business_days.drink_delivery_amount and marks drink_delivery_amount_entered for the open business day.
--   store.get_business_day_closing_attendance(p_department_id bigint, p_business_day_id bigint)
--     returns cast and staff attendance rows with person_type, person_id, attendance_id,
--       display_name, department_name, clock_in_at, clock_out_at, uses_send_service.
--     clock_out_at must be strictly later than clock_in_at on the business-date timeline.
--   store.save_business_day_closing_attendance(p_department_id bigint, p_business_day_id bigint, p_attendance_entries jsonb)
--     updates registered cast attendance rows with { attendance_id, clock_in_time, clock_out_time, uses_send_service }.
--   store.save_business_day_staff_attendance(p_department_id bigint, p_business_day_id bigint, p_attendance_entries jsonb)
--     updates current-store staff checked_in/cancelled rows with { staff_id, clock_in_time, is_selected }.
--   store.save_business_day_staff_closing_attendance(p_department_id bigint, p_business_day_id bigint, p_attendance_entries jsonb)
--     updates registered staff attendance rows with { attendance_id, clock_in_time, clock_out_time, uses_send_service }.
--   store.get_business_day_closing_readiness(p_department_id bigint, p_business_day_id bigint, p_pending_receipt_status text default 'unprocessed')
--     returns all closing counts, structured block reasons, can_close, and checked_at in one read.
--     pending_receipt_count is informational and does not affect can_close or block_reasons.
--   store.get_business_day_cast_sales_adjustment_status(p_department_id bigint, p_business_day_id bigint)
--     returns required_slip_count, completed_slip_count, missing_slip_count.
--   store.get_cast_sales_adjustment_slips(p_department_id bigint, p_business_day_id bigint)
--     returns checked-out slips with active nominated casts, customer_names, cast_names,
--     required/saved cast counts, and adjusted_sales_amount_total.
--   store.get_cast_sales_adjustment_detail(p_department_id bigint, p_slip_id bigint)
--     returns checkout snapshot and nominated cast rows for one checked-out slip.
--   store.save_cast_sales_adjustment(p_department_id bigint, p_slip_id bigint, p_adjustments jsonb, p_source_amount_type text, p_split_mode text)
--     replaces the per-slip adjustment rows for all active nominated casts.
--   store.get_business_day_cast_sales_adjustment_overview(p_department_id bigint, p_business_day_id bigint)
--     returns status, slips, and all cast detail rows as JSON in one read.
--   store.save_business_day_cast_sales_adjustments(p_department_id bigint, p_business_day_id bigint, p_slips jsonb)
--     validates and saves all supplied checked-out slips in one transaction.
--   store.close_business_day(p_department_id bigint, p_business_day_id bigint, p_memo text, p_pending_receipt_status text default 'unprocessed', p_ignore_closing_requirements boolean default false)
--     returns business_day_id, company_id, department_id, business_date, opened_at, closed_at, status, memo.
--     rejects closing when open slips, missing drink delivery input, no attendance,
--     missing clock-out, missing cast sales adjustment, or missing champagne back input remains.
--     p_ignore_closing_requirements skips the closing readiness checks for administrator override flows.
--     p_pending_receipt_status = '__ignore_closing_requirements__' is retained as a four-argument Edge Function compatibility signal.

-- Slip creation and listing:
--   store.get_tables(p_department_id bigint)
--     returns table_id, table_code, table_name, table_category_no
--   store.get_casts(p_department_id bigint)
--     returns cast_id, cast_code, display_name, department_name
--     includes active casts from all active departments across companies, for help/temporary assignments.
--   store.get_casts_admin(p_department_id bigint)
--     returns cast_id, display_name, drink_memo, joined_on for casts belonging to the current store.
--   store.create_cast(p_department_id bigint, p_display_name text, p_drink_memo text)
--     creates an active cast for the current store. joined_on is set to the current Asia/Tokyo date.
--   store.update_cast_drink_memo(p_department_id bigint, p_cast_id bigint, p_drink_memo text)
--     updates a current-store active cast's nullable drink memo. Empty input is stored as null.
--   store.get_staffs(p_department_id bigint)
--     returns staff_id, staff_code, display_name, department_name, employment_type for active current-store staff.
--   store.get_staffs_admin(p_department_id bigint)
--     returns staff_id, display_name, joined_on, employment_type for current-store staff.
--   store.create_staff(p_department_id bigint, p_display_name text, p_employment_type text default 'employee')
--     creates an active staff member for the current store. joined_on is set to the current Asia/Tokyo date.
--   store.update_staff_employment_type(p_department_id bigint, p_staff_id bigint, p_employment_type text)
--     changes a current-store active staff member between employee and part_time.
--   store.delete_staff(p_department_id bigint, p_staff_id bigint)
--     marks current-store staff inactive while preserving attendance history.
--   store.get_business_day_snapshot(p_department_id bigint, p_business_day_id bigint)
--     returns the current business_ui_revision and all open/checkout/checked-out slip details for the business hub.
--     Slip objects include customers, nominations, grouped orders, adjustments, karaoke quantity, and accounting amount.
--   store.get_order_entry_slips(p_department_id bigint, p_business_day_id bigint)
--     returns slip_id, table_id, table_code, table_name, opened_at, customer_count,
--       customer_names, nomination_cast_ids, nomination_cast_names, memo
--   store.get_order_items(p_department_id bigint)
--     returns item_id, item_name, item_type, default_price, category_code, category_name,
--       is_cast_back_target, cast_back_regular_unit_amount, cast_back_nomination_unit_amount, cast_back_type
--     returns only standard orderable items; system items such as karaoke and nomination_fee are excluded.
--   store.get_item_admin_catalog(p_department_id bigint)
--     returns category and item rows for Management/Items management.
--   store.get_nomination_back_master(p_department_id bigint)
--     returns store-defined nomination options with nomination_kind, display_name, companion_time, and back amount.
--   store.save_nomination_back_master(p_department_id bigint, p_settings jsonb)
--     upserts store_nomination_back_master rows. p_settings uses { nomination_kind, nomination_type, display_name, companion_time, back_unit_amount, sort_order, is_active }.
--   store.upsert_item_category(p_department_id, p_item_category_id, p_category_code, p_category_name, p_sort_order, p_is_active)
--     inserts/updates store_item_category_master for the current store.
--   store.upsert_item(p_department_id, p_item_id, p_item_category_id, p_item_name, p_default_price,
--       p_is_active, p_is_cast_back_target, p_cast_back_regular_unit_amount,
--       p_cast_back_nomination_unit_amount, p_cast_back_type)
--     inserts/updates store_item_master. v1 UI saves cast back type as drink.
--   store.delete_item(p_department_id bigint, p_item_id bigint)
--     soft-deletes a store item master row with is_active=false and preserves existing order item_id values.
--   store.get_order_attending_casts(p_department_id bigint, p_business_day_id bigint)
--     returns cast_id, display_name, drink_memo, department_name
--     display_name is the cast name; UI composes display_name:department_name when needed.
--     includes all casts who attended the business day even if they are already checked out.
--   store.add_order_lines(p_department_id bigint, p_slip_id bigint, p_order_lines jsonb)
--     returns inserted_count
--     p_order_lines supports { slip_id, item_id, quantity, cast_back_cast_id }.
--     p_slip_id is used as the fallback slip when order-line slip_id is omitted.
--     Rejects non-standard system items; business-hub karaoke is saved via store.flush_business_home_changes and nomination_fee is generated by nomination RPCs.
--     Back target items create store_order_line_cast_backs rows only when cast_back_cast_id is present.
--     The regular item back is called ドリンクバック; when the target cast is nominated on the slip it uses the 担当バック amount.
--   Legacy store.get_slip_detail was removed with the Slips/Edit module.
--     Business-hub detail projection now comes from store.get_business_day_snapshot.
--   store.add_slip_customers(p_department_id bigint, p_slip_id bigint, p_customer_labels text[], p_entered_at timestamptz)
--     adds active customer rows to an open slip with the selected entered_at and refreshes store_slips.customer_count.
--   store.add_slip_nominations(p_department_id bigint, p_slip_id bigint, p_cast_nominations jsonb)
--     adds nomination rows to an open slip. p_cast_nominations uses { cast_id, nomination_kind, nomination_price }.
--     nomination_price is selected each time as 指名料金 and creates one nomination_fee system order line.
--     active store_nomination_back_master rows with back_unit_amount > 0 create store_slip_cast_backs snapshots.
--   store.add_slip_adjustment(p_department_id bigint, p_slip_id bigint, p_line_name text, p_amount numeric)
--     adds one free-input adjustment row from the slip detail modal.
--     keeps existing active adjustment rows and allows a negative amount.
--   store.void_slip_adjustment(p_department_id bigint, p_charge_line_id bigint)
--     marks one active adjustment row on an open slip voided; it does not physically delete the audit row.
--   store.save_karaoke_lines(p_department_id bigint, p_business_day_id bigint, p_karaoke_lines jsonb)
--     Internal implementation used by store.flush_business_home_changes.
--     Replaces the active karaoke product order quantity for selected open slips in the business day.
--     p_karaoke_lines uses { slip_id, quantity }; item_type='karaoke' unit price is fixed at 200 and service-taxable.
--     same-slip karaoke order lines are collapsed to one row with ordered_at set to the entered/opened time.
--   store.save_order_line_quantities(p_department_id bigint, p_slip_id bigint, p_order_lines jsonb)
--     Legacy quantity-edit SQL. It is not exposed through the current C# repository or prosper-rpc allowlist.
--     Reintroduce only after confirming whether quantity changes should be cancel+re-add instead.
--   store.leave_slip_customer(p_department_id bigint, p_slip_customer_id bigint, p_left_at timestamptz)
--     marks an active customer row left and refreshes store_slips.customer_count.
--   store.void_order_line(p_department_id bigint, p_order_line_id bigint)
--     marks an active standard order line and related store_order_line_cast_backs rows voided.
--   store.cancel_slip_nomination(p_department_id bigint, p_slip_cast_id bigint)
--     marks one active nomination and its nomination back cancelled, and voids its source-linked nomination_fee order line.
--   store.get_business_day_snapshot(p_department_id bigint, p_business_day_id bigint)
--     returns the current business_ui_revision and all slip details for the business hub.
--     order objects include sourceType and sourceId so a nomination cancellation can project its linked fee correctly.
--   store.apply_business_slip_editor_operation(p_department_id bigint, p_business_day_id bigint, p_slip_id bigint,
--       p_operation_type text, p_operation_id text, p_payload jsonb)
--     Internal implementation used by store.flush_business_home_changes; not exposed through the current C# repository
--     or prosper-rpc allowlist. Operations are add/update/leave customer, add/cancel nomination, add/void adjustment,
--     and add/void standard order.
--   store.flush_business_home_changes(p_department_id bigint, p_business_day_id bigint, p_client_batch_id text,
--       p_operations jsonb, p_karaoke_lines jsonb)
--     applies all pending business-hub edits and karaoke drafts in one Edge RPC response. Each row receives an
--     independent success/failure result; repeating the same client batch id returns the stored row results without
--     reapplying additions, then includes a current full business-day snapshot.
--   store.issue_checkout_statement(p_department_id bigint, p_slip_id bigint, p_closed_at timestamptz)
--     changes an open slip to checkout_ready, fixes close time and non-left customer rows,
--     materializes pricing and stores versioned print/review/business-home payloads.
--   store.get_checkout_statement_print_data(p_department_id bigint, p_slip_id bigint)
--     returns the active stored print_data plus review_data without master lookups or recalculation.
--   store.release_checkout_ready(p_department_id bigint, p_slip_id bigint)
--     invalidates the active snapshot, voids automatic pricing rows, and returns checkout_ready to open.
--   store.confirm_checkout(p_department_id bigint, p_slip_id bigint, p_payments jsonb, p_received_amount numeric)
--     confirms only checkout_ready using totals from the active statement snapshot, stores issuer snapshot,
--     creates split payment rows, marks the slip checked_out, and returns receipt print_data.
--     p_payments supports { method_code, amount } with method_code in cash/cat/paypay.
--   store.get_checkout_receipt_print_data(p_department_id bigint, p_slip_id bigint)
--     returns checkout_id and receipt print_data for an active checked_out checkout.
--   store.cancel_checkout(p_department_id bigint, p_slip_id bigint)
--     cancels a confirmed checkout for an open business day, marks payments cancelled,
--     invalidates the snapshot, voids automatic pricing, marks cast-sales adjustments cancelled, and reopens the slip.
--   store.capture_business_day_closing_snapshot(p_department_id bigint, p_business_day_id bigint,
--       p_closed_at timestamptz, p_backfilled boolean)
--     stores the immutable business-home, checkout, payment, attendance, cast-sales, and daily-report payload.
--   store.get_business_day_daily_report(p_department_id bigint, p_business_day_id bigint default null)
--     returns the open-day provisional report, or the selected/latest immutable closed-day report.
--   store.guard_closed_business_day_mutation()
--     rejects inserts, updates, and deletes against transaction rows that belong to a closed business day.
--   store.create_slip(
--       p_department_id bigint,
--       p_table_id bigint,
--       p_opened_at timestamp with time zone,
--       p_customer_labels text[],
--       p_cast_nominations jsonb,
--       p_memo text
--     )
--     returns slip_id
--     initial nominations also create nomination_fee order lines and store_slip_cast_backs snapshots from the current nomination back master.

-- Receipt quick entry:
--   store.get_pending_receipts(p_department_id bigint, p_status text default 'unprocessed')
--     returns document_id, file_id, document_date, amount, status, file_name, drive_file_id, drive_url, storage_path
--   store.quick_enter_receipt(
--       p_department_id bigint,
--       p_document_id text,
--       p_payment_date date,
--       p_amount numeric,
--       p_account_subject text,
--       p_description text,
--       p_group_code text,
--       p_journal_payload jsonb default null,
--       p_status text default 'quick_entered',
--       p_business_day_id bigint default null,
--       p_advance_cast_id bigint default null
--     )
--     returns document_id
--     前渡金: キャスト requires one cast who is attending the matching open business day.
--   store.mark_receipt_scan_mistake(p_department_id bigint, p_document_id text, p_status text default 'excluded')
--     returns document_id

-- All RPC functions above are expected to be:
--   language sql/plpgsql
--   security definer
--   set search_path = public
--   defined in the store schema and called as store.<function_name>.
--   direct PostgREST RPC execution and store schema usage revoked from public/anon/authenticated/service_role.
--   Application calls are routed through the prosper-rpc Edge Function.

-- -----------------------------------------------------------------------------
-- Quick-entry account master updates
-- -----------------------------------------------------------------------------

-- Sql/quick_entry_account_master_updates.sql adjusts account/subaccount master data
-- for receipt quick-entry UI choices. The accounting masters are in the
-- accounting schema and company_master remains in public. The intended active
-- expense subjects include:
--   前渡金, 給料手当, 外注費, 水道光熱費, 通信費, リース料, 衛生費,
--   広告宣伝費, 仕入高, 消耗品費, 保険料, 旅費交通費, 地代家賃,
--   租税公課, 福利厚生費, 会議費, 接待交際費, 雑費.
-- The executable SQL file is currently the source of truth for account_code and
-- subaccount_code inserts/updates.
