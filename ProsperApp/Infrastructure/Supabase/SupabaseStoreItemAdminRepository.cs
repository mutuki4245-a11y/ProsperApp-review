using System.Text.Json.Serialization;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseStoreItemAdminRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache,
    IStoreMasterBootstrapper masterBootstrapper) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreItemAdminRepository
{
    private readonly IApplicationCache _cache = cache;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public async Task<Result<StoreItemAdminCatalog>> GetCatalogAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<StoreItemAdminCatalog>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.ItemAdminCatalog(departmentId);
        if (_cache.TryGetValue(cacheKey, out StoreItemAdminCatalog? cachedCatalog))
        {
            return Result<StoreItemAdminCatalog>.Success(cachedCatalog ?? new StoreItemAdminCatalog());
        }

        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded)
        {
            return Result<StoreItemAdminCatalog>.Failure(
                bootstrap.FailureKind ?? ResultFailureKind.Unavailable,
                bootstrap.ErrorMessage ?? "商品マスタを取得できませんでした。");
        }

        var rows = StoreBootstrapJson.ReadArray(bootstrap.Value.Row, "item_admin_catalog");
        var categories = rows
            .Where(row => string.Equals(ReadString(row, "row_type"), "category", StringComparison.OrdinalIgnoreCase))
            .Select(row => new StoreItemCategoryAdminItem
            {
                ItemCategoryId = ReadLong(row, "item_category_id") ?? 0,
                CategoryCode = ReadString(row, "category_code") ?? string.Empty,
                CategoryName = ReadString(row, "category_name") ?? string.Empty,
                SortOrder = (int)(ReadLong(row, "sort_order") ?? 0),
                IsActive = ReadBool(row, "is_active") ?? false
            })
            .Where(x => x.ItemCategoryId > 0)
            .ToList();

        var items = rows
            .Where(row => string.Equals(ReadString(row, "row_type"), "item", StringComparison.OrdinalIgnoreCase))
            .Select(row => new StoreItemAdminItem
            {
                ItemId = ReadLong(row, "item_id") ?? 0,
                ItemCategoryId = ReadLong(row, "item_category_id"),
                CategoryCode = ReadString(row, "category_code") ?? string.Empty,
                CategoryName = ReadString(row, "category_name") ?? string.Empty,
                ItemName = ReadString(row, "item_name") ?? string.Empty,
                ItemType = ReadString(row, "item_type") ?? "standard",
                DefaultPrice = ReadDecimal(row, "default_price") ?? 0,
                SortOrder = (int)(ReadLong(row, "sort_order") ?? 0),
                IsActive = ReadBool(row, "is_active") ?? false,
                IsCastBackTarget = ReadBool(row, "is_cast_back_target") ?? false,
                CastBackRegularUnitAmount = ReadDecimal(row, "cast_back_regular_unit_amount") ?? 0,
                CastBackNominationUnitAmount = ReadDecimal(row, "cast_back_nomination_unit_amount") ?? 0,
                CastBackType = ReadString(row, "cast_back_type") ?? "drink"
            })
            .Where(x => x.ItemId > 0)
            .ToList();

        var catalog = new StoreItemAdminCatalog { Categories = categories, Items = items };
        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, catalog, "商品管理カタログ");
        return Result<StoreItemAdminCatalog>.Success(catalog);
    }

    public async Task<StoreItemAdminSaveResult> SaveCategoryAsync(StoreItemCategoryInputModel input, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StoreItemAdminSaveResult.Failed("Supabase Edge Function設定が未設定です。商品カテゴリを保存できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.upsert_item_category",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_item_category_id = NormalizeId(input.ItemCategoryId),
                p_category_code = input.CategoryCode.Trim(),
                p_category_name = input.CategoryName.Trim(),
                p_sort_order = input.SortOrder,
                p_is_active = input.IsActive
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreItemAdminSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var id = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "item_category_id") ?? 0 : 0;
        if (id > 0)
        {
            StoreMasterCacheKeys.ClearItems(_cache, CurrentStoreDepartmentId);
        }

        return id > 0
            ? StoreItemAdminSaveResult.Success(id)
            : StoreItemAdminSaveResult.Failed("カテゴリを保存できませんでした。");
    }

    public async Task<StoreItemAdminSaveResult> SaveItemAsync(StoreItemInputModel input, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StoreItemAdminSaveResult.Failed("Supabase Edge Function設定が未設定です。商品を保存できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.upsert_item",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_item_id = NormalizeId(input.ItemId),
                p_item_category_id = input.ItemCategoryId,
                p_item_name = input.ItemName.Trim(),
                p_default_price = input.DefaultPrice,
                p_is_active = input.IsActive,
                p_is_cast_back_target = input.IsCastBackTarget,
                p_cast_back_regular_unit_amount = input.IsCastBackTarget ? input.CastBackRegularUnitAmount : 0,
                p_cast_back_nomination_unit_amount = input.IsCastBackTarget ? input.CastBackNominationUnitAmount : 0,
                p_cast_back_type = "drink"
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreItemAdminSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var id = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "item_id") ?? 0 : 0;
        if (id > 0)
        {
            StoreMasterCacheKeys.ClearItems(_cache, CurrentStoreDepartmentId);
        }

        return id > 0
            ? StoreItemAdminSaveResult.Success(id)
            : StoreItemAdminSaveResult.Failed("商品を保存できませんでした。");
    }

    public async Task<StoreItemAdminSaveResult> DeleteItemAsync(long itemId, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StoreItemAdminSaveResult.Failed("Supabase Edge Function設定が未設定です。商品を削除できません。");
        }

        if (itemId <= 0)
        {
            return StoreItemAdminSaveResult.Failed("削除する商品を選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.delete_item",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_item_id = itemId
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreItemAdminSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var deletedItemId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "item_id") ?? 0 : 0;
        if (deletedItemId > 0)
        {
            StoreMasterCacheKeys.ClearItems(_cache, CurrentStoreDepartmentId);
        }

        return deletedItemId > 0
            ? StoreItemAdminSaveResult.Success(deletedItemId)
            : StoreItemAdminSaveResult.Failed("商品を削除できませんでした。");
    }

    public async Task<StoreItemAdminSaveResult> DeleteCategoryAsync(long itemCategoryId, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StoreItemAdminSaveResult.Failed("Supabase Edge Function設定が未設定です。カテゴリを削除できません。");
        }

        if (itemCategoryId <= 0)
        {
            return StoreItemAdminSaveResult.Failed("削除するカテゴリを選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.delete_item_category",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_item_category_id = itemCategoryId
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreItemAdminSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var deletedCategoryId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "item_category_id") ?? 0 : 0;
        if (deletedCategoryId > 0)
        {
            StoreMasterCacheKeys.ClearItems(_cache, CurrentStoreDepartmentId);
        }

        return deletedCategoryId > 0
            ? StoreItemAdminSaveResult.Success(deletedCategoryId)
            : StoreItemAdminSaveResult.Failed("カテゴリを削除できませんでした。");
    }

    public async Task<StoreItemAdminSaveResult> ReorderItemsAsync(IReadOnlyList<StoreItemOrderInputModel> items, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StoreItemAdminSaveResult.Failed("Supabase Edge Function設定が未設定です。商品の並び順を保存できません。");
        }

        var payload = items
            .Where(x => x.ItemId > 0)
            .Select(x => new StoreItemOrderPayload(x.ItemId, x.SortOrder))
            .ToArray();

        if (payload.Length == 0)
        {
            return StoreItemAdminSaveResult.Failed("並べ替える商品を選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.reorder_items",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_items = payload
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreItemAdminSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var updatedCount = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "updated_count") ?? 0 : 0;
        if (updatedCount > 0)
        {
            StoreMasterCacheKeys.ClearItems(_cache, CurrentStoreDepartmentId);
        }

        return updatedCount > 0
            ? StoreItemAdminSaveResult.Success(updatedCount)
            : StoreItemAdminSaveResult.Failed("商品の並び順を保存できませんでした。");
    }

    private sealed record StoreItemOrderPayload(
        [property: JsonPropertyName("item_id")] long ItemId,
        [property: JsonPropertyName("sort_order")] int SortOrder);

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "保存できませんでした。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を確認してください。";
        }

        if (rawError.Contains("invalid_store_item_category", StringComparison.OrdinalIgnoreCase))
        {
            return "カテゴリの入力内容を確認してください。";
        }

        if (rawError.Contains("store_item_category_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "対象のカテゴリが見つかりません。";
        }

        if (rawError.Contains("store_item_category_in_use", StringComparison.OrdinalIgnoreCase))
        {
            return "商品が登録されているカテゴリは削除できません。先に配下の商品を削除してください。";
        }

        if (rawError.Contains("invalid_store_item", StringComparison.OrdinalIgnoreCase))
        {
            return "商品の入力内容を確認してください。";
        }

        if (rawError.Contains("store_item_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "削除対象の商品が見つかりません。";
        }

        if (rawError.Contains("store_item_locked", StringComparison.OrdinalIgnoreCase))
        {
            return "システム商品は削除できません。";
        }

        if (rawError.Contains("invalid_store_item_order", StringComparison.OrdinalIgnoreCase))
        {
            return "商品の並び順を確認してください。";
        }

        if (rawError.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            return "同じ商品名が既に登録されています。別の商品名を入力してください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"保存できませんでした。{rawError}";
    }
}
