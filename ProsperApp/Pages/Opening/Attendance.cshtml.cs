using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class OpeningAttendanceModel(IFeatureGate featureGate) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;

    public IActionResult OnGet()
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        return RedirectToPage("/Attendance");
    }

    public IActionResult OnPost()
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        return RedirectToPage("/Attendance");
    }
}
