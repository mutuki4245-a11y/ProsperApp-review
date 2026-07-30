using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Features.Shared;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseCastSalesAdjustmentRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), ICastSalesAdjustmentRepository
{
    public async Task<Result<CastSalesAdjustmentOverview>> GetOverviewAsync(
        long businessDayId,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<CastSalesAdjustmentOverview>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。キャスト売上額調整を取得できません。");
        }

        if (businessDayId <= 0)
        {
            return Result<CastSalesAdjustmentOverview>.Failure(
                ResultFailureKind.InvalidInput,
                "営業日情報が正しくありません。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.get_business_day_cast_sales_adjustment_overview",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);
        if (!result.Succeeded)
        {
            return await GetLegacyOverviewAsync(businessDayId, ct);
        }

        if (result.Value.Count == 0)
        {
            return Result<CastSalesAdjustmentOverview>.Failure(
                ResultFailureKind.InvalidResponse,
                "キャスト売上額調整の一覧を取得できませんでした。");
        }

        var row = result.Value[0];
        if (!TryReadJsonProperty(row, "status", JsonValueKind.Object, out var statusElement) ||
            !TryReadJsonProperty(row, "slips", JsonValueKind.Array, out var slipsElement) ||
            !TryReadJsonProperty(row, "details", JsonValueKind.Array, out var detailsElement))
        {
            return Result<CastSalesAdjustmentOverview>.Failure(
                ResultFailureKind.InvalidResponse,
                "キャスト売上額調整の応答形式が正しくありません。");
        }

        return Result<CastSalesAdjustmentOverview>.Success(new CastSalesAdjustmentOverview
        {
            Status = ParseStatus(statusElement),
            Slips = ParseSlips(slipsElement.EnumerateArray()),
            Details = ParseDetails(detailsElement.EnumerateArray())
        });
    }

    public async Task<CastSalesAdjustmentStatus> GetStatusAsync(long businessDayId, CancellationToken ct)
    {
        if (!HasRpcAccess() || businessDayId <= 0)
        {
            return new CastSalesAdjustmentStatus { RequiredSlipCount = 1, MissingSlipCount = 1 };
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_business_day_cast_sales_adjustment_status",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);
        var rows = result.Succeeded ? result.Rows : [];

        if (rows.Count == 0)
        {
            return new CastSalesAdjustmentStatus { RequiredSlipCount = 1, MissingSlipCount = 1 };
        }

        return ParseStatus(rows[0]);
    }

    public async Task<IReadOnlyList<CastSalesAdjustmentSlip>> GetSlipsAsync(long businessDayId, CancellationToken ct)
    {
        if (!HasRpcAccess() || businessDayId <= 0)
        {
            return [];
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_cast_sales_adjustment_slips",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);
        var rows = result.Succeeded ? result.Rows : [];

        return ParseSlips(rows);
    }

    public async Task<CastSalesAdjustmentDetail?> GetDetailAsync(long slipId, CancellationToken ct)
    {
        if (!HasRpcAccess() || slipId <= 0)
        {
            return null;
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_cast_sales_adjustment_detail",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId
            },
            ct);
        var rows = result.Succeeded ? result.Rows : [];

        return ParseDetail(rows);
    }

    public async Task<CastSalesAdjustmentSaveResult> SaveAsync(CastSalesAdjustmentSaveInput input, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return CastSalesAdjustmentSaveResult.Failed("Supabase Edge Function設定が未設定です。キャスト売上額調整を保存できません。");
        }

        if (input.SlipId is null or <= 0)
        {
            return CastSalesAdjustmentSaveResult.Failed("調整する伝票を選択してください。");
        }

        var sourceAmountType = NormalizeSourceAmountType(input.SourceAmountType);
        var splitMode = NormalizeSplitMode(input.SplitMode);
        var casts = input.Casts
            .Where(x => x.SlipCastId > 0)
            .GroupBy(x => x.SlipCastId)
            .Select(x => x.Last())
            .Select(x => new CastSalesAdjustmentPayload(x.SlipCastId, x.SalesAmount))
            .ToArray();

        if (casts.Length == 0)
        {
            return CastSalesAdjustmentSaveResult.Failed("指名キャストの売上額を入力してください。");
        }

        if (casts.Any(x => x.SalesAmount < 0 || decimal.Truncate(x.SalesAmount) != x.SalesAmount))
        {
            return CastSalesAdjustmentSaveResult.Failed("売上額は0円以上の整数で入力してください。");
        }

        var result = await RpcClient.PostScalarAsync(
            "store.save_cast_sales_adjustment",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = input.SlipId.Value,
                p_adjustments = casts,
                p_source_amount_type = sourceAmountType,
                p_split_mode = splitMode
            },
            ct);

        if (!result.Succeeded)
        {
            return CastSalesAdjustmentSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var value = NormalizeScalarBody(result.Body);
        return int.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var savedCount)
            ? CastSalesAdjustmentSaveResult.Success(savedCount)
            : CastSalesAdjustmentSaveResult.Failed("キャスト売上額調整を保存できませんでした。");
    }

    public async Task<CastSalesAdjustmentSaveResult> SaveBatchAsync(
        long businessDayId,
        IReadOnlyCollection<CastSalesAdjustmentSaveInput> inputs,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return CastSalesAdjustmentSaveResult.Failed("Supabase Edge Function設定が未設定です。キャスト売上額調整を保存できません。");
        }

        if (businessDayId <= 0 || inputs.Count == 0 || inputs.Count > 100)
        {
            return CastSalesAdjustmentSaveResult.Failed("確認する伝票を選択してください。");
        }

        var slips = new List<CastSalesAdjustmentBatchSlipPayload>(inputs.Count);
        foreach (var input in inputs)
        {
            if (input.BusinessDayId != businessDayId || input.SlipId is null or <= 0)
            {
                return CastSalesAdjustmentSaveResult.Failed("営業日または伝票情報が正しくありません。");
            }

            var adjustments = BuildAdjustmentPayload(input);
            if (adjustments is null)
            {
                return CastSalesAdjustmentSaveResult.Failed("指名キャスト全員の売上額を0円以上の整数で入力してください。");
            }

            slips.Add(new CastSalesAdjustmentBatchSlipPayload(
                input.SlipId.Value,
                adjustments,
                NormalizeSourceAmountType(input.SourceAmountType),
                NormalizeSplitMode(input.SplitMode)));
        }

        if (slips.Select(x => x.SlipId).Distinct().Count() != slips.Count)
        {
            return CastSalesAdjustmentSaveResult.Failed("同じ伝票が重複しています。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.save_business_day_cast_sales_adjustments",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId,
                p_slips = slips
            },
            ct);
        if (!result.Succeeded)
        {
            if (IsMissingBatchRpc(result.ErrorMessage))
            {
                var savedCount = 0;
                foreach (var input in inputs)
                {
                    var legacyResult = await SaveAsync(input, ct);
                    if (!legacyResult.Succeeded)
                    {
                        return legacyResult;
                    }

                    savedCount += legacyResult.SavedCount;
                }

                return CastSalesAdjustmentSaveResult.Success(savedCount);
            }

            return CastSalesAdjustmentSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return CastSalesAdjustmentSaveResult.Failed("キャスト売上額調整を保存できませんでした。");
        }

        return CastSalesAdjustmentSaveResult.Success(
            (int)(ReadLong(result.Rows[0], "saved_cast_count") ?? 0));
    }

    private sealed record CastSalesAdjustmentPayload(
        [property: JsonPropertyName("slip_cast_id")] long SlipCastId,
        [property: JsonPropertyName("sales_amount")] decimal SalesAmount);

    private sealed record CastSalesAdjustmentBatchSlipPayload(
        [property: JsonPropertyName("slip_id")] long SlipId,
        [property: JsonPropertyName("adjustments")] IReadOnlyList<CastSalesAdjustmentPayload> Adjustments,
        [property: JsonPropertyName("source_amount_type")] string SourceAmountType,
        [property: JsonPropertyName("split_mode")] string SplitMode);

    private static IReadOnlyList<CastSalesAdjustmentPayload>? BuildAdjustmentPayload(
        CastSalesAdjustmentSaveInput input)
    {
        var casts = input.Casts
            .Where(x => x.SlipCastId > 0)
            .GroupBy(x => x.SlipCastId)
            .Select(x => x.Last())
            .Select(x => new CastSalesAdjustmentPayload(x.SlipCastId, x.SalesAmount))
            .ToArray();

        return casts.Length == 0 ||
               casts.Any(x => x.SalesAmount < 0 || decimal.Truncate(x.SalesAmount) != x.SalesAmount)
            ? null
            : casts;
    }

    private async Task<Result<CastSalesAdjustmentOverview>> GetLegacyOverviewAsync(
        long businessDayId,
        CancellationToken ct)
    {
        var departmentId = CurrentStoreDepartmentId;
        var statusTask = RpcClient.PostArrayAsync(
            "store.get_business_day_cast_sales_adjustment_status",
            new { p_department_id = departmentId, p_business_day_id = businessDayId },
            ct);
        var slipsTask = RpcClient.PostArrayAsync(
            "store.get_cast_sales_adjustment_slips",
            new { p_department_id = departmentId, p_business_day_id = businessDayId },
            ct);
        await Task.WhenAll(statusTask, slipsTask);

        var status = await statusTask;
        var slips = await slipsTask;
        if (!status.Succeeded || status.Rows.Count == 0 || !slips.Succeeded)
        {
            return Result<CastSalesAdjustmentOverview>.Failure(
                ResultFailureKind.Unavailable,
                "キャスト売上額調整を取得できませんでした。時間をおいて再表示してください。");
        }

        var parsedSlips = ParseSlips(slips.Rows);
        var detailResults = await Task.WhenAll(parsedSlips.Select(slip =>
            RpcClient.PostArrayAsync(
                "store.get_cast_sales_adjustment_detail",
                new { p_department_id = departmentId, p_slip_id = slip.SlipId },
                ct)));
        if (detailResults.Any(result => !result.Succeeded))
        {
            return Result<CastSalesAdjustmentOverview>.Failure(
                ResultFailureKind.Unavailable,
                "キャスト売上額調整の明細を取得できませんでした。");
        }

        return Result<CastSalesAdjustmentOverview>.Success(new CastSalesAdjustmentOverview
        {
            Status = ParseStatus(status.Rows[0]),
            Slips = parsedSlips,
            Details = detailResults
                .Select(result => ParseDetail(result.Rows))
                .Where(detail => detail is not null)
                .Select(detail => detail!)
                .ToList()
        });
    }

    private static CastSalesAdjustmentStatus ParseStatus(JsonElement row)
    {
        return new CastSalesAdjustmentStatus
        {
            RequiredSlipCount = (int)(ReadLong(row, "required_slip_count") ?? 0),
            CompletedSlipCount = (int)(ReadLong(row, "completed_slip_count") ?? 0),
            MissingSlipCount = (int)(ReadLong(row, "missing_slip_count") ?? 0)
        };
    }

    private static IReadOnlyList<CastSalesAdjustmentSlip> ParseSlips(IEnumerable<JsonElement> rows)
    {
        return rows
            .Select(row => new CastSalesAdjustmentSlip
            {
                SlipId = ReadLong(row, "slip_id") ?? 0,
                SlipNo = ReadString(row, "slip_no"),
                TableId = ReadLong(row, "table_id"),
                TableCode = ReadString(row, "table_code"),
                TableName = ReadString(row, "table_name"),
                CustomerNames = ReadString(row, "customer_names"),
                CheckoutAt = ReadDateTimeOffset(row, "checkout_at") ?? DateTimeOffset.MinValue,
                SubtotalAmount = ReadDecimal(row, "subtotal_amount") ?? 0,
                ServiceChargeAmount = ReadDecimal(row, "service_charge_amount") ?? 0,
                TotalAmount = ReadDecimal(row, "total_amount") ?? 0,
                CastNames = NormalizeCastDisplayNameList(ReadString(row, "cast_names")),
                RequiredCastCount = (int)(ReadLong(row, "required_cast_count") ?? 0),
                SavedCastCount = (int)(ReadLong(row, "saved_cast_count") ?? 0),
                AdjustedSalesAmountTotal = ReadDecimal(row, "adjusted_sales_amount_total") ?? 0
            })
            .Where(x => x.SlipId > 0 && x.RequiredCastCount > 0)
            .ToList();
    }

    private static IReadOnlyList<CastSalesAdjustmentDetail> ParseDetails(IEnumerable<JsonElement> rows)
    {
        return rows
            .GroupBy(row => ReadLong(row, "slip_id") ?? 0)
            .Where(group => group.Key > 0)
            .Select(group => ParseDetail(group))
            .Where(detail => detail is not null)
            .Select(detail => detail!)
            .ToList();
    }

    private static CastSalesAdjustmentDetail? ParseDetail(IEnumerable<JsonElement> rows)
    {
        CastSalesAdjustmentDetail? detail = null;
        foreach (var row in rows)
        {
            detail ??= new CastSalesAdjustmentDetail
            {
                SlipId = ReadLong(row, "slip_id") ?? 0,
                SlipNo = ReadString(row, "slip_no"),
                BusinessDayId = ReadLong(row, "business_day_id") ?? 0,
                BusinessDate = ReadDateOnly(row, "business_date") ?? default,
                TableCode = ReadString(row, "table_code"),
                TableName = ReadString(row, "table_name"),
                CheckoutId = ReadLong(row, "checkout_id") ?? 0,
                CheckoutAt = ReadDateTimeOffset(row, "checkout_at") ?? DateTimeOffset.MinValue,
                SubtotalAmount = ReadDecimal(row, "subtotal_amount") ?? 0,
                ServiceChargeAmount = ReadDecimal(row, "service_charge_amount") ?? 0,
                TotalAmount = ReadDecimal(row, "total_amount") ?? 0
            };

            var slipCastId = ReadLong(row, "slip_cast_id") ?? 0;
            if (slipCastId <= 0)
            {
                continue;
            }

            detail.Casts.Add(new CastSalesAdjustmentCastRow
            {
                SlipCastId = slipCastId,
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
            });
        }

        return detail is { SlipId: > 0, Casts.Count: > 0 } ? detail : null;
    }

    private static bool TryReadJsonProperty(
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

    private static bool IsMissingBatchRpc(string? rawError)
    {
        return !string.IsNullOrWhiteSpace(rawError) &&
               (rawError.Contains("invalid_function_name", StringComparison.OrdinalIgnoreCase) ||
                rawError.Contains("PGRST202", StringComparison.OrdinalIgnoreCase) ||
                rawError.Contains(
                    "store.save_business_day_cast_sales_adjustments",
                    StringComparison.OrdinalIgnoreCase) &&
                rawError.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeSourceAmountType(string? value)
    {
        return value is LocalSettings.CastSalesAmountBasisSubtotal
            ? LocalSettings.CastSalesAmountBasisSubtotal
            : LocalSettings.CastSalesAmountBasisTotal;
    }

    private static string NormalizeSplitMode(string? value)
    {
        return value is LocalSettings.CastSalesSplitModeFull
            ? LocalSettings.CastSalesSplitModeFull
            : LocalSettings.CastSalesSplitModeSplit;
    }

    private static string? NormalizeScalarBody(string? body)
    {
        return string.IsNullOrWhiteSpace(body)
            ? null
            : body.Trim().Trim('"');
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "キャスト売上額調整を保存できません。";
        }

        if (rawError.Contains("cast_sales_adjustment_not_required", StringComparison.OrdinalIgnoreCase))
        {
            return "この伝票には売上額調整が必要な指名キャストがいません。";
        }

        if (rawError.Contains("cast_sales_checkout_not_found", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("store_slip_not_checked_out", StringComparison.OrdinalIgnoreCase))
        {
            return "会計済みの伝票を選択してください。";
        }

        if (rawError.Contains("invalid_cast_sales_adjustment_amount", StringComparison.OrdinalIgnoreCase))
        {
            return "売上額は0円以上の整数で入力してください。";
        }

        if (rawError.Contains("invalid_cast_sales_adjustment_payload", StringComparison.OrdinalIgnoreCase))
        {
            return "指名キャスト全員の売上額を入力してください。";
        }

        if (rawError.Contains("invalid_cast_sales_adjustment_settings", StringComparison.OrdinalIgnoreCase))
        {
            return "キャスト売上額調整の設定を確認してください。";
        }

        if (rawError.Contains("invalid_cast_sales_adjustment_batch", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("duplicate_cast_sales_adjustment_slip", StringComparison.OrdinalIgnoreCase))
        {
            return "確認する伝票の内容が正しくありません。画面を再読み込みしてください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"キャスト売上額調整を保存できません。{rawError}";
    }
}
