using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Catalog;

public interface IStoreItemAdminRepository
{
    Task<Result<StoreItemAdminCatalog>> GetCatalogAsync(CancellationToken ct);

    Task<StoreItemAdminSaveResult> SaveCategoryAsync(StoreItemCategoryInputModel input, CancellationToken ct);

    Task<StoreItemAdminSaveResult> SaveItemAsync(StoreItemInputModel input, CancellationToken ct);

    Task<StoreItemAdminSaveResult> DeleteItemAsync(long itemId, CancellationToken ct);

    Task<StoreItemAdminSaveResult> ReorderItemsAsync(IReadOnlyList<StoreItemOrderInputModel> items, CancellationToken ct);
}
