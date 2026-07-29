using ProsperApp.Models;

namespace ProsperApp.Services;

public static class CreateSlipEditor
{
    private const int CreateSlipTimeMinuteStep = 5;
    private const string TableIdKey = "CreateSlipInput.TableId";
    private const string OpenedTimeKey = "CreateSlipInput.OpenedTime";
    private const string OpenedAtKey = "CreateSlipInput.OpenedAt";
    private const string CustomerLabelsKey = "CreateSlipInput.CustomerLabels";
    private const string NominationLinesKey = "CreateSlipInput.CastNominations";

    public const string AttendanceRequiredMessage = "営業を開始する前に出勤キャストを選択してください。";

    public static CreateSlipInputModel ApplyDefaults(
        CreateSlipInputModel input,
        StoreBusinessDay? currentBusinessDay,
        DateOnly currentBusinessDate,
        IStoreClock storeClock)
    {
        var defaulted = new CreateSlipInputModel
        {
            TableId = input.TableId,
            BusinessDate = GetSafeBusinessDate(currentBusinessDay, currentBusinessDate, storeClock),
            BusinessDayId = HasValidBusinessDate(currentBusinessDay)
                ? currentBusinessDay?.BusinessDayId
                : null,
            OpenedTime = input.OpenedTime ?? GetDefaultOpenedTime(storeClock),
            CustomerLabels = input.CustomerLabels.Count > 0
                ? input.CustomerLabels.ToList()
                : [null],
            CastNominations = CloneNominations(input.CastNominations),
            Memo = input.Memo
        };

        defaulted.OpenedAt = ComposeOpenedAt(defaulted.BusinessDate, defaulted.OpenedTime, storeClock);
        return defaulted;
    }

    public static CreateSlipEdit Prepare(
        CreateSlipInputModel input,
        CreateSlipEditContext context,
        IStoreClock storeClock)
    {
        var submittedCustomerCount = input.CustomerLabels.Count;
        var prepared = Normalize(ApplyDefaults(input, context.CurrentBusinessDay, context.CurrentBusinessDate, storeClock), storeClock);
        FillCastDisplayNames(prepared.CastNominations, context.AttendanceCasts);
        var errors = Validate(prepared, context, storeClock).ToList();
        if (submittedCustomerCount == 0)
        {
            errors.Add(new CreateSlipValidationError(CustomerLabelsKey, "お客様情報は1人から20人まで登録できます。"));
        }

        return new CreateSlipEdit(prepared, errors);
    }

    private static CreateSlipInputModel Normalize(CreateSlipInputModel input, IStoreClock storeClock)
    {
        var openedTime = string.IsNullOrWhiteSpace(input.OpenedTime)
            ? input.OpenedTime
            : input.OpenedTime.Trim();

        var normalized = new CreateSlipInputModel
        {
            TableId = input.TableId,
            BusinessDate = input.BusinessDate,
            BusinessDayId = input.BusinessDayId,
            OpenedTime = openedTime,
            CustomerLabels = NormalizeLabels(input.CustomerLabels),
            CastNominations = NormalizeNominations(input.CastNominations),
            Memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim()
        };

        if (normalized.CustomerLabels.Count == 0)
        {
            normalized.CustomerLabels.Add(null);
        }

        normalized.OpenedAt = ComposeOpenedAt(normalized.BusinessDate, normalized.OpenedTime, storeClock);
        return normalized;
    }

    private static List<string?> NormalizeLabels(IEnumerable<string?> labels)
    {
        return labels
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .ToList();
    }

