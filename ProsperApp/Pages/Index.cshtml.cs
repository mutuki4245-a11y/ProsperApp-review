using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class IndexModel(IFeatureGate featureGate, IBusinessDayRepository businessDayRepository) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;

    public bool OpeningEnabled => _featureGate.IsEnabled(FeatureNames.Opening);
    public bool ClosingEnabled => _featureGate.IsEnabled(FeatureNames.Closing);

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
    }
}
