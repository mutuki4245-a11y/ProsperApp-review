using ProsperApp.Models;

namespace ProsperApp.Services;

public static class SlipNominationEditor
{
    private const string NominationLinesKey = "AddNominationsInput.CastNominations";

    public static SlipNominationEdit PrepareAdd(
        IEnumerable<CastNominationInputModel> inputNominations,
        IReadOnlyCollection<NominationBackMasterItem> nominationOptions,
        IReadOnlyCollection<StoreOrderAttendanceCastOption> attendanceCasts)
    {
        var nominations = Normalize(inputNominations);
        FillCastDisplayNames(nominations, attendanceCasts);
        var errors = Validate(nominations, nominationOptions, attendanceCasts);
        return new SlipNominationEdit(nominations, errors);
    }

    public static string? GetDefaultNominationKind(IEnumerable<NominationBackMasterItem> nominationOptions)
    {
        return nominationOptions.FirstOrDefault(x => string.Equals(x.NominationType, "nomination", StringComparison.Ordinal))?.NominationKind
            ?? nominationOptions.FirstOrDefault()?.NominationKind;
    }

    private static List<CastNominationInputModel> Normalize(IEnumerable<CastNominationInputModel> inputNominations)
    {
        return inputNominations
            .Select(x => new CastNominationInputModel
            {
                NominationKind = string.IsNullOrWhiteSpace(x.NominationKind) ? null : x.NominationKind.Trim(),
                NominationPrice = x.NominationPrice,
                CastId = x.CastId,
                CastName = string.IsNullOrWhiteSpace(x.CastName) ? null : x.CastName.Trim()
            })
            .Where(HasNominationCast)
            .ToList();
    }

    private static bool HasNominationCast(CastNominationInputModel nomination)
    {
        return nomination.CastId is not null || !string.IsNullOrWhiteSpace(nomination.CastName);
    }

    private static void FillCastDisplayNames(
        IEnumerable<CastNominationInputModel> nominations,
        IEnumerable<StoreOrderAttendanceCastOption> attendanceCasts)
    {
        var castsById = attendanceCasts
            .GroupBy(x => x.CastId)
            .ToDictionary(x => x.Key, x => x.First());
        foreach (var nomination in nominations)
        {
            if (nomination.CastId is not null &&
                string.IsNullOrWhiteSpace(nomination.CastName) &&
                castsById.TryGetValue(nomination.CastId.Value, out var cast))
            {
                nomination.CastName = cast.SearchDisplayName;
            }
        }
    }

    private static IReadOnlyList<SlipNominationValidationError> Validate(
        IReadOnlyList<CastNominationInputModel> nominations,
        IReadOnlyCollection<NominationBackMasterItem> nominationOptions,
        IReadOnlyCollection<StoreOrderAttendanceCastOption> attendanceCasts)
    {
        List<SlipNominationValidationError> errors = [];

        if (nominations.Count == 0)
        {
            errors.Add(new SlipNominationValidationError(NominationLinesKey, "追加する指名を入力してください。"));
            return errors;
        }

        if (nominations.Count > 20)
        {
            errors.Add(new SlipNominationValidationError(NominationLinesKey, "指名情報は20件まで追加できます。"));
        }

        var allowedCastIds = attendanceCasts.Select(x => x.CastId).ToHashSet();
        var allowedNominationKinds = nominationOptions
            .Select(x => x.NominationKind)
            .ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < nominations.Count; i++)
        {
            var nomination = nominations[i];
            ValidateNominationKind(nomination, allowedNominationKinds, i, errors);
            ValidateNominationPrice(nomination, i, errors);
            ValidateNominationCast(nomination, allowedCastIds, i, errors);
        }

        return errors;
    }

    private static void ValidateNominationKind(
        CastNominationInputModel nomination,
        IReadOnlySet<string> allowedNominationKinds,
        int index,
        List<SlipNominationValidationError> errors)
    {
        var key = $"{NominationLinesKey}[{index}].{nameof(CastNominationInputModel.NominationKind)}";
        if (allowedNominationKinds.Count == 0)
        {
            errors.Add(new SlipNominationValidationError(key, "指名種別マスタを登録してください。"));
        }
        else if (string.IsNullOrWhiteSpace(nomination.NominationKind) ||
                 !allowedNominationKinds.Contains(nomination.NominationKind))
        {
            errors.Add(new SlipNominationValidationError(key, "指名区分を選択してください。"));
        }
    }

    private static void ValidateNominationPrice(
        CastNominationInputModel nomination,
        int index,
        List<SlipNominationValidationError> errors)
    {
        if (nomination.NominationPrice is >= 1000 and <= 20000 &&
            nomination.NominationPrice % 1000 == 0)
        {
            return;
        }

        errors.Add(new SlipNominationValidationError(
            $"{NominationLinesKey}[{index}].{nameof(CastNominationInputModel.NominationPrice)}",
            "指名料金を選択してください。"));
    }

    private static void ValidateNominationCast(
        CastNominationInputModel nomination,
        IReadOnlySet<long> allowedCastIds,
        int index,
        List<SlipNominationValidationError> errors)
    {
        var key = $"{NominationLinesKey}[{index}].{nameof(CastNominationInputModel.CastName)}";
        if (nomination.CastId is null)
        {
            errors.Add(new SlipNominationValidationError(key, "候補からキャストを選択してください。"));
        }
        else if (!allowedCastIds.Contains(nomination.CastId.Value))
        {
            errors.Add(new SlipNominationValidationError(key, "出勤キャストから選択してください。"));
        }

        if (nomination.CastName is not null && nomination.CastName.Length > 160)
        {
            errors.Add(new SlipNominationValidationError(key, "キャスト名は160文字以内で入力してください。"));
        }
    }
}

public sealed record SlipNominationEdit(
    List<CastNominationInputModel> Nominations,
    IReadOnlyList<SlipNominationValidationError> Errors);

public sealed record SlipNominationValidationError(string Key, string Message);