    private static List<CastNominationInputModel> NormalizeNominations(IEnumerable<CastNominationInputModel> nominations)
    {
        return nominations
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

    private static List<CastNominationInputModel> CloneNominations(IEnumerable<CastNominationInputModel> nominations)
    {
        return nominations
            .Select(x => new CastNominationInputModel
            {
                NominationKind = x.NominationKind,
                NominationPrice = x.NominationPrice,
                CastId = x.CastId,
                CastName = x.CastName
            })
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

    private static DateTime? ComposeOpenedAt(DateOnly? businessDate, string? openedTime, IStoreClock storeClock)
    {
        return businessDate is not null && TimeOnly.TryParse(openedTime, out var time)
            ? storeClock.ComposeBusinessDateTime(businessDate.Value, time)
            : null;
    }

    private static string GetDefaultOpenedTime(IStoreClock storeClock)
    {
        return storeClock.FloorToMinuteStep(storeClock.GetStoreNow(), CreateSlipTimeMinuteStep).ToString("HH:mm");
    }

    private static DateOnly GetSafeBusinessDate(
        StoreBusinessDay? businessDay,
        DateOnly currentBusinessDate,
        IStoreClock storeClock)
    {
        return HasValidBusinessDate(businessDay)
            ? businessDay!.BusinessDate
            : currentBusinessDate == default
                ? storeClock.GetCurrentBusinessDate()
                : currentBusinessDate;
    }

    private static bool HasValidBusinessDate(StoreBusinessDay? businessDay)
    {
        return businessDay is { BusinessDayId: > 0, BusinessDate: var businessDate } && businessDate != default;
    }

    private static IReadOnlyList<CreateSlipValidationError> Validate(
        CreateSlipInputModel input,
        CreateSlipEditContext context,
        IStoreClock storeClock)
    {
        List<CreateSlipValidationError> errors = [];

        if (context.StoreContext is null)
        {
            errors.Add(new CreateSlipValidationError(string.Empty, "店舗設定を取得できません。Supabase設定とStoreDepartmentIdを確認してください。"));
        }

        if (context.IsPreviousBusinessDayOpen)
        {
            errors.Add(new CreateSlipValidationError(string.Empty, $"前回営業日 {context.CurrentBusinessDay?.BusinessDate:yyyy-MM-dd} の締め作業が未完了です。締め作業を完了してから新しい営業入力を開始してください。"));
        }

        if (context.Tables.Count == 0)
        {
            errors.Add(new CreateSlipValidationError(string.Empty, "卓番マスタが未登録です。store_table_masterにこの店舗の卓番を登録してください。"));
        }

        if (context.CanCreateSalesInput && context.AttendanceCasts.Count == 0)
        {
            errors.Add(new CreateSlipValidationError(string.Empty, AttendanceRequiredMessage));
        }

        ValidateTable(input, context.Tables, errors);
        ValidateNominations(input.CastNominations, context.NominationOptions, context.AttendanceCasts, errors);
        ValidateOpenedTime(input, context.TimeOptions, storeClock, errors);
        ValidateCustomers(input, errors);

        return errors;
    }

    private static void ValidateTable(
        CreateSlipInputModel input,
        IReadOnlyCollection<StoreTableOption> tables,
        List<CreateSlipValidationError> errors)
    {
        if (input.TableId is not null && tables.All(x => x.TableId != input.TableId.Value))
        {
            errors.Add(new CreateSlipValidationError(TableIdKey, "この店舗で利用できない卓番です。"));
        }
    }

    private static void ValidateNominations(
        IReadOnlyList<CastNominationInputModel> nominations,
        IReadOnlyCollection<NominationBackMasterItem> nominationOptions,
        IReadOnlyCollection<StoreOrderAttendanceCastOption> attendanceCasts,
        List<CreateSlipValidationError> errors)
    {
        var allowedCastIds = attendanceCasts.Select(x => x.CastId).ToHashSet();
        var allowedNominationKinds = nominationOptions
            .Select(x => x.NominationKind)
            .ToHashSet(StringComparer.Ordinal);
        var duplicateCastIds = nominations
            .Where(nomination => nomination.CastId is not null)
            .GroupBy(nomination => nomination.CastId!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        for (var i = 0; i < nominations.Count; i++)
        {
            var nomination = nominations[i];
            ValidateNominationKind(nomination, allowedNominationKinds, i, errors);
            ValidateNominationPrice(nomination, i, errors);
            ValidateNominationCast(nomination, allowedCastIds, i, errors);
            if (nomination.CastId is not null && duplicateCastIds.Contains(nomination.CastId.Value))
            {
                errors.Add(new CreateSlipValidationError(
                    $"{NominationLinesKey}[{i}].{nameof(CastNominationInputModel.CastId)}",
                    "同じキャストは1枚の伝票に重複して指名登録できません。"));
            }
        }
    }

    private static void ValidateNominationKind(
        CastNominationInputModel nomination,
        IReadOnlySet<string> allowedNominationKinds,
        int index,
        List<CreateSlipValidationError> errors)
    {
        var key = $"{NominationLinesKey}[{index}].{nameof(CastNominationInputModel.NominationKind)}";
        if (allowedNominationKinds.Count == 0)
        {
            errors.Add(new CreateSlipValidationError(key, "指名種別マスタを登録してください。"));
        }
        else if (string.IsNullOrWhiteSpace(nomination.NominationKind) ||
                 !allowedNominationKinds.Contains(nomination.NominationKind))
        {
            errors.Add(new CreateSlipValidationError(key, "指名区分を選択してください。"));
        }
    }

    private static void ValidateNominationPrice(
        CastNominationInputModel nomination,
        int index,
        List<CreateSlipValidationError> errors)
    {
        if (nomination.NominationPrice is >= 1000 and <= 20000 &&
            nomination.NominationPrice % 1000 == 0)
        {
            return;
        }

        errors.Add(new CreateSlipValidationError(
            $"{NominationLinesKey}[{index}].{nameof(CastNominationInputModel.NominationPrice)}",
            "指名料金を選択してください。"));
    }

    private static void ValidateNominationCast(
        CastNominationInputModel nomination,
        IReadOnlySet<long> allowedCastIds,
        int index,
        List<CreateSlipValidationError> errors)
    {
        if (nomination.CastId is null)
        {
            errors.Add(new CreateSlipValidationError(
                $"{NominationLinesKey}[{index}].{nameof(CastNominationInputModel.CastName)}",
                "候補からキャストを選択してください。"));
        }
        else if (!allowedCastIds.Contains(nomination.CastId.Value))
        {
            errors.Add(new CreateSlipValidationError(
                $"{NominationLinesKey}[{index}].{nameof(CastNominationInputModel.CastName)}",
                "出勤キャストから選択してください。"));
        }

        if (nomination.CastName is not null && nomination.CastName.Length > 160)
        {
            errors.Add(new CreateSlipValidationError(
                $"{NominationLinesKey}[{index}].{nameof(CastNominationInputModel.CastName)}",
                "キャスト名は160文字以内で入力してください。"));
        }
    }

    private static void ValidateOpenedTime(
        CreateSlipInputModel input,
        IReadOnlyCollection<string> timeOptions,
        IStoreClock storeClock,
        List<CreateSlipValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(input.OpenedTime) || !timeOptions.Contains(input.OpenedTime))
        {
            errors.Add(new CreateSlipValidationError(OpenedTimeKey, "入店時刻は5分単位で選択してください。"));
        }

        if (input.OpenedAt is null)
        {
            return;
        }

        var now = storeClock.GetStoreNow();
        if (input.OpenedAt.Value > now.AddMinutes(5))
        {
            errors.Add(new CreateSlipValidationError(OpenedAtKey, "入店時刻に未来時刻は指定できません。"));
        }

        if (input.OpenedAt.Value < now.AddDays(-2))
        {
            errors.Add(new CreateSlipValidationError(OpenedAtKey, "入店時刻は過去2日以内で入力してください。"));
        }
    }

    private static void ValidateCustomers(
        CreateSlipInputModel input,
        List<CreateSlipValidationError> errors)
    {
        if (input.CustomerLabels.Count is < 1 or > 20)
        {
            errors.Add(new CreateSlipValidationError(CustomerLabelsKey, "お客様情報は1人から20人まで登録できます。"));
        }

        if (input.CustomerLabels.Any(x => x is not null && x.Length > 100))
        {
            errors.Add(new CreateSlipValidationError(CustomerLabelsKey, "お客様名は1人100文字以内で入力してください。"));
        }
    }
}

public sealed record CreateSlipEdit(
    CreateSlipInputModel Input,
    IReadOnlyList<CreateSlipValidationError> Errors);

public sealed record CreateSlipEditContext(
    StoreBusinessDay? CurrentBusinessDay,
    DateOnly CurrentBusinessDate,
    StoreContext? StoreContext,
    IReadOnlyCollection<StoreTableOption> Tables,
    IReadOnlyCollection<NominationBackMasterItem> NominationOptions,
    IReadOnlyCollection<StoreOrderAttendanceCastOption> AttendanceCasts,
    IReadOnlyCollection<string> TimeOptions,
    bool IsPreviousBusinessDayOpen,
    bool CanCreateSalesInput);

public sealed record CreateSlipValidationError(string Key, string Message);
