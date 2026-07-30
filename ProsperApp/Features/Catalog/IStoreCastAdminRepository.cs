using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Catalog;

public interface IStoreCastAdminRepository
{
    Task<Result<IReadOnlyList<StoreCastAdminItem>>> GetCastsAsync(CancellationToken ct);

    Task<StoreCastSaveResult> CreateCastAsync(StoreCastCreateInputModel input, CancellationToken ct);

    Task<StoreCastSaveResult> UpdateDrinkMemoAsync(StoreCastDrinkMemoInputModel input, long? currentBusinessDayId, CancellationToken ct);

    Task<StoreCastSaveResult> DeleteCastAsync(long castId, CancellationToken ct);
}
