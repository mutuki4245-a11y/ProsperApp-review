using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IStoreCastAdminRepository
{
    Task<IReadOnlyList<StoreCastAdminItem>> GetCastsAsync(CancellationToken ct);

    Task<StoreCastSaveResult> CreateCastAsync(StoreCastCreateInputModel input, CancellationToken ct);

    Task<StoreCastSaveResult> DeleteCastAsync(long castId, CancellationToken ct);
}
