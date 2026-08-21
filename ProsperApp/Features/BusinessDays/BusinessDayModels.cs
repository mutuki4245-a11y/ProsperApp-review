using ProsperApp.Features.Closing;

namespace ProsperApp.Features.BusinessDays;

public sealed record CurrentClosingDashboard(
    long DepartmentId,
    bool HasBusinessDay,
    long? BusinessDayId,
    long BusinessDayRevision,
    DateOnly? BusinessDate,
    string? Memo,
    int OpenSlipCount,
    decimal DrinkDeliveryAmount,
    bool IsDrinkDeliveryAmountEntered,
    int AttendanceCount,
    int MissingClockOutCount,
    int CastSalesRequiredSlipCount,
    int CastSalesCompletedSlipCount,
    int CastSalesMissingSlipCount,
    int DrinkBackRequiredCastCount,
    int DrinkBackCompletedCastCount,
    int DrinkBackMissingCastCount,
    long DrinkBackTotalAmount,
    DrinkBackEditorFragment DrinkBackEditor,
    string CastMasterRevision,
    IReadOnlyList<DrinkBackCastOption>? ActiveCasts,
    int PendingReceiptCount,
    bool CanClose,
    IReadOnlyList<string> BlockReasons,
    DateTimeOffset CheckedAt);

public sealed record CurrentBusinessDayCloseMutation(
    string OperationId,
    long? ExpectedBusinessDayId,
    long? ExpectedBusinessDayRevision,
    string? Memo,
    bool IgnoreClosingRequirements);

public sealed record CurrentBusinessDayCloseOutput(
    string OperationId,
    string Status,
    string? Message,
    long? ClosedBusinessDayId,
    DateOnly? BusinessDate,
    DateTimeOffset? ClosedAt,
    long? ReportBusinessDayId,
    CurrentClosingDashboard Dashboard);

public sealed record CurrentDrinkDeliveryEditorState(
    long DepartmentId,
    bool HasBusinessDay,
    long? BusinessDayId,
    long BusinessDayRevision,
    DateOnly? BusinessDate,
    decimal DrinkDeliveryAmount,
    bool IsEntered,
    DateTimeOffset CheckedAt);

public sealed record CurrentBusinessDayDrinkDeliveryMutation(
    string OperationId,
    long? ExpectedBusinessDayId,
    long? ExpectedBusinessDayRevision,
    DateOnly BusinessDate,
    decimal Amount);

public sealed record ClosingDashboardDrinkDelta(
    long DepartmentId,
    long BusinessDayId,
    long BusinessDayRevision,
    decimal DrinkDeliveryAmount,
    bool IsDrinkDeliveryAmountEntered,
    DateTimeOffset CheckedAt);

public sealed record CurrentBusinessDayDrinkDeliverySaveOutput(
    string Status,
    string OperationId,
    string? Message,
    CurrentDrinkDeliveryEditorState Editor,
    ClosingDashboardDrinkDelta? DashboardDelta,
    StoreBusinessDay? BusinessDay);

public class BusinessDayEnsureResult
{
    public bool Succeeded { get; init; }

    public bool RequiresClosing { get; init; }

    public StoreBusinessDay? BusinessDay { get; init; }

    public DateOnly CurrentBusinessDate { get; init; }

    public string? ErrorMessage { get; init; }

    public static BusinessDayEnsureResult Success(StoreBusinessDay businessDay, DateOnly currentBusinessDate)
    {
        return new BusinessDayEnsureResult
        {
            Succeeded = true,
            BusinessDay = businessDay,
            CurrentBusinessDate = currentBusinessDate
        };
    }

    public static BusinessDayEnsureResult ClosingRequired(StoreBusinessDay businessDay, DateOnly currentBusinessDate)
    {
        return new BusinessDayEnsureResult
        {
            Succeeded = false,
            RequiresClosing = true,
            BusinessDay = businessDay,
            CurrentBusinessDate = currentBusinessDate,
            ErrorMessage = $"前回営業日 {businessDay.BusinessDate:yyyy-MM-dd} の締め作業が未完了です。締め作業を完了してください。"
        };
    }

    public static BusinessDayEnsureResult Failed(string message, DateOnly currentBusinessDate)
    {
        return new BusinessDayEnsureResult
        {
            Succeeded = false,
            CurrentBusinessDate = currentBusinessDate,
            ErrorMessage = message
        };
    }
}

public class BusinessDayClosingReadiness
{
    public StoreBusinessDay? BusinessDay { get; init; }

    public int OpenSlipCount { get; init; }

    public decimal DrinkDeliveryAmount { get; init; }

    public bool IsDrinkDeliveryAmountEntered { get; init; }

    public int AttendanceCount { get; init; }

    public int MissingClockOutCount { get; init; }

    public int CastSalesRequiredSlipCount { get; init; }

    public int CastSalesCompletedSlipCount { get; init; }

    public int CastSalesMissingSlipCount { get; init; }

    public int DrinkBackRequiredCastCount { get; init; }

