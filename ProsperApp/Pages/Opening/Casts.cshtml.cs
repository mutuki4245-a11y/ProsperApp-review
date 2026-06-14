using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class OpeningCastsModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreCastAdminRepository castAdminRepository) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreCastAdminRepository _castAdminRepository = castAdminRepository;

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public IReadOnlyList<StoreCastAdminItem> Casts { get; set; } = [];

    [BindProperty]
    public StoreCastCreateInputModel Input { get; set; } = new();

    [BindProperty]
    public long? DeleteCastId { get; set; }

    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        Casts = await _castAdminRepository.GetCastsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        ModelState.Clear();
        if (!TryValidateModel(Input, nameof(Input)))
        {
            Casts = await _castAdminRepository.GetCastsAsync(cancellationToken);
            return Page();
        }

        var result = await _castAdminRepository.CreateCastAsync(Input, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "キャストを登録できませんでした。");
            Casts = await _castAdminRepository.GetCastsAsync(cancellationToken);
            return Page();
        }

        ModelState.Clear();
        Input = new StoreCastCreateInputModel();
        SuccessMessage = "キャストを登録しました。";
        Casts = await _castAdminRepository.GetCastsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (DeleteCastId is null or <= 0)
        {
            ModelState.AddModelError(string.Empty, "削除するキャストを選択してください。");
            Casts = await _castAdminRepository.GetCastsAsync(cancellationToken);
            return Page();
        }

        var result = await _castAdminRepository.DeleteCastAsync(DeleteCastId.Value, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "キャストを削除できませんでした。");
            Casts = await _castAdminRepository.GetCastsAsync(cancellationToken);
            return Page();
        }

        ModelState.Clear();
        Input = new StoreCastCreateInputModel();
        SuccessMessage = "キャストを削除しました。";
        Casts = await _castAdminRepository.GetCastsAsync(cancellationToken);
        return Page();
    }
}
