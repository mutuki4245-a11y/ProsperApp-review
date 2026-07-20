using Microsoft.AspNetCore.Mvc;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{

    public async Task<IActionResult> OnGetBusinessEditorAsync(string? section, CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips) || !IsBusinessEditorSection(section))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        SetDefaultInputs();

        if (SlipId is null || Detail is null)
        {
            BusinessEditorUnavailableMessage = "伝票を取得できません。営業中一覧を再表示してください。";
            return Partial("_BusinessSlipEditorUnavailable", this);
        }

        if (!IsOpenSlip)
        {
            BusinessEditorUnavailableMessage = "この伝票は会計準備中または会計済みのため編集できません。営業中一覧を再表示してください。";
            return Partial("_BusinessSlipEditorUnavailable", this);
        }

        return Partial(BusinessEditorPartialName(section!), this);
    }

    private static bool IsBusinessEditorSection(string? section)
    {
        return section is "customers" or "nominations" or "adjustments";
    }

    private static string BusinessEditorPartialName(string section)
    {
        return section switch
        {
            "customers" => "_BusinessSlipCustomers",
            "nominations" => "_BusinessSlipNominations",
            "adjustments" => "_BusinessSlipAdjustments",
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
        };
    }
}
