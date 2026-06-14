namespace ProsperApp.Models;

public class PendingReceiptItem
{
    public string Id { get; init; } = string.Empty;
    public string? DocumentNo { get; init; }
    public string? FileName { get; init; }
    public string? DriveFileId { get; init; }
    public string? FilePath { get; init; }
    public string? PreviewUrl { get; init; }
    public DateOnly? PaymentDate { get; init; }
    public decimal? Amount { get; init; }
}
