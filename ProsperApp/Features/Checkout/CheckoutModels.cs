using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ProsperApp.Features.Checkout;

public class CheckoutPaymentMethod
{
    public string MethodCode { get; init; } = string.Empty;

    public string MethodName { get; init; } = string.Empty;

    public bool RequiresReceivedAmount { get; init; }

    public int SortOrder { get; init; }
}

public class CheckoutPaymentInputModel
{
    public string MethodCode { get; set; } = string.Empty;

    public bool IsSelected { get; set; }

    [Range(0, 99999999, ErrorMessage = "決済金額を確認してください。")]
    public decimal Amount { get; set; }
}

public sealed record IssueCheckoutStatementV2Mutation(
    string OperationId,
    long ExpectedBusinessDayId,
    long ExpectedBusinessDayRevision,
    long SlipId,
    DateTimeOffset ClosedAt);

public sealed record ReleaseCheckoutReadyV2Mutation(
    string OperationId,
    long ExpectedBusinessDayId,
    long ExpectedBusinessDayRevision,
    long SlipId);

public sealed record ConfirmCheckoutV2Mutation(
    string OperationId,
    long ExpectedBusinessDayId,
    long ExpectedBusinessDayRevision,
    long SlipId,
    IReadOnlyList<CheckoutPaymentInputModel> Payments,
    decimal? ReceivedAmount);

public sealed record CancelCheckoutV2Mutation(
    string OperationId,
    long ExpectedBusinessDayId,
    long ExpectedBusinessDayRevision,
    long SlipId);

public sealed record CheckoutMutationResult(
    string OperationId,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    long? SlipId,
    long? CheckoutId,
    long? BusinessDayId,
    long BusinessDayRevision,
    string? CurrentSlipStatus,
    decimal ChangeAmount,
    JsonElement? StatementPrintData,
    JsonElement? StatementReviewData,
    JsonElement? ReceiptPrintData,
    JsonElement? BusinessSnapshot)
{
    public bool Confirmed => string.Equals(Status, "confirmed", StringComparison.Ordinal);

    public bool Conflict => string.Equals(Status, "conflict", StringComparison.Ordinal);

    public bool ValidationError => string.Equals(Status, "validation_error", StringComparison.Ordinal);
}

public class CheckoutStatementResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public JsonElement? PrintData { get; init; }
    public JsonElement? ReviewData { get; init; }

    public static CheckoutStatementResult Success(JsonElement printData, JsonElement reviewData) => new()
    {
        Succeeded = true,
        PrintData = printData.Clone(),
        ReviewData = reviewData.Clone()
    };

    public static CheckoutStatementResult Failed(string message) => new()
    {
        Succeeded = false,
        ErrorMessage = message
    };
}

public class ReceiptPrintDataResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public long? CheckoutId { get; init; }
    public JsonElement? PrintData { get; init; }

    public static ReceiptPrintDataResult Success(long checkoutId, JsonElement printData) => new()
    {
        Succeeded = true,
        CheckoutId = checkoutId,
        PrintData = printData.Clone()
    };

    public static ReceiptPrintDataResult Failed(string message) => new()
    {
        Succeeded = false,
        ErrorMessage = message
    };
}
