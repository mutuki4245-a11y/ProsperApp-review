using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class OpeningAttendanceModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreOrderRepository orderRepository,
    ILocalSettingsProvider localSettingsProvider,
    IStoreClock storeClock) : PageModel
{
    private const string AttendanceDraftSessionKey = "OpeningAttendanceDraft";

    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IStoreClock _storeClock = storeClock;

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public StoreContext? StoreContext { get; set; }

    [BindProperty]
    public List<OpeningAttendanceCastInputModel> AttendanceCasts { get; set; } = [];

    public IReadOnlyList<string> TimeOptions { get; set; } = [];

    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        await LoadOptionsAsync(cancellationToken);
        SuccessMessage = TempData["SuccessMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        await LoadOptionsAsync(cancellationToken, preserveAttendance: true);
        ValidateAttendanceInput();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (CurrentBusinessDay is not null)
        {
            var attendanceEntries = AttendanceCasts
                .Where(x => x.IsSelected || x.IsRegistered)
                .Select(x => new BusinessDayAttendanceInput
                {
                    CastId = x.CastId,
                    IsSelected = x.IsSelected,
                    ClockInTime = x.ClockInTime?.Trim() ?? string.Empty
                })
                .ToArray();

            var result = await _businessDayRepository.SaveAttendanceAsync(
                CurrentBusinessDay.BusinessDayId,
                attendanceEntries,
                cancellationToken);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "出勤登録を保存できませんでした。");
                return Page();
            }

            TempData["SuccessMessage"] = $"出勤登録を保存しました。{AttendanceCasts.Count(x => x.IsSelected)}名";
            return RedirectToPage("/Opening/Attendance");
        }

        SaveAttendanceDraft();
        await LoadOptionsAsync(cancellationToken);
        TempData["SuccessMessage"] = $"出勤登録を保存しました。{AttendanceCasts.Count(x => x.IsSelected)}名";
        return RedirectToPage("/Opening/Index");
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken, bool preserveAttendance = false)
    {
        var postedRows = preserveAttendance
            ? AttendanceCasts.Select(x => new AttendanceDraftEntry(x.CastId, x.ClockInTime, x.IsSelected)).ToList()
            : [];
        var draft = preserveAttendance
            ? postedRows.Where(x => x.IsSelected).ToList()
            : CurrentBusinessDay is null
                ? LoadAttendanceDraft()
                : [];
        var registeredCasts = CurrentBusinessDay is null
            ? []
            : await _orderRepository.GetAttendanceCastsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        var registeredByCastId = registeredCasts.ToDictionary(x => x.CastId);
        HashSet<long> registeredCastIds = registeredByCastId.Keys.ToHashSet();
        HashSet<long> selectedCastIds = preserveAttendance
            ? postedRows.Where(x => x.IsSelected).Select(x => x.CastId).ToHashSet()
            : draft.Select(x => x.CastId).Concat(registeredCastIds).ToHashSet();
        var minuteStep = _localSettingsProvider.GetCurrent().AttendanceMinuteStep is 30 ? 30 : 15;
        TimeOptions = _storeClock.BuildTimeOptions(minuteStep);
        var defaultClockInTime = "18:00";
        HashSet<string> validTimes = TimeOptions.ToHashSet(StringComparer.Ordinal);
        Dictionary<long, string?> clockInTimes = registeredByCastId.ToDictionary(
            x => x.Key,
            x => validTimes.Contains(x.Value.ClockInTime ?? string.Empty) ? x.Value.ClockInTime : defaultClockInTime);
        foreach (var entry in draft)
        {
            clockInTimes[entry.CastId] = validTimes.Contains(entry.ClockInTime ?? string.Empty)
                ? entry.ClockInTime
                : defaultClockInTime;
        }

        StoreContext = await _slipRepository.GetStoreContextAsync(cancellationToken);
        var casts = await _slipRepository.GetCastsAsync(cancellationToken);
        AttendanceCasts = casts
            .Select(cast => new OpeningAttendanceCastInputModel
            {
                CastId = cast.CastId,
                DisplayName = cast.SearchDisplayName,
                DepartmentName = cast.DepartmentName,
                IsSelected = selectedCastIds.Contains(cast.CastId),
                IsRegistered = registeredCastIds.Contains(cast.CastId),
                ClockInTime = clockInTimes.TryGetValue(cast.CastId, out var preservedTime)
                    ? preservedTime
                    : defaultClockInTime
            })
            .ToList();
    }

    private void SaveAttendanceDraft()
    {
        var entries = AttendanceCasts
            .Where(x => x.IsSelected)
            .Select(x => new AttendanceDraftEntry(x.CastId, x.ClockInTime?.Trim()))
            .ToList();
        HttpContext.Session.SetString(AttendanceDraftSessionKey, JsonSerializer.Serialize(entries));
    }

    private List<AttendanceDraftEntry> LoadAttendanceDraft()
    {
        var json = HttpContext.Session.GetString(AttendanceDraftSessionKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AttendanceDraftEntry>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void ValidateAttendanceInput()
    {
        if (AttendanceCasts.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "キャストマスタが未登録です。先にキャスト情報を登録してください。");
            return;
        }

        var selectedRows = AttendanceCasts.Where(x => x.IsSelected).ToList();
        if (selectedRows.Count == 0)
        {
            ModelState.AddModelError(nameof(AttendanceCasts), "出勤キャストを1名以上選択してください。");
            return;
        }

        foreach (var row in selectedRows)
        {
            if (string.IsNullOrWhiteSpace(row.ClockInTime) ||
                !TimeOnly.TryParse(row.ClockInTime, CultureInfo.InvariantCulture, out _) ||
                !TimeOptions.Contains(row.ClockInTime))
            {
                ModelState.AddModelError(nameof(AttendanceCasts), $"{row.DisplayName} の出勤時刻を入力してください。");
            }
        }
    }

    private sealed record AttendanceDraftEntry(long CastId, string? ClockInTime, bool IsSelected = true);
}
