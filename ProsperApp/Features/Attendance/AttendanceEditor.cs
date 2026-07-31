using System.Globalization;
using System.Text.Json;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Features.Attendance;

public static class AttendanceEditor
{
    public static Result<ClosingAttendanceInputModel> MergeSelectedEntries(ClosingAttendanceInputModel input)
    {
        if (string.IsNullOrWhiteSpace(input.SelectedEntriesJson))
        {
            return Result<ClosingAttendanceInputModel>.Success(input);
        }

        List<PostedAttendanceEntry> selectedEntries;
        try
        {
            selectedEntries = JsonSerializer.Deserialize<List<PostedAttendanceEntry>>(input.SelectedEntriesJson) ?? [];
        }
        catch (JsonException)
        {
            return Result<ClosingAttendanceInputModel>.Failure(
                ResultFailureKind.InvalidInput,
                "選択した勤怠行を読み取れませんでした。画面を再読み込みしてください。");
        }

        var inputByPersonKey = input.Entries
            .Where(x => x.PersonId > 0)
            .GroupBy(AttendancePersonKey.Create)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);

        foreach (var posted in selectedEntries)
        {
            var personType = AttendancePersonTypes.Normalize(posted.PersonType);
            var personId = ResolvePostedPersonId(posted, personType);
            if (personId <= 0)
            {
                continue;
            }

            var personKey = AttendancePersonKey.Create(personType, personId);
            if (!inputByPersonKey.TryGetValue(personKey, out var entry))
            {
                entry = new BusinessDayAttendanceEntryInput
                {
                    PersonType = personType,
                    CastId = personType == AttendancePersonTypes.Cast ? personId : 0,
                    StaffId = personType == AttendancePersonTypes.Staff ? personId : 0
                };
                input.Entries.Add(entry);
                inputByPersonKey[personKey] = entry;
            }

            entry.PersonType = personType;
            if (personType == AttendancePersonTypes.Staff)
            {
                entry.StaffId = personId;
            }
            else
            {
                entry.CastId = personId;
            }
            entry.IsSelected = true;
            entry.AttendanceId = posted.AttendanceId > 0 ? posted.AttendanceId : entry.AttendanceId;
            entry.DisplayName = FirstNonEmpty(posted.DisplayName, entry.DisplayName);
            entry.DepartmentName = FirstNonEmpty(posted.DepartmentName, entry.DepartmentName);
            entry.IsRegistered = posted.IsRegistered || entry.IsRegistered;
            entry.ClockInTime = FirstNonEmpty(posted.ClockInTime, entry.ClockInTime);
            entry.ClockOutTime = FirstNonEmpty(posted.ClockOutTime, entry.ClockOutTime);
            entry.UsesSendService = posted.UsesSendService || entry.UsesSendService;
        }

        var selectedCastIds = ParseSelectedCastIds(input.SelectedCastIds);
        var selectedAttendanceKeys = AttendancePersonKey.ParseMany(input.SelectedAttendanceKeys);
        foreach (var posted in selectedEntries)
        {
            var personType = AttendancePersonTypes.Normalize(posted.PersonType);
            var personId = ResolvePostedPersonId(posted, personType);
            if (personId <= 0)
            {
                continue;
            }

            selectedAttendanceKeys.Add(AttendancePersonKey.Create(personType, personId));
            if (personType == AttendancePersonTypes.Cast)
            {
                selectedCastIds.Add(personId);
            }
        }

