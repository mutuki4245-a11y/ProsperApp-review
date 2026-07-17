using Microsoft.AspNetCore.Mvc;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{
    public async Task<IActionResult> OnGetPanelsAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        if (SlipId is null || Detail is null)
        {
            return NotFound();
        }

        SetDefaultInputs();
        return Partial("_SlipPanels", this);
    }
}
