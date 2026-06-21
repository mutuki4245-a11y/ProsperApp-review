using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Models;

public class DrinkDeliveryInputModel
{
    public long? BusinessDayId { get; set; }

    [Display(Name = "納品額")]
    [Range(0, 999999999999, ErrorMessage = "納品額は0円以上で入力してください。")]
    public decimal DrinkDeliveryAmount { get; set; }
}

public class BusinessDayAmountSaveResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public decimal Amount { get; init; }

    public static BusinessDayAmountSaveResult Success(decimal amount)
    {
        return new BusinessDayAmountSaveResult { Succeeded = true, Amount = amount };
    }

    public static BusinessDayAmountSaveResult Failed(string message)
    {
        return new BusinessDayAmountSaveResult { Succeeded = false, ErrorMessage = message };
    }
}

public class BusinessDayDrinkDeliveryStatus
{
    public decimal Amount { get; init; }

    public bool IsEntered { get; init; }
}

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

    public int PendingReceiptCount { get; init; }

    public bool ReceiptsEnabled { get; init; }

    public bool CanClose =>
        BusinessDay is not null &&
        OpenSlipCount == 0 &&
        IsDrinkDeliveryAmountEntered &&
        AttendanceCount > 0 &&
        MissingClockOutCount == 0 &&
        (!ReceiptsEnabled || PendingReceiptCount == 0);

    public IReadOnlyList<string> BlockReasons
    {
        get
        {
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
                reasons.Add("勤怠入力に出勤キャストが登録されていません。");
            }
            else if (MissingClockOutCount > 0)
            {
                reasons.Add($"退勤時刻が未入力のキャストが {MissingClockOutCount} 名います。");
            }

            if (ReceiptsEnabled && PendingReceiptCount > 0)
            {
                reasons.Add($"未入力領収書が {PendingReceiptCount} 件あります。");
            }

            return reasons;
        }
    }
}

public class BusinessDayClosingAttendanceItem
{
    public long AttendanceId { get; set; }

    public long CastId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? DepartmentName { get; set; }

    public string AttendanceStatus { get; set; } = string.Empty;

    public DateTimeOffset? ClockInAt { get; set; }

    public DateTimeOffset? ClockOutAt { get; set; }

    public bool UsesSendService { get; set; }

    public string SearchDisplayName => string.IsNullOrWhiteSpace(DepartmentName)
        ? DisplayName
        : $"{DisplayName}：{DepartmentName}";
}

public class BusinessDayClosingAttendanceInput
{
    public long AttendanceId { get; set; }

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
