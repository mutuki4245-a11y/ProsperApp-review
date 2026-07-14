using Microsoft.AspNetCore.Mvc;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{
    public async Task<IActionResult> OnPostSaveAdjustmentsAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        PrepareAdjustmentInput();
        var hasSubmittedAdjustmentLines = AdjustmentsInput.Lines.Count > 0;
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

        var result = await _slipRepository.SaveSlipAdjustmentsAsync(
            SlipId!.Value,
            AdjustmentsInput.Lines,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "調整明細を保存できませんでした。");
            ShowAdjustmentModal = true;
            SetDefaultInputs();
            return Page();
        }

        if (hasSubmittedAdjustmentLines && result.AffectedCount <= 0)
        {
            ModelState.AddModelError(string.Empty, "調整明細を保存できませんでした。入力内容を確認してください。");
            ShowAdjustmentModal = true;
            SetDefaultInputs();
            return Page();
        }

        SuccessMessage = "調整明細を保存しました。";
        ModelState.Clear();
        AdjustmentsInput = new SaveSlipAdjustmentsInputModel();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        return Page();
    }

    private void PrepareAdjustmentInput()
    {
        var edit = SlipAdjustmentEditor.PrepareSave(AdjustmentLinesJson, AdjustmentsInput.Lines);
        AdjustmentsInput.Lines = edit.Lines;
        foreach (var error in edit.Errors)
        {
            ModelState.AddModelError(error.Key, error.Message);
        }
    }
}
