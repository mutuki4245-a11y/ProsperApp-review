using Microsoft.AspNetCore.Mvc;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{
    public async Task<IActionResult> OnPostAddAdjustmentAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        PrepareAdjustmentInput();
        await LoadAsync(cancellationToken);

        if (!EnsureSlipLoaded())
        {
            ShowAdjustmentModal = true;
            SetDefaultInputs();
            return Page();
        }

        if (!ModelState.IsValid)
        {
            ShowAdjustmentModal = true;
            SetDefaultInputs();
            return Page();
        }

        var result = await _slipRepository.AddSlipAdjustmentAsync(
            SlipId!.Value,
            AdjustmentInput,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "自由入力明細を追加できませんでした。");
            ShowAdjustmentModal = true;
            SetDefaultInputs();
            return Page();
        }

        if (result.AffectedCount <= 0)
        {
            ModelState.AddModelError(string.Empty, "自由入力明細を追加できませんでした。入力内容を確認してください。");
            ShowAdjustmentModal = true;
            SetDefaultInputs();
            return Page();
        }

        SuccessMessage = "自由入力明細を追加しました。";
        ModelState.Clear();
        AdjustmentInput = new SlipAdjustmentInputModel();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        return Page();
    }

    private void PrepareAdjustmentInput()
    {
        var edit = SlipAdjustmentEditor.PrepareAdd(AdjustmentInput);
        AdjustmentInput = edit.Line;
        foreach (var error in edit.Errors)
        {
            ModelState.AddModelError(error.Key, error.Message);
        }
    }
}
