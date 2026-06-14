using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IStoreItemAdminRepository
{
    Task<StoreItemAdminCatalog> GetCatalogAsync(CancellationToken ct);

    Task<StoreItemAdminSaveResult> SaveCategoryAsync(StoreItemCategoryInputModel input, CancellationToken ct);

    Task<StoreItemAdminSaveResult> SaveItemAsync(StoreItemInputModel input, CancellationToken ct);

    Task<StoreItemAdminSaveResult> DeleteItemAsync(long itemId, CancellationToken ct);

    Task<StoreItemAdminSaveResult> ReorderItemsAsync(IReadOnlyList<StoreItemOrderInputModel> items, CancellationToken ct);
}
