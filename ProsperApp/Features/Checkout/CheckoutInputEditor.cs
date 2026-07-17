using ProsperApp.Models;

namespace ProsperApp.Services;

public static class CheckoutInputEditor
{
    private const int CheckoutTimeMinuteStep = 5;
    private const string ClosedTimeKey = "CheckoutInput.ClosedTime";
    private const string PaymentsKey = "CheckoutInput.Payments";
    private const string ReceivedAmountKey = "CheckoutInput.ReceivedAmount";

    public static CheckoutInputModel ApplyDefaults(
        CheckoutInputModel input,
        IStoreClock storeClock,
        SlipDetail? detail = null)
    {
        return new CheckoutInputModel
        {
            ReceiptAddressee = input.ReceiptAddressee,
            ClosedTime = input.ClosedTime ?? GetDefaultClosedTime(storeClock, detail),
            ClosedAt = input.ClosedAt,
            Payments = PreparePaymentRows(input.Payments),
            ConfirmedSnapshotJson = input.ConfirmedSnapshotJson,
            ReceivedAmount = input.ReceivedAmount
        };
    }

    public static CheckoutInputEdit PrepareConfirm(
        CheckoutInputModel input,
        SlipDetail detail,
        CheckoutTotals totals,
        IReadOnlyCollection<string> timeOptions,
        IStoreClock storeClock,
        bool requireReceivedAmount)
    {
        var prepared = new CheckoutInputModel
        {
            ReceiptAddressee = NormalizeReceiptAddressee(input.ReceiptAddressee),
            ClosedTime = input.ClosedTime,
            ClosedAt = ComposeClosedAt(detail.BusinessDate, input.ClosedTime, storeClock),
            Payments = PreparePaymentRows(input.Payments),
            ConfirmedSnapshotJson = input.ConfirmedSnapshotJson,
            ReceivedAmount = input.ReceivedAmount
        };

        var selectedPayments = prepared.Payments.Where(x => x.IsSelected).ToList();
        var errors = Validate(prepared, detail, totals, timeOptions, storeClock, selectedPayments, requireReceivedAmount);
        return new CheckoutInputEdit(
            prepared,
            selectedPayments.Any(x => string.Equals(x.MethodCode, "cash", StringComparison.Ordinal)),
            errors);
    }

    private static List<CheckoutPaymentInputModel> PreparePaymentRows(
        IEnumerable<CheckoutPaymentInputModel> inputPayments)
    {
        var current = inputPayments
            .Where(x => !string.IsNullOrWhiteSpace(x.MethodCode))
            .GroupBy(x => x.MethodCode.Trim().ToLowerInvariant())
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        return PaymentTemplates
            .Select(template =>
            {
                var methodCode = template.MethodCode.Trim().ToLowerInvariant();
                var methodName = template.MethodName.Trim();
                return current.TryGetValue(methodCode, out var existing)
                    ? new CheckoutPaymentInputModel
                    {
                        MethodCode = methodCode,
                        MethodName = methodName,
                        IsSelected = existing.IsSelected,
                        Amount = existing.Amount
                    }
                    : new CheckoutPaymentInputModel
                    {
                        MethodCode = methodCode,
                        MethodName = methodName
                    };
            })
            .ToList();
    }