        input.SelectedCastIds = string.Join(',', selectedCastIds);
        input.SelectedAttendanceKeys = string.Join(',', selectedAttendanceKeys);
        return Result<ClosingAttendanceInputModel>.Success(input);
    }

    public static IReadOnlyList<AttendanceTimeOption> BuildTimeOptions(
        int centerHour,
        int centerMinute,
        int minuteStep)
    {
        if (minuteStep <= 0 || 60 % minuteStep != 0)
        {
            minuteStep = 15;
        }

        var centerTotalMinutes = centerHour * 60 + centerMinute;
        var startMinutes = centerTotalMinutes - 12 * 60;
        var endMinutes = centerTotalMinutes + 12 * 60;
        var options = new List<AttendanceTimeOption>();
        var seenValues = new HashSet<string>(StringComparer.Ordinal);

        for (var totalMinutes = startMinutes; totalMinutes < endMinutes; totalMinutes += minuteStep)
        {
            var normalizedMinutes = ((totalMinutes % (24 * 60)) + (24 * 60)) % (24 * 60);
            var value = $"{normalizedMinutes / 60:00}:{normalizedMinutes % 60:00}";
            if (seenValues.Add(value))
            {
                options.Add(new AttendanceTimeOption(
                    value,
                    $"{totalMinutes / 60:00}:{totalMinutes % 60:00}"));
            }
        }

        return options;
    }

    public static string ResolveDefaultTime(
        IReadOnlyList<AttendanceTimeOption> timeOptions,
        string preferredValue)
    {
        return timeOptions.Any(x => string.Equals(x.Value, preferredValue, StringComparison.Ordinal))
            ? preferredValue
            : timeOptions.FirstOrDefault()?.Value ?? string.Empty;
    }

    public static HashSet<long> ParseSelectedCastIds(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => long.TryParse(x, CultureInfo.InvariantCulture, out var castId) ? castId : 0)
            .Where(x => x > 0)
            .ToHashSet();
    }

    public static HashSet<string> ParseSelectedAttendanceKeys(string? value)
    {
        return AttendancePersonKey.ParseMany(value);
    }

    public static IReadOnlyList<AttendanceValidationError> Validate(
        ClosingAttendanceInputModel input,
        IReadOnlyList<AttendanceTimeOption> clockInOptions,
        IReadOnlyList<AttendanceTimeOption> clockOutOptions,
        DateOnly businessDate,
        IStoreClock storeClock,
        string? castLoadErrorMessage)
    {
        var selectedCastIds = ParseSelectedCastIds(input.SelectedCastIds);
        var selectedAttendanceKeys = ParseSelectedAttendanceKeys(input.SelectedAttendanceKeys);
        foreach (var entry in input.Entries)
        {
            entry.PersonType = AttendancePersonTypes.Normalize(entry.PersonType);
            entry.IsSelected = entry.IsSelected ||
                selectedAttendanceKeys.Contains(AttendancePersonKey.Create(entry)) ||
                (entry.PersonType == AttendancePersonTypes.Cast && selectedCastIds.Contains(entry.CastId));
        }

        if (input.Entries.Count == 0)
        {
            return
            [
                new AttendanceValidationError(
                    string.Empty,
                    string.IsNullOrWhiteSpace(castLoadErrorMessage)
                        ? "キャストまたはスタッフ情報が未登録です。先にマスタ情報を登録してください。"
                        : castLoadErrorMessage)
            ];
        }

        if (input.Entries.All(x => !x.IsSelected))
        {
            return [new AttendanceValidationError(nameof(input.Entries), "出勤者を1名以上選択してください。")];
        }

        var errors = new List<AttendanceValidationError>();
        var validClockInTimes = clockInOptions.Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var validClockOutTimes = clockOutOptions.Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < input.Entries.Count; index++)
        {
            var entry = input.Entries[index];
            if (entry.PersonId <= 0)
            {
                errors.Add(new AttendanceValidationError(string.Empty, "出勤者の選択内容を確認してください。"));
                continue;
            }

            if (!entry.IsSelected)
            {
                continue;
            }

            var clockInTime = ParseTime(entry.ClockInTime, validClockInTimes);
            if (clockInTime is null)
            {
                errors.Add(new AttendanceValidationError(
                    $"Input.Entries[{index}].ClockInTime",
                    $"{entry.DisplayName} の出勤時刻を選択してください。"));
            }

            TimeOnly? clockOutTime = null;
            if (!string.IsNullOrWhiteSpace(entry.ClockOutTime))
            {
                clockOutTime = ParseTime(entry.ClockOutTime, validClockOutTimes);
                if (clockOutTime is null)
                {
                    errors.Add(new AttendanceValidationError(
                        $"Input.Entries[{index}].ClockOutTime",
                        $"{entry.DisplayName} の退勤時刻を確認してください。"));
                }
            }

            if (businessDate != default &&
                clockInTime is { } validClockInTime &&
                clockOutTime is { } validClockOutTime &&
                storeClock.ComposeBusinessDateTime(businessDate, validClockOutTime) <=
                storeClock.ComposeBusinessDateTime(businessDate, validClockInTime))
            {
                errors.Add(new AttendanceValidationError(
                    $"Input.Entries[{index}].ClockOutTime",
                    $"{entry.DisplayName} の退勤時刻は出勤時刻より後にしてください。"));
            }
        }

        return errors;
    }

    private static TimeOnly? ParseTime(string? value, IReadOnlySet<string> validTimes)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               validTimes.Contains(value) &&
               TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string FirstNonEmpty(string? primary, string? fallback)
    {
        return string.IsNullOrWhiteSpace(primary)
            ? fallback ?? string.Empty
            : primary.Trim();
    }

    private static long ResolvePostedPersonId(PostedAttendanceEntry entry, string personType)
    {
        if (entry.PersonId > 0)
        {
            return entry.PersonId;
        }

        return personType == AttendancePersonTypes.Staff
            ? entry.StaffId
            : entry.CastId;
    }
}

public sealed record AttendanceValidationError(string Field, string Message);
