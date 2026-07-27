using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseStorePricingPlanRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStorePricingPlanRepository
{
    public async Task<StorePricingPlanInputModel> GetAsync(CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return new StorePricingPlanInputModel();
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_pricing_plan",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        if (!result.Succeeded || result.Rows.Count == 0)
        {
            return new StorePricingPlanInputModel();
        }

        var row = result.Rows[0];
        return new StorePricingPlanInputModel
        {
            SetMinutes = (int)(ReadLong(row, "set_minutes") ?? 60),
            SetUnitPriceSingle = ReadDecimal(row, "set_unit_price_single") ?? 0,
            SetUnitPricePerCustomer = ReadDecimal(row, "set_unit_price_per_customer") ?? 0,
            ExtensionUnitPriceSingle = ReadDecimal(row, "extension_unit_price_single") ?? 0,
            ExtensionUnitPricePerCustomer = ReadDecimal(row, "extension_unit_price_per_customer") ?? 0,
            IsActive = ReadBool(row, "is_active") ?? false
        };
    }

    public async Task<StorePricingPlanSaveResult> SaveAsync(StorePricingPlanInputModel plan, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return StorePricingPlanSaveResult.Failed("Supabase Edge Function設定が未設定です。料金設定を保存できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.save_pricing_plan",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_set_minutes = plan.SetMinutes,
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
        return version > 0
            ? StorePricingPlanSaveResult.Success(version)
            : StorePricingPlanSaveResult.Failed("料金設定を保存できませんでした。");
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
