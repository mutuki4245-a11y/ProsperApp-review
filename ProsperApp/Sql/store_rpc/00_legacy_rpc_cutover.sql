begin;

-- The v2 application and database contracts are deployed as one release. Drop
-- aggregate entry points first so their dependencies can be removed without
-- CASCADE and without retaining a legacy execution path.
drop function if exists store.get_business_home_bootstrap_v2(bigint);
drop function if exists store.sync_business_home_changes_v2(bigint, uuid, bigint, bigint, date, jsonb, jsonb);
drop function if exists store.submit_current_order_entry_v2(bigint, text, bigint, bigint, jsonb);
drop function if exists store.issue_checkout_statement_v2(bigint, text, bigint, bigint, bigint, timestamp with time zone);
drop function if exists store.release_checkout_ready_v2(bigint, text, bigint, bigint, bigint);
drop function if exists store.confirm_checkout_v2(bigint, text, bigint, bigint, bigint, jsonb, numeric);
drop function if exists store.cancel_checkout_v2(bigint, text, bigint, bigint, bigint);
drop function if exists store.get_attendance_editor_bootstrap_v2(bigint);
drop function if exists store.get_current_attendance_editor_snapshot(bigint, bigint, bigint);
drop function if exists store.close_business_day_v2(bigint, text, bigint, bigint, text, boolean);
drop function if exists store.get_current_closing_dashboard(bigint, text);
drop function if exists store.save_current_business_day_drink_delivery_amount_v2(bigint, text, bigint, bigint, date, numeric);
drop function if exists store.get_current_drink_delivery_editor(bigint);
drop function if exists store.save_current_cast_sales_adjustment_v2(
    bigint, text, bigint, bigint, bigint, timestamp with time zone,
    bigint, timestamp with time zone, jsonb, text, text);
drop function if exists store.confirm_current_cast_sales_adjustments_v2(bigint, text, bigint, bigint, jsonb);
drop function if exists store.get_current_cast_sales_adjustment_overview(bigint);
drop function if exists store.save_drink_back_adjustments_v2(bigint, text, bigint, bigint, jsonb, jsonb, jsonb);
drop function if exists store.get_current_drink_back_editor(bigint);
drop function if exists store.advance_receipt_work_queue_v2(bigint, text, text, text, text, date, numeric, text, text, text, bigint);
drop function if exists store.is_pending_receipt_drive_file_allowed(bigint, text);
drop function if exists store.save_management_master_v2(bigint, text, text, text, text, jsonb, boolean);
drop function if exists store.get_management_master_snapshot(bigint, text);
drop function if exists store.get_store_bootstrap(bigint);
drop function if exists store.get_business_home_bootstrap(bigint);
drop function if exists store.get_current_business_home_snapshot(bigint);
drop function if exists store.get_current_business_home_snapshot(bigint, bigint);
drop function if exists store.get_current_order_entry_candidates(bigint);
drop function if exists store.flush_current_business_home_changes(bigint, text, jsonb, jsonb);
drop function if exists store.flush_business_home_changes(bigint, bigint, text, jsonb, jsonb);
drop function if exists store.save_current_business_day_attendance_v2(bigint, bigint, date, jsonb, jsonb);
drop function if exists store.save_current_business_day_attendance_v2(bigint, text, bigint, date, jsonb, jsonb);
drop function if exists store.save_current_business_day_attendance_v2(bigint, text, bigint, bigint, date, jsonb, jsonb);
drop function if exists store.get_current_receipt_work_queue(bigint);
drop function if exists store.get_current_receipt_work_queue(bigint, text);
drop function if exists store.advance_receipt_work_queue_v2(bigint, text, text, text, text, date, numeric, text, text, text, bigint);
drop function if exists store.close_current_business_day(bigint, bigint, text, text, boolean);
drop function if exists store.close_current_business_day(bigint, text, bigint, text, text, boolean);
drop function if exists store.close_current_business_day(bigint, text, bigint, bigint, text, text, boolean);
drop function if exists store.save_current_business_day_drink_delivery_amount_v2(bigint, bigint, date, numeric);
drop function if exists store.save_current_business_day_drink_delivery_amount_v2(bigint, text, bigint, date, numeric);
drop function if exists store.save_current_business_day_drink_delivery_amount_v2(bigint, text, bigint, bigint, date, numeric);
drop function if exists store.get_business_day_cast_sales_adjustment_overview(bigint, bigint);
drop function if exists store.save_business_day_cast_sales_adjustments(bigint, bigint, jsonb);
drop function if exists store.get_business_day_champagne_back_overview(bigint, bigint);
drop function if exists store.save_business_day_champagne_backs(bigint, bigint, jsonb);

