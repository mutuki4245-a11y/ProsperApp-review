using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ClosingModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    ICastSalesAdjustmentRepository castSalesAdjustmentRepository,
    ILocalSettingsProvider localSettingsProvider,
    IReceiptRepository receiptRepository) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly ICastSalesAdjustmentRepository _castSalesAdjustmentRepository = castSalesAdjustmentRepository;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IReceiptRepository _receiptRepository = receiptRepository;

    [BindProperty]
    public long? BusinessDayId { get; set; }

    [BindProperty]
    public string? ClosingMemo { get; set; }

    [BindProperty]
    public CastSalesAdjustmentSaveInput CastSalesAdjustmentInput { get; set; } = new();

    public bool ReceiptsEnabled => _featureGate.IsEnabled(FeatureNames.Receipts);

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public BusinessDayClosingReadiness Readiness { get; private set; } = new();

    public int OpenSlipCount { get; set; }

    public decimal DrinkDeliveryAmount { get; set; }

    public bool IsDrinkDeliveryAmountEntered { get; set; }

    public int ClosingAttendanceCount { get; set; }

    public int ClosingAttendanceMissingClockOutCount { get; set; }

    public int PendingReceiptCount { get; set; }

    public CastSalesAdjustmentStatus CastSalesAdjustmentStatus { get; private set; } = new();

    public IReadOnlyList<CastSalesAdjustmentSlip> CastSalesAdjustmentSlips { get; private set; } = [];

    public IReadOnlyList<CastSalesAdjustmentDetail> CastSalesAdjustmentDetails { get; private set; } = [];

    public string CastSalesAmountBasis { get; private set; } = LocalSettings.CastSalesAmountBasisTotal;

    public string CastSalesSplitMode { get; private set; } = LocalSettings.CastSalesSplitModeSplit;

    public string? SuccessMessage { get; set; }

    public long? ShowCastSalesAdjustmentModalSlipId { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCloseBusinessDayAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業中の営業日がありません。");
            return Page();
        }

        if (BusinessDayId != CurrentBusinessDay.BusinessDayId)
        {
            ModelState.AddModelError(string.Empty, "営業日情報が更新されています。画面を再読み込みしてください。");
            return Page();
        }

        if (!Readiness.CanClose)
        {
            foreach (var reason in Readiness.BlockReasons)
            {
                ModelState.AddModelError(string.Empty, reason);
            }

            return Page();
        }

        var result = await _businessDayRepository.CloseAsync(CurrentBusinessDay.BusinessDayId, ClosingMemo, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "営業日を締められませんでした。");
            await LoadBusinessDayAsync(cancellationToken);
            return Page();
        }

        SuccessMessage = $"営業日 {result.BusinessDay?.BusinessDate:yyyy-MM-dd} を締めました。";
        await LoadBusinessDayAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveCastSalesAdjustmentAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業中の営業日がありません。");
            return Page();
        }

        if (CastSalesAdjustmentInput.BusinessDayId != CurrentBusinessDay.BusinessDayId)
        {
            ModelState.AddModelError(string.Empty, "営業日情報が更新されています。画面を再読み込みしてください。");
            ShowCastSalesAdjustmentModalSlipId = CastSalesAdjustmentInput.SlipId;
            return Page();
        }

        NormalizeCastSalesAdjustmentInput();
        ValidateCastSalesAdjustmentInput();
        if (!ModelState.IsValid)
        {
            ShowCastSalesAdjustmentModalSlipId = CastSalesAdjustmentInput.SlipId;
            return Page();
        }

        var result = await _castSalesAdjustmentRepository.SaveAsync(CastSalesAdjustmentInput, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "キャスト売上額調整を保存できませんでした。");
            await LoadBusinessDayAsync(cancellationToken);
            ShowCastSalesAdjustmentModalSlipId = CastSalesAdjustmentInput.SlipId;
            return Page();
        }

        SuccessMessage = "キャスト売上額調整を保存しました。";
        await LoadBusinessDayAsync(cancellationToken);
        return Page();
    }

    private async Task LoadBusinessDayAsync(CancellationToken cancellationToken)
    {
        var settings = _localSettingsProvider.GetCurrent();
        CastSalesAmountBasis = settings.CastSalesAmountBasis;
        CastSalesSplitMode = settings.CastSalesSplitMode;
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        OpenSlipCount = CurrentBusinessDay is null
            ? 0
            : await _businessDayRepository.GetOpenSlipCountAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        var drinkDeliveryStatus = CurrentBusinessDay is null
            ? new BusinessDayDrinkDeliveryStatus()
            : await _businessDayRepository.GetDrinkDeliveryStatusAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        DrinkDeliveryAmount = drinkDeliveryStatus.Amount;
        IsDrinkDeliveryAmountEntered = drinkDeliveryStatus.IsEntered;

        IReadOnlyList<BusinessDayClosingAttendanceItem> closingAttendance = CurrentBusinessDay is null
            ? []
            : await _businessDayRepository.GetClosingAttendanceAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        ClosingAttendanceCount = closingAttendance.Count;
        ClosingAttendanceMissingClockOutCount = closingAttendance.Count(x => x.ClockOutAt is null);
        PendingReceiptCount = ReceiptsEnabled
            ? (await _receiptRepository.GetPendingAsync(cancellationToken)).Count
            : 0;
        CastSalesAdjustmentStatus = CurrentBusinessDay is null
            ? new CastSalesAdjustmentStatus()
            : await _castSalesAdjustmentRepository.GetStatusAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        CastSalesAdjustmentSlips = CurrentBusinessDay is null
            ? []
            : await _castSalesAdjustmentRepository.GetSlipsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        CastSalesAdjustmentDetails = CurrentBusinessDay is null
            ? []
            : await LoadCastSalesAdjustmentDetailsAsync(CastSalesAdjustmentSlips, cancellationToken);
        Readiness = new BusinessDayClosingReadiness
        {
            BusinessDay = CurrentBusinessDay,
            OpenSlipCount = OpenSlipCount,
            DrinkDeliveryAmount = DrinkDeliveryAmount,
            IsDrinkDeliveryAmountEntered = IsDrinkDeliveryAmountEntered,
            AttendanceCount = ClosingAttendanceCount,
            MissingClockOutCount = ClosingAttendanceMissingClockOutCount,
            IsCastSalesAdjustmentCompleted = CastSalesAdjustmentStatus.IsCompleted,
            PendingReceiptCount = PendingReceiptCount,
            ReceiptsEnabled = ReceiptsEnabled
        };
        BusinessDayId = CurrentBusinessDay?.BusinessDayId;
    }

    public CastSalesAdjustmentDetail? FindCastSalesAdjustmentDetail(long slipId)
    {
        return CastSalesAdjustmentDetails.FirstOrDefault(x => x.SlipId == slipId);
    }

    public string? GetCastSalesAdjustmentCastNames(CastSalesAdjustmentSlip slip)
    {
        if (!string.IsNullOrWhiteSpace(slip.CastNames))
        {
            return slip.CastNames;
        }

        var detail = FindCastSalesAdjustmentDetail(slip.SlipId);
        return detail is null
            ? null
            : string.Join("、", detail.Casts.Select(x => x.CastDisplayName).Distinct(StringComparer.Ordinal));
    }

    public static string FormatAmountValue(decimal amount)
    {
        return amount.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<CastSalesAdjustmentDetail>> LoadCastSalesAdjustmentDetailsAsync(
        IReadOnlyList<CastSalesAdjustmentSlip> slips,
        CancellationToken cancellationToken)
    {
        var details = new List<CastSalesAdjustmentDetail>();
        foreach (var slip in slips)
        {
            var detail = await _castSalesAdjustmentRepository.GetDetailAsync(slip.SlipId, cancellationToken);
            if (detail is null)
            {
                continue;
            }

            ApplyInitialCastSalesAmounts(detail);
            details.Add(detail);
        }

        return details;
    }

    private void ApplyInitialCastSalesAmounts(CastSalesAdjustmentDetail detail)
    {
        if (detail.Casts.Count == 0)
        {
            return;
        }

        var baseAmount = CastSalesAmountBasis == LocalSettings.CastSalesAmountBasisSubtotal
            ? detail.SubtotalAmount
            : detail.TotalAmount;
        var baseAmountYen = (long)decimal.Truncate(baseAmount);

        if (CastSalesSplitMode == LocalSettings.CastSalesSplitModeFull)
        {
            foreach (var row in detail.Casts)
            {
                row.InitialSalesAmount = baseAmountYen;
            }

            return;
        }

        var castCount = detail.Casts.Count;
        var dividedAmount = baseAmountYen / castCount;
        var remainder = baseAmountYen % castCount;
        for (var i = 0; i < detail.Casts.Count; i++)
        {
            detail.Casts[i].InitialSalesAmount = dividedAmount + (i < remainder ? 1 : 0);
        }
    }

    private void NormalizeCastSalesAdjustmentInput()
    {
        CastSalesAdjustmentInput.SourceAmountType =
            CastSalesAdjustmentInput.SourceAmountType == LocalSettings.CastSalesAmountBasisSubtotal
                ? LocalSettings.CastSalesAmountBasisSubtotal
                : LocalSettings.CastSalesAmountBasisTotal;
        CastSalesAdjustmentInput.SplitMode =
            CastSalesAdjustmentInput.SplitMode == LocalSettings.CastSalesSplitModeFull
                ? LocalSettings.CastSalesSplitModeFull
                : LocalSettings.CastSalesSplitModeSplit;
        CastSalesAdjustmentInput.Casts = CastSalesAdjustmentInput.Casts
            .Where(x => x.SlipCastId > 0)
            .GroupBy(x => x.SlipCastId)
            .Select(x => x.Last())
            .ToList();
    }

    private void ValidateCastSalesAdjustmentInput()
    {
        if (CastSalesAdjustmentInput.SlipId is null or <= 0)
        {
            ModelState.AddModelError(string.Empty, "調整する伝票を選択してください。");
        }

        if (CastSalesAdjustmentInput.Casts.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "指名キャストの売上額を入力してください。");
        }

        if (CastSalesAdjustmentInput.Casts.Any(x => x.SalesAmount < 0 || decimal.Truncate(x.SalesAmount) != x.SalesAmount))
        {
            ModelState.AddModelError(string.Empty, "売上額は0円以上の整数で入力してください。");
        }
    }
}
