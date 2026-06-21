using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ClosingDrinkCostModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty]
    public DrinkDeliveryInputModel Input { get; set; } = new();

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public DateOnly CurrentBusinessDate { get; private set; }

    public bool IsDrinkDeliveryAmountEntered { get; private set; }

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

        await LoadAsync(cancellationToken, preserveInput: true);
        if (CurrentBusinessDay is not null && Input.BusinessDayId != CurrentBusinessDay.BusinessDayId)
        {
            ModelState.AddModelError(string.Empty, "営業日情報が更新されています。画面を再読み込みしてください。");
            return Page();
        }

        if (decimal.Truncate(Input.DrinkDeliveryAmount) != Input.DrinkDeliveryAmount)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.DrinkDeliveryAmount)}", "納品額は1円単位で入力してください。");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (CurrentBusinessDay is null)
        {
            var ensureResult = await _businessDayRepository.EnsureCurrentAsync(cancellationToken);
            if (!ensureResult.Succeeded || ensureResult.BusinessDay is null)
            {
                ModelState.AddModelError(string.Empty, ensureResult.ErrorMessage ?? "営業日を自動作成できませんでした。");
                return Page();
            }

            CurrentBusinessDay = ensureResult.BusinessDay;
            Input.BusinessDayId = CurrentBusinessDay.BusinessDayId;
        }

        var result = await _businessDayRepository.SaveDrinkDeliveryAmountAsync(
            CurrentBusinessDay.BusinessDayId,
            Input.DrinkDeliveryAmount,
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "納品額を保存できませんでした。");
            return Page();
        }

        TempData["SuccessMessage"] = $"納品額 {result.Amount:N0} 円を保存しました。";
        return RedirectToPage("/Closing/Index");
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool preserveInput = false)
    {
        CurrentBusinessDate = _storeClock.GetCurrentBusinessDate();
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            if (!preserveInput)
            {
                Input = new DrinkDeliveryInputModel();
            }

            Input.BusinessDayId = null;
            return;
        }

        var status = await _businessDayRepository.GetDrinkDeliveryStatusAsync(
            CurrentBusinessDay.BusinessDayId,
            cancellationToken);
        IsDrinkDeliveryAmountEntered = status.IsEntered;

        if (!preserveInput)
        {
            Input.DrinkDeliveryAmount = status.Amount;
        }

        Input.BusinessDayId = CurrentBusinessDay.BusinessDayId;
    }
}
