using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ManagementPricingModel(
    IFeatureGate featureGate,
    IStorePricingPlanRepository pricingPlanRepository,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IStorePricingPlanRepository _pricingPlanRepository = pricingPlanRepository;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty]
    public StorePricingPlanInputModel Plan { get; set; } = new();

    public string? SuccessMessage { get; private set; }
    public PageLoadStatus? LoadStatus { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening)) return NotFound();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening)) return NotFound();

        if (Plan.SetMinutes % 5 != 0)
        {
            ModelState.AddModelError(nameof(Plan.SetMinutes), "セット時間は5分単位で入力してください。");
        }

        if (Plan.ExtensionMinutes % 5 != 0)
        {
            ModelState.AddModelError(nameof(Plan.ExtensionMinutes), "延長時間は5分単位で入力してください。");
        }

        if (!ModelState.IsValid) return Page();

        var result = await _pricingPlanRepository.SaveAsync(Plan, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "料金設定を保存できませんでした。");
            return Page();
        }

        SuccessMessage = Plan.IsActive
            ? $"料金設定を保存しました（プラン版 {result.PlanVersion}）。営業中の見積りは次回同期時に更新されます。"
            : "時間料金を無効にして保存しました。";
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var result = await _pricingPlanRepository.GetAsync(ct);
        if (result.Succeeded)
        {
            Plan = result.Value;
            LoadStatus = PageLoadStatus.Success(
                _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()));
            return;
        }

        LoadStatus = PageLoadStatus.Failure(
            result.FailureKind ?? ResultFailureKind.Unavailable,
            result.ErrorMessage ?? "料金設定を取得できませんでした。");
    }
}
