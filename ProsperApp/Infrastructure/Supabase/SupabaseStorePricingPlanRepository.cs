using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseStorePricingPlanRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache,
    IStoreMasterBootstrapper masterBootstrapper)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStorePricingPlanRepository
{
    private readonly IApplicationCache _cache = cache;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public async Task<Result<StorePricingPlanInputModel>> GetAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<StorePricingPlanInputModel>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.PricingPlan(departmentId);
        if (_cache.TryGetValue(cacheKey, out StorePricingPlanInputModel? cachedPlan) &&
            cachedPlan is not null)
        {
            return Result<StorePricingPlanInputModel>.Success(cachedPlan);
        }

        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded)
        {
            return Result<StorePricingPlanInputModel>.Failure(
                bootstrap.FailureKind ?? ResultFailureKind.Unavailable,
                bootstrap.ErrorMessage ?? "料金設定を取得できませんでした。");
        }

        if (!StoreBootstrapJson.TryReadObject(bootstrap.Value.Row, "pricing_plan", out var row))
        {
            var emptyPlan = new StorePricingPlanInputModel();
            StoreMasterCacheKeys.SetMaster(_cache, cacheKey, emptyPlan, "料金設定");
            return Result<StorePricingPlanInputModel>.Success(emptyPlan);
        }

        var plan = new StorePricingPlanInputModel
        {
            SetMinutes = (int)(ReadLong(row, "set_minutes") ?? 60),
            ExtensionMinutes = (int)(ReadLong(row, "extension_minutes") ?? 30),
            SetUnitPriceSingle = ReadDecimal(row, "set_unit_price_single") ?? 0,
            SetUnitPricePerCustomer = ReadDecimal(row, "set_unit_price_per_customer") ?? 0,
            ExtensionUnitPriceSingle = ReadDecimal(row, "extension_unit_price_single") ?? 0,
            ExtensionUnitPricePerCustomer = ReadDecimal(row, "extension_unit_price_per_customer") ?? 0,
            IsActive = ReadBool(row, "is_active") ?? false
        };
        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, plan, "料金設定");
        return Result<StorePricingPlanInputModel>.Success(plan);
    }

    public async Task<StorePricingPlanSaveResult> SaveAsync(StorePricingPlanInputModel plan, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return StorePricingPlanSaveResult.Failed("Supabase Edge Function設定が未設定です。料金設定を保存できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.save_pricing_plan_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_set_minutes = plan.SetMinutes,
                p_extension_minutes = plan.ExtensionMinutes,
                p_set_unit_price_single = plan.SetUnitPriceSingle,
                p_set_unit_price_per_customer = plan.SetUnitPricePerCustomer,
                p_extension_unit_price_single = plan.ExtensionUnitPriceSingle,
                p_extension_unit_price_per_customer = plan.ExtensionUnitPricePerCustomer,
                p_is_active = plan.IsActive
            },
            ct);

        if (!result.Succeeded)
        {
            return StorePricingPlanSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return StorePricingPlanSaveResult.Failed("料金設定を保存できませんでした。");
        }

        var version = (int)(ReadLong(result.Rows[0], "plan_version") ?? 0);
        if (version <= 0)
        {
            return StorePricingPlanSaveResult.Failed("料金設定を保存できませんでした。");
        }

        StoreMasterCacheKeys.ClearPricingPlan(_cache, CurrentStoreDepartmentId);
        return StorePricingPlanSaveResult.Success(version);
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError)) return "料金設定を保存できませんでした。";
        if (rawError.Contains("invalid_pricing_plan", StringComparison.OrdinalIgnoreCase)) return "セット時間と料金を確認してください。";
        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase)) return "店舗設定を確認してください。";
        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) || rawError.Contains("403", StringComparison.OrdinalIgnoreCase)) return PermissionErrorMessage();
        return $"料金設定を保存できませんでした。{rawError}";
    }
}
