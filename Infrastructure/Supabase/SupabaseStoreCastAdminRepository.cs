using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseStoreCastAdminRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreCastAdminRepository
{
    public async Task<IReadOnlyList<StoreCastAdminItem>> GetCastsAsync(CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_store_cast_admin_list",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        return rows.Select(row => new StoreCastAdminItem
            {
                CastId = ReadLong(row, "cast_id") ?? 0,
                DisplayName = ReadString(row, "display_name") ?? string.Empty,
                JoinedOn = ReadDateOnly(row, "joined_on") ?? DateOnly.MinValue
            })
            .Where(x => x.CastId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
    }

    public async Task<StoreCastSaveResult> CreateCastAsync(StoreCastCreateInputModel input, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return StoreCastSaveResult.Failed("Supabase SecretKeyが未設定です。キャストを登録できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "create_store_cast",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_display_name = input.DisplayName.Trim()
            },
            requireSecretKey: true,
            ct);

        if (!result.Succeeded)
        {
            return StoreCastSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var castId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "cast_id") ?? 0 : 0;
        return castId > 0
            ? StoreCastSaveResult.Success(castId)
            : StoreCastSaveResult.Failed("キャストを登録できませんでした。");
    }

    public async Task<StoreCastSaveResult> DeleteCastAsync(long castId, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return StoreCastSaveResult.Failed("Supabase SecretKeyが未設定です。キャストを削除できません。");
        }

        if (castId <= 0)
        {
            return StoreCastSaveResult.Failed("削除するキャストを選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "delete_store_cast",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_cast_id = castId
            },
            requireSecretKey: true,
            ct);

        if (!result.Succeeded)
        {
            return StoreCastSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var deletedCastId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "cast_id") ?? 0 : 0;
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
            return "キャスト名を入力してください。";
        }

        if (rawError.Contains("store_cast_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "削除対象のキャストが見つかりません。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"キャストを登録できませんでした。{rawError}";
    }
}
