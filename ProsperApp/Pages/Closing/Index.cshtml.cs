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
        if (CurrentBusinessDay is null)
        {
            OpenSlipCount = 0;
            DrinkDeliveryAmount = 0;
            IsDrinkDeliveryAmountEntered = false;
            ClosingAttendanceCount = 0;
            ClosingAttendanceMissingClockOutCount = 0;
            PendingReceiptCount = 0;
            CastSalesAdjustmentStatus = new CastSalesAdjustmentStatus();
            Readiness = new BusinessDayClosingReadiness
            {
                ReceiptsEnabled = ReceiptsEnabled
            };
            BusinessDayId = null;
            return;
        }

        var openSlipCountTask = _businessDayRepository.GetOpenSlipCountAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        var drinkDeliveryStatusTask = _businessDayRepository.GetDrinkDeliveryStatusAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        var closingAttendanceTask = _businessDayRepository.GetClosingAttendanceAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        var pendingReceiptsTask = ReceiptsEnabled
            ? _receiptRepository.GetPendingAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<PendingReceiptItem>>([]);
        var castSalesAdjustmentStatusTask = _castSalesAdjustmentRepository.GetStatusAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);

        await Task.WhenAll(
            openSlipCountTask,
            drinkDeliveryStatusTask,
            closingAttendanceTask,
            pendingReceiptsTask,
            castSalesAdjustmentStatusTask);

        OpenSlipCount = await openSlipCountTask;
        var drinkDeliveryStatus = await drinkDeliveryStatusTask;
        DrinkDeliveryAmount = drinkDeliveryStatus.Amount;
        IsDrinkDeliveryAmountEntered = drinkDeliveryStatus.IsEntered;

        var closingAttendance = await closingAttendanceTask;
        ClosingAttendanceCount = closingAttendance.Count;
        ClosingAttendanceMissingClockOutCount = closingAttendance.Count(x => x.ClockOutAt is null);
        PendingReceiptCount = (await pendingReceiptsTask).Count;
        CastSalesAdjustmentStatus = await castSalesAdjustmentStatusTask;
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
