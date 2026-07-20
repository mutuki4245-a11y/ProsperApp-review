using Microsoft.Extensions.Caching.Memory;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseStoreCastAdminRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IMemoryCache memoryCache) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreCastAdminRepository
{
    private readonly IMemoryCache _memoryCache = memoryCache;

    public async Task<IReadOnlyList<StoreCastAdminItem>> GetCastsAsync(CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return [];
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.CastAdminList(departmentId);
        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<StoreCastAdminItem>? cachedCasts))
        {
            return cachedCasts ?? [];
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_casts_admin",
            new { p_department_id = departmentId },
            ct);

        if (!result.Succeeded)
        {
            return [];
        }

        var casts = result.Rows.Select(row => new StoreCastAdminItem
            {
                CastId = ReadLong(row, "cast_id") ?? 0,
                DisplayName = ReadString(row, "display_name") ?? string.Empty,
                DrinkMemo = ReadString(row, "drink_memo"),
                JoinedOn = ReadDateOnly(row, "joined_on") ?? DateOnly.MinValue
            })
            .Where(x => x.CastId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
        _memoryCache.Set(cacheKey, casts, StoreMasterCacheKeys.CreateOptions());
        return casts;
    }

    public async Task<StoreCastSaveResult> CreateCastAsync(StoreCastCreateInputModel input, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return StoreCastSaveResult.Failed("Supabase Edge Function設定が未設定です。キャストを登録できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.create_cast",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_display_name = input.DisplayName.Trim(),
                p_drink_memo = NormalizeDrinkMemo(input.DrinkMemo)
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreCastSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var castId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "cast_id") ?? 0 : 0;
        if (castId > 0)
        {
            StoreMasterCacheKeys.ClearCasts(_memoryCache, CurrentStoreDepartmentId);
        }

        return castId > 0
            ? StoreCastSaveResult.Success(castId)
            : StoreCastSaveResult.Failed("キャストを登録できませんでした。");
    }

    public async Task<StoreCastSaveResult> UpdateDrinkMemoAsync(StoreCastDrinkMemoInputModel input, long? currentBusinessDayId, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return StoreCastSaveResult.Failed("Supabase Edge Function設定が未設定です。ドリンクメモを更新できません。");
        }

        if (input.CastId <= 0)
        {
            return StoreCastSaveResult.Failed("編集するキャストを選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.update_cast_drink_memo",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_cast_id = input.CastId,
                p_drink_memo = NormalizeDrinkMemo(input.DrinkMemo)
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreCastSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var castId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "cast_id") ?? 0 : 0;
        if (castId > 0)
        {
            StoreMasterCacheKeys.ClearCasts(_memoryCache, CurrentStoreDepartmentId);
            StoreMasterCacheKeys.ClearOrderAttendingCasts(_memoryCache, CurrentStoreDepartmentId, currentBusinessDayId ?? 0);
        }

        return castId > 0
            ? StoreCastSaveResult.Success(castId)
            : StoreCastSaveResult.Failed("ドリンクメモを更新できませんでした。");
    }

    public async Task<StoreCastSaveResult> DeleteCastAsync(long castId, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return StoreCastSaveResult.Failed("Supabase Edge Function設定が未設定です。キャストを削除できません。");
        }

        if (castId <= 0)
        {
            return StoreCastSaveResult.Failed("削除するキャストを選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.delete_cast",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_cast_id = castId
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreCastSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var deletedCastId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "cast_id") ?? 0 : 0;
        if (deletedCastId > 0)
        {
            StoreMasterCacheKeys.ClearCasts(_memoryCache, CurrentStoreDepartmentId);
        }

        return deletedCastId > 0
            ? StoreCastSaveResult.Success(deletedCastId)
            : StoreCastSaveResult.Failed("キャストを削除できませんでした。");
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "キャストを登録できませんでした。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を確認してください。";
        }

        if (rawError.Contains("invalid_store_cast", StringComparison.OrdinalIgnoreCase))
        {
            return "キャスト名またはドリンクメモを確認してください。";
        }

        if (rawError.Contains("store_cast_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "対象のキャストが見つかりません。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"キャスト情報を保存できませんでした。{rawError}";
    }

    private static string? NormalizeDrinkMemo(string? drinkMemo)
    {
        return string.IsNullOrWhiteSpace(drinkMemo) ? null : drinkMemo.Trim();
    }
}
