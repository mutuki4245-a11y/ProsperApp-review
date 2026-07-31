using ProsperApp.Features.Shared;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public sealed class SupabaseStoreTableAdminRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreTableAdminRepository
{
    private readonly IApplicationCache _cache = cache;

    public async Task<Result<IReadOnlyList<StoreTableAdminItem>>> GetTablesAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<IReadOnlyList<StoreTableAdminItem>>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.TableAdminList(departmentId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<StoreTableAdminItem>? cachedTables))
        {
            return Result<IReadOnlyList<StoreTableAdminItem>>.Success(cachedTables ?? []);
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_table_admin_list",
            new { p_department_id = departmentId },
            ct);

        if (!result.Succeeded)
        {
            return RpcFailure<IReadOnlyList<StoreTableAdminItem>>(
                result.ErrorMessage,
                "卓番マスタを取得できませんでした。");
        }

        var tables = result.Rows
            .Select(row => new StoreTableAdminItem
            {
                TableId = ReadLong(row, "table_id") ?? 0,
                TableCode = ReadString(row, "table_code") ?? string.Empty,
                TableName = ReadString(row, "table_name"),
                TableCategoryNo = (int)(ReadLong(row, "table_category_no") ?? 0),
                SortOrder = (int)(ReadLong(row, "sort_order") ?? 0),
                IsActive = ReadBool(row, "is_active") ?? false
            })
            .Where(table => table.TableId > 0)
            .ToList();

        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, tables, "卓番管理一覧");
        return Result<IReadOnlyList<StoreTableAdminItem>>.Success(tables);
    }

    public async Task<StoreTableAdminSaveResult> SaveTableAsync(StoreTableInputModel input, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StoreTableAdminSaveResult.Failed("Supabase Edge Function設定が未設定です。卓番を保存できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.upsert_table",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_table_id = NormalizeId(input.TableId),
                p_table_code = input.TableCode.Trim(),
                p_table_name = string.IsNullOrWhiteSpace(input.TableName) ? null : input.TableName.Trim(),
                p_table_category_no = input.TableCategoryNo,
                p_sort_order = input.SortOrder,
                p_is_active = input.IsActive
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreTableAdminSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var tableId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "table_id") ?? 0 : 0;
        if (tableId > 0)
        {
            StoreMasterCacheKeys.ClearTables(_cache, CurrentStoreDepartmentId);
        }

        return tableId > 0
            ? StoreTableAdminSaveResult.Success(tableId)
            : StoreTableAdminSaveResult.Failed("卓番を保存できませんでした。");
    }

    public async Task<StoreTableAdminSaveResult> DeleteTableAsync(long tableId, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StoreTableAdminSaveResult.Failed("Supabase Edge Function設定が未設定です。卓番を削除できません。");
        }

        if (tableId <= 0)
        {
            return StoreTableAdminSaveResult.Failed("削除する卓番を選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.delete_table",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_table_id = tableId
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreTableAdminSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var deletedTableId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "table_id") ?? 0 : 0;
        if (deletedTableId > 0)
        {
            StoreMasterCacheKeys.ClearTables(_cache, CurrentStoreDepartmentId);
        }

        return deletedTableId > 0
            ? StoreTableAdminSaveResult.Success(deletedTableId)
            : StoreTableAdminSaveResult.Failed("卓番を削除できませんでした。");
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "卓番を保存できませんでした。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を確認してください。";
        }

        if (rawError.Contains("invalid_store_table", StringComparison.OrdinalIgnoreCase))
        {
            return "卓番の入力内容を確認してください。";
        }

        if (rawError.Contains("store_table_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "対象の卓番が見つかりません。";
        }

        if (rawError.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            return "同じ卓番コードが既に登録されています。";
        }

        return "卓番を保存できませんでした。";
    }
}
