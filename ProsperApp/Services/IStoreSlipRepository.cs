using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IStoreSlipRepository
{
    Task<StoreContext?> GetStoreContextAsync(CancellationToken ct);

    Task<IReadOnlyList<StoreTableOption>> GetTablesAsync(CancellationToken ct);

    Task<IReadOnlyList<CastOption>> GetCastsAsync(CancellationToken ct);

    Task<IReadOnlyList<BusinessSlipListItem>> GetBusinessDaySlipsAsync(long businessDayId, CancellationToken ct);

    Task<CreateSlipResult> CreateSlipAsync(CreateSlipInputModel input, CancellationToken ct);
}
