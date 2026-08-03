using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseStoreStaffAdminRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache,
    IStoreMasterBootstrapper masterBootstrapper) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreStaffAdminRepository
{
    private readonly IApplicationCache _cache = cache;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public async Task<Result<IReadOnlyList<StaffOption>>> GetStaffOptionsAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<IReadOnlyList<StaffOption>>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.StoreStaffs(departmentId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<StaffOption>? cachedStaffs))
        {
            return Result<IReadOnlyList<StaffOption>>.Success(cachedStaffs ?? []);
        }

        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded)
        {
            return Result<IReadOnlyList<StaffOption>>.Failure(
                bootstrap.FailureKind ?? ResultFailureKind.Unavailable,
                ToStaffLoadFriendlyError(bootstrap.ErrorMessage));
        }

        var staffs = StoreBootstrapJson.ReadArray(bootstrap.Value.Row, "staffs").Select(row => new StaffOption
            {
                StaffId = ReadLong(row, "staff_id") ?? 0,
                StaffCode = ReadString(row, "staff_code"),
                DepartmentName = ReadString(row, "department_name"),
                DisplayName = ReadString(row, "display_name") ?? string.Empty,
                EmploymentType = StoreStaffEmploymentTypes.Normalize(ReadString(row, "employment_type"))
            })
            .Where(x => x.StaffId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, staffs, "スタッフ候補");
        return Result<IReadOnlyList<StaffOption>>.Success(staffs);
    }

    public async Task<Result<IReadOnlyList<StoreStaffAdminItem>>> GetStaffsAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<IReadOnlyList<StoreStaffAdminItem>>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.StaffAdminList(departmentId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<StoreStaffAdminItem>? cachedStaffs))
        {
            return Result<IReadOnlyList<StoreStaffAdminItem>>.Success(cachedStaffs ?? []);
        }

        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded)
        {
            return Result<IReadOnlyList<StoreStaffAdminItem>>.Failure(
                bootstrap.FailureKind ?? ResultFailureKind.Unavailable,
                bootstrap.ErrorMessage ?? "スタッフ一覧を取得できませんでした。");
        }

        var staffs = StoreBootstrapJson.ReadArray(bootstrap.Value.Row, "staffs_admin").Select(row => new StoreStaffAdminItem
            {
                StaffId = ReadLong(row, "staff_id") ?? 0,
                DisplayName = ReadString(row, "display_name") ?? string.Empty,
                JoinedOn = ReadDateOnly(row, "joined_on") ?? DateOnly.MinValue,
                EmploymentType = StoreStaffEmploymentTypes.Normalize(ReadString(row, "employment_type"))
            })
            .Where(x => x.StaffId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, staffs, "スタッフ管理一覧");
        return Result<IReadOnlyList<StoreStaffAdminItem>>.Success(staffs);
    }

    public async Task<StoreStaffSaveResult> CreateStaffAsync(StoreStaffCreateInputModel input, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StoreStaffSaveResult.Failed("Supabase Edge Function設定が未設定です。スタッフを登録できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.create_staff",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_display_name = input.DisplayName.Trim(),
                p_employment_type = input.EmploymentType
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreStaffSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var staffId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "staff_id") ?? 0 : 0;
        if (staffId > 0)
        {
            StoreMasterCacheKeys.ClearStaffs(_cache, CurrentStoreDepartmentId);
        }

        return staffId > 0
            ? StoreStaffSaveResult.Success(staffId)
            : StoreStaffSaveResult.Failed("スタッフを登録できませんでした。");
    }

    public async Task<StoreStaffSaveResult> UpdateStaffEmploymentTypeAsync(
        long staffId,
        string employmentType,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StoreStaffSaveResult.Failed("Supabase Edge Function設定が未設定です。雇用区分を変更できません。");
        }

        if (staffId <= 0 || !StoreStaffEmploymentTypes.IsValid(employmentType))
        {
            return StoreStaffSaveResult.Failed("変更するスタッフと雇用区分を確認してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.update_staff_employment_type",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_staff_id = staffId,
                p_employment_type = employmentType
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreStaffSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var updatedStaffId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "staff_id") ?? 0 : 0;
        if (updatedStaffId > 0)
        {
            StoreMasterCacheKeys.ClearStaffs(_cache, CurrentStoreDepartmentId);
        }

        return updatedStaffId > 0
            ? StoreStaffSaveResult.Success(updatedStaffId)
            : StoreStaffSaveResult.Failed("雇用区分を変更できませんでした。");
    }

    public async Task<StoreStaffSaveResult> DeleteStaffAsync(long staffId, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StoreStaffSaveResult.Failed("Supabase Edge Function設定が未設定です。スタッフを削除できません。");
        }

        if (staffId <= 0)
        {
            return StoreStaffSaveResult.Failed("削除するスタッフを選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.delete_staff",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_staff_id = staffId
            },
            ct);

        if (!result.Succeeded)
        {
            return StoreStaffSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var deletedStaffId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "staff_id") ?? 0 : 0;
        if (deletedStaffId > 0)
        {
            StoreMasterCacheKeys.ClearStaffs(_cache, CurrentStoreDepartmentId);
        }

        return deletedStaffId > 0
            ? StoreStaffSaveResult.Success(deletedStaffId)
            : StoreStaffSaveResult.Failed("スタッフを削除できませんでした。");
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "スタッフ情報を保存できませんでした。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を確認してください。";
        }

        if (rawError.Contains("invalid_store_staff_employment_type", StringComparison.OrdinalIgnoreCase))
        {
            return "雇用区分を確認してください。";
        }

        if (rawError.Contains("invalid_store_staff", StringComparison.OrdinalIgnoreCase))
        {
            return "スタッフ名を確認してください。";
        }

        if (rawError.Contains("store_staff_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "対象のスタッフが見つかりません。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"スタッフ情報を保存できませんでした。{rawError}";
    }

    private static string ToStaffLoadFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "出勤候補のスタッフ情報を取得できません。Supabase RPC設定を確認してください。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を取得できません。管理者設定で利用店舗を選択してください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"出勤候補のスタッフ情報を取得できません。{rawError}";
    }
}
