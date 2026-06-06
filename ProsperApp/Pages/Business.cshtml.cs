using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class BusinessModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public IReadOnlyList<BusinessSlipListItem> Slips { get; set; } = [];

    public bool SlipsEnabled => _featureGate.IsEnabled(FeatureNames.Slips);

    public bool CheckoutEnabled => _featureGate.IsEnabled(FeatureNames.Checkout);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            return Page();
        }

        Slips = await _slipRepository.GetBusinessDaySlipsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        return Page();
    }
}
