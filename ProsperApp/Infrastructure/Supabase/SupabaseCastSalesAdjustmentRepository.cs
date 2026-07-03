using System.Globalization;
using System.Text.Json.Serialization;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseCastSalesAdjustmentRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), ICastSalesAdjustmentRepository
{
    public async Task<CastSalesAdjustmentStatus> GetStatusAsync(long businessDayId, CancellationToken ct)
    {
        if (!HasMutationSettings() || businessDayId <= 0)
        {
            return new CastSalesAdjustmentStatus { RequiredSlipCount = 1, MissingSlipCount = 1 };
        }

        var result = await RpcClient.PostArrayAsync(
            "get_business_day_cast_sales_adjustment_status",
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

        return new CastSalesAdjustmentStatus
        {
            RequiredSlipCount = (int)(ReadLong(rows[0], "required_slip_count") ?? 0),
            CompletedSlipCount = (int)(ReadLong(rows[0], "completed_slip_count") ?? 0),
            MissingSlipCount = (int)(ReadLong(rows[0], "missing_slip_count") ?? 0)
        };
    }

    public async Task<IReadOnlyList<CastSalesAdjustmentSlip>> GetSlipsAsync(long businessDayId, CancellationToken ct)
    {
        if (!HasMutationSettings() || businessDayId <= 0)
        {
            return [];
        }

        var result = await RpcClient.PostArrayAsync(
            "get_cast_sales_adjustment_slips",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);
        var rows = result.Succeeded ? result.Rows : [];

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
                ServiceTaxAmount = ReadDecimal(row, "service_tax_amount") ?? 0,
                TotalAmount = ReadDecimal(row, "total_amount") ?? 0,
                CastNames = NormalizeCastDisplayNameList(ReadString(row, "cast_names")),
                RequiredCastCount = (int)(ReadLong(row, "required_cast_count") ?? 0),
                SavedCastCount = (int)(ReadLong(row, "saved_cast_count") ?? 0),
                AdjustedSalesAmountTotal = ReadDecimal(row, "adjusted_sales_amount_total") ?? 0
            })
            .Where(x => x.SlipId > 0 && x.RequiredCastCount > 0)
            .ToList();
    }

    public async Task<CastSalesAdjustmentDetail?> GetDetailAsync(long slipId, CancellationToken ct)
    {
        if (!HasMutationSettings() || slipId <= 0)
        {
            return null;
        }

        var result = await RpcClient.PostArrayAsync(
            "get_cast_sales_adjustment_detail",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId
            },
            ct);
        var rows = result.Succeeded ? result.Rows : [];

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
                ServiceTaxAmount = ReadDecimal(row, "service_tax_amount") ?? 0,
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
                NominationType = ReadString(row, "nomination_type") ?? string.Empty,
                StartedAt = ReadDateTimeOffset(row, "started_at"),
                SalesAmount = ReadDecimal(row, "sales_amount"),
                SourceAmountType = ReadString(row, "source_amount_type"),
                SplitMode = ReadString(row, "split_mode")
            });
        }

        return detail is { SlipId: > 0, Casts.Count: > 0 } ? detail : null;
    }

    public async Task<CastSalesAdjustmentSaveResult> SaveAsync(CastSalesAdjustmentSaveInput input, CancellationToken ct)
    {
        if (!HasMutationSettings())
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
            "save_cast_sales_adjustment",
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

    private sealed record CastSalesAdjustmentPayload(
        [property: JsonPropertyName("slip_cast_id")] long SlipCastId,
        [property: JsonPropertyName("sales_amount")] decimal SalesAmount);

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

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"キャスト売上額調整を保存できません。{rawError}";
    }
}
