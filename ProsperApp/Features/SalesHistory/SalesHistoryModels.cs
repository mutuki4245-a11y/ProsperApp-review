using System.Text.Json.Serialization;

namespace ProsperApp.Features.SalesHistory;

public sealed class SalesHistoryPage
{
    public IReadOnlyList<SalesHistorySummary> Items { get; init; } = [];

    public bool HasMore { get; init; }

    public DateOnly? NextBeforeBusinessDate { get; init; }

    public long? NextBeforeBusinessDayId { get; init; }
}

public sealed class SalesHistorySummary
{
    public long BusinessDayId { get; init; }

    public DateOnly BusinessDate { get; init; }

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset? OpenedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public decimal SalesAmount { get; init; }

    public int CustomerCount { get; init; }

    public int SlipCount { get; init; }

    public string? Memo { get; init; }

    public int? LatestSnapshotVersion { get; init; }

    public bool ReportAvailable { get; init; }

    public bool IsCorrected { get; init; }

    public bool CanEdit { get; init; }

    public string? EditReason { get; init; }
}

public sealed class SalesHistoryCorrectionEditor
{
    [JsonPropertyName("department_id")]
    public long DepartmentId { get; init; }

    [JsonPropertyName("business_day_id")]
    public long BusinessDayId { get; init; }

    [JsonPropertyName("business_date")]
    public DateOnly BusinessDate { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("snapshot_version")]
    public int? SnapshotVersion { get; init; }

    [JsonPropertyName("can_edit")]
    public bool CanEdit { get; init; }

    [JsonPropertyName("edit_reason")]
    public string? EditReason { get; init; }

    [JsonPropertyName("is_corrected")]
    public bool IsCorrected { get; init; }

    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    [JsonPropertyName("drink_delivery_amount")]
    public decimal DrinkDeliveryAmount { get; init; }

    [JsonPropertyName("cast_entries")]
    public IReadOnlyList<SalesHistoryCastAttendanceEditorRow> CastEntries { get; init; } = [];

    [JsonPropertyName("staff_entries")]
    public IReadOnlyList<SalesHistoryStaffAttendanceEditorRow> StaffEntries { get; init; } = [];

    [JsonPropertyName("drink_back_adjustments")]
    public IReadOnlyList<SalesHistoryDrinkBackEditorRow> DrinkBackAdjustments { get; init; } = [];

    [JsonPropertyName("cast_sales_slips")]
    public IReadOnlyList<SalesHistoryCastSalesSlipEditorRow> CastSalesSlips { get; init; } = [];

    [JsonPropertyName("checked_at")]
    public DateTimeOffset CheckedAt { get; init; }
}

