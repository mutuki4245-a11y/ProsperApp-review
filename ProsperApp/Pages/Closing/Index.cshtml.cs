using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Closing;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ClosingModel(
    IFeatureGate featureGate,
    IClosingApplicationService closingApplicationService) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IClosingApplicationService _closingApplicationService = closingApplicationService;

    [BindProperty]
    public long? BusinessDayId { get; set; }

    [BindProperty]
    public string? ClosingMemo { get; set; }

    public bool ReceiptsEnabled => _featureGate.IsEnabled(FeatureNames.Receipts);

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public BusinessDayClosingReadiness Readiness { get; private set; } = new();

    public PageLoadStatus? LoadStatus { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var result = await _closingApplicationService.LoadAsync(
            includeReadiness: false,
            ReceiptsEnabled,
            forceRefresh: false,
            cancellationToken);
        if (!result.Succeeded)
        {
            LoadStatus = PageLoadStatus.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "現在営業日を取得できませんでした。");
        }
        else
        {
            ApplyState(result.Value);
        }

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

        var result = await _closingApplicationService.LoadAsync(
            includeReadiness: true,
            ReceiptsEnabled,
            forceRefresh: true,
            cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                failureKind = result.FailureKind?.ToString(),
                errorMessage = result.ErrorMessage
            });
        }

        ApplyState(result.Value);
        if (CurrentBusinessDay is null)
        {
            return new JsonResult(new
            {
                succeeded = true,
                hasBusinessDay = false,
                businessDayId = (long?)null
            });
        }

        var readiness = result.Value.Readiness!;
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

        var result = await _closingApplicationService.CloseAsync(
            BusinessDayId,
            ClosingMemo,
            ReceiptsEnabled,
            cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var message in (result.ErrorMessage ?? "営業日を締められませんでした。")
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                ModelState.AddModelError(string.Empty, message);
            }

            var reload = await _closingApplicationService.LoadAsync(
                includeReadiness: true,
                ReceiptsEnabled,
                forceRefresh: true,
                cancellationToken);
            if (reload.Succeeded)
            {
                ApplyState(reload.Value);
                BusinessDayId = CurrentBusinessDay?.BusinessDayId;
            }
            return Page();
        }

        SuccessMessage = $"営業日 {result.Value.BusinessDate:yyyy-MM-dd} を締めました。";
        return RedirectToPage("/Index");
    }

    private void ApplyState(ClosingPageState state)
    {
        CurrentBusinessDay = state.BusinessDay;
        Readiness = state.Readiness ?? new BusinessDayClosingReadiness { BusinessDay = state.BusinessDay };
        LoadStatus = PageLoadStatus.Success(
            state.LastUpdatedAt ?? DateTimeOffset.UtcNow);
    }
}
