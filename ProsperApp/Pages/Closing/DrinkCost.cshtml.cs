using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Shared;
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

    public PageLoadStatus? LoadStatus { get; private set; }

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
        if (LoadStatus is { Succeeded: false })
        {
            ModelState.AddModelError(string.Empty, "必要な情報を取得できないため納品額を保存できません。再試行してください。");
            return Page();
        }

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

        TempData["SuccessMessage"] = $"納品額 {StoreUiText.Yen(result.Amount)}を保存しました。";
        return RedirectToPage("/Closing/Index");
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool preserveInput = false)
    {
        CurrentBusinessDate = _storeClock.GetCurrentBusinessDate();
        var businessDayResult = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (!businessDayResult.Succeeded)
        {
            LoadStatus = PageLoadStatus.Failure(
                businessDayResult.FailureKind ?? ResultFailureKind.Unavailable,
                businessDayResult.ErrorMessage ?? "現在営業日を取得できませんでした。");
            CurrentBusinessDay = null;
            return;
        }

        CurrentBusinessDay = businessDayResult.Value;
        if (CurrentBusinessDay is null)
        {
            LoadStatus = PageLoadStatus.Success(
                _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()));
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
        if (!status.Succeeded)
        {
            LoadStatus = PageLoadStatus.Failure(
                status.FailureKind ?? ResultFailureKind.Unavailable,
                status.ErrorMessage ?? "酒代入力状況を取得できませんでした。");
            return;
        }

        LoadStatus = PageLoadStatus.Success(
            _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()));
        IsDrinkDeliveryAmountEntered = status.Value.IsEntered;

        if (!preserveInput)
        {
            Input.DrinkDeliveryAmount = status.Value.Amount;
        }

        Input.BusinessDayId = CurrentBusinessDay.BusinessDayId;
    }
}
