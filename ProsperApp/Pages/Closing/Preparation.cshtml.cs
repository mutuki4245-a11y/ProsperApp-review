using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ClosingPreparationModel(IFeatureGate featureGate) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;

    public string Title { get; private set; } = "準備中";
    public int? Step { get; private set; }

    public IActionResult OnGet(string? title, int? step)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        Title = string.IsNullOrWhiteSpace(title) ? "準備中" : title;
        Step = step;
        return Page();
    }
}
