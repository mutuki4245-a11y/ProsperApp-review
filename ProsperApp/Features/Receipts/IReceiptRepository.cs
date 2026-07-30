using ProsperApp.Features.Shared;
using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IReceiptRepository
{
    Task<Result<IReadOnlyList<PendingReceiptItem>>> GetPendingResultAsync(CancellationToken ct);

    Task<IReadOnlyList<PendingReceiptItem>> GetPendingAsync(CancellationToken ct);
    Task<bool> IsPendingDriveFileAllowedAsync(string driveFileId, CancellationToken ct);
    Task<SaveReceiptResult> SaveQuickEntryAsync(QuickEntryInputModel input, CancellationToken ct);
    Task<SaveReceiptResult> MarkScanMistakeAsync(string documentId, CancellationToken ct);
}
