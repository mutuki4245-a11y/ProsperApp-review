using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ClosingModel(IFeatureGate featureGate, IBusinessDayRepository businessDayRepository) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;

    [BindProperty]
    public long? BusinessDayId { get; set; }

    [BindProperty]
    public string? ClosingMemo { get; set; }

    public bool ReceiptsEnabled => _featureGate.IsEnabled(FeatureNames.Receipts);

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public int OpenSlipCount { get; set; }

    public string? SuccessMessage { get; set; }

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

    private async Task LoadBusinessDayAsync(CancellationToken cancellationToken)
    {
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        OpenSlipCount = CurrentBusinessDay is null
            ? 0
            : await _businessDayRepository.GetOpenSlipCountAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        BusinessDayId = CurrentBusinessDay?.BusinessDayId;
    }
}
