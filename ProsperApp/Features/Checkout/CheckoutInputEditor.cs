using ProsperApp.Models;

namespace ProsperApp.Services;

public static class CheckoutInputEditor
{
    private const string ClosedTimeKey = "CheckoutInput.ClosedTime";
    private const string PaymentsKey = "CheckoutInput.Payments";
    private const string ReceivedAmountKey = "CheckoutInput.ReceivedAmount";

    public static CheckoutInputModel ApplyDefaults(
        CheckoutInputModel input,
        IReadOnlyList<CheckoutPaymentInputModel> paymentTemplates,
        string defaultClosedTime)
    {
        return new CheckoutInputModel
        {
            ClosedTime = input.ClosedTime ?? defaultClosedTime,
            ClosedAt = input.ClosedAt,
            Payments = PreparePaymentRows(input.Payments, paymentTemplates),
            ConfirmedSnapshotJson = input.ConfirmedSnapshotJson,
            ReceivedAmount = input.ReceivedAmount
        };
    }

    public static CheckoutInputEdit PrepareConfirm(
        CheckoutInputModel input,
        SlipDetail detail,
        CheckoutTotals totals,
        IReadOnlyList<CheckoutPaymentInputModel> paymentTemplates,
        IReadOnlyCollection<string> timeOptions,
        IStoreClock storeClock,
        bool requireReceivedAmount)
    {
        var prepared = new CheckoutInputModel
        {
            ClosedTime = input.ClosedTime,
            ClosedAt = ComposeClosedAt(detail.BusinessDate, input.ClosedTime, storeClock),
            Payments = PreparePaymentRows(input.Payments, paymentTemplates),
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
        IEnumerable<CheckoutPaymentInputModel> inputPayments,
        IReadOnlyList<CheckoutPaymentInputModel> paymentTemplates)
    {
        var current = inputPayments
            .Where(x => !string.IsNullOrWhiteSpace(x.MethodCode))
            .GroupBy(x => x.MethodCode.Trim().ToLowerInvariant())
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        return paymentTemplates
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

    private static DateTime? ComposeClosedAt(DateOnly businessDate, string? closedTime, IStoreClock storeClock)
    {
        return TimeOnly.TryParse(closedTime, out var time)
            ? storeClock.ComposeBusinessDateTime(businessDate, time)
            : null;
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

        if (input.ClosedAt is null || input.ClosedAt.Value < storeClock.ToStoreDateTime(detail.OpenedAt))
        {
            errors.Add(new CheckoutInputValidationError(ClosedTimeKey, "退店時刻は入店時刻以降で入力してください。"));
        }

        if (selectedPayments.Count == 0)
        {
            errors.Add(new CheckoutInputValidationError(PaymentsKey, "決済方法を選択してください。"));
            return errors;
        }

        if (selectedPayments.Any(x => x.Amount <= 0))
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
