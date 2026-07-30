using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Admin;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ClosingModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    ICastSalesAdjustmentRepository castSalesAdjustmentRepository,
    IReceiptRepository receiptRepository,
    IAdminAuthorizationService adminAuthorizationService) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly ICastSalesAdjustmentRepository _castSalesAdjustmentRepository = castSalesAdjustmentRepository;
    private readonly IReceiptRepository _receiptRepository = receiptRepository;
    private readonly IAdminAuthorizationService _adminAuthorizationService = adminAuthorizationService;

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

    public bool IsAdminMode { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayShellAsync(cancellationToken);
        ClosingMemo = CurrentBusinessDay?.Memo;
        return Page();
    }

    public async Task<IActionResult> OnGetBusinessDayShellAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayShellAsync(cancellationToken, forceRefresh: true);
        return new JsonResult(new
        {
            succeeded = true,
            hasBusinessDay = CurrentBusinessDay is not null,
            businessDayId = CurrentBusinessDay?.BusinessDayId,
            isAdminMode = IsAdminMode
        });
    }

    public async Task<IActionResult> OnGetOpenSlipPanelAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayShellAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            return new JsonResult(new
            {
                succeeded = true,
                hasBusinessDay = false,
                businessDayId = (long?)null,
                openSlipCount = 0,
                isAdminMode = IsAdminMode
            });
        }

        var openSlipCount = await _businessDayRepository.GetOpenSlipCountAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        return new JsonResult(new
        {
            succeeded = true,
            hasBusinessDay = true,
            businessDayId = CurrentBusinessDay.BusinessDayId,
            openSlipCount,
            isAdminMode = IsAdminMode
        });
    }

    public async Task<IActionResult> OnGetDrinkDeliveryPanelAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayShellAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            return new JsonResult(new
            {
                succeeded = true,
                hasBusinessDay = false,
                businessDayId = (long?)null,
                amount = 0,
                isEntered = false
            });
        }

        var status = await _businessDayRepository.GetDrinkDeliveryStatusAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        return new JsonResult(new
        {
            succeeded = true,
            hasBusinessDay = true,
            businessDayId = CurrentBusinessDay.BusinessDayId,
            amount = status.Amount,
            isEntered = status.IsEntered
        });
    }

    public async Task<IActionResult> OnGetAttendancePanelAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayShellAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            return new JsonResult(new
            {
                succeeded = true,
                hasBusinessDay = false,
                businessDayId = (long?)null,
                attendanceCount = 0,
                missingClockOutCount = 0
            });
        }

        var attendance = await _businessDayRepository.GetClosingAttendanceAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        return new JsonResult(new
        {
            succeeded = true,
            hasBusinessDay = true,
            businessDayId = CurrentBusinessDay.BusinessDayId,
            attendanceCount = attendance.Count,
            missingClockOutCount = attendance.Count(x => x.ClockOutAt is null)
        });
    }

    public async Task<IActionResult> OnGetCastSalesAdjustmentPanelAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayShellAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            return new JsonResult(new
            {
                succeeded = true,
                hasBusinessDay = false,
                businessDayId = (long?)null,
                requiredSlipCount = 0,
                completedSlipCount = 0,
                missingSlipCount = 0,
                isCompleted = false
            });
        }

        var status = await _castSalesAdjustmentRepository.GetStatusAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        return new JsonResult(new
        {
            succeeded = true,
            hasBusinessDay = true,
            businessDayId = CurrentBusinessDay.BusinessDayId,
            requiredSlipCount = status.RequiredSlipCount,
            completedSlipCount = status.CompletedSlipCount,
            missingSlipCount = status.MissingSlipCount,
            isCompleted = status.IsCompleted
        });
    }

    public async Task<IActionResult> OnGetReceiptsPanelAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadBusinessDayShellAsync(cancellationToken);
        if (!ReceiptsEnabled || CurrentBusinessDay is null)
        {
            return new JsonResult(new
            {
                succeeded = true,
                enabled = ReceiptsEnabled,
                hasBusinessDay = CurrentBusinessDay is not null,
                businessDayId = CurrentBusinessDay?.BusinessDayId,
                pendingReceiptCount = 0
            });
        }

        var pendingReceipts = await _receiptRepository.GetPendingAsync(cancellationToken);
        return new JsonResult(new
        {
            succeeded = true,
            enabled = true,
            hasBusinessDay = true,
            businessDayId = CurrentBusinessDay.BusinessDayId,
            pendingReceiptCount = pendingReceipts.Count
        });
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

        if (!IsAdminMode && !Readiness.CanClose)
        {
            foreach (var reason in Readiness.BlockReasons)
            {
                ModelState.AddModelError(string.Empty, reason);
            }

            return Page();
        }

        var result = await _businessDayRepository.CloseAsync(CurrentBusinessDay.BusinessDayId, ClosingMemo, IsAdminMode, cancellationToken);
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
        await LoadBusinessDayShellAsync(cancellationToken);
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

    private async Task LoadBusinessDayShellAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        IsAdminMode = _adminAuthorizationService.IsAdminMode;
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken, forceRefresh);
        BusinessDayId = CurrentBusinessDay?.BusinessDayId;
    }
}
