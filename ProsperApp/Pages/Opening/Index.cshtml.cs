using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class OpeningModel(IFeatureGate featureGate) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;

    public IActionResult OnGet()
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        return RedirectToPage("/Index");
    }

    public IActionResult OnPost()
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        return RedirectToPage("/Index");
    }
}