    private static string NormalizeReceiptAddressee(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static readonly IReadOnlyList<CheckoutPaymentInputModel> PaymentTemplates =
    [
        new() { MethodCode = "cash", MethodName = "現金" },
        new() { MethodCode = "cat", MethodName = "CAT" },
        new() { MethodCode = "paypay", MethodName = "PAYPAY" }
    ];

    private static DateTime? ComposeClosedAt(DateOnly businessDate, string? closedTime, IStoreClock storeClock)
    {
        return TimeOnly.TryParse(closedTime, out var time)
            ? storeClock.ComposeBusinessDateTime(businessDate, time)
            : null;
    }

    private static string GetDefaultClosedTime(IStoreClock storeClock, SlipDetail? detail)
    {
        var defaultTime = storeClock.FloorToMinuteStep(storeClock.GetStoreNow(), CheckoutTimeMinuteStep).ToString("HH:mm");
        if (detail is null || !TimeOnly.TryParse(defaultTime, out var parsedDefaultTime))
        {
            return defaultTime;
        }

        var defaultClosedAt = storeClock.ComposeBusinessDateTime(detail.BusinessDate, parsedDefaultTime);
        var minimumClosedAt = GetMinimumClosedAt(detail, storeClock);
        return defaultClosedAt < minimumClosedAt
            ? CeilToMinuteStep(minimumClosedAt, CheckoutTimeMinuteStep).ToString("HH:mm")
            : defaultTime;
    }

    private static IReadOnlyList<CheckoutInputValidationError> Validate(
        CheckoutInputModel input,
        SlipDetail detail,
        CheckoutTotals totals,
        IReadOnlyCollection<string> timeOptions,
        IStoreClock storeClock,
        IReadOnlyList<CheckoutPaymentInputModel> selectedPayments,
        bool requireReceivedAmount)
    {
        List<CheckoutInputValidationError> errors = [];

        if (string.IsNullOrWhiteSpace(input.ClosedTime) || !timeOptions.Contains(input.ClosedTime))
        {
            errors.Add(new CheckoutInputValidationError(ClosedTimeKey, "退店時刻は5分単位で選択してください。"));
        }

        if (input.ClosedAt is null || input.ClosedAt.Value < GetMinimumClosedAt(detail, storeClock))
        {
            errors.Add(new CheckoutInputValidationError(ClosedTimeKey, "退店時刻はすべての客の入店時刻以降で入力してください。"));
        }

        if (selectedPayments.Count == 0)
        {
            errors.Add(new CheckoutInputValidationError(PaymentsKey, "決済方法を選択してください。"));
            return errors;
        }

        if (selectedPayments.Any(x => x.Amount < 0) ||
            (totals.TotalAmount > 0 && selectedPayments.Any(x => x.Amount <= 0)))
        {
            errors.Add(new CheckoutInputValidationError(PaymentsKey, "選択した決済方法の金額を入力してください。"));
        }

        var selectedTotal = selectedPayments.Sum(x => x.Amount);
        if (selectedTotal != totals.TotalAmount)
        {
            errors.Add(new CheckoutInputValidationError(PaymentsKey, "決済金額の合計が合計額と一致していません。"));
        }

        if (requireReceivedAmount)
        {
            ValidateReceivedAmount(input, selectedPayments, errors);
        }

        return errors;
    }

    private static DateTime GetMinimumClosedAt(SlipDetail detail, IStoreClock storeClock)
    {
        var minimumClosedAt = storeClock.ToStoreDateTime(detail.OpenedAt);
        foreach (var customer in detail.Customers.Where(x => !string.Equals(x.Status, "cancelled", StringComparison.Ordinal)))
        {
            var enteredAt = storeClock.ToStoreDateTime(customer.EnteredAt);
            if (enteredAt > minimumClosedAt)
            {
                minimumClosedAt = enteredAt;
            }
        }

        return minimumClosedAt;
    }

    private static DateTime CeilToMinuteStep(DateTime value, int minuteStep)
    {
        if (minuteStep <= 0)
        {
            minuteStep = CheckoutTimeMinuteStep;
        }

        var floored = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0);
        var remainder = value.Minute % minuteStep;
        return remainder == 0 && value == floored
            ? floored
            : floored.AddMinutes(remainder == 0 ? minuteStep : minuteStep - remainder);
    }

    private static void ValidateReceivedAmount(
        CheckoutInputModel input,
        IEnumerable<CheckoutPaymentInputModel> selectedPayments,
        List<CheckoutInputValidationError> errors)
    {
        var cashAmount = selectedPayments
            .Where(x => string.Equals(x.MethodCode, "cash", StringComparison.Ordinal))
            .Sum(x => x.Amount);

        if (cashAmount <= 0)
        {
            errors.Add(new CheckoutInputValidationError(PaymentsKey, "現金決済が選択されていません。"));
        }

        if (input.ReceivedAmount is null)
        {
            errors.Add(new CheckoutInputValidationError(ReceivedAmountKey, "受取額を入力してください。"));
        }
        else if (input.ReceivedAmount.Value < cashAmount)
        {
            errors.Add(new CheckoutInputValidationError(ReceivedAmountKey, "受取額が現金決済額を下回っています。"));
        }
    }
}

public sealed record CheckoutInputEdit(
    CheckoutInputModel Input,
    bool HasSelectedCashPayment,
    IReadOnlyList<CheckoutInputValidationError> Errors);

public sealed record CheckoutInputValidationError(string Key, string Message);
