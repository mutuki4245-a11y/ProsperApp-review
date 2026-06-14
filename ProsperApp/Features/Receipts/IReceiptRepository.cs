using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IReceiptRepository
{
    Task<IReadOnlyList<PendingReceiptItem>> GetPendingAsync(CancellationToken ct);
    Task<bool> IsPendingDriveFileAllowedAsync(string driveFileId, CancellationToken ct);
    Task<SaveReceiptResult> SaveQuickEntryAsync(QuickEntryInputModel input, CancellationToken ct);
    Task<SaveReceiptResult> MarkScanMistakeAsync(string documentId, CancellationToken ct);
}
