using System.Text.Json;
using ProsperApp.Features.Shared;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public sealed class SupabaseCastSalesAdjustmentRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), ICastSalesAdjustmentRepository
{
    public async Task<Result<CastSalesAdjustmentOverview>> GetCurrentOverviewAsync(CancellationToken ct)
    {
        var result = await PostRpcArrayResultAsync(
            "store.get_current_cast_sales_adjustment_overview",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);
        if (!result.Succeeded)
        {
            return Result<CastSalesAdjustmentOverview>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "キャスト売上額調整を取得できませんでした。");
        }

        if (result.Value.Count != 1)
        {
            return Result<CastSalesAdjustmentOverview>.Failure(
                ResultFailureKind.InvalidResponse,
                "キャスト売上額調整の応答形式が正しくありません。");
        }

        return ParseOverview(result.Value[0]) is { } overview
            ? Result<CastSalesAdjustmentOverview>.Success(overview)
            : Result<CastSalesAdjustmentOverview>.Failure(
                ResultFailureKind.InvalidResponse,
                "キャスト売上額調整の応答形式が正しくありません。");
    }

    public Task<Result<CastSalesAdjustmentMutationResult>> SaveCurrentAsync(
        SaveCurrentCastSalesAdjustmentMutation mutation,
        CancellationToken ct)
    {
        return ExecuteMutationAsync(
            "store.save_current_cast_sales_adjustment_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = mutation.OperationId,
                p_expected_business_day_id = mutation.ExpectedBusinessDayId,
                p_expected_business_day_revision = mutation.ExpectedBusinessDayRevision,
                p_slip_id = mutation.SlipId,
                p_expected_slip_version = mutation.ExpectedSlipVersion,
                p_expected_checkout_id = mutation.ExpectedCheckoutId,
                p_expected_checkout_version = mutation.ExpectedCheckoutVersion,
                p_adjustments = mutation.Adjustments.Select(ToPayload).ToArray(),
                p_source_amount_type = NormalizeSourceAmountType(mutation.SourceAmountType),
                p_split_mode = NormalizeSplitMode(mutation.SplitMode)
            },
            ct);
    }

    public Task<Result<CastSalesAdjustmentMutationResult>> ConfirmCurrentAsync(
        ConfirmCurrentCastSalesAdjustmentsMutation mutation,
        CancellationToken ct)
    {
        return ExecuteMutationAsync(
            "store.confirm_current_cast_sales_adjustments_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = mutation.OperationId,
                p_expected_business_day_id = mutation.ExpectedBusinessDayId,
                p_expected_business_day_revision = mutation.ExpectedBusinessDayRevision,
                p_slips = mutation.Slips.Select(slip => new
                {
                    slip_id = slip.SlipId,
                    expected_slip_version = slip.ExpectedSlipVersion,
                    expected_checkout_id = slip.ExpectedCheckoutId,
                    expected_checkout_version = slip.ExpectedCheckoutVersion,
                    source_amount_type = NormalizeSourceAmountType(slip.SourceAmountType),
                    split_mode = NormalizeSplitMode(slip.SplitMode),
                    adjustments = slip.Adjustments.Select(ToPayload).ToArray()
                }).ToArray()
            },
            ct);
    }

    private async Task<Result<CastSalesAdjustmentMutationResult>> ExecuteMutationAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct)
    {
        var result = await PostRpcArrayResultAsync(functionName, payload, ct);
        if (!result.Succeeded)
        {
            return Result<CastSalesAdjustmentMutationResult>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                ToFriendlyError(result.ErrorMessage));
        }

        if (result.Value.Count != 1)
        {
            return Result<CastSalesAdjustmentMutationResult>.Failure(
                ResultFailureKind.InvalidResponse,
                "キャスト売上額調整の保存応答が正しくありません。");
        }

        var row = result.Value[0];
        var status = ReadString(row, "status");
        var operationId = ReadString(row, "operation_id");
        if (string.IsNullOrWhiteSpace(status) || string.IsNullOrWhiteSpace(operationId))
        {
            return Result<CastSalesAdjustmentMutationResult>.Failure(
                ResultFailureKind.InvalidResponse,
                "キャスト売上額調整の保存応答が正しくありません。");
        }

        var detail = TryReadJson(row, "detail", JsonValueKind.Array, out var detailRows)
            ? ParseDetails(detailRows.EnumerateArray()).FirstOrDefault()
            : null;
        var latestOverview = TryReadJson(row, "latest_overview", JsonValueKind.Object, out var overviewElement)
            ? ParseOverview(overviewElement)
            : null;

        return Result<CastSalesAdjustmentMutationResult>.Success(new CastSalesAdjustmentMutationResult
        {
            Status = status,
            OperationId = operationId,
            Message = ToFriendlyStatusMessage(ReadString(row, "message")),
            BusinessDayId = ReadLong(row, "business_day_id"),
            BusinessDayRevision = ReadLong(row, "business_day_revision") ?? 0,
            SavedAdjustments = TryReadJson(row, "saved_adjustments", JsonValueKind.Array, out var savedRows)
                ? ParseSavedAdjustments(savedRows.EnumerateArray())
                : [],
            Detail = detail,
            OverviewStatus = TryReadJson(row, "overview_status", JsonValueKind.Object, out var statusElement)
                ? ParseStatus(statusElement)
                : null,
            DashboardDelta = TryReadJson(row, "dashboard_delta", JsonValueKind.Object, out var deltaElement)
                ? ParseDashboardDelta(deltaElement)
                : null,
            LatestOverview = latestOverview
        });
    }

    private static object ToPayload(CastSalesAdjustmentValue value) => new
    {
        slip_cast_id = value.SlipCastId,
        sales_amount = value.SalesAmount
    };

    private static CastSalesAdjustmentOverview? ParseOverview(JsonElement row)
    {
        if (!TryReadJson(row, "status", JsonValueKind.Object, out var statusElement) ||
            !TryReadJson(row, "slips", JsonValueKind.Array, out var slipsElement) ||
            !TryReadJson(row, "details", JsonValueKind.Array, out var detailsElement))
        {
            return null;
        }

        return new CastSalesAdjustmentOverview
        {
            DepartmentId = ReadLong(row, "department_id") ?? 0,
            HasBusinessDay = ReadBool(row, "has_business_day") ?? false,
            BusinessDayId = ReadLong(row, "business_day_id"),
            BusinessDayRevision = ReadLong(row, "business_day_revision") ?? 0,
            BusinessDate = ReadDateOnly(row, "business_date"),
            SourceAmountType = NormalizeSourceAmountType(ReadString(row, "source_amount_type")),
            SplitMode = NormalizeSplitMode(ReadString(row, "split_mode")),
            Status = ParseStatus(statusElement),
            Slips = ParseSlips(slipsElement.EnumerateArray()),
            Details = ParseDetails(detailsElement.EnumerateArray()),
            CheckedAt = ReadDateTimeOffset(row, "checked_at") ?? DateTimeOffset.MinValue
        };
    }

    private static CastSalesAdjustmentStatus ParseStatus(JsonElement row) => new()
    {
        RequiredSlipCount = (int)(ReadLong(row, "required_slip_count") ?? 0),
        CompletedSlipCount = (int)(ReadLong(row, "completed_slip_count") ?? 0),
        MissingSlipCount = (int)(ReadLong(row, "missing_slip_count") ?? 0)
    };

    private static IReadOnlyList<CastSalesAdjustmentSlip> ParseSlips(IEnumerable<JsonElement> rows)
    {
        return rows.Select(row => new CastSalesAdjustmentSlip
            {
                SlipId = ReadLong(row, "slip_id") ?? 0,
                TableId = ReadLong(row, "table_id"),
                TableCode = ReadString(row, "table_code"),
                TableName = ReadString(row, "table_name"),
                CustomerNames = ReadString(row, "customer_names"),
                CheckoutId = ReadLong(row, "checkout_id") ?? 0,
                CheckoutAt = ReadDateTimeOffset(row, "checkout_at") ?? DateTimeOffset.MinValue,
                SlipVersion = ReadDateTimeOffset(row, "slip_version") ?? DateTimeOffset.MinValue,
                CheckoutVersion = ReadDateTimeOffset(row, "checkout_version") ?? DateTimeOffset.MinValue,
                SubtotalAmount = ReadDecimal(row, "subtotal_amount") ?? 0,
                ServiceChargeAmount = ReadDecimal(row, "service_charge_amount") ?? 0,
                TotalAmount = ReadDecimal(row, "total_amount") ?? 0,
                CastNames = NormalizeCastDisplayNameList(ReadString(row, "cast_names")),
                RequiredCastCount = (int)(ReadLong(row, "required_cast_count") ?? 0),
                SavedCastCount = (int)(ReadLong(row, "saved_cast_count") ?? 0),
                AdjustedSalesAmountTotal = ReadDecimal(row, "adjusted_sales_amount_total") ?? 0
            })
            .Where(row => row.SlipId > 0 && row.CheckoutId > 0 && row.RequiredCastCount > 0)
            .ToList();
    }

    private static IReadOnlyList<CastSalesAdjustmentDetail> ParseDetails(IEnumerable<JsonElement> rows)
    {
        return rows
            .GroupBy(row => ReadLong(row, "slip_id") ?? 0)
            .Where(group => group.Key > 0)
            .Select(ParseDetail)
            .Where(detail => detail is not null)
            .Select(detail => detail!)
            .ToList();
    }

    private static CastSalesAdjustmentDetail? ParseDetail(IEnumerable<JsonElement> rows)
    {
        var values = rows.ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        var first = values[0];
        var casts = values.Select(row => new CastSalesAdjustmentCastRow
            {
                SlipCastId = ReadLong(row, "slip_cast_id") ?? 0,
                CastId = ReadLong(row, "cast_id") ?? 0,
                DisplayName = ReadString(row, "cast_display_name") ?? string.Empty,
                DepartmentName = ReadString(row, "cast_department_name"),
                NominationKind = ReadString(row, "nomination_kind") ?? string.Empty,
                NominationType = ReadString(row, "nomination_type") ?? string.Empty,
                NominationDisplayNameFromMaster = ReadString(row, "nomination_display_name"),
                StartedAt = ReadDateTimeOffset(row, "started_at"),
                SalesAmount = ReadDecimal(row, "sales_amount"),
                SourceAmountType = ReadString(row, "source_amount_type"),
                SplitMode = ReadString(row, "split_mode"),
                SuggestedSubtotalSalesAmount = ReadDecimal(row, "suggested_subtotal_sales_amount"),
                SubtotalSuggestionFallbackReason = ReadString(row, "subtotal_suggestion_fallback_reason"),
                SuggestedTotalSalesAmount = ReadDecimal(row, "suggested_total_sales_amount"),
                TotalSuggestionFallbackReason = ReadString(row, "total_suggestion_fallback_reason")
            })
            .Where(row => row.SlipCastId > 0)
            .ToList();
        if (casts.Count == 0)
        {
            return null;
        }

        return new CastSalesAdjustmentDetail
        {
            SlipId = ReadLong(first, "slip_id") ?? 0,
            BusinessDayId = ReadLong(first, "business_day_id") ?? 0,
            BusinessDate = ReadDateOnly(first, "business_date") ?? default,
            TableCode = ReadString(first, "table_code"),
            TableName = ReadString(first, "table_name"),
            CheckoutId = ReadLong(first, "checkout_id") ?? 0,
            CheckoutAt = ReadDateTimeOffset(first, "checkout_at") ?? DateTimeOffset.MinValue,
            SlipVersion = ReadDateTimeOffset(first, "slip_version") ?? DateTimeOffset.MinValue,
            CheckoutVersion = ReadDateTimeOffset(first, "checkout_version") ?? DateTimeOffset.MinValue,
            SubtotalAmount = ReadDecimal(first, "subtotal_amount") ?? 0,
            ServiceChargeAmount = ReadDecimal(first, "service_charge_amount") ?? 0,
            TotalAmount = ReadDecimal(first, "total_amount") ?? 0,
            Casts = casts
        };
    }

    private static IReadOnlyList<CastSalesAdjustmentSavedRow> ParseSavedAdjustments(IEnumerable<JsonElement> rows)
    {
        return rows.Select(row => new CastSalesAdjustmentSavedRow
            {
                AdjustmentId = ReadLong(row, "adjustment_id") ?? 0,
                SlipId = ReadLong(row, "slip_id") ?? 0,
                CheckoutId = ReadLong(row, "checkout_id") ?? 0,
                SlipCastId = ReadLong(row, "slip_cast_id") ?? 0,
                CastId = ReadLong(row, "cast_id") ?? 0,
                SalesAmount = ReadDecimal(row, "sales_amount") ?? 0,
                SourceAmountType = ReadString(row, "source_amount_type") ?? string.Empty,
                SplitMode = ReadString(row, "split_mode") ?? string.Empty,
                UpdatedAt = ReadDateTimeOffset(row, "updated_at") ?? DateTimeOffset.MinValue
            })
            .Where(row => row.AdjustmentId > 0)
            .ToList();
    }

    private static CastSalesAdjustmentDashboardDelta ParseDashboardDelta(JsonElement row) => new()
    {
        DepartmentId = ReadLong(row, "department_id") ?? 0,
        BusinessDayId = ReadLong(row, "business_day_id") ?? 0,
        BusinessDayRevision = ReadLong(row, "business_day_revision") ?? 0,
        CastSalesRequiredSlipCount = (int)(ReadLong(row, "cast_sales_required_slip_count") ?? 0),
        CastSalesCompletedSlipCount = (int)(ReadLong(row, "cast_sales_completed_slip_count") ?? 0),
        CastSalesMissingSlipCount = (int)(ReadLong(row, "cast_sales_missing_slip_count") ?? 0),
        CheckedAt = ReadDateTimeOffset(row, "checked_at") ?? DateTimeOffset.MinValue
    };

    private static bool TryReadJson(
        JsonElement row,
        string propertyName,
        JsonValueKind expectedKind,
        out JsonElement value)
    {
        if (row.TryGetProperty(propertyName, out value) && value.ValueKind == expectedKind)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string NormalizeSourceAmountType(string? value) =>
        value is LocalSettings.CastSalesAmountBasisSubtotal
            ? LocalSettings.CastSalesAmountBasisSubtotal
            : LocalSettings.CastSalesAmountBasisTotal;

    private static string NormalizeSplitMode(string? value) =>
        value is LocalSettings.CastSalesSplitModeFull
            ? LocalSettings.CastSalesSplitModeFull
            : LocalSettings.CastSalesSplitModeSplit;

    private static string? ToFriendlyStatusMessage(string? message) => message switch
    {
        "business_day_not_found" => "営業中の営業日がありません。",
        "business_day_revision_conflict" => "営業日が更新されています。最新の内容を確認してください。",
        "cast_sales_target_conflict" => "会計または調整対象が更新されています。最新の内容を確認してください。",
        "cast_sales_settings_conflict" => "配分設定が更新されています。最新の内容を確認してください。",
        "cast_sales_adjustment_not_required" => "この伝票には売上額調整が必要な指名キャストがいません。",
        "invalid_cast_sales_adjustment_payload" => "指名キャスト全員の売上額を0円以上の整数で入力してください。",
        "invalid_cast_sales_adjustment_batch" => "確認する伝票の内容が正しくありません。",
        null or "" => null,
        _ => message
    };

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "キャスト売上額調整を保存できませんでした。";
        }

        if (rawError is "business_day_operation_id_reused")
        {
            return "同じ操作IDに異なる内容が指定されました。画面を再読み込みしてください。";
        }

        if (rawError is "invalid_cast_sales_adjustment_v2_input" or "invalid_cast_sales_adjustment_payload")
        {
            return "キャスト売上額調整の入力内容が正しくありません。";
        }

        if (rawError is "access_denied" or "invalid_signature")
        {
            return PermissionErrorMessage();
        }

        return "キャスト売上額調整を保存できませんでした。";
    }
}
