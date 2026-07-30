using System.Globalization;
using System.Text.Json;

namespace ProsperApp.Infrastructure.Supabase;

public sealed class ReviewSupabaseRpcClient : ISupabaseRpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _sync = new();
    private readonly ReviewStore _store = ReviewStore.Create();

    public bool HasAccess => true;

    public Task<SupabaseRpcResult> PostArrayAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var payloadElement = JsonSerializer.SerializeToElement(payload, JsonOptions);
        lock (_sync)
        {
            return Task.FromResult(HandleArray(functionName, payloadElement));
        }
    }

    public Task<SupabaseRpcResult> PostScalarAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var payloadElement = JsonSerializer.SerializeToElement(payload, JsonOptions);
        lock (_sync)
        {
            return Task.FromResult(HandleScalar(functionName, payloadElement));
        }
    }

    private SupabaseRpcResult HandleArray(string functionName, JsonElement payload)
    {
        return functionName switch
        {
            "store.get_departments" => Rows(_store.Departments.Select(DepartmentRow)),
            "store.get_context" => Rows(StoreContextRow()),
            "store.get_tables" => Rows(_store.Tables.Select(TableRow)),
            "store.get_casts" => Rows(_store.Casts.Where(cast => cast.IsActive).Select(CastRow)),
            "store.get_casts_admin" => Rows(_store.Casts.Select(CastAdminRow)),
            "store.get_order_items" => Rows(_store.Items.Where(item => item.IsActive && item.ItemType == "standard").Select(OrderItemRow)),
            "store.get_item_admin_catalog" => Rows(BuildItemAdminCatalogRows()),
            "store.get_nomination_back_master" => Rows(_store.NominationBacks.Select(NominationBackRow)),
            "store.get_pricing_plan" => Rows(PricingPlanRow()),
            "store.get_payment_methods" => Rows(_store.PaymentMethods.Select(PaymentMethodRow)),
            "store.get_current_business_day" => Rows(GetCurrentBusinessDayRows()),
            "store.open_business_day" => OpenBusinessDay(payload, withAttendance: false),
            "store.open_business_day_with_attendance" => OpenBusinessDay(payload, withAttendance: true),
            "store.save_business_day_attendance" => SaveAttendance(payload),
            "store.get_business_day_snapshot" => Rows(new
            {
                business_day_revision = _store.Revision,
                snapshot = BuildSnapshot()
            }),
            "store.flush_business_home_changes" => FlushBusinessHomeChanges(payload),
            "store.create_slip" => CreateSlip(payload),
            "store.get_order_entry_slips" => Rows(_store.Slips.Where(slip => slip.Status == "open").Select(OrderEntrySlipRow)),
            "store.get_order_attending_casts" => Rows(_store.Attendance.Select(AttendingCastRow)),
            "store.add_order_lines" => AddOrderLines(payload),
            "store.issue_checkout_statement" => IssueCheckoutStatement(payload),
            "store.get_checkout_statement_print_data" => GetCheckoutStatementPrintData(payload),
            "store.release_checkout_ready" => ReleaseCheckoutReady(payload),
            "store.confirm_checkout" => ConfirmCheckout(payload),
            "store.get_checkout_receipt_print_data" => GetCheckoutReceiptPrintData(payload),
            "store.cancel_checkout" => CancelCheckout(payload),
            "store.get_business_day_drink_delivery_status" => Rows(new
            {
                drink_delivery_amount = _store.DrinkDeliveryAmount,
                is_entered = _store.IsDrinkDeliveryAmountEntered
            }),
            "store.get_business_day_closing_attendance" => Rows(_store.Attendance.Select(ClosingAttendanceRow)),
            "store.get_business_day_closing_readiness" => Rows(ClosingReadinessRow()),
            "store.close_business_day" => CloseBusinessDay(payload),
            "store.get_pending_receipts" => Rows(_store.PendingReceipts.Select(PendingReceiptRow)),
            "store.quick_enter_receipt" => QuickEnterReceipt(payload),
            "store.mark_receipt_scan_mistake" => MarkReceiptScanMistake(payload),
            "store.get_business_day_cast_sales_adjustment_status" => Rows(CastSalesAdjustmentStatusRow()),
            "store.get_business_day_cast_sales_adjustment_overview" => Rows(CastSalesAdjustmentOverviewRow()),
            "store.get_cast_sales_adjustment_slips" => Rows(BuildCastSalesAdjustmentSlips()),
            "store.get_cast_sales_adjustment_detail" => Rows(BuildCastSalesAdjustmentDetail(ReadLong(payload, "p_slip_id") ?? 0)),
            "store.save_business_day_cast_sales_adjustments" => Rows(new { saved_cast_count = 1 }),
            "store.create_cast" => CreateCast(payload),
            "store.update_cast_drink_memo" => UpdateCastDrinkMemo(payload),
            "store.delete_cast" => DeleteCast(payload),
            "store.upsert_item_category" => UpsertItemCategory(payload),
            "store.upsert_item" => UpsertItem(payload),
            "store.delete_item" => DeleteItem(payload),
            "store.reorder_items" => ReorderItems(payload),
            "store.save_nomination_back_master" => SaveNominationBackMaster(payload),
            "store.save_pricing_plan" => SavePricingPlan(payload),
            "store.delete_non_master_records" => Rows(new { table_name = "review_mock_data", deleted_count = 0 }),
            _ => Rows()
        };
    }

    private SupabaseRpcResult HandleScalar(string functionName, JsonElement payload)
    {
        return functionName switch
        {
            "store.get_open_slip_count" => Scalar(_store.Slips.Count(slip => slip.Status is "open" or "checkout_ready")),
            "store.save_business_day_drink_delivery_amount" => SaveDrinkDeliveryAmount(payload),
            "store.save_business_day_closing_attendance" => SaveClosingAttendance(payload),
            "store.save_cast_sales_adjustment" => Scalar(1),
            _ => Scalar(1)
        };
    }

    private SupabaseRpcResult OpenBusinessDay(JsonElement payload, bool withAttendance)
    {
        var businessDate = ReadDateOnly(payload, "p_business_date") ?? DateOnly.FromDateTime(DateTime.Now);
        _store.BusinessDayStatus = "open";
        _store.BusinessDate = businessDate;
        _store.OpenedAt = DateTimeOffset.Now;
        _store.ClosedAt = null;
        if (withAttendance)
        {
            ReplaceAttendance(ReadArray(payload, "p_attendance_entries"));
        }

        _store.Touch();
        return Rows(BusinessDayRow());
    }

    private SupabaseRpcResult SaveAttendance(JsonElement payload)
    {
        ReplaceAttendance(ReadArray(payload, "p_attendance_entries"));
        _store.Touch();
        return Rows(BusinessDayRow());
    }

    private SupabaseRpcResult SaveClosingAttendance(JsonElement payload)
    {
        var savedCount = 0;
        foreach (var item in ReadArray(payload, "p_attendance_entries"))
        {
            var attendanceId = ReadLong(item, "attendance_id") ?? 0;
            var attendance = _store.Attendance.FirstOrDefault(row => row.AttendanceId == attendanceId);
            if (attendance is null)
            {
                continue;
            }

            attendance.ClockOutTime = ReadString(item, "clock_out_time") ?? attendance.ClockOutTime;
            attendance.UsesSendService = ReadBool(item, "uses_send_service") ?? attendance.UsesSendService;
            savedCount++;
        }

        _store.Touch();
        return Scalar(savedCount);
    }

    private void ReplaceAttendance(IEnumerable<JsonElement> rows)
    {
        var next = new List<ReviewAttendance>();
        foreach (var item in rows)
        {
            if (ReadBool(item, "is_selected") == false)
            {
                continue;
            }

            var castId = ReadLong(item, "cast_id") ?? 0;
            var cast = _store.Casts.FirstOrDefault(candidate => candidate.CastId == castId);
            if (cast is null)
            {
                continue;
            }

            next.Add(new ReviewAttendance
            {
                AttendanceId = _store.NextAttendanceId++,
                CastId = cast.CastId,
                ClockInTime = ReadString(item, "clock_in_time") ?? "20:00",
                ClockOutTime = null,
                UsesSendService = false
            });
        }

        if (next.Count > 0)
        {
            _store.Attendance.Clear();
            _store.Attendance.AddRange(next);
        }
    }

    private SupabaseRpcResult CreateSlip(JsonElement payload)
    {
        var tableId = ReadLong(payload, "p_table_id") ?? _store.Tables[0].TableId;
        var openedAt = ReadDateTimeOffset(payload, "p_opened_at") ?? DateTimeOffset.Now;
        var slip = new ReviewSlip
        {
            SlipId = _store.NextSlipId++,
            SlipNo = $"R-{_store.NextSlipNo++:000}",
            TableId = tableId,
            OpenedAt = openedAt,
            Status = "open",
            Memo = ReadString(payload, "p_memo")
        };

        var lineNo = 1;
        foreach (var customer in ReadArray(payload, "p_customer_labels"))
        {
            var label = customer.ValueKind == JsonValueKind.String ? customer.GetString() : null;
            slip.Customers.Add(new ReviewCustomer
            {
                CustomerId = _store.NextCustomerId++,
                LineNo = lineNo++,
                Label = label,
                EnteredAt = openedAt,
                Status = "active"
            });
        }

        if (slip.Customers.Count == 0)
        {
            slip.Customers.Add(new ReviewCustomer
            {
                CustomerId = _store.NextCustomerId++,
                LineNo = 1,
                Label = null,
                EnteredAt = openedAt,
                Status = "active"
            });
        }

        foreach (var nomination in ReadArray(payload, "p_cast_nominations"))
        {
            AddNomination(slip, nomination, openedAt);
        }

        _store.Slips.Add(slip);
        _store.Touch();
        return Rows(new { slip_id = slip.SlipId });
    }

    private SupabaseRpcResult FlushBusinessHomeChanges(JsonElement payload)
    {
        var operationResults = new List<object>();
        foreach (var operation in ReadArray(payload, "p_operations"))
        {
            var operationId = ReadString(operation, "operation_id") ?? Guid.NewGuid().ToString("N");
            var slipId = ReadLong(operation, "slip_id") ?? 0;
            var operationType = ReadString(operation, "operation_type") ?? string.Empty;
            var slip = _store.Slips.FirstOrDefault(candidate => candidate.SlipId == slipId);
            if (slip is null || slip.Status != "open")
            {
                operationResults.Add(new { operation_id = operationId, succeeded = false, message = "store_slip_not_found" });
                continue;
            }

            var rowSucceeded = ApplyBusinessHomeOperation(slip, operationType, ReadObject(operation, "payload"));
            operationResults.Add(new
            {
                operation_id = operationId,
                succeeded = rowSucceeded,
                message = rowSucceeded ? "review_mock_saved" : "invalid_review_operation"
            });
        }

        var karaokeResults = new List<object>();
        foreach (var line in ReadArray(payload, "p_karaoke_lines"))
        {
            var draftId = ReadString(line, "draft_id") ?? Guid.NewGuid().ToString("N");
            var slipId = ReadLong(line, "slip_id") ?? 0;
            var quantity = Math.Max(0, (int)(ReadDecimal(line, "quantity") ?? 0));
            var slip = _store.Slips.FirstOrDefault(candidate => candidate.SlipId == slipId && candidate.Status == "open");
            if (slip is null)
            {
                karaokeResults.Add(new { draft_id = draftId, succeeded = false, message = "store_slip_not_found" });
                continue;
            }

            SetKaraokeQuantity(slip, quantity);
            karaokeResults.Add(new { draft_id = draftId, succeeded = true, quantity });
        }

        _store.Touch();
        return Rows(new
        {
            snapshot = BuildSnapshot(),
            operation_results = operationResults,
            karaoke_results = karaokeResults
        });
    }

    private bool ApplyBusinessHomeOperation(ReviewSlip slip, string operationType, JsonElement payload)
    {
        switch (operationType)
        {
            case "add_customer":
                slip.Customers.Add(new ReviewCustomer
                {
                    CustomerId = _store.NextCustomerId++,
                    LineNo = slip.Customers.Count == 0 ? 1 : slip.Customers.Max(customer => customer.LineNo) + 1,
                    Label = ReadString(payload, "customer_label"),
                    EnteredAt = ComposeTime(ReadString(payload, "entered_time")) ?? DateTimeOffset.Now,
                    Status = "active"
                });
                return true;
            case "update_customer":
                var customer = slip.Customers.FirstOrDefault(row => row.CustomerId == (ReadLong(payload, "slip_customer_id") ?? 0));
                if (customer is null) return false;
                customer.Label = ReadString(payload, "customer_label");
                return true;
            case "leave_customer":
                var leavingCustomer = slip.Customers.FirstOrDefault(row => row.CustomerId == (ReadLong(payload, "slip_customer_id") ?? 0));
                if (leavingCustomer is null) return false;
                leavingCustomer.Status = "left";
                leavingCustomer.LeftAt = ComposeTime(ReadString(payload, "left_time")) ?? DateTimeOffset.Now;
                return true;
            case "add_nomination":
                AddNomination(slip, payload, DateTimeOffset.Now);
                return true;
            case "cancel_nomination":
                var nomination = slip.Nominations.FirstOrDefault(row => row.SlipCastId == (ReadLong(payload, "slip_cast_id") ?? 0));
                if (nomination is null) return false;
                nomination.Status = "cancelled";
                slip.Orders.Where(order => order.SourceType == "nomination_fee" && order.SourceId == nomination.SlipCastId)
                    .ToList()
                    .ForEach(order => order.Status = "voided");
                return true;
            case "add_order":
                AddOrderLine(slip, ReadLong(payload, "item_id") ?? 0, (int)(ReadLong(payload, "quantity") ?? 1), ReadLong(payload, "cast_back_cast_id"));
                return true;
            case "void_order":
                var order = slip.Orders.FirstOrDefault(row => row.OrderLineId == (ReadLong(payload, "order_line_id") ?? 0));
                if (order is null) return false;
                order.Status = "voided";
                return true;
            case "add_adjustment":
                slip.Adjustments.Add(new ReviewAdjustment
                {
                    ChargeLineId = _store.NextChargeLineId++,
                    LineNo = slip.Adjustments.Count == 0 ? 1 : slip.Adjustments.Max(row => row.LineNo) + 1,
                    LineName = ReadString(payload, "line_name") ?? "レビュー調整",
                    Amount = ReadDecimal(payload, "amount") ?? 0,
                    CreatedAt = DateTimeOffset.Now,
                    Status = "active"
                });
                return true;
            case "void_adjustment":
                var adjustment = slip.Adjustments.FirstOrDefault(row => row.ChargeLineId == (ReadLong(payload, "charge_line_id") ?? 0));
                if (adjustment is null) return false;
                adjustment.Status = "voided";
                return true;
            default:
                return false;
        }
    }

    private SupabaseRpcResult AddOrderLines(JsonElement payload)
    {
        var inserted = 0;
        var fallbackSlipId = ReadLong(payload, "p_slip_id");
        foreach (var line in ReadArray(payload, "p_order_lines"))
        {
            var slipId = ReadLong(line, "slip_id") ?? fallbackSlipId ?? 0;
            var slip = _store.Slips.FirstOrDefault(candidate => candidate.SlipId == slipId && candidate.Status == "open");
            if (slip is null)
            {
                continue;
            }

            AddOrderLine(
                slip,
                ReadLong(line, "item_id") ?? 0,
                (int)(ReadLong(line, "quantity") ?? 1),
                ReadLong(line, "cast_back_cast_id"));
            inserted++;
        }

        _store.Touch();
        return Rows(new { inserted_count = inserted });
    }

    private SupabaseRpcResult IssueCheckoutStatement(JsonElement payload)
    {
        var slip = FindSlip(payload);
        if (slip is null || slip.Status != "open")
        {
            return Failed("checkout_statement_slip_not_found");
        }

        slip.Status = "checkout_ready";
        slip.ClosedAt = ReadDateTimeOffset(payload, "p_closed_at") ?? DateTimeOffset.Now;
        _store.Touch();
        return Rows(new
        {
            print_data = BuildStatementPrintData(slip),
            review_data = BuildStatementReviewData(slip)
        });
    }

    private SupabaseRpcResult GetCheckoutStatementPrintData(JsonElement payload)
    {
        var slip = FindSlip(payload);
        return slip is null
            ? Failed("checkout_ready_not_found")
            : Rows(new
            {
                print_data = BuildStatementPrintData(slip),
                review_data = BuildStatementReviewData(slip)
            });
    }

    private SupabaseRpcResult ReleaseCheckoutReady(JsonElement payload)
    {
        var slip = FindSlip(payload);
        if (slip is null || slip.Status != "checkout_ready")
        {
            return Failed("checkout_ready_not_found");
        }

        slip.Status = "open";
        slip.ClosedAt = null;
        _store.Touch();
        return Rows(new { slip_id = slip.SlipId });
    }

    private SupabaseRpcResult ConfirmCheckout(JsonElement payload)
    {
        var slip = FindSlip(payload);
        if (slip is null || slip.Status != "checkout_ready")
        {
            return Failed("checkout_ready_not_found");
        }

        var checkoutId = slip.CheckoutId ?? _store.NextCheckoutId++;
        slip.CheckoutId = checkoutId;
        slip.Status = "checked_out";
        slip.ClosedAt ??= DateTimeOffset.Now;
        _store.Touch();

        var paid = ReadArray(payload, "p_payments")
            .Select(payment => new
            {
                method_code = ReadString(payment, "method_code") ?? "cash",
                amount = ReadDecimal(payment, "amount") ?? 0
            })
            .Where(payment => payment.amount > 0)
            .ToList();
        var payments = paid.Count > 0
            ? paid.Select(payment =>
            {
                var method = _store.PaymentMethods.FirstOrDefault(row => row.MethodCode == payment.method_code);
                return new
                {
                    method_code = payment.method_code,
                    method_name = method?.MethodName ?? payment.method_code,
                    amount = payment.amount
                };
            })
            : [];

        return Rows(new
        {
            checkout_id = checkoutId,
            change_amount = 0,
            print_data = BuildReceiptPrintData(slip, checkoutId, payments)
        });
    }

    private SupabaseRpcResult GetCheckoutReceiptPrintData(JsonElement payload)
    {
        var slip = FindSlip(payload);
        if (slip is null)
        {
            return Failed("checkout_not_found");
        }

        var checkoutId = slip.CheckoutId ?? _store.NextCheckoutId++;
        slip.CheckoutId = checkoutId;
        return Rows(new
        {
            checkout_id = checkoutId,
            print_data = BuildReceiptPrintData(slip, checkoutId, [])
        });
    }

    private SupabaseRpcResult CancelCheckout(JsonElement payload)
    {
        var slip = FindSlip(payload);
        if (slip is null)
        {
            return Failed("checkout_not_found");
        }

        var checkoutId = slip.CheckoutId ?? _store.NextCheckoutId++;
        slip.CheckoutId = checkoutId;
        slip.Status = "open";
        slip.ClosedAt = null;
        _store.Touch();
        return Rows(new { checkout_id = checkoutId });
    }

    private SupabaseRpcResult SaveDrinkDeliveryAmount(JsonElement payload)
    {
        _store.DrinkDeliveryAmount = ReadDecimal(payload, "p_drink_delivery_amount") ?? 0;
        _store.IsDrinkDeliveryAmountEntered = true;
        _store.Touch();
        return Scalar(_store.DrinkDeliveryAmount);
    }

    private SupabaseRpcResult CloseBusinessDay(JsonElement payload)
    {
        _store.BusinessDayStatus = "closed";
        _store.ClosedAt = DateTimeOffset.Now;
        _store.Touch();
        return Rows(BusinessDayRow());
    }

    private SupabaseRpcResult QuickEnterReceipt(JsonElement payload)
    {
        var documentId = ReadString(payload, "p_document_id");
        if (!string.IsNullOrWhiteSpace(documentId))
        {
            _store.PendingReceipts.RemoveAll(receipt => receipt.DocumentId == documentId);
        }

        _store.Touch();
        return Rows(new { document_id = documentId ?? "review-receipt" });
    }

    private SupabaseRpcResult MarkReceiptScanMistake(JsonElement payload)
    {
        var documentId = ReadString(payload, "p_document_id");
        if (!string.IsNullOrWhiteSpace(documentId))
        {
            _store.PendingReceipts.RemoveAll(receipt => receipt.DocumentId == documentId);
        }

        _store.Touch();
        return Rows(new { document_id = documentId ?? "review-receipt" });
    }

    private SupabaseRpcResult CreateCast(JsonElement payload)
    {
        var cast = new ReviewCast
        {
            CastId = _store.NextCastId++,
            DisplayName = ReadString(payload, "p_display_name") ?? "レビューキャスト",
            DrinkMemo = ReadString(payload, "p_drink_memo"),
            JoinedOn = DateOnly.FromDateTime(DateTime.Now),
            IsActive = true
        };
        _store.Casts.Add(cast);
        _store.Touch();
        return Rows(new { cast_id = cast.CastId });
    }

    private SupabaseRpcResult UpdateCastDrinkMemo(JsonElement payload)
    {
        var castId = ReadLong(payload, "p_cast_id") ?? 0;
        var cast = _store.Casts.FirstOrDefault(candidate => candidate.CastId == castId);
        if (cast is not null)
        {
            cast.DrinkMemo = ReadString(payload, "p_drink_memo");
        }

        _store.Touch();
        return Rows(new { cast_id = castId });
    }

    private SupabaseRpcResult DeleteCast(JsonElement payload)
    {
        var castId = ReadLong(payload, "p_cast_id") ?? 0;
        var cast = _store.Casts.FirstOrDefault(candidate => candidate.CastId == castId);
        if (cast is not null)
        {
            cast.IsActive = false;
        }

        _store.Touch();
        return Rows(new { cast_id = castId });
    }

    private SupabaseRpcResult UpsertItemCategory(JsonElement payload)
    {
        var categoryId = ReadLong(payload, "p_item_category_id") ?? 0;
        var category = categoryId > 0
            ? _store.Categories.FirstOrDefault(candidate => candidate.ItemCategoryId == categoryId)
            : null;
        if (category is null)
        {
            category = new ReviewCategory { ItemCategoryId = _store.NextCategoryId++ };
            _store.Categories.Add(category);
        }

        category.CategoryCode = ReadString(payload, "p_category_code") ?? category.CategoryCode;
        category.CategoryName = ReadString(payload, "p_category_name") ?? category.CategoryName;
        category.SortOrder = (int)(ReadLong(payload, "p_sort_order") ?? category.SortOrder);
        category.IsActive = ReadBool(payload, "p_is_active") ?? category.IsActive;
        _store.Touch();
        return Rows(new { item_category_id = category.ItemCategoryId });
    }

    private SupabaseRpcResult UpsertItem(JsonElement payload)
    {
        var itemId = ReadLong(payload, "p_item_id") ?? 0;
        var item = itemId > 0
            ? _store.Items.FirstOrDefault(candidate => candidate.ItemId == itemId)
            : null;
        if (item is null)
        {
            item = new ReviewItem { ItemId = _store.NextItemId++, ItemType = "standard" };
            _store.Items.Add(item);
        }

        item.ItemCategoryId = ReadLong(payload, "p_item_category_id") ?? item.ItemCategoryId;
        item.ItemName = ReadString(payload, "p_item_name") ?? item.ItemName;
        item.DefaultPrice = ReadDecimal(payload, "p_default_price") ?? item.DefaultPrice;
        item.IsActive = ReadBool(payload, "p_is_active") ?? item.IsActive;
        item.IsCastBackTarget = ReadBool(payload, "p_is_cast_back_target") ?? item.IsCastBackTarget;
        item.CastBackRegularUnitAmount = ReadDecimal(payload, "p_cast_back_regular_unit_amount") ?? item.CastBackRegularUnitAmount;
        item.CastBackNominationUnitAmount = ReadDecimal(payload, "p_cast_back_nomination_unit_amount") ?? item.CastBackNominationUnitAmount;
        _store.Touch();
        return Rows(new { item_id = item.ItemId });
    }

    private SupabaseRpcResult DeleteItem(JsonElement payload)
    {
        var itemId = ReadLong(payload, "p_item_id") ?? 0;
        var item = _store.Items.FirstOrDefault(candidate => candidate.ItemId == itemId && candidate.ItemType == "standard");
        if (item is not null)
        {
            item.IsActive = false;
        }

        _store.Touch();
        return Rows(new { item_id = itemId });
    }

    private SupabaseRpcResult ReorderItems(JsonElement payload)
    {
        var updated = 0;
        foreach (var row in ReadArray(payload, "p_items"))
        {
            var itemId = ReadLong(row, "item_id") ?? 0;
            var item = _store.Items.FirstOrDefault(candidate => candidate.ItemId == itemId);
            if (item is null)
            {
                continue;
            }

            item.SortOrder = (int)(ReadLong(row, "sort_order") ?? item.SortOrder);
            updated++;
        }

        _store.Touch();
        return Rows(new { updated_count = updated });
    }

    private SupabaseRpcResult SaveNominationBackMaster(JsonElement payload)
    {
        var updated = 0;
        foreach (var row in ReadArray(payload, "p_settings"))
        {
            var kind = ReadString(row, "nomination_kind");
            if (string.IsNullOrWhiteSpace(kind))
            {
                continue;
            }

            var setting = _store.NominationBacks.FirstOrDefault(candidate => candidate.NominationKind == kind);
            if (setting is null)
            {
                setting = new ReviewNominationBack { NominationKind = kind };
                _store.NominationBacks.Add(setting);
            }

            setting.NominationType = ReadString(row, "nomination_type") ?? setting.NominationType;
            setting.DisplayName = ReadString(row, "display_name") ?? setting.DisplayName;
            setting.CompanionTime = ReadString(row, "companion_time");
            setting.BackUnitAmount = ReadDecimal(row, "back_unit_amount") ?? setting.BackUnitAmount;
            setting.SortOrder = (int)(ReadLong(row, "sort_order") ?? setting.SortOrder);
            setting.IsActive = ReadBool(row, "is_active") ?? setting.IsActive;
            updated++;
        }

        _store.Touch();
        return Rows(new { updated_count = updated });
    }

    private SupabaseRpcResult SavePricingPlan(JsonElement payload)
    {
        _store.PricingPlan.SetMinutes = (int)(ReadLong(payload, "p_set_minutes") ?? _store.PricingPlan.SetMinutes);
        _store.PricingPlan.SetUnitPriceSingle = ReadDecimal(payload, "p_set_unit_price_single") ?? _store.PricingPlan.SetUnitPriceSingle;
        _store.PricingPlan.SetUnitPricePerCustomer = ReadDecimal(payload, "p_set_unit_price_per_customer") ?? _store.PricingPlan.SetUnitPricePerCustomer;
        _store.PricingPlan.ExtensionUnitPriceSingle = ReadDecimal(payload, "p_extension_unit_price_single") ?? _store.PricingPlan.ExtensionUnitPriceSingle;
        _store.PricingPlan.ExtensionUnitPricePerCustomer = ReadDecimal(payload, "p_extension_unit_price_per_customer") ?? _store.PricingPlan.ExtensionUnitPricePerCustomer;
        _store.PricingPlan.IsActive = ReadBool(payload, "p_is_active") ?? _store.PricingPlan.IsActive;
        _store.PricingPlan.PlanVersion++;
        _store.Touch();
        return Rows(new { plan_version = _store.PricingPlan.PlanVersion });
    }

    private void AddNomination(ReviewSlip slip, JsonElement payload, DateTimeOffset startedAt)
    {
        var castId = ReadLong(payload, "cast_id") ?? 0;
        var cast = _store.Casts.FirstOrDefault(candidate => candidate.CastId == castId) ?? _store.Casts.First();
        var kind = ReadString(payload, "nomination_kind") ?? "nomination";
        var setting = _store.NominationBacks.FirstOrDefault(row => row.NominationKind == kind);
        var price = ReadDecimal(payload, "nomination_price") ?? 3000;
        var slipCastId = _store.NextSlipCastId++;
        slip.Nominations.Add(new ReviewNomination
        {
            SlipCastId = slipCastId,
            CastId = cast.CastId,
            NominationKind = kind,
            NominationType = setting?.NominationType ?? kind,
            NominationDisplayName = setting?.DisplayName ?? kind,
            NominationPrice = price,
            StartedAt = startedAt,
            Status = "active"
        });
        slip.Orders.Add(new ReviewOrder
        {
            OrderLineId = _store.NextOrderLineId++,
            LineNo = slip.Orders.Count == 0 ? 1 : slip.Orders.Max(order => order.LineNo) + 1,
            ItemName = "指名料金",
            ItemType = "nomination_fee",
            Quantity = 1,
            UnitPrice = price,
            OrderedAt = startedAt,
            Status = "active",
            SourceType = "nomination_fee",
            SourceId = slipCastId
        });
    }

    private void AddOrderLine(ReviewSlip slip, long itemId, int quantity, long? castBackCastId)
    {
        var item = _store.Items.FirstOrDefault(candidate => candidate.ItemId == itemId);
        if (item is null)
        {
            return;
        }

        var cast = castBackCastId is > 0
            ? _store.Casts.FirstOrDefault(candidate => candidate.CastId == castBackCastId.Value)
            : null;
        slip.Orders.Add(new ReviewOrder
        {
            OrderLineId = _store.NextOrderLineId++,
            LineNo = slip.Orders.Count == 0 ? 1 : slip.Orders.Max(order => order.LineNo) + 1,
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            ItemType = item.ItemType,
            Quantity = Math.Max(1, quantity),
            UnitPrice = item.DefaultPrice,
            OrderedAt = DateTimeOffset.Now,
            Status = "active",
            BackCastId = cast?.CastId,
            BackCastDisplayName = cast?.DisplayName
        });
    }

    private void SetKaraokeQuantity(ReviewSlip slip, int quantity)
    {
        var karaokeItem = _store.Items.First(item => item.ItemType == "karaoke");
        var order = slip.Orders.FirstOrDefault(row => row.ItemType == "karaoke");
        if (order is null)
        {
            slip.Orders.Add(new ReviewOrder
            {
                OrderLineId = _store.NextOrderLineId++,
                LineNo = slip.Orders.Count == 0 ? 1 : slip.Orders.Max(row => row.LineNo) + 1,
                ItemId = karaokeItem.ItemId,
                ItemName = karaokeItem.ItemName,
                ItemType = "karaoke",
                Quantity = quantity,
                UnitPrice = karaokeItem.DefaultPrice,
                OrderedAt = DateTimeOffset.Now,
                Status = quantity > 0 ? "active" : "voided"
            });
            return;
        }

        order.Quantity = quantity;
        order.Status = quantity > 0 ? "active" : "voided";
    }

    private ReviewSlip? FindSlip(JsonElement payload) =>
        _store.Slips.FirstOrDefault(slip => slip.SlipId == (ReadLong(payload, "p_slip_id") ?? 0));

    private object StoreContextRow() => new
    {
        company_id = _store.CompanyId,
        department_id = _store.DepartmentId,
        department_name = _store.DepartmentName,
        attendance_minute_step = 15,
        cast_sales_amount_basis = "total",
        cast_sales_split_mode = "split"
    };

    private object BusinessDayRow() => new
    {
        business_day_id = _store.BusinessDayId,
        company_id = _store.CompanyId,
        department_id = _store.DepartmentId,
        business_date = _store.BusinessDate,
        opened_at = _store.OpenedAt,
        closed_at = _store.ClosedAt,
        status = _store.BusinessDayStatus,
        memo = "レビュー用モック営業日"
    };

    private IEnumerable<object> GetCurrentBusinessDayRows() =>
        _store.BusinessDayStatus == "open" ? [BusinessDayRow()] : [];

    private object BuildSnapshot()
    {
        var slips = _store.Slips.Select(BuildSlipObject).ToList();
        return new
        {
            businessDayId = _store.BusinessDayId,
            businessDate = _store.BusinessDate,
            businessDateDisplay = _store.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            businessDayRevision = _store.Revision,
            hasBusinessDay = _store.BusinessDayStatus == "open",
            openSlipCount = _store.Slips.Count(slip => slip.Status is "open" or "checkout_ready"),
            checkedOutSlipCount = _store.Slips.Count(slip => slip.Status == "checked_out"),
            estimatedSalesAmount = slips.Sum(slip => slip.AccountingAmount),
            slips
        };
    }

    private ReviewSlipPayload BuildSlipObject(ReviewSlip slip)
    {
        var table = _store.Tables.FirstOrDefault(row => row.TableId == slip.TableId);
        var customers = slip.Customers.Select(CustomerObject).ToList();
        var nominations = slip.Nominations.Select(NominationObject).ToList();
        var orders = slip.Orders.Select(OrderObject).ToList();
        var adjustments = slip.Adjustments.Select(AdjustmentObject).ToList();
        var pricingLines = slip.Status == "open" ? BuildPricingLines(slip).ToList() : [];
        var activeCustomers = slip.Customers.Where(customer => customer.Status == "active").ToList();
        var activeNominations = slip.Nominations.Where(nomination => nomination.Status != "cancelled").ToList();
        var activeOrders = slip.Orders.Where(order => order.Status == "active").ToList();
        var activeAdjustments = slip.Adjustments.Where(adjustment => adjustment.Status == "active").ToList();
        var orderSubtotal = activeOrders.Sum(order => order.Amount);
        var pricingSubtotal = pricingLines.Sum(line => line.Amount);
        var adjustmentAmount = activeAdjustments.Sum(adjustment => adjustment.Amount);
        var billableSubtotal = orderSubtotal + pricingSubtotal;
        var serviceCharge = Math.Round(billableSubtotal * 0.20m, MidpointRounding.AwayFromZero);
        var accountingAmount = Math.Max(0, billableSubtotal + serviceCharge + adjustmentAmount);
        return new ReviewSlipPayload
        {
            Id = slip.SlipId,
            SlipNo = slip.SlipNo,
            TableDisplay = TableDisplay(table),
            OpenedAt = slip.OpenedAt,
            OpenedTime = FormatTime(slip.OpenedAt),
            ClosedAt = slip.ClosedAt,
            Status = slip.Status,
            StatusDisplay = StatusDisplay(slip.Status),
            StatusBadgeClass = StatusBadgeClass(slip.Status),
            CustomerCount = activeCustomers.Count,
            CustomerNames = string.Join("、", activeCustomers.Select(CustomerDisplayName)),
            CastNames = activeNominations.Count == 0
                ? "指名なし"
                : string.Join("、", activeNominations.Select(nomination => CastName(nomination.CastId)).Distinct()),
            OrderCount = activeOrders.Count,
            OrderSubtotalAmount = orderSubtotal,
            PricingSubtotalAmount = pricingSubtotal,
            AdjustmentAmount = adjustmentAmount,
            Memo = string.IsNullOrWhiteSpace(slip.Memo) ? "-" : slip.Memo,
            AccountingAmount = accountingAmount,
            KaraokeQuantity = activeOrders.Where(order => order.ItemType == "karaoke").Sum(order => order.Quantity),
            Customers = customers,
            Nominations = nominations,
            Orders = orders,
            Adjustments = adjustments,
            PricingLines = pricingLines
        };
    }

    private IEnumerable<ReviewPricingLinePayload> BuildPricingLines(ReviewSlip slip)
    {
        if (!_store.PricingPlan.IsActive)
        {
            yield break;
        }

        var activeCustomerCount = Math.Max(1, slip.Customers.Count(customer => customer.Status == "active"));
        var unitPrice = activeCustomerCount == 1
            ? _store.PricingPlan.SetUnitPriceSingle
            : _store.PricingPlan.SetUnitPricePerCustomer;
        yield return new ReviewPricingLinePayload
        {
            PricingCode = "set",
            LineName = "セット料金",
            OccurredAt = slip.OpenedAt,
            Quantity = activeCustomerCount,
            UnitPrice = unitPrice,
            Amount = unitPrice * activeCustomerCount,
            CustomerCount = activeCustomerCount,
            Status = "active"
        };
    }

    private object BuildStatementPrintData(ReviewSlip slip)
    {
        var payload = BuildSlipObject(slip);
        var orders = payload.PricingLines.Select(line => new
            {
                item_name = line.LineName,
                item_type = "set_fee",
                quantity = line.Quantity,
                unit_price = line.UnitPrice,
                amount = line.Amount,
                customer_count = line.CustomerCount
            })
            .Concat(slip.Orders
                .Where(order => order.Status == "active")
                .Select(order => new
                {
                    item_name = order.ItemName,
                    item_type = order.ItemType,
                    quantity = order.Quantity,
                    unit_price = order.UnitPrice,
                    amount = order.Amount,
                    customer_count = 0
                }))
            .ToList();
        var adjustments = slip.Adjustments
            .Where(adjustment => adjustment.Status == "active")
            .Select(adjustment => new { line_name = adjustment.LineName, amount = adjustment.Amount })
            .ToList();
        var subtotal = payload.OrderSubtotalAmount + payload.PricingSubtotalAmount;
        var serviceCharge = Math.Round(subtotal * 0.20m, MidpointRounding.AwayFromZero);
        return new
        {
            table_display_name = payload.TableDisplay,
            business_date = _store.BusinessDate,
            opened_at = slip.OpenedAt,
            closed_at = slip.ClosedAt ?? DateTimeOffset.Now,
            customer_count = payload.CustomerCount,
            orders,
            adjustments,
            subtotal_amount = subtotal,
            service_charge_amount = serviceCharge,
            total_amount = payload.AccountingAmount,
            consumption_tax_amount = Math.Round(payload.AccountingAmount / 11m, MidpointRounding.AwayFromZero)
        };
    }

    private object BuildStatementReviewData(ReviewSlip slip) => new
    {
        orders = slip.Orders.Where(order => order.Status == "active").Select(OrderObject)
    };

    private object BuildReceiptPrintData(ReviewSlip slip, long checkoutId, IEnumerable<object> payments)
    {
        var payload = BuildSlipObject(slip);
        return new
        {
            checkoutId,
            issued_at = DateTimeOffset.Now,
            total_amount = payload.AccountingAmount,
            consumption_tax_amount = Math.Round(payload.AccountingAmount / 11m, MidpointRounding.AwayFromZero),
            particulars = "ご飲食代として",
            issuer = new
            {
                logo = "ProsperApp Review",
                store_name = _store.DepartmentName,
                address = "レビュー用サンプル住所",
                phone = "00-0000-0000",
                invoice_registration_number = "T0000000000000"
            },
            payments
        };
    }

    private object ClosingReadinessRow()
    {
        var openSlipCount = _store.Slips.Count(slip => slip.Status is "open" or "checkout_ready");
        var attendanceCount = _store.Attendance.Count;
        var missingClockOutCount = _store.Attendance.Count(attendance => string.IsNullOrWhiteSpace(attendance.ClockOutTime));
        var pendingReceiptCount = _store.PendingReceipts.Count;
        var blockReasons = new List<string>();
        if (openSlipCount > 0) blockReasons.Add($"未会計伝票が {openSlipCount} 件あります。");
        if (!_store.IsDrinkDeliveryAmountEntered) blockReasons.Add("酒代が未入力です。");
        if (attendanceCount == 0) blockReasons.Add("勤怠入力がありません。");
        if (missingClockOutCount > 0) blockReasons.Add($"退勤時刻が未入力のキャストが {missingClockOutCount} 名います。");
        if (pendingReceiptCount > 0) blockReasons.Add($"未入力領収書が {pendingReceiptCount} 件あります。");

        return new
        {
            open_slip_count = openSlipCount,
            drink_delivery_amount = _store.DrinkDeliveryAmount,
            is_drink_delivery_amount_entered = _store.IsDrinkDeliveryAmountEntered,
            attendance_count = attendanceCount,
            missing_clock_out_count = missingClockOutCount,
            cast_sales_required_slip_count = 1,
            cast_sales_completed_slip_count = 1,
            cast_sales_missing_slip_count = 0,
            pending_receipt_count = pendingReceiptCount,
            can_close = blockReasons.Count == 0,
            block_reasons = blockReasons,
            checked_at = DateTimeOffset.Now
        };
    }

    private object CastSalesAdjustmentStatusRow() => new
    {
        required_slip_count = 1,
        completed_slip_count = 1,
        missing_slip_count = 0
    };

    private object CastSalesAdjustmentOverviewRow() => new
    {
        status = CastSalesAdjustmentStatusRow(),
        slips = BuildCastSalesAdjustmentSlips(),
        details = _store.Slips
            .Where(slip => slip.Status == "checked_out" && slip.Nominations.Any(nomination => nomination.Status == "active"))
            .SelectMany(slip => BuildCastSalesAdjustmentDetail(slip.SlipId))
    };

    private IEnumerable<object> BuildCastSalesAdjustmentSlips()
    {
        return _store.Slips
            .Where(slip => slip.Status == "checked_out" && slip.Nominations.Any(nomination => nomination.Status == "active"))
            .Select(slip =>
            {
                var payload = BuildSlipObject(slip);
                return new
                {
                    slip_id = slip.SlipId,
                    slip_no = slip.SlipNo,
                    table_id = slip.TableId,
                    table_code = _store.Tables.First(table => table.TableId == slip.TableId).TableCode,
                    table_name = _store.Tables.First(table => table.TableId == slip.TableId).TableName,
                    customer_names = payload.CustomerNames,
                    checkout_at = slip.ClosedAt ?? DateTimeOffset.Now,
                    subtotal_amount = payload.OrderSubtotalAmount + payload.PricingSubtotalAmount,
                    service_charge_amount = Math.Round((payload.OrderSubtotalAmount + payload.PricingSubtotalAmount) * 0.20m, MidpointRounding.AwayFromZero),
                    total_amount = payload.AccountingAmount,
                    cast_names = payload.CastNames,
                    required_cast_count = slip.Nominations.Count(nomination => nomination.Status == "active"),
                    saved_cast_count = slip.Nominations.Count(nomination => nomination.Status == "active"),
                    adjusted_sales_amount_total = payload.AccountingAmount
                };
            })
            .ToList();
    }

    private IEnumerable<object> BuildCastSalesAdjustmentDetail(long slipId)
    {
        var slip = _store.Slips.FirstOrDefault(candidate => candidate.SlipId == slipId);
        if (slip is null)
        {
            return [];
        }

        var payload = BuildSlipObject(slip);
        return slip.Nominations
            .Where(nomination => nomination.Status == "active")
            .Select(nomination => new
            {
                slip_id = slip.SlipId,
                slip_no = slip.SlipNo,
                business_day_id = _store.BusinessDayId,
                business_date = _store.BusinessDate,
                table_code = _store.Tables.First(table => table.TableId == slip.TableId).TableCode,
                table_name = _store.Tables.First(table => table.TableId == slip.TableId).TableName,
                checkout_id = slip.CheckoutId ?? 0,
                checkout_at = slip.ClosedAt ?? DateTimeOffset.Now,
                subtotal_amount = payload.OrderSubtotalAmount + payload.PricingSubtotalAmount,
                service_charge_amount = Math.Round((payload.OrderSubtotalAmount + payload.PricingSubtotalAmount) * 0.20m, MidpointRounding.AwayFromZero),
                total_amount = payload.AccountingAmount,
                slip_cast_id = nomination.SlipCastId,
                cast_id = nomination.CastId,
                cast_display_name = CastName(nomination.CastId),
                cast_department_name = _store.DepartmentName,
                nomination_kind = nomination.NominationKind,
                nomination_type = nomination.NominationType,
                nomination_display_name = nomination.NominationDisplayName,
                started_at = nomination.StartedAt,
                sales_amount = Math.Round(payload.AccountingAmount / Math.Max(1, slip.Nominations.Count(row => row.Status == "active")), MidpointRounding.AwayFromZero),
                source_amount_type = "total",
                split_mode = "split",
                suggested_subtotal_sales_amount = Math.Round(payload.AccountingAmount / Math.Max(1, slip.Nominations.Count(row => row.Status == "active")), MidpointRounding.AwayFromZero),
                subtotal_suggestion_fallback_reason = (string?)null,
                suggested_total_sales_amount = Math.Round(payload.AccountingAmount / Math.Max(1, slip.Nominations.Count(row => row.Status == "active")), MidpointRounding.AwayFromZero),
                total_suggestion_fallback_reason = (string?)null
            })
            .ToList();
    }

    private object DepartmentRow(ReviewDepartment department) => new
    {
        company_id = department.CompanyId,
        department_id = department.DepartmentId,
        department_code = department.DepartmentCode,
        department_name = department.DepartmentName
    };

    private static object TableRow(ReviewTable table) => new
    {
        table_id = table.TableId,
        table_code = table.TableCode,
        table_name = table.TableName,
        table_category_no = table.TableCategoryNo
    };

    private object CastRow(ReviewCast cast) => new
    {
        cast_id = cast.CastId,
        cast_code = $"C{cast.CastId:000}",
        department_name = _store.DepartmentName,
        display_name = cast.DisplayName,
        drink_memo = cast.DrinkMemo,
        clock_in_time = _store.Attendance.FirstOrDefault(row => row.CastId == cast.CastId)?.ClockInTime
    };

    private object CastAdminRow(ReviewCast cast) => new
    {
        cast_id = cast.CastId,
        display_name = cast.DisplayName,
        drink_memo = cast.DrinkMemo,
        joined_on = cast.JoinedOn
    };

    private object OrderItemRow(ReviewItem item)
    {
        var category = _store.Categories.FirstOrDefault(row => row.ItemCategoryId == item.ItemCategoryId);
        return new
        {
            item_id = item.ItemId,
            item_name = item.ItemName,
            item_type = item.ItemType,
            default_price = item.DefaultPrice,
            category_code = category?.CategoryCode,
            category_name = category?.CategoryName ?? "未分類",
            is_cast_back_target = item.IsCastBackTarget,
            cast_back_regular_unit_amount = item.CastBackRegularUnitAmount,
            cast_back_nomination_unit_amount = item.CastBackNominationUnitAmount,
            cast_back_type = "drink"
        };
    }

    private IEnumerable<object> BuildItemAdminCatalogRows()
    {
        foreach (var category in _store.Categories)
        {
            yield return new
            {
                row_type = "category",
                item_category_id = category.ItemCategoryId,
                category_code = category.CategoryCode,
                category_name = category.CategoryName,
                sort_order = category.SortOrder,
                is_active = category.IsActive
            };
        }

        foreach (var item in _store.Items)
        {
            var category = _store.Categories.FirstOrDefault(row => row.ItemCategoryId == item.ItemCategoryId);
            yield return new
            {
                row_type = "item",
                item_id = item.ItemId,
                item_category_id = item.ItemCategoryId,
                category_code = category?.CategoryCode,
                category_name = category?.CategoryName,
                item_name = item.ItemName,
                item_type = item.ItemType,
                default_price = item.DefaultPrice,
                sort_order = item.SortOrder,
                is_active = item.IsActive,
                is_cast_back_target = item.IsCastBackTarget,
                cast_back_regular_unit_amount = item.CastBackRegularUnitAmount,
                cast_back_nomination_unit_amount = item.CastBackNominationUnitAmount,
                cast_back_type = "drink"
            };
        }
    }

    private static object NominationBackRow(ReviewNominationBack row) => new
    {
        nomination_kind = row.NominationKind,
        nomination_type = row.NominationType,
        display_name = row.DisplayName,
        companion_time = row.CompanionTime,
        back_type = "nomination",
        back_unit_amount = row.BackUnitAmount,
        sort_order = row.SortOrder,
        is_active = row.IsActive
    };

    private object PricingPlanRow() => new
    {
        set_minutes = _store.PricingPlan.SetMinutes,
        set_unit_price_single = _store.PricingPlan.SetUnitPriceSingle,
        set_unit_price_per_customer = _store.PricingPlan.SetUnitPricePerCustomer,
        extension_unit_price_single = _store.PricingPlan.ExtensionUnitPriceSingle,
        extension_unit_price_per_customer = _store.PricingPlan.ExtensionUnitPricePerCustomer,
        is_active = _store.PricingPlan.IsActive
    };

    private static object PaymentMethodRow(ReviewPaymentMethod row) => new
    {
        method_code = row.MethodCode,
        method_name = row.MethodName,
        requires_received_amount = row.RequiresReceivedAmount,
        sort_order = row.SortOrder
    };

    private object OrderEntrySlipRow(ReviewSlip slip)
    {
        var payload = BuildSlipObject(slip);
        var table = _store.Tables.FirstOrDefault(row => row.TableId == slip.TableId);
        return new
        {
            slip_id = slip.SlipId,
            table_id = slip.TableId,
            table_code = table?.TableCode,
            table_name = table?.TableName,
            opened_at = slip.OpenedAt,
            customer_count = payload.CustomerCount,
            customer_names = payload.CustomerNames,
            nomination_cast_ids = string.Join(",", slip.Nominations.Where(row => row.Status == "active").Select(row => row.CastId)),
            nomination_cast_names = payload.CastNames,
            memo = slip.Memo
        };
    }

    private object AttendingCastRow(ReviewAttendance attendance)
    {
        var cast = _store.Casts.First(row => row.CastId == attendance.CastId);
        return new
        {
            cast_id = cast.CastId,
            display_name = cast.DisplayName,
            drink_memo = cast.DrinkMemo,
            department_name = _store.DepartmentName,
            clock_in_time = attendance.ClockInTime
        };
    }

    private object ClosingAttendanceRow(ReviewAttendance attendance)
    {
        var cast = _store.Casts.First(row => row.CastId == attendance.CastId);
        return new
        {
            attendance_id = attendance.AttendanceId,
            cast_id = cast.CastId,
            cast_display_name = cast.DisplayName,
            cast_department_name = _store.DepartmentName,
            attendance_status = "active",
            clock_in_at = ComposeTime(attendance.ClockInTime),
            clock_out_at = string.IsNullOrWhiteSpace(attendance.ClockOutTime) ? null : ComposeTime(attendance.ClockOutTime),
            uses_send_service = attendance.UsesSendService
        };
    }

    private static object PendingReceiptRow(ReviewReceipt row) => new
    {
        document_id = row.DocumentId,
        file_name = row.FileName,
        drive_url = $"/DrivePreview/{row.DriveFileId}",
        storage_path = (string?)null,
        drive_file_id = row.DriveFileId,
        document_date = row.PaymentDate,
        amount = row.Amount
    };

    private object CustomerObject(ReviewCustomer customer) => new
    {
        id = customer.CustomerId,
        lineNo = customer.LineNo,
        displayName = CustomerDisplayName(customer),
        customerLabel = customer.Label,
        enteredAt = customer.EnteredAt,
        enteredTime = FormatTime(customer.EnteredAt),
        leftAt = customer.LeftAt,
        leftTime = customer.LeftAt is null ? null : FormatTime(customer.LeftAt.Value),
        status = customer.Status
    };

    private object NominationObject(ReviewNomination nomination) => new
    {
        id = nomination.SlipCastId,
        castId = nomination.CastId,
        displayName = CastName(nomination.CastId),
        departmentName = _store.DepartmentName,
        nominationKind = nomination.NominationKind,
        nominationType = nomination.NominationType,
        nominationDisplayName = nomination.NominationDisplayName,
        nominationPrice = nomination.NominationPrice,
        startedAt = nomination.StartedAt,
        startedTime = FormatTime(nomination.StartedAt),
        status = nomination.Status
    };

    private object OrderObject(ReviewOrder order) => new
    {
        id = order.OrderLineId,
        lineNo = order.LineNo,
        itemName = order.ItemName,
        itemType = order.ItemType,
        quantity = order.Quantity,
        unitPrice = order.UnitPrice,
        amount = order.Amount,
        orderedAt = order.OrderedAt,
        orderedTime = FormatTime(order.OrderedAt),
        status = order.Status,
        sourceType = order.SourceType,
        sourceId = order.SourceId,
        backCastId = order.BackCastId,
        backCastDisplayName = order.BackCastDisplayName,
        backCastDepartmentName = order.BackCastId is null ? null : _store.DepartmentName,
        isDynamicPricing = false
    };

    private static object AdjustmentObject(ReviewAdjustment adjustment) => new
    {
        id = adjustment.ChargeLineId,
        lineNo = adjustment.LineNo,
        lineName = adjustment.LineName,
        amount = adjustment.Amount,
        createdAt = adjustment.CreatedAt,
        createdTime = FormatTime(adjustment.CreatedAt),
        status = adjustment.Status
    };

    private string CastName(long castId) =>
        _store.Casts.FirstOrDefault(cast => cast.CastId == castId)?.DisplayName ?? "レビューキャスト";

    private static string CustomerDisplayName(ReviewCustomer customer) =>
        string.IsNullOrWhiteSpace(customer.Label) ? $"ご新規様{customer.LineNo}" : customer.Label;

    private static string TableDisplay(ReviewTable? table) =>
        table is null
            ? "-"
            : string.IsNullOrWhiteSpace(table.TableName)
                ? table.TableCode
                : $"{table.TableCode} {table.TableName}";

    private static string StatusDisplay(string status) => status switch
    {
        "open" => "在席",
        "checkout_ready" => "会計準備中",
        "checked_out" => "会計済み",
        "cancelled" => "取消",
        _ => status
    };

    private static string StatusBadgeClass(string status) => status switch
    {
        "open" => "text-bg-success",
        "checkout_ready" => "text-bg-warning",
        "checked_out" => "text-bg-secondary",
        "cancelled" => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    private static string FormatTime(DateTimeOffset value) =>
        value.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ComposeTime(string? value)
    {
        if (!TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var time))
        {
            return null;
        }

        var date = DateOnly.FromDateTime(DateTime.Now);
        return new DateTimeOffset(date.ToDateTime(time), DateTimeOffset.Now.Offset);
    }

    private static SupabaseRpcResult Rows(IEnumerable<object> rows)
    {
        var body = JsonSerializer.Serialize(rows, JsonOptions);
        return SupabaseRpcResult.Success(body) with { Rows = ParseRows(body) };
    }

    private static SupabaseRpcResult Rows(params object[] rows) => Rows((IEnumerable<object>)rows);

    private static SupabaseRpcResult Scalar(object? value)
    {
        var body = JsonSerializer.Serialize(value, JsonOptions);
        return SupabaseRpcResult.Success(body);
    }

    private static SupabaseRpcResult Failed(string message) => SupabaseRpcResult.Failed(message);

    private static IReadOnlyList<JsonElement> ParseRows(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().Select(row => row.Clone()).ToList()
            : [document.RootElement.Clone()];
    }

    private static JsonElement ReadObject(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : EmptyObject();
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().Select(row => row.Clone()).ToList();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static DateOnly? ReadDateOnly(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private sealed class ReviewStore
    {
        public long CompanyId { get; init; } = 1;
        public long DepartmentId { get; init; } = 1;
        public string DepartmentName { get; init; } = "レビュー店舗";
        public long BusinessDayId { get; init; } = 1001;
        public DateOnly BusinessDate { get; set; }
        public DateTimeOffset OpenedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string BusinessDayStatus { get; set; } = "open";
        public long Revision { get; private set; } = 1;
        public decimal DrinkDeliveryAmount { get; set; } = 0;
        public bool IsDrinkDeliveryAmountEntered { get; set; }
        public List<ReviewDepartment> Departments { get; } = [];
        public List<ReviewTable> Tables { get; } = [];
        public List<ReviewCast> Casts { get; } = [];
        public List<ReviewCategory> Categories { get; } = [];
        public List<ReviewItem> Items { get; } = [];
        public List<ReviewNominationBack> NominationBacks { get; } = [];
        public List<ReviewPaymentMethod> PaymentMethods { get; } = [];
        public List<ReviewAttendance> Attendance { get; } = [];
        public List<ReviewSlip> Slips { get; } = [];
        public List<ReviewReceipt> PendingReceipts { get; } = [];
        public long NextCastId { get; set; } = 10;
        public long NextCategoryId { get; set; } = 10;
        public long NextItemId { get; set; } = 20;
        public long NextSlipId { get; set; } = 200;
        public long NextSlipNo { get; set; } = 200;
        public long NextCustomerId { get; set; } = 1000;
        public long NextSlipCastId { get; set; } = 2000;
        public long NextOrderLineId { get; set; } = 3000;
        public long NextChargeLineId { get; set; } = 4000;
        public long NextAttendanceId { get; set; } = 5000;
        public long NextCheckoutId { get; set; } = 6000;
        public ReviewPricingPlan PricingPlan { get; } = new();

        public void Touch() => Revision++;

        public static ReviewStore Create()
        {
            var now = DateTimeOffset.Now;
            var businessDate = DateOnly.FromDateTime(now.DateTime);
            var store = new ReviewStore
            {
                BusinessDate = businessDate,
                OpenedAt = now.AddHours(-4),
                IsDrinkDeliveryAmountEntered = false
            };
            store.Departments.Add(new ReviewDepartment(1, 1, "review", "レビュー店舗"));
            store.Tables.AddRange([
                new ReviewTable(1, "A1", "メイン", 1),
                new ReviewTable(2, "A2", "メイン", 1),
                new ReviewTable(3, "B1", "個室", 2),
                new ReviewTable(4, "C1", "カウンター", 3)
            ]);
            store.Casts.AddRange([
                new ReviewCast { CastId = 1, DisplayName = "凛", DrinkMemo = "レモン少なめ", JoinedOn = businessDate.AddDays(-120), IsActive = true },
                new ReviewCast { CastId = 2, DisplayName = "美咲", DrinkMemo = "ソーダ", JoinedOn = businessDate.AddDays(-80), IsActive = true },
                new ReviewCast { CastId = 3, DisplayName = "葵", DrinkMemo = "烏龍茶", JoinedOn = businessDate.AddDays(-30), IsActive = true }
            ]);
            store.Categories.AddRange([
                new ReviewCategory { ItemCategoryId = 1, CategoryCode = "drink", CategoryName = "ドリンク", SortOrder = 10, IsActive = true },
                new ReviewCategory { ItemCategoryId = 2, CategoryCode = "food", CategoryName = "フード", SortOrder = 20, IsActive = true },
                new ReviewCategory { ItemCategoryId = 3, CategoryCode = "system", CategoryName = "システム", SortOrder = 99, IsActive = true }
            ]);
            store.Items.AddRange([
                new ReviewItem { ItemId = 1, ItemCategoryId = 1, ItemName = "グラスドリンク", ItemType = "standard", DefaultPrice = 1000, SortOrder = 10, IsActive = true, IsCastBackTarget = true, CastBackRegularUnitAmount = 200, CastBackNominationUnitAmount = 500 },
                new ReviewItem { ItemId = 2, ItemCategoryId = 1, ItemName = "ボトル", ItemType = "standard", DefaultPrice = 12000, SortOrder = 20, IsActive = true, IsCastBackTarget = true, CastBackRegularUnitAmount = 1000, CastBackNominationUnitAmount = 2000 },
                new ReviewItem { ItemId = 3, ItemCategoryId = 2, ItemName = "フード", ItemType = "standard", DefaultPrice = 1800, SortOrder = 30, IsActive = true, IsCastBackTarget = false },
                new ReviewItem { ItemId = 4, ItemCategoryId = 3, ItemName = "カラオケ", ItemType = "karaoke", DefaultPrice = 200, SortOrder = 90, IsActive = true, IsCastBackTarget = false }
            ]);
            store.NominationBacks.AddRange([
                new ReviewNominationBack { NominationKind = "nomination", NominationType = "nomination", DisplayName = "本指名", BackUnitAmount = 1500, SortOrder = 10, IsActive = true },
                new ReviewNominationBack { NominationKind = "in_store", NominationType = "in_store", DisplayName = "場内指名", BackUnitAmount = 1000, SortOrder = 20, IsActive = true },
                new ReviewNominationBack { NominationKind = "companion_20", NominationType = "companion", DisplayName = "同伴20:00", CompanionTime = "20:00", BackUnitAmount = 2500, SortOrder = 30, IsActive = true }
            ]);
            store.PaymentMethods.AddRange([
                new ReviewPaymentMethod("cash", "現金", true, 10),
                new ReviewPaymentMethod("card", "カード", false, 20),
                new ReviewPaymentMethod("paypay", "PayPay", false, 30)
            ]);
            store.Attendance.AddRange([
                new ReviewAttendance { AttendanceId = 1, CastId = 1, ClockInTime = "20:00" },
                new ReviewAttendance { AttendanceId = 2, CastId = 2, ClockInTime = "20:15" }
            ]);
            SeedSlips(store, now);
            store.PendingReceipts.Add(new ReviewReceipt("review-receipt-001", "review-receipt-001.html", "review-receipt-001", businessDate, 8800));
            return store;
        }

        private static void SeedSlips(ReviewStore store, DateTimeOffset now)
        {
            var open = new ReviewSlip
            {
                SlipId = 101,
                SlipNo = "R-101",
                TableId = 1,
                OpenedAt = now.AddHours(-3),
                Status = "open",
                Memo = "レビュー用サンプル伝票"
            };
            open.Customers.AddRange([
                new ReviewCustomer { CustomerId = 1011, LineNo = 1, Label = "田中様", EnteredAt = open.OpenedAt, Status = "active" },
                new ReviewCustomer { CustomerId = 1012, LineNo = 2, Label = null, EnteredAt = open.OpenedAt.AddMinutes(10), Status = "active" }
            ]);
            open.Nominations.Add(new ReviewNomination { SlipCastId = 2011, CastId = 1, NominationKind = "nomination", NominationType = "nomination", NominationDisplayName = "本指名", NominationPrice = 3000, StartedAt = open.OpenedAt, Status = "active" });
            open.Orders.AddRange([
                new ReviewOrder { OrderLineId = 3011, LineNo = 1, ItemId = 1, ItemName = "グラスドリンク", ItemType = "standard", Quantity = 3, UnitPrice = 1000, OrderedAt = open.OpenedAt.AddMinutes(5), Status = "active", BackCastId = 1, BackCastDisplayName = "凛" },
                new ReviewOrder { OrderLineId = 3012, LineNo = 2, ItemId = 3, ItemName = "フード", ItemType = "standard", Quantity = 1, UnitPrice = 1800, OrderedAt = open.OpenedAt.AddMinutes(30), Status = "active" },
                new ReviewOrder { OrderLineId = 3013, LineNo = 3, ItemName = "指名料金", ItemType = "nomination_fee", Quantity = 1, UnitPrice = 3000, OrderedAt = open.OpenedAt, Status = "active", SourceType = "nomination_fee", SourceId = 2011 }
            ]);
            open.Adjustments.Add(new ReviewAdjustment { ChargeLineId = 4011, LineNo = 1, LineName = "レビュー割引", Amount = -500, CreatedAt = open.OpenedAt.AddHours(1), Status = "active" });

            var ready = new ReviewSlip
            {
                SlipId = 102,
                SlipNo = "R-102",
                TableId = 2,
                OpenedAt = now.AddHours(-2.5),
                ClosedAt = now.AddMinutes(-10),
                Status = "checkout_ready",
                Memo = "会計準備中サンプル"
            };
            ready.Customers.Add(new ReviewCustomer { CustomerId = 1021, LineNo = 1, Label = "佐藤様", EnteredAt = ready.OpenedAt, Status = "active" });
            ready.Orders.Add(new ReviewOrder { OrderLineId = 3021, LineNo = 1, ItemId = 2, ItemName = "ボトル", ItemType = "standard", Quantity = 1, UnitPrice = 12000, OrderedAt = ready.OpenedAt.AddMinutes(20), Status = "active" });

            var paid = new ReviewSlip
            {
                SlipId = 103,
                SlipNo = "R-103",
                TableId = 3,
                OpenedAt = now.AddHours(-5),
                ClosedAt = now.AddHours(-1),
                Status = "checked_out",
                CheckoutId = 6103,
                Memo = "会計済みサンプル"
            };
            paid.Customers.Add(new ReviewCustomer { CustomerId = 1031, LineNo = 1, Label = "山田様", EnteredAt = paid.OpenedAt, LeftAt = paid.ClosedAt, Status = "left" });
            paid.Nominations.Add(new ReviewNomination { SlipCastId = 2031, CastId = 2, NominationKind = "in_store", NominationType = "in_store", NominationDisplayName = "場内指名", NominationPrice = 2000, StartedAt = paid.OpenedAt.AddMinutes(20), Status = "active" });
            paid.Orders.AddRange([
                new ReviewOrder { OrderLineId = 3031, LineNo = 1, ItemId = 1, ItemName = "グラスドリンク", ItemType = "standard", Quantity = 2, UnitPrice = 1000, OrderedAt = paid.OpenedAt.AddMinutes(15), Status = "active", BackCastId = 2, BackCastDisplayName = "美咲" },
                new ReviewOrder { OrderLineId = 3032, LineNo = 2, ItemName = "場内指名料", ItemType = "nomination_fee", Quantity = 1, UnitPrice = 2000, OrderedAt = paid.OpenedAt.AddMinutes(20), Status = "active", SourceType = "nomination_fee", SourceId = 2031 }
            ]);

            store.Slips.AddRange([open, ready, paid]);
        }
    }

    private sealed record ReviewDepartment(long CompanyId, long DepartmentId, string DepartmentCode, string DepartmentName);
    private sealed record ReviewTable(long TableId, string TableCode, string? TableName, int TableCategoryNo);
    private sealed record ReviewPaymentMethod(string MethodCode, string MethodName, bool RequiresReceivedAmount, int SortOrder);
    private sealed record ReviewReceipt(string DocumentId, string FileName, string DriveFileId, DateOnly PaymentDate, decimal Amount);

    private sealed class ReviewCast
    {
        public long CastId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string? DrinkMemo { get; set; }
        public DateOnly JoinedOn { get; init; }
        public bool IsActive { get; set; }
    }

    private sealed class ReviewCategory
    {
        public long ItemCategoryId { get; init; }
        public string CategoryCode { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class ReviewItem
    {
        public long ItemId { get; init; }
        public long ItemCategoryId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string ItemType { get; init; } = "standard";
        public decimal DefaultPrice { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public bool IsCastBackTarget { get; set; }
        public decimal CastBackRegularUnitAmount { get; set; }
        public decimal CastBackNominationUnitAmount { get; set; }
    }

    private sealed class ReviewNominationBack
    {
        public string NominationKind { get; init; } = string.Empty;
        public string NominationType { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? CompanionTime { get; set; }
        public decimal BackUnitAmount { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class ReviewPricingPlan
    {
        public int SetMinutes { get; set; } = 60;
        public decimal SetUnitPriceSingle { get; set; } = 5000;
        public decimal SetUnitPricePerCustomer { get; set; } = 4000;
        public decimal ExtensionUnitPriceSingle { get; set; } = 3000;
        public decimal ExtensionUnitPricePerCustomer { get; set; } = 2500;
        public bool IsActive { get; set; } = true;
        public int PlanVersion { get; set; } = 1;
    }

    private sealed class ReviewAttendance
    {
        public long AttendanceId { get; init; }
        public long CastId { get; init; }
        public string ClockInTime { get; set; } = "20:00";
        public string? ClockOutTime { get; set; }
        public bool UsesSendService { get; set; }
    }

    private sealed class ReviewSlip
    {
        public long SlipId { get; init; }
        public string SlipNo { get; init; } = string.Empty;
        public long TableId { get; init; }
        public DateTimeOffset OpenedAt { get; init; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string Status { get; set; } = "open";
        public string? Memo { get; init; }
        public long? CheckoutId { get; set; }
        public List<ReviewCustomer> Customers { get; } = [];
        public List<ReviewNomination> Nominations { get; } = [];
        public List<ReviewOrder> Orders { get; } = [];
        public List<ReviewAdjustment> Adjustments { get; } = [];
    }

    private sealed class ReviewCustomer
    {
        public long CustomerId { get; init; }
        public int LineNo { get; init; }
        public string? Label { get; set; }
        public DateTimeOffset EnteredAt { get; init; }
        public DateTimeOffset? LeftAt { get; set; }
        public string Status { get; set; } = "active";
    }

    private sealed class ReviewNomination
    {
        public long SlipCastId { get; init; }
        public long CastId { get; init; }
        public string NominationKind { get; init; } = string.Empty;
        public string NominationType { get; init; } = string.Empty;
        public string NominationDisplayName { get; init; } = string.Empty;
        public decimal NominationPrice { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public string Status { get; set; } = "active";
    }

    private sealed class ReviewOrder
    {
        public long OrderLineId { get; init; }
        public int LineNo { get; init; }
        public long? ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public string ItemType { get; init; } = "standard";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; init; }
        public decimal Amount => UnitPrice * Quantity;
        public DateTimeOffset OrderedAt { get; init; }
        public string Status { get; set; } = "active";
        public string? SourceType { get; init; }
        public long? SourceId { get; init; }
        public long? BackCastId { get; init; }
        public string? BackCastDisplayName { get; init; }
    }

    private sealed class ReviewAdjustment
    {
        public long ChargeLineId { get; init; }
        public int LineNo { get; init; }
        public string LineName { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public string Status { get; set; } = "active";
    }

    private sealed class ReviewSlipPayload
    {
        public long Id { get; init; }
        public string SlipNo { get; init; } = string.Empty;
        public string TableDisplay { get; init; } = string.Empty;
        public DateTimeOffset OpenedAt { get; init; }
        public string OpenedTime { get; init; } = string.Empty;
        public DateTimeOffset? ClosedAt { get; init; }
        public string Status { get; init; } = string.Empty;
        public string StatusDisplay { get; init; } = string.Empty;
        public string StatusBadgeClass { get; init; } = string.Empty;
        public int CustomerCount { get; init; }
        public string CustomerNames { get; init; } = string.Empty;
        public string CastNames { get; init; } = string.Empty;
        public int OrderCount { get; init; }
        public decimal OrderSubtotalAmount { get; init; }
        public decimal PricingSubtotalAmount { get; init; }
        public decimal AdjustmentAmount { get; init; }
        public string Memo { get; init; } = string.Empty;
        public decimal AccountingAmount { get; init; }
        public int KaraokeQuantity { get; init; }
        public IReadOnlyList<object> Customers { get; init; } = [];
        public IReadOnlyList<object> Nominations { get; init; } = [];
        public IReadOnlyList<object> Orders { get; init; } = [];
        public IReadOnlyList<object> Adjustments { get; init; } = [];
        public IReadOnlyList<ReviewPricingLinePayload> PricingLines { get; init; } = [];
    }

    private sealed class ReviewPricingLinePayload
    {
        public string PricingCode { get; init; } = string.Empty;
        public string LineName { get; init; } = string.Empty;
        public DateTimeOffset OccurredAt { get; init; }
        public int Quantity { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal Amount { get; init; }
        public int CustomerCount { get; init; }
        public string Status { get; init; } = "active";
    }
}
