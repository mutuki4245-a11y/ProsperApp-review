using System.Globalization;
using System.Text.Json;
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
        var hasFormAdjustmentRows = HasAdjustmentRows(AdjustmentsInput.Lines);
        var hasValidAdjustmentJson = TryReadAdjustmentLinesJson(AdjustmentLinesJson, out var postedAdjustmentLines);
        if (!hasValidAdjustmentJson)
        {
            ModelState.AddModelError(nameof(AdjustmentsInput.Lines), "調整明細の入力内容を確認してください。");
        }
        else if (postedAdjustmentLines.Count > 0 || !hasFormAdjustmentRows)
        {
            AdjustmentsInput.Lines = postedAdjustmentLines;
        }

        NormalizeAdjustmentInput();
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

    private static bool HasAdjustmentRows(IEnumerable<SlipAdjustmentInputModel> lines)
    {
        return lines.Any(x => !string.IsNullOrWhiteSpace(x.LineName) || x.Amount != 0);
    }

    private static bool TryReadAdjustmentLinesJson(string? value, out List<SlipAdjustmentInputModel> lines)
    {
        lines = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!TryReadJsonDecimal(item, "amount", out var amount))
                {
                    return false;
                }

                lines.Add(new SlipAdjustmentInputModel
                {
                    LineName = ReadJsonString(item, "lineName") ?? ReadJsonString(item, "line_name"),
                    Amount = amount
                });
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadJsonString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool TryReadJsonDecimal(JsonElement item, string propertyName, out decimal amount)
    {
        amount = 0;
        if (!item.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
        {
            amount = number;
            return true;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var text = property.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                amount = parsed;
                return true;
            }
        }

        return property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
    }
}
