using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Receipts;

public sealed record ReceiptWorkQueue(
    string Revision,
    int PendingCount,
    StoreBusinessDay? BusinessDay,
    PendingReceiptItem? WorkItem,
    IReadOnlyList<PendingReceiptItem> Buffer,
    string? ResumeCursor,
    IReadOnlyList<BusinessDayClosingAttendanceItem> AdvanceCastOptions);

public sealed record ReceiptWorkQueueAdvanceInput(
    string OperationId,
    string Action,
    string WorkItemToken,
    string DocumentId,
    DateOnly? PaymentDate,
    decimal? Amount,
    string? AccountSubject,
    string? Description,
    string? GroupCode,
    long? AdvanceCastId);

public sealed record ReceiptWorkQueueAdvanceResult(
    string Status,
    string? DocumentId,
    string? Message,
    ReceiptWorkQueue Queue,
    int PendingReceiptCount)
{
    public bool IsConfirmed => string.Equals(Status, "confirmed", StringComparison.Ordinal);

    public bool IsTerminalFailure => Status is "validation_error" or "stale_work_item";
}
