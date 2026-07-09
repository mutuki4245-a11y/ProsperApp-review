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

    public async Task<IActionResult> OnPostSaveKaraokeAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        NormalizeKaraokeInput();
        await LoadAsync(cancellationToken);

        if (!EnsureSlipLoaded())
        {
            SetDefaultInputs();
            return Page();
        }

        ValidateKaraokeInput();
        if (!ModelState.IsValid)
        {
            SetDefaultInputs();
            return Page();
        }

        var result = await _slipRepository.SaveKaraokeLinesAsync(
            Detail!.BusinessDate == default || CurrentBusinessDay is null ? 0 : CurrentBusinessDay.BusinessDayId,
            KaraokeLines,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "カラオケ回数を保存できませんでした。");
            SetDefaultInputs();
            return Page();
        }

        SuccessMessage = "カラオケ回数を保存しました。";
        ModelState.Clear();
        KaraokeLines = [];
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

    private void NormalizeKaraokeInput()
    {
        KaraokeLines = KaraokeLines
            .Where(x => x.SlipId > 0)
            .GroupBy(x => x.SlipId)
            .Select(x => new KaraokeQuantityInputModel
            {
                SlipId = x.Key,
                Quantity = x.Last().Quantity
            })
            .ToList();
    }

    private void ValidateKaraokeInput()
    {
        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業中の営業日がありません。");
            return;
        }

        if (KaraokeLines.Count != 1 || KaraokeLines[0].SlipId != SlipId)
        {
            ModelState.AddModelError(nameof(KaraokeLines), "カラオケ回数を保存する伝票を確認してください。");
            return;
        }

        var quantity = KaraokeLines[0].Quantity;
        if (quantity < 0 || quantity > 999 || quantity != decimal.Truncate(quantity))
        {
            ModelState.AddModelError("KaraokeLines[0].Quantity", "カラオケ回数を確認してください。");
        }
    }
}
