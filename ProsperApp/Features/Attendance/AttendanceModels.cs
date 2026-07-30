using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ProsperApp.Features.Attendance;

public class ClosingAttendanceInputModel
{
    public long? BusinessDayId { get; set; }

    public string SelectedCastIds { get; set; } = string.Empty;

    public string SelectedEntriesJson { get; set; } = string.Empty;

    public List<BusinessDayAttendanceEntryInput> Entries { get; set; } = [];
}

public class BusinessDayAttendanceEntryInput
{
    public long CastId { get; set; }

    public long AttendanceId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? DepartmentName { get; set; }

    public bool IsSelected { get; set; }

    public bool IsRegistered { get; set; }

    [StringLength(5, ErrorMessage = "出勤時刻を確認してください。")]
    public string? ClockInTime { get; set; }

    [StringLength(5, ErrorMessage = "退勤時刻を確認してください。")]
    public string? ClockOutTime { get; set; }

    public bool UsesSendService { get; set; }
}

public sealed record AttendanceTimeOption(string Value, string Label);

internal sealed record PostedAttendanceEntry(
    [property: JsonPropertyName("cast_id")] long CastId,
    [property: JsonPropertyName("attendance_id")] long AttendanceId,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("department_name")] string? DepartmentName,
    [property: JsonPropertyName("is_registered")] bool IsRegistered,
    [property: JsonPropertyName("clock_in_time")] string? ClockInTime,
    [property: JsonPropertyName("clock_out_time")] string? ClockOutTime,
    [property: JsonPropertyName("uses_send_service")] bool UsesSendService);
