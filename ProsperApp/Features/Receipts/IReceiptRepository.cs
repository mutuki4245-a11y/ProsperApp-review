using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Receipts;

public interface IReceiptRepository
{
    Task<Result<IReadOnlyList<PendingReceiptItem>>> GetPendingResultAsync(CancellationToken ct);

    Task<Result<bool>> IsPendingDriveFileAllowedAsync(string driveFileId, CancellationToken ct);
    Task<SaveReceiptResult> SaveQuickEntryAsync(QuickEntryInputModel input, CancellationToken ct);
    Task<SaveReceiptResult> MarkScanMistakeAsync(string documentId, CancellationToken ct);
}
