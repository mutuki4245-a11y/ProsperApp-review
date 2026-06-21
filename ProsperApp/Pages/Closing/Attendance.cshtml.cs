using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ClosingAttendanceModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    ILocalSettingsProvider localSettingsProvider,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty]
    public ClosingAttendanceInputModel Input { get; set; } = new();

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public IReadOnlyList<string> TimeOptions { get; private set; } = [];

    public string? SuccessMessage { get; private set; }

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
        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業中の営業日がありません。");
            return Page();
        }

        if (postedBusinessDayId != CurrentBusinessDay.BusinessDayId)
        {
            ModelState.AddModelError(string.Empty, "営業日情報が更新されています。画面を再読み込みしてください。");
            return Page();
        }

        ValidateAttendanceInput();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _businessDayRepository.SaveClosingAttendanceAsync(
            CurrentBusinessDay.BusinessDayId,
            Input.Entries,
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "勤怠入力を保存できませんでした。");
            return Page();
        }

        TempData["SuccessMessage"] = $"勤怠入力を保存しました。{result.SavedCount}名";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool preserveInput = false)
    {
        var minuteStep = _localSettingsProvider.GetCurrent().AttendanceMinuteStep is 30 ? 30 : 15;
        TimeOptions = _storeClock.BuildTimeOptions(minuteStep);
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            Input = new ClosingAttendanceInputModel();
            return;
        }

        var postedByAttendanceId = preserveInput
            ? Input.Entries
                .Where(x => x.AttendanceId > 0)
                .GroupBy(x => x.AttendanceId)
                .ToDictionary(x => x.Key, x => x.Last())
            : [];
        var attendanceItems = await _businessDayRepository.GetClosingAttendanceAsync(
            CurrentBusinessDay.BusinessDayId,
            cancellationToken);

        Input = new ClosingAttendanceInputModel
        {
            BusinessDayId = CurrentBusinessDay.BusinessDayId,
            Entries = attendanceItems
                .Select(item =>
                {
                    postedByAttendanceId.TryGetValue(item.AttendanceId, out var posted);
                    return new BusinessDayClosingAttendanceInput
                    {
                        AttendanceId = item.AttendanceId,
                        DisplayName = item.SearchDisplayName,
                        DepartmentName = item.DepartmentName,
                        ClockInTime = posted?.ClockInTime ?? _storeClock.FormatStoreTime(item.ClockInAt, string.Empty),
                        ClockOutTime = posted?.ClockOutTime ?? _storeClock.FormatStoreTime(item.ClockOutAt, string.Empty),
                        UsesSendService = posted?.UsesSendService ?? item.UsesSendService
                    };
                })
                .ToList()
        };
    }

    private void ValidateAttendanceInput()
    {
        if (Input.Entries.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "出勤登録済みキャストがいません。");
            return;
        }

        var validTimes = TimeOptions.ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < Input.Entries.Count; i++)
        {
            var entry = Input.Entries[i];
            TimeOnly? clockInTime = null;
            TimeOnly? clockOutTime = null;

            if (entry.AttendanceId <= 0)
            {
                ModelState.AddModelError(string.Empty, "出勤登録済みキャストの選択内容を確認してください。");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.ClockInTime) ||
                !TimeOnly.TryParse(entry.ClockInTime, CultureInfo.InvariantCulture, out var parsedClockInTime) ||
                !validTimes.Contains(entry.ClockInTime))
            {
                ModelState.AddModelError(
                    $"Input.Entries[{i}].ClockInTime",
                    $"{entry.DisplayName} の出勤時刻を選択してください。");
            }
            else
            {
                clockInTime = parsedClockInTime;
            }

            if (string.IsNullOrWhiteSpace(entry.ClockOutTime) ||
                !TimeOnly.TryParse(entry.ClockOutTime, CultureInfo.InvariantCulture, out var parsedClockOutTime) ||
                !validTimes.Contains(entry.ClockOutTime))
            {
                ModelState.AddModelError(
                    $"Input.Entries[{i}].ClockOutTime",
                    $"{entry.DisplayName} の退勤時刻を選択してください。");
            }
            else
            {
                clockOutTime = parsedClockOutTime;
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

    public List<BusinessDayClosingAttendanceInput> Entries { get; set; } = [];
}