public abstract class SalesHistoryAttendanceEditorRow
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("department_name")]
    public string? DepartmentName { get; init; }

    [JsonPropertyName("is_active_candidate")]
    public bool IsActiveCandidate { get; init; }

    [JsonPropertyName("is_selected")]
    public bool IsSelected { get; init; }

    [JsonPropertyName("clock_in_time")]
    public string? ClockInTime { get; init; }

    [JsonPropertyName("clock_out_time")]
    public string? ClockOutTime { get; init; }

    [JsonPropertyName("uses_send_service")]
    public bool UsesSendService { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

public sealed class SalesHistoryCastAttendanceEditorRow : SalesHistoryAttendanceEditorRow
{
    [JsonPropertyName("cast_id")]
    public long CastId { get; init; }
}

public sealed class SalesHistoryStaffAttendanceEditorRow : SalesHistoryAttendanceEditorRow
{
    [JsonPropertyName("staff_id")]
    public long StaffId { get; init; }

    [JsonPropertyName("employment_type")]
    public string EmploymentType { get; init; } = string.Empty;
}

public sealed class SalesHistoryDrinkBackEditorRow
{
    [JsonPropertyName("cast_id")]
    public long CastId { get; init; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("department_name")]
    public string? DepartmentName { get; init; }

    [JsonPropertyName("is_required")]
    public bool IsRequired { get; init; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    [JsonPropertyName("adjustment_amount")]
    public decimal? AdjustmentAmount { get; init; }

    [JsonPropertyName("is_active_candidate")]
    public bool IsActiveCandidate { get; init; }
}

public sealed class SalesHistoryCastSalesSlipEditorRow
{
    [JsonPropertyName("slip_id")]
    public long SlipId { get; init; }

    [JsonPropertyName("table_code")]
    public string? TableCode { get; init; }

    [JsonPropertyName("table_name")]
    public string? TableName { get; init; }

    [JsonPropertyName("customer_names")]
    public string? CustomerNames { get; init; }

    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; init; }

    [JsonPropertyName("source_amount_type")]
    public string SourceAmountType { get; init; } = string.Empty;

    [JsonPropertyName("split_mode")]
    public string SplitMode { get; init; } = string.Empty;

    [JsonPropertyName("adjustments")]
    public IReadOnlyList<SalesHistoryCastSalesEditorRow> Adjustments { get; init; } = [];
}

public sealed class SalesHistoryCastSalesEditorRow
{
    [JsonPropertyName("slip_cast_id")]
    public long SlipCastId { get; init; }

    [JsonPropertyName("cast_id")]
    public long CastId { get; init; }

    [JsonPropertyName("cast_display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("sales_amount")]
    public decimal? SalesAmount { get; init; }

    [JsonPropertyName("suggested_sales_amount")]
    public decimal? SuggestedSalesAmount { get; init; }
}

public sealed record SalesHistoryCorrectionMutation
{
    public string? OperationId { get; init; }

    public long BusinessDayId { get; init; }

    public int ExpectedSnapshotVersion { get; init; }

    public string? Reason { get; init; }

    public string? Memo { get; init; }

    public decimal DrinkDeliveryAmount { get; init; }

    public IReadOnlyList<SalesHistoryCastAttendanceMutation> CastEntries { get; init; } = [];

    public IReadOnlyList<SalesHistoryStaffAttendanceMutation> StaffEntries { get; init; } = [];

    public IReadOnlyList<SalesHistoryDrinkBackMutation> DrinkBackAdjustments { get; init; } = [];

    public IReadOnlyList<SalesHistoryCastSalesSlipMutation> CastSalesSlips { get; init; } = [];

    [JsonIgnore]
    public DateOnly CurrentBusinessDate { get; init; }

    [JsonIgnore]
    public string ActorEmail { get; init; } = string.Empty;
}

public sealed record SalesHistoryCastAttendanceMutation(
    long CastId,
    bool IsSelected,
    string? ClockInTime,
    string? ClockOutTime,
    bool UsesSendService);

public sealed record SalesHistoryStaffAttendanceMutation(
    long StaffId,
    bool IsSelected,
    string? ClockInTime,
    string? ClockOutTime,
    bool UsesSendService);

public sealed record SalesHistoryDrinkBackMutation(
    long CastId,
    bool IsActive,
    decimal? AdjustmentAmount);

public sealed record SalesHistoryCastSalesSlipMutation(
    long SlipId,
    string SourceAmountType,
    string SplitMode,
    IReadOnlyList<SalesHistoryCastSalesMutation> Adjustments);

public sealed record SalesHistoryCastSalesMutation(long SlipCastId, decimal? SalesAmount);

public sealed class SalesHistoryCorrectionResult
{
    public string Status { get; init; } = string.Empty;

    public string OperationId { get; init; } = string.Empty;

    public string? Message { get; init; }

    public long BusinessDayId { get; init; }

    public int SnapshotVersion { get; init; }

    public DateTimeOffset? CorrectedAt { get; init; }

    public IReadOnlyList<string> ChangedSections { get; init; } = [];
}

public sealed record SalesHistoryQuery(
    DateOnly? FromBusinessDate,
    DateOnly? ToBusinessDate,
    DateOnly? BeforeBusinessDate,
    long? BeforeBusinessDayId,
    int PageSize = 30);
