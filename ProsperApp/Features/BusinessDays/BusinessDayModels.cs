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
