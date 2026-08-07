using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public sealed class ManagementItemsModel(
    IFeatureGate featureGate,
    IAdminModeService adminModeService) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IAdminModeService _adminModeService = adminModeService;

    public bool IsAdminMode => _adminModeService.IsEnabled;

    public IActionResult OnGet()
    {
        return _featureGate.IsEnabled(FeatureNames.Opening)
            ? Page()
            : NotFound();
    }
}
