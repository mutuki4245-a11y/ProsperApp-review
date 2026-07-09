using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class AttendanceModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty]
    public ClosingAttendanceInputModel Input { get; set; } = new();

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public DateOnly CurrentBusinessDate { get; private set; }

    public IReadOnlyList<AttendanceTimeOption> ClockInTimeOptions { get; private set; } = [];

    public IReadOnlyList<AttendanceTimeOption> ClockOutTimeOptions { get; private set; } = [];

    public string DefaultClockInTime { get; private set; } = "19:00";

    public string DefaultClockOutTime { get; private set; } = "00:00";

    public string? SuccessMessage { get; private set; }

    public bool IsClosingContext => Request.Path.StartsWithSegments("/Closing/Attendance", StringComparison.OrdinalIgnoreCase);

    public string WorkflowLabel => IsClosingContext ? "締め作業" : "営業中";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        SuccessMessage = TempData["SuccessMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var postedBusinessDayId = Input.BusinessDayId;
        await LoadAsync(cancellationToken, preserveInput: true);
        if (CurrentBusinessDay is not null && postedBusinessDayId != CurrentBusinessDay.BusinessDayId)
        {
            ModelState.AddModelError(string.Empty, "営業日情報が更新されています。画面を再読み込みしてください。");
            return Page();
        }

        ValidateAttendanceInput();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (CurrentBusinessDay is null)
        {
            var ensureResult = await _businessDayRepository.EnsureCurrentAsync(cancellationToken);
            if (!ensureResult.Succeeded || ensureResult.BusinessDay is null)
            {
                ModelState.AddModelError(string.Empty, ensureResult.ErrorMessage ?? "営業日を自動作成できませんでした。");
                return Page();
            }

            CurrentBusinessDay = ensureResult.BusinessDay;
            Input.BusinessDayId = CurrentBusinessDay.BusinessDayId;
        }

        var attendanceEntries = Input.Entries
            .Where(x => x.IsSelected || (x.IsRegistered && !string.IsNullOrWhiteSpace(x.ClockInTime)))
            .Select(x => new BusinessDayAttendanceInput
            {
                CastId = x.CastId,
                IsSelected = x.IsSelected,
                ClockInTime = x.ClockInTime?.Trim() ?? string.Empty
            })
            .ToArray();

        var attendanceResult = await _businessDayRepository.SaveAttendanceAsync(
            CurrentBusinessDay.BusinessDayId,
            attendanceEntries,
            cancellationToken);

        if (!attendanceResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, attendanceResult.ErrorMessage ?? "勤怠入力を保存できませんでした。");
            return Page();
        }

        var savedAttendance = await _businessDayRepository.GetClosingAttendanceAsync(
            CurrentBusinessDay.BusinessDayId,
            cancellationToken);
        var attendanceIdByCastId = savedAttendance
            .GroupBy(x => x.CastId)
            .ToDictionary(x => x.Key, x => x.Last().AttendanceId);
        var clockOutEntries = Input.Entries
            .Where(x => x.IsSelected && !string.IsNullOrWhiteSpace(x.ClockOutTime))
            .Select(x =>
            {
                attendanceIdByCastId.TryGetValue(x.CastId, out var attendanceId);
                return new BusinessDayClosingAttendanceInput
                {
                    AttendanceId = attendanceId,
                    DisplayName = x.DisplayName,
                    DepartmentName = x.DepartmentName,
                    ClockInTime = x.ClockInTime?.Trim(),
                    ClockOutTime = x.ClockOutTime?.Trim(),
                    UsesSendService = x.UsesSendService
                };
            })
            .Where(x => x.AttendanceId > 0)
            .ToArray();

        var savedClockOutCount = 0;
        if (clockOutEntries.Length > 0)
        {
            var closingResult = await _businessDayRepository.SaveClosingAttendanceAsync(
                CurrentBusinessDay.BusinessDayId,
                clockOutEntries,
                cancellationToken);

            if (!closingResult.Succeeded)
            {
                ModelState.AddModelError(string.Empty, closingResult.ErrorMessage ?? "退勤時刻を保存できませんでした。");
                await LoadAsync(cancellationToken);
                return Page();
            }

            savedClockOutCount = closingResult.SavedCount;
        }

        var selectedCount = Input.Entries.Count(x => x.IsSelected);
        TempData["SuccessMessage"] = $"勤怠入力を保存しました。出勤 {selectedCount}名 / 退勤 {savedClockOutCount}名";
        return IsClosingContext
            ? RedirectToPage("/Closing/Index")
            : RedirectToCurrentPath();
    }

    private IActionResult RedirectToCurrentPath()
    {
        return LocalRedirect(Request.PathBase.Add(Request.Path).Value ?? "/Attendance");
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool preserveInput = false)
    {
        var storeContext = await _slipRepository.GetStoreContextAsync(cancellationToken);
        var minuteStep = storeContext?.AttendanceMinuteStep ?? 15;
        ClockInTimeOptions = BuildAttendanceTimeOptions(19, 0, minuteStep);
        ClockOutTimeOptions = BuildAttendanceTimeOptions(24, 0, minuteStep);
        DefaultClockInTime = ResolveDefaultTime(ClockInTimeOptions, "19:00");
        DefaultClockOutTime = ResolveDefaultTime(ClockOutTimeOptions, "00:00");
        CurrentBusinessDate = _storeClock.GetCurrentBusinessDate();
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        var postedByCastId = preserveInput
            ? Input.Entries
                .Where(x => x.CastId > 0)
                .GroupBy(x => x.CastId)
                .ToDictionary(x => x.Key, x => x.Last())
            : [];
        var postedSelectedCastIds = preserveInput
            ? ParseSelectedCastIds(Input.SelectedCastIds)
            : new HashSet<long>();
        IReadOnlyList<BusinessDayClosingAttendanceItem> attendanceItems = CurrentBusinessDay is null
            ? []
            : await _businessDayRepository.GetClosingAttendanceAsync(
                CurrentBusinessDay.BusinessDayId,
                cancellationToken);
        var attendanceByCastId = attendanceItems
            .GroupBy(x => x.CastId)
            .ToDictionary(x => x.Key, x => x.Last());
        var casts = await _slipRepository.GetCastsAsync(cancellationToken);
        var entries = casts
            .Select(cast =>
            {
                postedByCastId.TryGetValue(cast.CastId, out var posted);
                attendanceByCastId.TryGetValue(cast.CastId, out var attendance);
                var clockInTime = posted?.ClockInTime ??
                    _storeClock.FormatStoreTime(attendance?.ClockInAt, string.Empty);
                if (attendance is null && posted is null && string.IsNullOrWhiteSpace(clockInTime))
                {
                    clockInTime = DefaultClockInTime;
                }

                var clockOutTime = posted?.ClockOutTime ?? _storeClock.FormatStoreTime(attendance?.ClockOutAt, string.Empty);
                if (string.IsNullOrWhiteSpace(clockOutTime))
                {
                    clockOutTime = DefaultClockOutTime;
                }

                return new BusinessDayAttendanceEntryInput
                {
                    CastId = cast.CastId,
                    AttendanceId = attendance?.AttendanceId ?? posted?.AttendanceId ?? 0,
                    DisplayName = cast.SearchDisplayName,
                    DepartmentName = cast.DepartmentName,
                    IsSelected = posted?.IsSelected ?? (attendance is not null && !string.IsNullOrWhiteSpace(clockInTime)),
                    IsRegistered = attendance is not null,
                    ClockInTime = clockInTime,
                    ClockOutTime = clockOutTime,
                    UsesSendService = posted?.UsesSendService ?? attendance?.UsesSendService ?? false
                };
            })
            .ToList();
        var listedCastIds = entries.Select(x => x.CastId).ToHashSet();
        entries.AddRange(attendanceItems
            .Where(item => !listedCastIds.Contains(item.CastId))
            .Select(item =>
            {
                postedByCastId.TryGetValue(item.CastId, out var posted);
                var clockInTime = posted?.ClockInTime ?? _storeClock.FormatStoreTime(item.ClockInAt, string.Empty);
                var clockOutTime = posted?.ClockOutTime ?? _storeClock.FormatStoreTime(item.ClockOutAt, string.Empty);
                if (string.IsNullOrWhiteSpace(clockOutTime))
                {
                    clockOutTime = DefaultClockOutTime;
                }

                return new BusinessDayAttendanceEntryInput
                {
                    CastId = item.CastId,
                    AttendanceId = item.AttendanceId,
                    DisplayName = item.SearchDisplayName,
                    DepartmentName = item.DepartmentName,
                    IsSelected = posted?.IsSelected ?? !string.IsNullOrWhiteSpace(clockInTime),
                    IsRegistered = true,
                    ClockInTime = clockInTime,
                    ClockOutTime = clockOutTime,
                    UsesSendService = posted?.UsesSendService ?? item.UsesSendService
                };
            }));

        if (postedSelectedCastIds.Count > 0)
        {
            foreach (var entry in entries)
            {
                entry.IsSelected = postedSelectedCastIds.Contains(entry.CastId);
            }
        }

        Input = new ClosingAttendanceInputModel
        {
            BusinessDayId = CurrentBusinessDay?.BusinessDayId,
            SelectedCastIds = string.Join(',', entries.Where(x => x.IsSelected).Select(x => x.CastId)),
            Entries = entries
        };
    }

    private static HashSet<long> ParseSelectedCastIds(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => long.TryParse(x, CultureInfo.InvariantCulture, out var castId) ? castId : 0)
            .Where(x => x > 0)
            .ToHashSet();
    }

    private static IReadOnlyList<AttendanceTimeOption> BuildAttendanceTimeOptions(
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
            if (!seenValues.Add(value))
            {
                continue;
            }

            options.Add(new AttendanceTimeOption(
                value,
                $"{totalMinutes / 60:00}:{totalMinutes % 60:00}"));
        }

        return options;
    }

    private static string ResolveDefaultTime(IReadOnlyList<AttendanceTimeOption> timeOptions, string preferredValue)
    {
        return timeOptions.Any(x => string.Equals(x.Value, preferredValue, StringComparison.Ordinal))
            ? preferredValue
            : timeOptions.FirstOrDefault()?.Value ?? string.Empty;
    }

    private void ValidateAttendanceInput()
    {
        if (Input.Entries.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "キャスト情報が未登録です。先にキャスト情報を登録してください。");
            return;
        }

        var selectedRows = Input.Entries.Where(x => x.IsSelected).ToList();
        if (selectedRows.Count == 0)
        {
            ModelState.AddModelError(nameof(Input.Entries), "出勤キャストを1名以上選択してください。");
            return;
        }

        var validClockInTimes = ClockInTimeOptions.Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var validClockOutTimes = ClockOutTimeOptions.Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < Input.Entries.Count; i++)
        {
            var entry = Input.Entries[i];
            TimeOnly? clockInTime = null;
            TimeOnly? clockOutTime = null;

            if (entry.CastId <= 0)
            {
                ModelState.AddModelError(string.Empty, "キャストの選択内容を確認してください。");
                continue;
            }

            if (!entry.IsSelected)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.ClockInTime) ||
                !TimeOnly.TryParse(entry.ClockInTime, CultureInfo.InvariantCulture, out var parsedClockInTime) ||
                !validClockInTimes.Contains(entry.ClockInTime))
            {
                ModelState.AddModelError(
                    $"Input.Entries[{i}].ClockInTime",
                    $"{entry.DisplayName} の出勤時刻を選択してください。");
            }
            else
            {
                clockInTime = parsedClockInTime;
            }

            if (!string.IsNullOrWhiteSpace(entry.ClockOutTime))
            {
                if (!TimeOnly.TryParse(entry.ClockOutTime, CultureInfo.InvariantCulture, out var parsedClockOutTime) ||
                    !validClockOutTimes.Contains(entry.ClockOutTime))
                {
                    ModelState.AddModelError(
                        $"Input.Entries[{i}].ClockOutTime",
                        $"{entry.DisplayName} の退勤時刻を確認してください。");
                }
                else
                {
                    clockOutTime = parsedClockOutTime;
                }
            }

            if (CurrentBusinessDay is not null &&
                clockInTime is { } validClockInTime &&
                clockOutTime is { } validClockOutTime &&
                _storeClock.ComposeBusinessDateTime(CurrentBusinessDay.BusinessDate, validClockOutTime) <
                _storeClock.ComposeBusinessDateTime(CurrentBusinessDay.BusinessDate, validClockInTime))
            {
                ModelState.AddModelError(
                    $"Input.Entries[{i}].ClockOutTime",
                    $"{entry.DisplayName} の退勤時刻は出勤時刻以降にしてください。");
            }
        }
    }
}

public class ClosingAttendanceInputModel
{
    public long? BusinessDayId { get; set; }

    public string SelectedCastIds { get; set; } = string.Empty;

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
