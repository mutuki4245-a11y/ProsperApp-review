using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ProsperApp.Features.Attendance;

public class ClosingAttendanceInputModel
{
    public string OperationId { get; set; } = Guid.NewGuid().ToString("D");

    public long? BusinessDayId { get; set; }

    public long? BusinessDayRevision { get; set; }

    public DateOnly? BusinessDate { get; set; }

    public string SelectedCastIds { get; set; } = string.Empty;

    public string SelectedAttendanceKeys { get; set; } = string.Empty;

    public string SelectedEntriesJson { get; set; } = string.Empty;

    public List<BusinessDayAttendanceEntryInput> Entries { get; set; } = [];
}

public class BusinessDayAttendanceEntryInput
{
    public string PersonType { get; set; } = AttendancePersonTypes.Cast;

    public long CastId { get; set; }

    public long StaffId { get; set; }

    public long AttendanceId { get; set; }

    public string? ExpectedVersion { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? DepartmentName { get; set; }

    public bool IsSelected { get; set; }

    public bool IsRegistered { get; set; }

    [StringLength(5, ErrorMessage = "出勤時刻を確認してください。")]
    public string? ClockInTime { get; set; }

    [StringLength(5, ErrorMessage = "退勤時刻を確認してください。")]
    public string? ClockOutTime { get; set; }

    public bool UsesSendService { get; set; }

    public long PersonId => AttendancePersonTypes.Normalize(PersonType) == AttendancePersonTypes.Staff
        ? StaffId
        : CastId;

    public string PersonKey => AttendancePersonKey.Create(PersonType, PersonId);
}

public sealed record AttendanceTimeOption(string Value, string Label);

public sealed record AttendanceRosterPerson(
    string PersonType,
    long PersonId,
    string DisplayName,
    string? DepartmentName,
    bool IsActive);

public sealed record AttendanceEditorEntrySnapshot(
    long AttendanceId,
    string PersonType,
    long PersonId,
    string DisplayName,
    string? DepartmentName,
    string AttendanceStatus,
    string ClockInTime,
    string? ClockOutTime,
    bool UsesSendService,
    string Version);

public sealed record AttendanceEditorSnapshot(
    bool HasBusinessDay,
    bool Unchanged,
    StoreBusinessDay? BusinessDay,
    long BusinessDayRevision,
    IReadOnlyList<AttendanceEditorEntrySnapshot> AttendanceEntries,
    IReadOnlyList<StoreOrderAttendanceCastOption> AttendingCasts);

public sealed record AttendanceShellBootstrap(
    StoreContext StoreContext,
    IReadOnlyList<AttendanceRosterPerson> Roster,
    AttendanceEditorSnapshot? InitialSnapshot,
    bool WasFetched);

public sealed record AttendanceSaveRowResult(
    string PersonType,
    long PersonId,
    string Status,
    string? Message);

internal sealed record PostedAttendanceEntry(
    [property: JsonPropertyName("person_type")] string? PersonType,
    [property: JsonPropertyName("person_id")] long PersonId,
    [property: JsonPropertyName("cast_id")] long CastId,
    [property: JsonPropertyName("staff_id")] long StaffId,
    [property: JsonPropertyName("attendance_id")] long AttendanceId,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("department_name")] string? DepartmentName,
    [property: JsonPropertyName("is_registered")] bool IsRegistered,
    [property: JsonPropertyName("clock_in_time")] string? ClockInTime,
    [property: JsonPropertyName("clock_out_time")] string? ClockOutTime,
    [property: JsonPropertyName("uses_send_service")] bool UsesSendService);

public static class AttendancePersonTypes
{
    public const string Cast = "cast";
    public const string Staff = "staff";

    public static string Normalize(string? value)
    {
        return string.Equals(value, Staff, StringComparison.OrdinalIgnoreCase)
            ? Staff
            : Cast;
    }
}

public static class AttendancePersonKey
{
    public static string Create(string? personType, long personId)
    {
        return personId > 0
            ? $"{AttendancePersonTypes.Normalize(personType)}:{personId}"
            : string.Empty;
    }

    public static string Create(BusinessDayAttendanceEntryInput entry)
    {
        return Create(entry.PersonType, entry.PersonId);
    }

    public static HashSet<string> ParseMany(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Contains(':', StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }
}
