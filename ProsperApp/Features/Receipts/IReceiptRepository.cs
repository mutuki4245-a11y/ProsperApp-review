using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Receipts;

public interface IReceiptRepository
{
    Task<Result<ReceiptWorkQueue>> GetCurrentWorkQueueAsync(string? resumeCursor, CancellationToken ct);

    Task<Result<ReceiptWorkQueueAdvanceResult>> AdvanceWorkQueueAsync(
        ReceiptWorkQueueAdvanceInput input,
        CancellationToken ct);

    Task<Result<bool>> IsPendingDriveFileAllowedAsync(string driveFileId, CancellationToken ct);
}
