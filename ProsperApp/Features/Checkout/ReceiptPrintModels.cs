namespace ProsperApp.Models;

public class ReceiptPrintRequest
{
    public string SchemaVersion { get; set; } = "checkout-receipt-v1";

    public string Provider { get; set; } = "sii";

    public long CheckoutId { get; set; }

    public long SlipId { get; set; }

    public string? SlipNo { get; set; }

    public string StoreName { get; set; } = string.Empty;

    public string Addressee { get; set; } = string.Empty;

    public string TableDisplayName { get; set; } = string.Empty;

    public DateTimeOffset ClosedAt { get; set; }

    public decimal SubtotalAmount { get; set; }

    public decimal ServiceTaxAmount { get; set; }

    public decimal AdjustmentAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal? ReceivedAmount { get; set; }

    public decimal ChangeAmount { get; set; }

    public List<ReceiptPrintLine> Lines { get; set; } = [];

    public List<ReceiptPrintPayment> Payments { get; set; } = [];
}

public class ReceiptPrintLine
{
    public string LineType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Amount { get; set; }
}

public class ReceiptPrintPayment
{
    public string MethodCode { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}
