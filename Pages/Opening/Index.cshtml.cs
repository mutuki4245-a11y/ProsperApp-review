using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class OpeningModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreOrderRepository orderRepository,
    IStoreClock storeClock) : PageModel
{
    private const string AttendanceDraftSessionKey = "OpeningAttendanceDraft";

    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty]
    [Display(Name = "営業日")]
    [Required(ErrorMessage = "営業日を入力してください。")]
    public DateOnly? BusinessDate { get; set; }

    [BindProperty]
    [Display(Name = "メモ")]
    [StringLength(500, ErrorMessage = "メモは500文字以内で入力してください。")]
    public string? Memo { get; set; }

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public StoreContext? StoreContext { get; set; }

    public string? SuccessMessage { get; set; }

    [BindProperty]
    public List<OpeningAttendanceCastInputModel> AttendanceCasts { get; set; } = [];

    public IReadOnlyList<StoreOrderItemOption> Items { get; set; } = [];

    public int SelectedAttendanceCount => AttendanceCasts.Count(x => x.IsSelected);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        BusinessDate ??= DateOnly.FromDateTime(_storeClock.GetStoreNow());
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        await LoadOpeningOptionsAsync(cancellationToken);
        SuccessMessage = TempData["SuccessMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        BusinessDate ??= DateOnly.FromDateTime(_storeClock.GetStoreNow());
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        await LoadOpeningOptionsAsync(cancellationToken);
        if (CurrentBusinessDay is not null)
        {
            ModelState.AddModelError(string.Empty, $"営業日 {CurrentBusinessDay.BusinessDate:yyyy-MM-dd} は既に営業中です。");
            return Page();
        }

        if (!ModelState.IsValid || BusinessDate is null)
        {
            return Page();
        }

        ValidateOpeningReadiness();
        ValidateAttendanceInput();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var today = DateOnly.FromDateTime(_storeClock.GetStoreNow());
        if (BusinessDate.Value > today)
        {
            ModelState.AddModelError(nameof(BusinessDate), "未来日は営業日に指定できません。");
            return Page();
        }

        if (BusinessDate.Value < today.AddDays(-2))
        {
            ModelState.AddModelError(nameof(BusinessDate), "営業日は過去2日以内で指定してください。");
            return Page();
        }

        var attendanceEntries = AttendanceCasts
            .Where(x => x.IsSelected)
            .Select(x => new BusinessDayAttendanceInput
            {
                CastId = x.CastId,
                ClockInTime = x.ClockInTime?.Trim() ?? string.Empty
            })
            .ToArray();

        var validCastIds = AttendanceCasts.Select(x => x.CastId).ToHashSet();
        if (attendanceEntries.Any(x => !validCastIds.Contains(x.CastId)))
        {
            ModelState.AddModelError(string.Empty, "出勤キャストの選択内容を確認してください。");
            return Page();
        }

        var result = await _businessDayRepository.OpenAsync(BusinessDate.Value, Memo, attendanceEntries, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "営業日を開始できませんでした。");
            return Page();
        }

        CurrentBusinessDay = result.BusinessDay;
        HttpContext.Session.Remove(AttendanceDraftSessionKey);
        TempData["SuccessMessage"] = $"営業日 {CurrentBusinessDay?.BusinessDate:yyyy-MM-dd} を開始しました。";
        return RedirectToPage("/Index");
    }

    private async Task LoadOpeningOptionsAsync(CancellationToken cancellationToken, bool preserveAttendance = false)
    {
        var draft = preserveAttendance
            ? AttendanceCasts.Where(x => x.IsSelected).Select(x => new AttendanceDraftEntry(x.CastId, x.ClockInTime)).ToList()
            : CurrentBusinessDay is null
                ? LoadAttendanceDraft()
                : [];
        var registeredCastIds = CurrentBusinessDay is null
            ? []
            : (await _orderRepository.GetAttendanceCastsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken))
                .Select(x => x.CastId)
                .ToHashSet();
        HashSet<long> selectedCastIds = draft.Select(x => x.CastId).Concat(registeredCastIds).ToHashSet();
        Dictionary<long, string?> clockInTimes = draft.ToDictionary(x => x.CastId, x => x.ClockInTime);
        var defaultClockInTime = _storeClock.GetStoreNow().ToString("HH:mm");

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
        Items = await _orderRepository.GetItemsAsync(cancellationToken);
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

    private sealed record AttendanceDraftEntry(long CastId, string? ClockInTime);

    private void ValidateOpeningReadiness()
    {
        if (AttendanceCasts.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "キャストマスタが未登録です。営業開始前にキャスト一覧を登録してください。");
        }

        if (Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "商品マスタが未登録です。営業開始前に商品一覧を登録してください。");
        }
    }

    private void ValidateAttendanceInput()
    {
        var selectedRows = AttendanceCasts.Where(x => x.IsSelected).ToList();
        if (selectedRows.Count == 0)
        {
            ModelState.AddModelError(nameof(AttendanceCasts), "出勤キャストを1名以上選択してください。");
            return;
        }

        foreach (var row in selectedRows)
        {
            if (string.IsNullOrWhiteSpace(row.ClockInTime) ||
                !TimeOnly.TryParse(row.ClockInTime, CultureInfo.InvariantCulture, out _))
            {
                ModelState.AddModelError(nameof(AttendanceCasts), $"{row.DisplayName} の出勤時刻を入力してください。");
            }
        }
    }

}
