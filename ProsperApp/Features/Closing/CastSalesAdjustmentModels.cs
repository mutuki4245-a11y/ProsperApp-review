using System.ComponentModel.DataAnnotations;
using ProsperApp.Services;

namespace ProsperApp.Models;

public class CastSalesAdjustmentStatus
{
    public int RequiredSlipCount { get; init; }

    public int CompletedSlipCount { get; init; }

    public int MissingSlipCount { get; init; }

    public bool IsCompleted => MissingSlipCount == 0;
}

public class CastSalesAdjustmentSlip
{
    public long SlipId { get; init; }

    public string? SlipNo { get; init; }

    public long? TableId { get; init; }

    public string? TableCode { get; init; }

    public string? TableName { get; init; }

    public string? CustomerNames { get; init; }

    public DateTimeOffset CheckoutAt { get; init; }

    public decimal SubtotalAmount { get; init; }

    public decimal ServiceChargeAmount { get; init; }

    public decimal TotalAmount { get; init; }

    public string? CastNames { get; init; }

    public int RequiredCastCount { get; init; }

    public int SavedCastCount { get; init; }

    public decimal AdjustedSalesAmountTotal { get; init; }

    public bool IsAdjusted => RequiredCastCount > 0 && SavedCastCount >= RequiredCastCount;

    public string TotalAmountText => StoreUiText.Yen(TotalAmount);

    public string TableDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(TableCode) && !string.IsNullOrWhiteSpace(TableName))
            {
                return $"{TableCode} {TableName}";
            }

            return TableCode ?? TableName ?? "-";
        }
    }

    public string CustomerDisplayNames => string.IsNullOrWhiteSpace(CustomerNames)
        ? "お客様名なし"
        : CustomerNames;
}

public class CastSalesAdjustmentDetail
{
    public long SlipId { get; init; }

    public string? SlipNo { get; init; }

    public long BusinessDayId { get; init; }

    public DateOnly BusinessDate { get; init; }

    public string? TableCode { get; init; }

    public string? TableName { get; init; }

    public long CheckoutId { get; init; }

    public DateTimeOffset CheckoutAt { get; init; }

    public decimal SubtotalAmount { get; init; }

    public decimal ServiceChargeAmount { get; init; }

    public decimal TotalAmount { get; init; }

    public bool UsesTimeBasedInitialSalesAmount { get; set; }

    public string? InitialSalesAmountFallbackReason { get; set; }

    public List<CastSalesAdjustmentCastRow> Casts { get; } = [];

    public string TableDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(TableCode) && !string.IsNullOrWhiteSpace(TableName))
            {
                return $"{TableCode} {TableName}";
            }

            return TableCode ?? TableName ?? "-";
        }
    }
}

public class CastSalesAdjustmentCastRow
{
    public long SlipCastId { get; init; }

    public long CastId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? DepartmentName { get; init; }

    public string NominationKind { get; init; } = string.Empty;

    public string NominationType { get; init; } = string.Empty;

    public string? NominationDisplayNameFromMaster { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public decimal? SalesAmount { get; init; }

    public decimal InitialSalesAmount { get; set; }

    public string? SourceAmountType { get; init; }

    public string? SplitMode { get; init; }

    public decimal? SuggestedSubtotalSalesAmount { get; init; }

    public string? SubtotalSuggestionFallbackReason { get; init; }

    public decimal? SuggestedTotalSalesAmount { get; init; }

    public string? TotalSuggestionFallbackReason { get; init; }

    public decimal EffectiveSalesAmount => SalesAmount ?? InitialSalesAmount;

    public string EffectiveSalesAmountText => StoreUiText.Yen(EffectiveSalesAmount);

    public string EffectiveSalesAmountInputValue => StoreUiText.NumberInputValue(EffectiveSalesAmount);

    public string CastDisplayName => DisplayName;

    public string NominationDisplayName => !string.IsNullOrWhiteSpace(NominationDisplayNameFromMaster)
        ? NominationDisplayNameFromMaster
        : !string.IsNullOrWhiteSpace(NominationKind)
            ? NominationKind
            : NominationType;
}

public class CastSalesAdjustmentSaveInput
{
    public long? BusinessDayId { get; set; }

    public long? SlipId { get; set; }

    public string SourceAmountType { get; set; } = LocalSettings.CastSalesAmountBasisTotal;

    public string SplitMode { get; set; } = LocalSettings.CastSalesSplitModeSplit;

    public List<CastSalesAdjustmentCastInput> Casts { get; set; } = [];
}

public class CastSalesAdjustmentCastInput
{
    public long SlipCastId { get; set; }

    [Display(Name = "売上額")]
    [Range(0, 999999999999, ErrorMessage = "売上額は0円以上の整数で入力してください。")]
    public decimal SalesAmount { get; set; }
}

public class CastSalesAdjustmentSaveResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public int SavedCount { get; init; }

    public static CastSalesAdjustmentSaveResult Success(int savedCount)
    {
        return new CastSalesAdjustmentSaveResult { Succeeded = true, SavedCount = savedCount };
    }

    public static CastSalesAdjustmentSaveResult Failed(string message)
    {
        return new CastSalesAdjustmentSaveResult { Succeeded = false, ErrorMessage = message };
    }
}
