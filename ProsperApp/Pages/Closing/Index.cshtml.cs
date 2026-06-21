using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ClosingModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    ICastSalesAdjustmentRepository castSalesAdjustmentRepository,
    IReceiptRepository receiptRepository) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly ICastSalesAdjustmentRepository _castSalesAdjustmentRepository = castSalesAdjustmentRepository;
    private readonly IReceiptRepository _receiptRepository = receiptRepository;

    [BindProperty]
    public long? BusinessDayId { get; set; }

    [BindProperty]
    public string? ClosingMemo { get; set; }

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

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayAsync(cancellationToken);
        ClosingMemo = CurrentBusinessDay?.Memo;
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
        return RedirectToPage("/Index");
    }

    private async Task LoadBusinessDayAsync(CancellationToken cancellationToken)
    {
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
}
