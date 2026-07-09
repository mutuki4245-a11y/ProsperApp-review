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
        NormalizeAdjustmentInput();
        await LoadAsync(cancellationToken);

        if (!EnsureSlipLoaded())
        {
            ShowAdjustmentModal = true;
            SetDefaultInputs();
            return Page();
        }

        ValidateAdjustmentInput();
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

        SuccessMessage = "調整明細を保存しました。";
        ModelState.Clear();
        AdjustmentsInput = new SaveSlipAdjustmentsInputModel();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        return Page();
    }

    private void NormalizeAdjustmentInput()
    {
        AdjustmentsInput.Lines = AdjustmentsInput.Lines
            .Select(x => new SlipAdjustmentInputModel
            {
                LineName = string.IsNullOrWhiteSpace(x.LineName) ? null : x.LineName.Trim(),
                Amount = x.Amount
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.LineName) || x.Amount != 0)
            .ToList();
    }

    private void ValidateAdjustmentInput()
    {
        if (AdjustmentsInput.Lines.Count > 20)
        {
            ModelState.AddModelError(nameof(AdjustmentsInput.Lines), "調整明細は20件まで登録できます。");
        }

        for (var i = 0; i < AdjustmentsInput.Lines.Count; i++)
        {
            var line = AdjustmentsInput.Lines[i];
            if (string.IsNullOrWhiteSpace(line.LineName))
            {
                ModelState.AddModelError($"AdjustmentsInput.Lines[{i}].LineName", "明細名を入力してください。");
            }

            if (line.Amount != decimal.Truncate(line.Amount) || Math.Abs(line.Amount) > 99999999)
            {
                ModelState.AddModelError($"AdjustmentsInput.Lines[{i}].Amount", "価格を確認してください。");
            }
        }
    }
}
