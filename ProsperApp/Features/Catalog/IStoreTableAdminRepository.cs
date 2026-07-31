using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Catalog;

public interface IStoreTableAdminRepository
{
    Task<Result<IReadOnlyList<StoreTableAdminItem>>> GetTablesAsync(CancellationToken ct);

    Task<StoreTableAdminSaveResult> SaveTableAsync(StoreTableInputModel input, CancellationToken ct);

    Task<StoreTableAdminSaveResult> DeleteTableAsync(long tableId, CancellationToken ct);
}
