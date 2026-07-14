using ProsperApp.Models;

namespace ProsperApp.Services;

public static class SlipAdjustmentEditor
{
    private const string AdjustmentInputKey = "AdjustmentInput";

    public static SlipAdjustmentAdd PrepareAdd(SlipAdjustmentInputModel input)
    {
        var line = Normalize([input]).FirstOrDefault() ?? new SlipAdjustmentInputModel();
        var errors = new List<SlipAdjustmentValidationError>();
        if (!line.HasContent)
        {
            errors.Add(new SlipAdjustmentValidationError(AdjustmentInputKey, "追加する自由入力明細を入力してください。"));
            return new SlipAdjustmentAdd(line, errors);
        }

        errors.AddRange(Validate([line], AdjustmentInputKey));
        return new SlipAdjustmentAdd(line, errors);
    }

    private static List<SlipAdjustmentInputModel> Normalize(IEnumerable<SlipAdjustmentInputModel> lines)
    {
        return lines
            .Select(x => new SlipAdjustmentInputModel
            {
                LineName = string.IsNullOrWhiteSpace(x.LineName) ? null : x.LineName.Trim(),
                Amount = x.Amount
            })
            .Where(x => x.HasContent)
            .ToList();
    }

    private static IReadOnlyList<SlipAdjustmentValidationError> Validate(
        IReadOnlyList<SlipAdjustmentInputModel> lines,
        string keyPrefix)
    {
        List<SlipAdjustmentValidationError> errors = [];

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line.LineName))
            {
                errors.Add(new SlipAdjustmentValidationError(
                    BuildValidationKey(keyPrefix, nameof(SlipAdjustmentInputModel.LineName)),
                    "明細名を入力してください。"));
            }

            if (line.Amount != decimal.Truncate(line.Amount) ||
                line.Amount is < -99999999 or > 99999999)
            {
                errors.Add(new SlipAdjustmentValidationError(
                    BuildValidationKey(keyPrefix, nameof(SlipAdjustmentInputModel.Amount)),
                    "価格を確認してください。"));
            }
        }

        return errors;
    }

    private static string BuildValidationKey(string keyPrefix, string propertyName)
    {
        return $"{keyPrefix}.{propertyName}";
    }
}

public sealed record SlipAdjustmentAdd(
    SlipAdjustmentInputModel Line,
    IReadOnlyList<SlipAdjustmentValidationError> Errors);

public sealed record SlipAdjustmentValidationError(string Key, string Message);
