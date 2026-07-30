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

        var inputByCastId = input.Entries
            .Where(x => x.CastId > 0)
            .GroupBy(x => x.CastId)
            .ToDictionary(x => x.Key, x => x.Last());

        foreach (var posted in selectedEntries.Where(x => x.CastId > 0))
        {
            if (!inputByCastId.TryGetValue(posted.CastId, out var entry))
            {
                entry = new BusinessDayAttendanceEntryInput { CastId = posted.CastId };
                input.Entries.Add(entry);
                inputByCastId[posted.CastId] = entry;
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
        foreach (var castId in selectedEntries.Select(x => x.CastId).Where(x => x > 0))
        {
            selectedCastIds.Add(castId);
        }

        input.SelectedCastIds = string.Join(',', selectedCastIds);
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

    public static IReadOnlyList<AttendanceValidationError> Validate(
        ClosingAttendanceInputModel input,
        IReadOnlyList<AttendanceTimeOption> clockInOptions,
        IReadOnlyList<AttendanceTimeOption> clockOutOptions,
        DateOnly businessDate,
        IStoreClock storeClock,
        string? castLoadErrorMessage)
    {
        var selectedCastIds = ParseSelectedCastIds(input.SelectedCastIds);
        foreach (var entry in input.Entries)
        {
            entry.IsSelected = entry.IsSelected || selectedCastIds.Contains(entry.CastId);
        }

        if (input.Entries.Count == 0)
        {
            return
            [
                new AttendanceValidationError(
                    string.Empty,
                    string.IsNullOrWhiteSpace(castLoadErrorMessage)
                        ? "キャスト情報が未登録です。先にキャスト情報を登録してください。"
                        : castLoadErrorMessage)
            ];
        }

        if (input.Entries.All(x => !x.IsSelected))
        {
            return [new AttendanceValidationError(nameof(input.Entries), "出勤キャストを1名以上選択してください。")];
        }

        var errors = new List<AttendanceValidationError>();
        var validClockInTimes = clockInOptions.Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var validClockOutTimes = clockOutOptions.Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < input.Entries.Count; index++)
        {
            var entry = input.Entries[index];
            if (entry.CastId <= 0)
            {
                errors.Add(new AttendanceValidationError(string.Empty, "キャストの選択内容を確認してください。"));
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
}

public sealed record AttendanceValidationError(string Field, string Message);
