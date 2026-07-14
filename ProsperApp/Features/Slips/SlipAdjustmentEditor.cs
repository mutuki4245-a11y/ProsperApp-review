using System.Globalization;
using System.Text.Json;
using ProsperApp.Models;

namespace ProsperApp.Services;

public static class SlipAdjustmentEditor
{
    private const string AdjustmentLinesKey = "AdjustmentsInput.Lines";

    public static SlipAdjustmentEdit PrepareSave(
        string? postedLinesJson,
        IEnumerable<SlipAdjustmentInputModel> fallbackLines)
    {
        var fallbackList = fallbackLines.ToList();
        var hasFallbackRows = HasRows(fallbackList);
        var errors = new List<SlipAdjustmentValidationError>();

        if (!TryReadPostedLines(postedLinesJson, out var postedLines))
        {
            errors.Add(new SlipAdjustmentValidationError(AdjustmentLinesKey, "調整明細の入力内容を確認してください。"));
            return new SlipAdjustmentEdit(Normalize(fallbackList), errors);
        }

        var lines = Normalize(SelectSubmittedLines(postedLines, fallbackList, hasFallbackRows));
        errors.AddRange(Validate(lines));
        return new SlipAdjustmentEdit(lines, errors);
    }

    private static List<SlipAdjustmentInputModel> SelectSubmittedLines(
        List<SlipAdjustmentInputModel> postedLines,
        List<SlipAdjustmentInputModel> fallbackLines,
        bool hasFallbackRows)
    {
        return postedLines.Count > 0 || !hasFallbackRows
            ? postedLines
            : fallbackLines;
    }

    private static List<SlipAdjustmentInputModel> Normalize(IEnumerable<SlipAdjustmentInputModel> lines)
    {
        return lines
            .Select(x => new SlipAdjustmentInputModel
            {
                LineName = string.IsNullOrWhiteSpace(x.LineName) ? null : x.LineName.Trim(),
                Amount = x.Amount
            })
            .Where(HasContent)
            .ToList();
    }

    private static IReadOnlyList<SlipAdjustmentValidationError> Validate(IReadOnlyList<SlipAdjustmentInputModel> lines)
    {
        List<SlipAdjustmentValidationError> errors = [];

        if (lines.Count > 20)
        {
            errors.Add(new SlipAdjustmentValidationError(AdjustmentLinesKey, "調整明細は20件まで登録できます。"));
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line.LineName))
            {
                errors.Add(new SlipAdjustmentValidationError(
                    $"{AdjustmentLinesKey}[{i}].{nameof(SlipAdjustmentInputModel.LineName)}",
                    "明細名を入力してください。"));
            }

            if (line.Amount != decimal.Truncate(line.Amount) ||
                line.Amount is < -99999999 or > 99999999)
            {
                errors.Add(new SlipAdjustmentValidationError(
                    $"{AdjustmentLinesKey}[{i}].{nameof(SlipAdjustmentInputModel.Amount)}",
                    "価格を確認してください。"));
            }
        }

        return errors;
    }

    private static bool HasRows(IEnumerable<SlipAdjustmentInputModel> lines)
    {
        return lines.Any(HasContent);
    }

    private static bool HasContent(SlipAdjustmentInputModel line)
    {
        return !string.IsNullOrWhiteSpace(line.LineName) || line.Amount != 0;
    }

    private static bool TryReadPostedLines(string? value, out List<SlipAdjustmentInputModel> lines)
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

public sealed record SlipAdjustmentEdit(
    List<SlipAdjustmentInputModel> Lines,
    IReadOnlyList<SlipAdjustmentValidationError> Errors);

public sealed record SlipAdjustmentValidationError(string Key, string Message);
