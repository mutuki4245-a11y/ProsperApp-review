using ProsperApp.Models;

namespace ProsperApp.Services;

public static class SlipCustomerEditor
{
    private const int CustomerTimeMinuteStep = 5;
    private const string AddCustomerLabelsKey = "AddCustomersInput.CustomerLabels";
    private const string AddEnteredTimeKey = "AddCustomersInput.EnteredTime";
    private const string LeaveCustomerIdKey = "LeaveCustomerInput.SlipCustomerId";
    private const string LeaveLeftTimeKey = "LeaveCustomerInput.LeftTime";
    private const string UpdateCustomerIdKey = "UpdateCustomerInput.SlipCustomerId";
    private const string UpdateCustomerLabelKey = "UpdateCustomerInput.CustomerLabel";

    public static AddSlipCustomersInputModel ApplyAddDefaults(
        AddSlipCustomersInputModel input,
        IStoreClock storeClock)
    {
        var labels = input.CustomerLabels.Count > 0
            ? input.CustomerLabels.ToList()
            : [null];

        return new AddSlipCustomersInputModel
        {
            CustomerLabels = labels,
            EnteredTime = input.EnteredTime ?? GetDefaultCustomerTime(storeClock),
            EnteredAt = input.EnteredAt
        };
    }

    public static LeaveSlipCustomerInputModel ApplyLeaveDefaults(
        LeaveSlipCustomerInputModel input,
        IStoreClock storeClock)
    {
        return new LeaveSlipCustomerInputModel
        {
            SlipCustomerId = input.SlipCustomerId,
            LeftTime = input.LeftTime ?? GetDefaultCustomerTime(storeClock),
            LeftAt = input.LeftAt
        };
    }

    public static SlipCustomerAddEdit PrepareAdd(
        AddSlipCustomersInputModel input,
        SlipDetail detail,
        IReadOnlyCollection<string> timeOptions,
        IStoreClock storeClock)
    {
        var defaulted = ApplyAddDefaults(input, storeClock);
        var labels = NormalizeLabels(defaulted.CustomerLabels);
        if (labels.Count == 0)
        {
            labels.Add(null);
        }

        var enteredTime = string.IsNullOrWhiteSpace(defaulted.EnteredTime)
            ? GetDefaultCustomerTime(storeClock)
            : defaulted.EnteredTime.Trim();
        var enteredAt = ComposeCustomerTime(detail.BusinessDate, enteredTime, storeClock);

        var prepared = new AddSlipCustomersInputModel
        {
            CustomerLabels = labels,
            EnteredTime = enteredTime,
            EnteredAt = enteredAt
        };

        return new SlipCustomerAddEdit(prepared, ValidateAdd(prepared, detail, timeOptions, storeClock));
    }

    public static SlipCustomerLeaveEdit PrepareLeave(
        LeaveSlipCustomerInputModel input,
        SlipDetail detail,
        IReadOnlyCollection<string> timeOptions,
        IStoreClock storeClock)
    {
        var leftTime = string.IsNullOrWhiteSpace(input.LeftTime) ? null : input.LeftTime.Trim();
        var prepared = new LeaveSlipCustomerInputModel
        {
            SlipCustomerId = input.SlipCustomerId,
            LeftTime = leftTime,
            LeftAt = ComposeCustomerTime(detail.BusinessDate, leftTime, storeClock)
        };

        return new SlipCustomerLeaveEdit(prepared, ValidateLeave(prepared, detail, timeOptions, storeClock));
    }

    public static SlipCustomerUpdateEdit PrepareUpdate(UpdateSlipCustomerInputModel input, SlipDetail detail)
    {
        var prepared = new UpdateSlipCustomerInputModel
        {
            SlipCustomerId = input.SlipCustomerId,
            CustomerLabel = string.IsNullOrWhiteSpace(input.CustomerLabel) ? null : input.CustomerLabel.Trim()
        };

        return new SlipCustomerUpdateEdit(prepared, ValidateUpdate(prepared, detail));
    }

    private static List<string?> NormalizeLabels(IEnumerable<string?> labels)
    {
        return labels
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .ToList();
    }

    private static DateTime? ComposeCustomerTime(DateOnly businessDate, string? timeText, IStoreClock storeClock)
    {
        return TimeOnly.TryParse(timeText, out var time)
            ? storeClock.ComposeBusinessDateTime(businessDate, time)
            : null;
    }

    private static string GetDefaultCustomerTime(IStoreClock storeClock)
    {
        return storeClock.FloorToMinuteStep(storeClock.GetStoreNow(), CustomerTimeMinuteStep).ToString("HH:mm");
    }

