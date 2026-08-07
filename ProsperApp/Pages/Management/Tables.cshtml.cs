using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public sealed class ManagementTablesModel(IFeatureGate featureGate) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;

    public IActionResult OnGet()
    {
        return _featureGate.IsEnabled(FeatureNames.Opening)
            ? Page()
            : NotFound();
    }
}
