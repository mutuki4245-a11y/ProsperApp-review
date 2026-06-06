using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class OpeningCastsModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public IReadOnlyList<CastOption> Casts { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        Casts = await _slipRepository.GetCastsAsync(cancellationToken);
        return Page();
    }
}