    public int DrinkBackCompletedCastCount { get; init; }

    public int DrinkBackMissingCastCount { get; init; }

    public long DrinkBackTotalAmount { get; init; }

    public bool? CanCloseFromStore { get; init; }

    public IReadOnlyList<string> BlockReasonsFromStore { get; init; } = [];

    public DateTimeOffset? CheckedAt { get; init; }

    public bool IsCastSalesAdjustmentCompleted => CastSalesMissingSlipCount == 0;

    public bool IsDrinkBackCompleted => DrinkBackMissingCastCount == 0;

    public bool CanClose => CanCloseFromStore ?? (
        BusinessDay is not null &&
        OpenSlipCount == 0 &&
        IsDrinkDeliveryAmountEntered &&
        AttendanceCount > 0 &&
        MissingClockOutCount == 0 &&
        IsCastSalesAdjustmentCompleted &&
        IsDrinkBackCompleted);

    public IReadOnlyList<string> BlockReasons
    {
        get
        {
            if (CanCloseFromStore is not null)
            {
                return BlockReasonsFromStore;
            }

            var reasons = new List<string>();
            if (BusinessDay is null)
            {
                reasons.Add("営業中の営業日がありません。最初の業務入力で営業日が自動作成されます。");
                return reasons;
            }

            if (OpenSlipCount > 0)
            {
                reasons.Add($"未会計伝票が {OpenSlipCount} 件あります。");
            }

            if (!IsDrinkDeliveryAmountEntered)
            {
                reasons.Add("酒代が未入力です。酒代がない場合も0円で保存してください。");
            }

            if (AttendanceCount == 0)
            {
                reasons.Add("勤怠入力に出勤者が登録されていません。");
            }
            else if (MissingClockOutCount > 0)
            {
                reasons.Add($"退勤時刻が未入力の出勤者が {MissingClockOutCount} 名います。");
            }

            if (!IsCastSalesAdjustmentCompleted)
            {
                reasons.Add("キャスト売上額調整が未完了です。");
            }

            if (!IsDrinkBackCompleted)
            {
                reasons.Add("ドリンクバック調整が未入力です。0円の場合も保存してください。");
            }

            return reasons;
        }
    }
}

public class BusinessDayClosingAttendanceItem
{
    public long AttendanceId { get; set; }

    public string PersonType { get; set; } = AttendancePersonTypes.Cast;

    public long PersonId { get; set; }

    public long CastId { get; set; }

    public long StaffId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? DepartmentName { get; set; }

    public string AttendanceStatus { get; set; } = string.Empty;

    public DateTimeOffset? ClockInAt { get; set; }

    public DateTimeOffset? ClockOutAt { get; set; }

    public bool UsesSendService { get; set; }

    public string SearchDisplayName => string.IsNullOrWhiteSpace(DepartmentName)
        ? DisplayName
        : $"{DisplayName}：{DepartmentName}";

    public bool IsCast => AttendancePersonTypes.Normalize(PersonType) == AttendancePersonTypes.Cast;

    public bool IsStaff => AttendancePersonTypes.Normalize(PersonType) == AttendancePersonTypes.Staff;

    public string PersonKey => AttendancePersonKey.Create(PersonType, PersonId);
}

public class BusinessDayClosingAttendanceInput
{
    public long AttendanceId { get; set; }

    public string PersonType { get; set; } = AttendancePersonTypes.Cast;

    public string DisplayName { get; set; } = string.Empty;

    public string? DepartmentName { get; set; }

    public string? ClockInTime { get; set; }

    public string? ClockOutTime { get; set; }

    public bool UsesSendService { get; set; }
}

public class BusinessDayAttendanceSaveResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public int SavedCount { get; init; }

    public static BusinessDayAttendanceSaveResult Success(int savedCount)
    {
        return new BusinessDayAttendanceSaveResult { Succeeded = true, SavedCount = savedCount };
    }

    public static BusinessDayAttendanceSaveResult Failed(string message)
    {
        return new BusinessDayAttendanceSaveResult { Succeeded = false, ErrorMessage = message };
    }
}

public sealed record CurrentBusinessDayAttendanceMutation(
    string OperationId,
    long? ExpectedBusinessDayId,
    long? ExpectedBusinessDayRevision,
    DateOnly BusinessDate,
    IReadOnlyCollection<CurrentBusinessDayAttendanceEntry> CastEntries,
    IReadOnlyCollection<CurrentBusinessDayAttendanceEntry> StaffEntries);

public sealed record CurrentBusinessDayAttendanceEntry(
    long PersonId,
    bool IsSelected,
    string ClockInTime,
    string? ClockOutTime,
    bool UsesSendService,
    string? ExpectedVersion);

public sealed record CurrentBusinessDayAttendanceSaveOutput(
    string OperationId,
    string Status,
    string Message,
    AttendanceEditorSnapshot Snapshot,
    int SavedCount,
    int SavedClockOutCount,
    IReadOnlyList<AttendanceSaveRowResult> RowResults);
