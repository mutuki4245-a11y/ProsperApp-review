using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class SlipEditModel(IFeatureGate featureGate) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;

    public long? SlipId { get; private set; }

    public IActionResult OnGet(long? slipId)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        SlipId = slipId;
        return Page();
    }
}