    private static IReadOnlyList<SlipCustomerValidationError> ValidateAdd(
        AddSlipCustomersInputModel input,
        SlipDetail detail,
        IReadOnlyCollection<string> timeOptions,
        IStoreClock storeClock)
    {
        List<SlipCustomerValidationError> errors = [];

        if (string.IsNullOrWhiteSpace(input.EnteredTime) || !timeOptions.Contains(input.EnteredTime))
        {
            errors.Add(new SlipCustomerValidationError(AddEnteredTimeKey, "入店時刻は5分単位で選択してください。"));
        }

        if (input.EnteredAt is not null)
        {
            var openedAt = storeClock.ToStoreDateTime(detail.OpenedAt);
            if (input.EnteredAt.Value < openedAt)
            {
                errors.Add(new SlipCustomerValidationError(AddEnteredTimeKey, "入店時刻は伝票の入店時刻以降で入力してください。"));
            }

            if (input.EnteredAt.Value > storeClock.GetStoreNow().AddMinutes(5))
            {
                errors.Add(new SlipCustomerValidationError(AddEnteredTimeKey, "入店時刻に未来時刻は指定できません。"));
            }
        }

        if (input.CustomerLabels.Count is < 1 or > 20)
        {
            errors.Add(new SlipCustomerValidationError(AddCustomerLabelsKey, "客情報は1人から20人まで登録できます。"));
        }

        if (input.CustomerLabels.Any(x => x is not null && x.Length > 100))
        {
            errors.Add(new SlipCustomerValidationError(AddCustomerLabelsKey, "客名は1人100文字以内で入力してください。"));
        }

        return errors;
    }

    private static IReadOnlyList<SlipCustomerValidationError> ValidateLeave(
        LeaveSlipCustomerInputModel input,
        SlipDetail detail,
        IReadOnlyCollection<string> timeOptions,
        IStoreClock storeClock)
    {
        List<SlipCustomerValidationError> errors = [];

        var activeCustomerIds = detail.Customers
            .Where(x => string.Equals(x.Status, "active", StringComparison.Ordinal))
            .Select(x => x.SlipCustomerId)
            .ToHashSet();

        if (input.SlipCustomerId is null || !activeCustomerIds.Contains(input.SlipCustomerId.Value))
        {
            errors.Add(new SlipCustomerValidationError(LeaveCustomerIdKey, "退店する客を選択してください。"));
        }

        if (string.IsNullOrWhiteSpace(input.LeftTime) || !timeOptions.Contains(input.LeftTime))
        {
            errors.Add(new SlipCustomerValidationError(LeaveLeftTimeKey, "退店時刻は5分単位で選択してください。"));
        }

        if (input.LeftAt is not null)
        {
            var customer = detail.Customers.FirstOrDefault(x => x.SlipCustomerId == input.SlipCustomerId);
            if (customer is not null && input.LeftAt.Value <= storeClock.ToStoreDateTime(customer.EnteredAt))
            {
                errors.Add(new SlipCustomerValidationError(LeaveLeftTimeKey, "退店時刻は入店時刻より後にしてください。"));
            }
        }

        return errors;
    }

    private static IReadOnlyList<SlipCustomerValidationError> ValidateUpdate(
        UpdateSlipCustomerInputModel input,
        SlipDetail detail)
    {
        List<SlipCustomerValidationError> errors = [];

        var editableCustomerIds = detail.Customers
            .Where(x => !string.Equals(x.Status, "cancelled", StringComparison.Ordinal))
            .Select(x => x.SlipCustomerId)
            .ToHashSet();

        if (input.SlipCustomerId is null || !editableCustomerIds.Contains(input.SlipCustomerId.Value))
        {
            errors.Add(new SlipCustomerValidationError(UpdateCustomerIdKey, "変更する客を確認してください。"));
        }

        if (input.CustomerLabel is not null && input.CustomerLabel.Length > 100)
        {
            errors.Add(new SlipCustomerValidationError(UpdateCustomerLabelKey, "客名は100文字以内で入力してください。"));
        }

        return errors;
    }
}

public sealed record SlipCustomerAddEdit(
    AddSlipCustomersInputModel Input,
    IReadOnlyList<SlipCustomerValidationError> Errors);

public sealed record SlipCustomerLeaveEdit(
    LeaveSlipCustomerInputModel Input,
    IReadOnlyList<SlipCustomerValidationError> Errors);

public sealed record SlipCustomerUpdateEdit(
    UpdateSlipCustomerInputModel Input,
    IReadOnlyList<SlipCustomerValidationError> Errors);

public sealed record SlipCustomerValidationError(string Key, string Message);
