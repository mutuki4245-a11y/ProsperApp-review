using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Slips;

public interface IStoreSlipRepository
{
    Task<Result<StoreContext>> GetStoreContextAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<StoreTableOption>>> GetTablesAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<CastOption>>> GetCastsAsync(CancellationToken ct);

    Task<BusinessHomeBootstrapResult> GetBusinessHomeBootstrapAsync(CancellationToken ct);

    Task<BusinessDaySnapshotResult> GetBusinessDaySnapshotAsync(long businessDayId, CancellationToken ct);

    Task<Result<CurrentBusinessHomeSnapshotResult>> GetCurrentBusinessHomeSnapshotAsync(CancellationToken ct);

    Task<BusinessHomeChangeFlushResult> FlushBusinessHomeChangesAsync(BusinessHomeChangeFlushInput input, CancellationToken ct);

    Task<CreateSlipResult> CreateSlipAsync(CreateSlipInputModel input, CancellationToken ct);
}

public sealed record CurrentBusinessHomeSnapshotResult(
    StoreBusinessDay? BusinessDay,
    DateOnly BusinessDate,
    System.Text.Json.JsonElement? Snapshot);
