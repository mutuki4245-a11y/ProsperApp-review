using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Features.Closing;

public sealed record DrinkBackStatus(
    int RequiredCastCount,
    int CompletedCastCount,
    int MissingCastCount,
    long TotalAmount)
{
    public bool IsCompleted => MissingCastCount == 0;
}

public sealed record DrinkBackAdjustmentRow(
    long CastId,
    string DisplayName,
    string? DepartmentName,
    long? AdjustmentAmount,
    bool IsSaved,
    bool IsRequired)
{
    public string SearchDisplayName => string.IsNullOrWhiteSpace(DepartmentName)
        ? DisplayName
        : $"{DisplayName}：{DepartmentName}";
}

public sealed record DrinkBackCastOption(
    long CastId,
    string DisplayName,
    string? DepartmentName)
{
    public string SearchDisplayName => string.IsNullOrWhiteSpace(DepartmentName)
        ? DisplayName
        : $"{DisplayName}：{DepartmentName}";
}

public sealed record DrinkBackEditorFragment(
    long DepartmentId,
    bool HasBusinessDay,
    long? BusinessDayId,
    long BusinessDayRevision,
    DateOnly? BusinessDate,
    IReadOnlyList<DrinkBackAdjustmentRow> RequiredRows,
    IReadOnlyList<DrinkBackAdjustmentRow> OptionalRows,
    DrinkBackStatus Status,
    int OptionalAdjustmentCount,
    DateTimeOffset CheckedAt);

public sealed record CurrentDrinkBackEditorState(
    DrinkBackEditorFragment Editor,
    string CastMasterRevision,
    IReadOnlyList<DrinkBackCastOption> ActiveCasts);

public sealed class DrinkBackAdjustmentInput
{
    [Range(1, long.MaxValue, ErrorMessage = "キャストを選択してください。")]
    public long CastId { get; set; }

    [Display(Name = "ドリンクバック調整額")]
    [Range(-999999999999L, 999999999999L, ErrorMessage = "ドリンクバック調整額は符号付き整数円で入力してください。")]
    public long AdjustmentAmount { get; set; }
}

public sealed class DrinkBackSaveMutation
{
    public string OperationId { get; set; } = string.Empty;

    public long? ExpectedBusinessDayId { get; set; }

    public long? ExpectedBusinessDayRevision { get; set; }

    public List<DrinkBackAdjustmentInput> RequiredAdjustments { get; set; } = [];

    public List<DrinkBackAdjustmentInput> OptionalAdjustments { get; set; } = [];

    public List<long> RemoveCastIds { get; set; } = [];
}

public sealed record ClosingDashboardDrinkBackDelta(
    long DepartmentId,
    long BusinessDayId,
    long BusinessDayRevision,
    int DrinkBackRequiredCastCount,
    int DrinkBackCompletedCastCount,
    int DrinkBackMissingCastCount,
    long DrinkBackTotalAmount,
    bool CanClose,
    IReadOnlyList<string> BlockReasons,
    DrinkBackEditorFragment Editor,
    DateTimeOffset CheckedAt);

public sealed record DrinkBackSaveOutput(
    string Status,
    string OperationId,
    string? Message,
    DrinkBackEditorFragment Editor,
    DrinkBackStatus DrinkBackStatus,
    ClosingDashboardDrinkBackDelta? DashboardDelta,
    long BusinessDayRevision);
