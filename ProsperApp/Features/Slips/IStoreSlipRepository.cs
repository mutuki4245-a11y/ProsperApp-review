using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Slips;

public interface IStoreSlipRepository
{
    Task<Result<StoreContext>> GetStoreContextAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<StoreTableOption>>> GetTablesAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<CastOption>>> GetCastsAsync(CancellationToken ct);

    Task<BusinessDaySnapshotResult> GetBusinessDaySnapshotAsync(long businessDayId, CancellationToken ct);

    Task<BusinessHomeChangeFlushResult> FlushBusinessHomeChangesAsync(BusinessHomeChangeFlushInput input, long businessDayId, CancellationToken ct);

    Task<CreateSlipResult> CreateSlipAsync(CreateSlipInputModel input, CancellationToken ct);
}
