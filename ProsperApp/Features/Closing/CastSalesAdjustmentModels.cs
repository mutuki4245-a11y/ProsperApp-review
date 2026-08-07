using ProsperApp.Services;

namespace ProsperApp.Features.Closing;

public sealed class CastSalesAdjustmentStatus
{
    public int RequiredSlipCount { get; init; }

    public int CompletedSlipCount { get; init; }

    public int MissingSlipCount { get; init; }

    public bool IsCompleted => MissingSlipCount == 0;
}

public sealed class CastSalesAdjustmentOverview
{
    public long DepartmentId { get; init; }

    public bool HasBusinessDay { get; init; }

    public long? BusinessDayId { get; init; }

    public long BusinessDayRevision { get; init; }

    public DateOnly? BusinessDate { get; init; }

    public string SourceAmountType { get; init; } = LocalSettings.CastSalesAmountBasisTotal;

    public string SplitMode { get; init; } = LocalSettings.CastSalesSplitModeSplit;

    public CastSalesAdjustmentStatus Status { get; init; } = new();

    public IReadOnlyList<CastSalesAdjustmentSlip> Slips { get; init; } = [];

    public IReadOnlyList<CastSalesAdjustmentDetail> Details { get; init; } = [];

    public DateTimeOffset CheckedAt { get; init; }
}

public sealed class CastSalesAdjustmentSlip
{
    public long SlipId { get; init; }

    public string? SlipNo { get; init; }

    public long? TableId { get; init; }

    public string? TableCode { get; init; }

    public string? TableName { get; init; }

    public string? CustomerNames { get; init; }

    public long CheckoutId { get; init; }

    public DateTimeOffset CheckoutAt { get; init; }

    public DateTimeOffset SlipVersion { get; init; }

    public DateTimeOffset CheckoutVersion { get; init; }

    public decimal SubtotalAmount { get; init; }

    public decimal ServiceChargeAmount { get; init; }

    public decimal TotalAmount { get; init; }

    public string? CastNames { get; init; }

    public int RequiredCastCount { get; init; }

    public int SavedCastCount { get; init; }

    public decimal AdjustedSalesAmountTotal { get; init; }

    public bool IsAdjusted => RequiredCastCount > 0 && SavedCastCount >= RequiredCastCount;

    public string TotalAmountText => StoreUiText.Yen(TotalAmount);

    public string TableDisplayName => !string.IsNullOrWhiteSpace(TableCode) && !string.IsNullOrWhiteSpace(TableName)
        ? $"{TableCode} {TableName}"
        : TableCode ?? TableName ?? "-";

    public string CustomerDisplayNames => string.IsNullOrWhiteSpace(CustomerNames)
        ? "お客様名なし"
        : CustomerNames;
}

public sealed class CastSalesAdjustmentDetail
{
    public long SlipId { get; init; }

    public string? SlipNo { get; init; }

    public long BusinessDayId { get; init; }

    public DateOnly BusinessDate { get; init; }

    public string? TableCode { get; init; }

    public string? TableName { get; init; }

    public long CheckoutId { get; init; }

    public DateTimeOffset CheckoutAt { get; init; }

    public DateTimeOffset SlipVersion { get; init; }

    public DateTimeOffset CheckoutVersion { get; init; }

    public decimal SubtotalAmount { get; init; }

    public decimal ServiceChargeAmount { get; init; }

    public decimal TotalAmount { get; init; }

    public IReadOnlyList<CastSalesAdjustmentCastRow> Casts { get; init; } = [];

    public string TableDisplayName => !string.IsNullOrWhiteSpace(TableCode) && !string.IsNullOrWhiteSpace(TableName)
        ? $"{TableCode} {TableName}"
        : TableCode ?? TableName ?? "-";
}

public sealed class CastSalesAdjustmentCastRow
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

    public string? SourceAmountType { get; init; }

    public string? SplitMode { get; init; }

    public decimal? SuggestedSubtotalSalesAmount { get; init; }

    public string? SubtotalSuggestionFallbackReason { get; init; }

    public decimal? SuggestedTotalSalesAmount { get; init; }

    public string? TotalSuggestionFallbackReason { get; init; }

    public string CastDisplayName => DisplayName;

    public string NominationDisplayName => !string.IsNullOrWhiteSpace(NominationDisplayNameFromMaster)
        ? NominationDisplayNameFromMaster
        : !string.IsNullOrWhiteSpace(NominationKind)
            ? NominationKind
            : NominationType;
}

public sealed record CastSalesAdjustmentValue(long SlipCastId, decimal SalesAmount);

public sealed record SaveCurrentCastSalesAdjustmentMutation(
    string OperationId,
    long ExpectedBusinessDayId,
    long ExpectedBusinessDayRevision,
    long SlipId,
    DateTimeOffset ExpectedSlipVersion,
    long ExpectedCheckoutId,
    DateTimeOffset ExpectedCheckoutVersion,
    string SourceAmountType,
    string SplitMode,
    IReadOnlyList<CastSalesAdjustmentValue> Adjustments);

public sealed record ConfirmCurrentCastSalesAdjustmentSlip(
    long SlipId,
    DateTimeOffset ExpectedSlipVersion,
    long ExpectedCheckoutId,
    DateTimeOffset ExpectedCheckoutVersion,
    string SourceAmountType,
    string SplitMode,
    IReadOnlyList<CastSalesAdjustmentValue> Adjustments);

public sealed record ConfirmCurrentCastSalesAdjustmentsMutation(
    string OperationId,
    long ExpectedBusinessDayId,
    long ExpectedBusinessDayRevision,
    IReadOnlyList<ConfirmCurrentCastSalesAdjustmentSlip> Slips);

public sealed class CastSalesAdjustmentSavedRow
{
    public long AdjustmentId { get; init; }

    public long SlipId { get; init; }

    public long CheckoutId { get; init; }

    public long SlipCastId { get; init; }

    public long CastId { get; init; }

    public decimal SalesAmount { get; init; }

    public string SourceAmountType { get; init; } = string.Empty;

    public string SplitMode { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CastSalesAdjustmentDashboardDelta
{
    public long DepartmentId { get; init; }

    public long BusinessDayId { get; init; }

    public long BusinessDayRevision { get; init; }

    public int CastSalesRequiredSlipCount { get; init; }

    public int CastSalesCompletedSlipCount { get; init; }

    public int CastSalesMissingSlipCount { get; init; }

    public DateTimeOffset CheckedAt { get; init; }
}

public sealed class CastSalesAdjustmentMutationResult
{
    public string Status { get; init; } = string.Empty;

    public string OperationId { get; init; } = string.Empty;

    public string? Message { get; init; }

    public long? BusinessDayId { get; init; }

    public long BusinessDayRevision { get; init; }

    public IReadOnlyList<CastSalesAdjustmentSavedRow> SavedAdjustments { get; init; } = [];

    public CastSalesAdjustmentDetail? Detail { get; init; }

    public CastSalesAdjustmentStatus? OverviewStatus { get; init; }

    public CastSalesAdjustmentDashboardDelta? DashboardDelta { get; init; }

    public CastSalesAdjustmentOverview? LatestOverview { get; init; }
}
