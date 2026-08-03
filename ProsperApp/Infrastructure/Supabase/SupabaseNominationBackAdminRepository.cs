using System.Text.Json.Serialization;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseNominationBackAdminRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache,
    IStoreMasterBootstrapper masterBootstrapper) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), INominationBackAdminRepository
{
    private readonly IApplicationCache _cache = cache;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public async Task<Result<IReadOnlyList<NominationBackMasterItem>>> GetSettingsAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<IReadOnlyList<NominationBackMasterItem>>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.NominationBackMaster(departmentId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<NominationBackMasterItem>? cachedSettings))
        {
            return Result<IReadOnlyList<NominationBackMasterItem>>.Success(cachedSettings ?? []);
        }

        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded)
        {
            return Result<IReadOnlyList<NominationBackMasterItem>>.Failure(
                bootstrap.FailureKind ?? ResultFailureKind.Unavailable,
                bootstrap.ErrorMessage ?? "指名バック設定を取得できませんでした。");
        }

        var settings = StoreBootstrapJson.ReadArray(bootstrap.Value.Row, "nomination_options")
            .Select(row => new NominationBackMasterItem
            {
                NominationKind = ReadString(row, "nomination_kind") ?? ReadString(row, "nomination_type") ?? string.Empty,
                NominationType = ReadString(row, "nomination_type") ?? string.Empty,
                DisplayName = ReadString(row, "display_name") ?? string.Empty,
                CompanionTime = ReadString(row, "companion_time"),
                BackType = ReadString(row, "back_type") ?? "nomination",
                BackUnitAmount = ReadDecimal(row, "back_unit_amount") ?? 0,
                SortOrder = (int)(ReadLong(row, "sort_order") ?? 0),
                IsActive = ReadBool(row, "is_active") ?? true
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.NominationKind))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToList();

        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, settings, "指名バック設定");
        return Result<IReadOnlyList<NominationBackMasterItem>>.Success(settings);
    }

    public async Task<NominationBackMasterSaveResult> SaveSettingsAsync(
        IReadOnlyList<NominationBackMasterInputModel> settings,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return NominationBackMasterSaveResult.Failed("Supabase Edge Function設定が未設定です。指名バック設定を保存できません。");
        }

        var payload = settings
            .Where(x => !string.IsNullOrWhiteSpace(x.NominationKind))
            .Select(x => new NominationBackMasterPayload(
                x.NominationKind.Trim(),
                x.NominationType.Trim(),
                x.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(x.CompanionTime) ? null : x.CompanionTime.Trim(),
                x.BackUnitAmount,
                x.SortOrder,
                x.IsActive))
            .ToArray();

        if (payload.Length == 0)
        {
            return NominationBackMasterSaveResult.Failed("保存する指名バック設定がありません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.save_nomination_back_master",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_settings = payload
            },
            ct);

        if (!result.Succeeded)
        {
            return NominationBackMasterSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var updatedCount = result.Rows.Count > 0 ? (int)(ReadLong(result.Rows[0], "updated_count") ?? 0) : 0;
        if (updatedCount > 0)
        {
            StoreMasterCacheKeys.ClearNominationBacks(_cache, CurrentStoreDepartmentId);
        }

        return updatedCount > 0
            ? NominationBackMasterSaveResult.Success(updatedCount)
            : NominationBackMasterSaveResult.Failed("指名バック設定を保存できませんでした。");
    }

    private sealed record NominationBackMasterPayload(
        [property: JsonPropertyName("nomination_kind")] string NominationKind,
        [property: JsonPropertyName("nomination_type")] string NominationType,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("companion_time")] string? CompanionTime,
        [property: JsonPropertyName("back_unit_amount")] decimal BackUnitAmount,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_active")] bool IsActive);

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "指名バック設定を保存できませんでした。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を確認してください。";
        }

        if (rawError.Contains("invalid_nomination_back_master", StringComparison.OrdinalIgnoreCase))
        {
            return "指名バック設定の入力内容を確認してください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"指名バック設定を保存できませんでした。{rawError}";
    }
}
