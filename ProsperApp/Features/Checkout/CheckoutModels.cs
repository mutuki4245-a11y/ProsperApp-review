using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ProsperApp.Models;

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

public class ConfirmCheckoutResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public long? CheckoutId { get; init; }
    public decimal ChangeAmount { get; init; }
    public JsonElement? ReceiptPrintData { get; init; }
    public bool RequiresReload { get; init; }

    public static ConfirmCheckoutResult Success(long checkoutId, decimal changeAmount, JsonElement receiptPrintData)
    {
        return new ConfirmCheckoutResult
        {
            Succeeded = true,
            CheckoutId = checkoutId,
            ChangeAmount = changeAmount,
            ReceiptPrintData = receiptPrintData.Clone()
        };
    }

    public static ConfirmCheckoutResult Failed(string message, bool requiresReload = false)
    {
        return new ConfirmCheckoutResult { Succeeded = false, ErrorMessage = message, RequiresReload = requiresReload };
    }
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

public class ReleaseCheckoutReadyResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }

    public static ReleaseCheckoutReadyResult Success() => new() { Succeeded = true };
    public static ReleaseCheckoutReadyResult Failed(string message) => new() { Succeeded = false, ErrorMessage = message };
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

public class CancelCheckoutResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public long? CheckoutId { get; init; }

    public static CancelCheckoutResult Success(long checkoutId)
    {
        return new CancelCheckoutResult { Succeeded = true, CheckoutId = checkoutId };
    }

    public static CancelCheckoutResult Failed(string message)
    {
        return new CancelCheckoutResult { Succeeded = false, ErrorMessage = message };
    }
}
