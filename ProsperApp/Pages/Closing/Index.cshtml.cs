using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ClosingModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;

    [BindProperty]
    public long? BusinessDayId { get; set; }

    [BindProperty]
    public string? ClosingMemo { get; set; }

    public bool ReceiptsEnabled => _featureGate.IsEnabled(FeatureNames.Receipts);

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public BusinessDayClosingReadiness Readiness { get; private set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        BusinessDayId = CurrentBusinessDay?.BusinessDayId;
        ClosingMemo = CurrentBusinessDay?.Memo;
        return Page();
    }

    public async Task<IActionResult> OnGetReadinessAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(
            cancellationToken,
            forceRefresh: true);
        if (CurrentBusinessDay is null)
        {
            return new JsonResult(new
            {
                succeeded = true,
                hasBusinessDay = false,
                businessDayId = (long?)null
            });
        }

        var result = await _businessDayRepository.GetClosingReadinessAsync(
            CurrentBusinessDay,
            ReceiptsEnabled,
            cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                hasBusinessDay = true,
                businessDayId = CurrentBusinessDay.BusinessDayId,
                errorMessage = result.ErrorMessage
            });
        }

        var readiness = result.Value;
        return new JsonResult(new
        {
            succeeded = true,
            hasBusinessDay = true,
            businessDayId = CurrentBusinessDay.BusinessDayId,
            openSlipCount = readiness.OpenSlipCount,
            drinkDeliveryAmount = readiness.DrinkDeliveryAmount,
            isDrinkDeliveryAmountEntered = readiness.IsDrinkDeliveryAmountEntered,
            attendanceCount = readiness.AttendanceCount,
            missingClockOutCount = readiness.MissingClockOutCount,
            castSalesRequiredSlipCount = readiness.CastSalesRequiredSlipCount,
            castSalesCompletedSlipCount = readiness.CastSalesCompletedSlipCount,
            castSalesMissingSlipCount = readiness.CastSalesMissingSlipCount,
            pendingReceiptCount = readiness.PendingReceiptCount,
            canClose = readiness.CanClose,
            blockReasons = readiness.BlockReasons,
            checkedAt = readiness.CheckedAt
        });
    }

    public async Task<IActionResult> OnPostCloseBusinessDayAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var submittedBusinessDayId = BusinessDayId;
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(
            cancellationToken,
            forceRefresh: true);
        if (CurrentBusinessDay is null)
        {
            BusinessDayId = null;
            ModelState.AddModelError(string.Empty, "営業中の営業日がありません。");
            return Page();
        }

        BusinessDayId = CurrentBusinessDay.BusinessDayId;
        if (submittedBusinessDayId != CurrentBusinessDay.BusinessDayId)
        {
            ModelState.AddModelError(string.Empty, "営業日情報が更新されています。画面を再読み込みしてください。");
            return Page();
        }

        var readinessResult = await _businessDayRepository.GetClosingReadinessAsync(
            CurrentBusinessDay,
            ReceiptsEnabled,
            cancellationToken);
        if (!readinessResult.Succeeded)
        {
            ModelState.AddModelError(
                string.Empty,
                readinessResult.ErrorMessage ?? "締め条件を取得できませんでした。");
            return Page();
        }

        Readiness = readinessResult.Value;
        if (!Readiness.CanClose)
        {
            foreach (var reason in Readiness.BlockReasons)
            {
                ModelState.AddModelError(string.Empty, reason);
            }

            return Page();
        }

        var result = await _businessDayRepository.CloseAsync(
            CurrentBusinessDay.BusinessDayId,
            ClosingMemo,
            ReceiptsEnabled,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(
                string.Empty,
                result.ErrorMessage ?? "営業日を締められませんでした。");
            return Page();
        }

        SuccessMessage = $"営業日 {result.BusinessDay?.BusinessDate:yyyy-MM-dd} を締めました。";
        return RedirectToPage("/Index");
    }
}
