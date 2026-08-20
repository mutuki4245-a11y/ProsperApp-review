begin;

create schema if not exists store;

revoke usage on schema store from public, anon, authenticated, service_role;
alter default privileges in schema store revoke execute on functions from public;
alter default privileges in schema store revoke execute on functions from anon, authenticated, service_role;

create table if not exists store.current_business_day_operation_results (
    department_id bigint not null,
    operation_id text not null,
    operation_type text not null,
    request_body jsonb not null,
    response jsonb not null,
    created_at timestamp with time zone not null default now(),
    primary key (department_id, operation_id)
);

revoke all on table store.current_business_day_operation_results from public, anon, authenticated, service_role;
create index if not exists current_business_day_operation_results_created_at_idx
    on store.current_business_day_operation_results (created_at);

create or replace function store.business_timezone()
returns text language sql immutable parallel safe
set search_path = pg_catalog
as $$ select 'Asia/Tokyo'::text $$;

create or replace function store.business_day_cutover_time()
returns time without time zone language sql immutable parallel safe
set search_path = pg_catalog
as $$ select time '12:00:00' $$;

create or replace function store.service_charge_rate()
returns numeric language sql immutable parallel safe
set search_path = pg_catalog
as $$ select 0.20::numeric $$;

create or replace function store.consumption_tax_inclusive_ratio()
returns numeric language sql immutable parallel safe
set search_path = pg_catalog
as $$ select (10::numeric / 110::numeric) $$;

revoke all on function store.business_timezone() from public, anon, authenticated, service_role;
revoke all on function store.business_day_cutover_time() from public, anon, authenticated, service_role;
revoke all on function store.service_charge_rate() from public, anon, authenticated, service_role;
revoke all on function store.consumption_tax_inclusive_ratio() from public, anon, authenticated, service_role;

drop function if exists public.to_base36_3(integer);
drop function if exists public.get_store_departments();
drop function if exists public.get_store_context(bigint);
drop function if exists public.get_current_business_day(bigint);
drop function if exists public.get_open_slip_count(bigint, bigint);
drop function if exists public.get_business_day_drink_delivery_status(bigint, bigint);
drop function if exists public.get_business_day_closing_attendance(bigint, bigint);
drop function if exists public.get_business_day_cast_sales_adjustment_status(bigint, bigint);
drop function if exists public.get_business_day_champagne_back_status(bigint, bigint);
drop function if exists public.get_business_day_champagne_back_overview(bigint, bigint);
drop function if exists public.save_business_day_champagne_backs(bigint, bigint, jsonb);
drop function if exists public.get_cast_sales_adjustment_slips(bigint, bigint);
drop function if exists public.get_cast_sales_adjustment_detail(bigint, bigint);
drop function if exists public.save_cast_sales_adjustment(bigint, bigint, jsonb, text, text);
drop function if exists public.close_business_day(bigint, bigint, text);
drop function if exists public.close_business_day(bigint, bigint, text, text);
drop function if exists public.close_business_day(bigint, bigint, text, text, boolean);
drop function if exists public.open_business_day(bigint, date, text);
drop function if exists public.open_business_day_with_attendance(bigint, date, bigint[], text);
drop function if exists public.open_business_day_with_attendance(bigint, date, jsonb, text);
drop function if exists public.add_business_day_attendance(bigint, bigint, jsonb);
drop function if exists public.save_business_day_attendance(bigint, bigint, jsonb);
drop function if exists public.save_business_day_staff_attendance(bigint, bigint, jsonb);
drop function if exists public.get_business_day_drink_delivery_amount(bigint, bigint);
drop function if exists public.save_business_day_drink_delivery_amount(bigint, bigint, numeric);
drop function if exists public.save_business_day_closing_attendance(bigint, bigint, jsonb);
drop function if exists public.save_business_day_staff_closing_attendance(bigint, bigint, jsonb);
drop function if exists public.get_store_tables(bigint);
drop function if exists public.get_store_casts(bigint);
drop function if exists public.get_store_cast_admin_list(bigint);
drop function if exists public.create_store_cast(bigint, text);
drop function if exists public.delete_store_cast(bigint, bigint);
drop function if exists public.get_store_staffs(bigint);
drop function if exists public.get_store_staff_admin_list(bigint);
drop function if exists public.create_store_staff(bigint, text);
drop function if exists public.delete_store_staff(bigint, bigint);
drop function if exists public.get_business_day_slips(bigint, bigint);
drop function if exists public.get_order_entry_slips(bigint, bigint);
drop function if exists public.get_store_order_items(bigint);
drop function if exists public.get_store_item_admin_catalog(bigint);
drop function if exists public.get_store_nomination_back_master(bigint);
drop function if exists public.save_store_nomination_back_master(bigint, jsonb);
drop function if exists public.upsert_store_item_category(bigint, bigint, text, text, integer, boolean);
drop function if exists public.upsert_store_item(bigint, bigint, bigint, text, text, numeric, integer, boolean, boolean, numeric, text);
drop function if exists public.upsert_store_item(bigint, bigint, bigint, text, text, numeric, integer, boolean, boolean, numeric, numeric, text);
drop function if exists public.upsert_store_item(bigint, bigint, bigint, text, numeric, boolean, boolean, numeric, numeric, text);
drop function if exists public.delete_store_item(bigint, bigint);
drop function if exists public.reorder_store_items(bigint, jsonb);
drop function if exists public.get_order_attending_casts(bigint, bigint);
drop function if exists public.add_store_order_lines(bigint, bigint, jsonb);
drop function if exists public.get_store_slip_detail(bigint, bigint);
drop function if exists public.add_store_slip_customers(bigint, bigint, text[]);
drop function if exists public.add_store_slip_customers(bigint, bigint, text[], timestamp with time zone);
drop function if exists public.add_store_slip_nominations(bigint, bigint, jsonb);
drop function if exists public.save_store_slip_adjustments(bigint, bigint, jsonb);
drop function if exists public.save_store_karaoke_lines(bigint, bigint, jsonb);
drop function if exists public.leave_store_slip_customer(bigint, bigint, timestamp with time zone);
drop function if exists public.update_store_slip_customer_label(bigint, bigint, text);
drop function if exists public.void_store_order_line(bigint, bigint);
drop function if exists public.confirm_store_checkout(bigint, bigint, timestamp with time zone, jsonb, numeric);
drop function if exists public.create_store_slip(bigint, bigint, timestamp with time zone, text[], bigint[], text);
drop function if exists public.create_store_slip(bigint, bigint, timestamp with time zone, text[], jsonb, text);
drop function if exists public.get_pending_receipts(bigint, text);
drop function if exists public.save_quick_entry(bigint, numeric, text, text, text, text);
drop function if exists public.quick_enter_receipt(bigint, text, date, numeric, text, text, text, text);
drop function if exists public.quick_enter_receipt(bigint, text, date, numeric, text, text, text, jsonb, text);
drop function if exists public.mark_receipt_scan_mistake(bigint, text, text);

commit;
