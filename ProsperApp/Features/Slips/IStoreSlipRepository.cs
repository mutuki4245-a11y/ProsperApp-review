using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Slips;

public interface IStoreSlipRepository
{
    Task<Result<StoreContext>> GetStoreContextAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<StoreTableOption>>> GetTablesAsync(CancellationToken ct);

    Task<BusinessHomeBootstrapResult> GetBusinessHomeBootstrapAsync(CancellationToken ct);

    Task<Result<CurrentBusinessHomeSnapshotResult>> GetCurrentBusinessHomeSnapshotAsync(long? knownRevision, CancellationToken ct);

    Task<BusinessHomeChangeFlushResult> FlushBusinessHomeChangesAsync(BusinessHomeChangeFlushInput input, CancellationToken ct);

}

public sealed record CurrentBusinessHomeSnapshotResult(
    StoreBusinessDay? BusinessDay,
    DateOnly BusinessDate,
    long Revision,
    bool Unchanged,
    System.Text.Json.JsonElement? AttendanceCasts,
    System.Text.Json.JsonElement? Snapshot);