-- Legacy application-facing reads and mutations. Internal v2 replacements use
-- distinct *_internal names and are created by the subsequent feature files.
drop function if exists store.get_context(bigint);
drop function if exists store.get_current_business_day(bigint);
drop function if exists store.open_business_day(bigint, date, text);
drop function if exists store.open_business_day_with_attendance(bigint, date, bigint[], text);
drop function if exists store.open_business_day_with_attendance(bigint, date, jsonb, text);
drop function if exists store.save_business_day_attendance(bigint, bigint, jsonb);
drop function if exists store.save_business_day_staff_attendance(bigint, bigint, jsonb);
drop function if exists store.get_business_day_closing_attendance(bigint, bigint);
drop function if exists store.save_business_day_closing_attendance(bigint, bigint, jsonb);
drop function if exists store.save_business_day_staff_closing_attendance(bigint, bigint, jsonb);
drop function if exists store.get_open_slip_count(bigint, bigint);
drop function if exists store.get_business_day_drink_delivery_status(bigint, bigint);
drop function if exists store.save_business_day_drink_delivery_amount(bigint, bigint, numeric);
drop function if exists store.get_business_day_closing_readiness(bigint, bigint, text);
drop function if exists store.close_business_day(bigint, bigint, text, text, boolean);

drop function if exists store.upsert_table(bigint, bigint, text, text, integer, integer, boolean);
drop function if exists store.delete_table(bigint, bigint);
drop function if exists store.create_cast(bigint, text);
drop function if exists store.create_cast(bigint, text, text);
drop function if exists store.update_cast_drink_memo(bigint, bigint, text);
drop function if exists store.delete_cast(bigint, bigint);
drop function if exists store.create_staff(bigint, text);
drop function if exists store.create_staff(bigint, text, text);
drop function if exists store.update_staff_employment_type(bigint, bigint, text);
drop function if exists store.delete_staff(bigint, bigint);
drop function if exists store.get_order_entry_slips(bigint, bigint);
drop function if exists store.save_nomination_back_master(bigint, jsonb);
drop function if exists store.upsert_item_category(bigint, bigint, text, text, integer, boolean);
drop function if exists store.upsert_item(bigint, bigint, bigint, text, text, numeric, integer, boolean, boolean, numeric, text);
drop function if exists store.upsert_item(bigint, bigint, bigint, text, text, numeric, integer, boolean, boolean, numeric, numeric, text);
drop function if exists store.upsert_item(bigint, bigint, bigint, text, numeric, boolean, boolean, numeric, numeric, text);
drop function if exists store.delete_item(bigint, bigint);
drop function if exists store.delete_item_category(bigint, bigint);
drop function if exists store.reorder_items(bigint, jsonb);

drop function if exists store.get_order_attending_casts(bigint, bigint);
drop function if exists store.add_order_lines(bigint, bigint, jsonb);
drop function if exists store.cancel_checkout(bigint, bigint);
drop function if exists store.create_slip(bigint, bigint, timestamp with time zone, text[], bigint[], text);
drop function if exists store.create_slip(bigint, bigint, timestamp with time zone, text[], jsonb, text);

drop function if exists store.get_pending_receipts(bigint, text);
drop function if exists store.quick_enter_receipt(bigint, text, date, numeric, text, text, text, text);
drop function if exists store.quick_enter_receipt(bigint, text, date, numeric, text, text, text, jsonb, text);
drop function if exists store.quick_enter_receipt(bigint, text, date, numeric, text, text, text, jsonb, text, bigint, bigint);
drop function if exists store.mark_receipt_scan_mistake(bigint, text, text);

drop function if exists store.get_business_day_cast_sales_adjustment_status(bigint, bigint);
drop function if exists store.save_cast_sales_adjustment(bigint, bigint, jsonb, text, text);
drop function if exists store.get_business_day_champagne_back_status(bigint, bigint);

drop function if exists store.issue_checkout_statement(bigint, bigint, timestamp with time zone);
drop function if exists store.release_checkout_ready(bigint, bigint);
drop function if exists store.confirm_checkout(bigint, bigint, jsonb, numeric);
drop function if exists store.confirm_checkout(bigint, bigint, timestamp with time zone, jsonb, numeric);
drop function if exists store.confirm_checkout(bigint, bigint, timestamp with time zone, jsonb, numeric, jsonb);
drop function if exists store.get_business_day_snapshot(bigint, bigint);
drop function if exists store.apply_business_slip_editor_operation(bigint, bigint, bigint, text, text, jsonb);
drop function if exists store.save_pricing_plan(bigint, integer, numeric, numeric, numeric, numeric, boolean);
drop function if exists store.save_pricing_plan_v2(bigint, integer, integer, numeric, numeric, numeric, numeric, boolean);

commit;
