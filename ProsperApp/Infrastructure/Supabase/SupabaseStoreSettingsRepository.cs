using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseStoreSettingsRepository(ISupabaseRpcClient rpcClient, IMemoryCache memoryCache)
    : SupabaseRepositoryBase(rpcClient), IStoreSettingsRepository
{
    private readonly IMemoryCache _memoryCache = memoryCache;

    public async Task<StoreSettingsLoadResult> GetDepartmentsAsync(CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return StoreSettingsLoadResult.Failed("Supabase Edge Function設定が未設定です。Azure App Serviceの環境変数 SUPABASE_RPC_EDGE_FUNCTION_URL と Supabase_Edge_Key を設定してください。");
        }

        if (_memoryCache.TryGetValue(StoreMasterCacheKeys.Departments, out IReadOnlyList<DepartmentOption>? cachedDepartments) &&
            cachedDepartments is { Count: > 0 })
        {
            return StoreSettingsLoadResult.Success(cachedDepartments, $"cache: {cachedDepartments.Count}件");
        }

        var rpcResult = await GetDepartmentsFromRpcAsync(ct);
        if (rpcResult.Departments.Count > 0)
        {
            _memoryCache.Set(StoreMasterCacheKeys.Departments, rpcResult.Departments, StoreMasterCacheKeys.CreateOptions());
            return StoreSettingsLoadResult.Success(rpcResult.Departments, rpcResult.Status);
        }

        var diagnostic = BuildDiagnosticMessage(rpcResult.Status);
        return StoreSettingsLoadResult.Failed(diagnostic, rpcResult.Status);
    }

    private async Task<(IReadOnlyList<DepartmentOption> Departments, string Status)> GetDepartmentsFromRpcAsync(CancellationToken ct)
    {
        var result = await RpcClient.PostArrayAsync(
            "get_store_departments",
            new { },
            ct);
        if (!result.Succeeded)
        {
            return ([], result.Status ?? result.ErrorMessage ?? "RPC failed");
        }

        var departments = ParseDepartments(result.Rows);
        return departments.Count > 0
            ? (departments, $"RPC ok: {departments.Count}件")
            : ([], "RPC ok but 0件");
    }

    private static string BuildDiagnosticMessage(string rpcStatus)
    {
        if (rpcStatus.Contains("404", StringComparison.OrdinalIgnoreCase) ||
            rpcStatus.Contains("PGRST202", StringComparison.OrdinalIgnoreCase) ||
            rpcStatus.Contains("Could not find", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗一覧取得RPCが見つかりません。Supabase SQL Editorで Sql/store_settings_functions.sql を実行してください。";
        }

        if (rpcStatus.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rpcStatus.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗一覧取得RPCの認証に失敗しました。AzureのSupabase_Edge_KeyとSupabaseのProsperApp_API_KEYが一致しているか確認してください。";
        }

        if (rpcStatus.Contains("0件", StringComparison.OrdinalIgnoreCase))
        {
            return "有効な店舗が0件です。department_masterのis_active=trueの店舗を確認してください。";
        }

        return "店舗一覧を取得できません。AzureのSUPABASE_RPC_EDGE_FUNCTION_URL / Supabase_Edge_Key、最新デプロイ、Supabaseのprosper-rpc Edge Functionを確認してください。";
    }

    private static IReadOnlyList<DepartmentOption> ParseDepartments(IReadOnlyList<JsonElement> rows)
    {
        var departments = new List<DepartmentOption>();
        foreach (var item in rows)
        {
            var departmentId = ReadLong(item, "department_id");
            var departmentName = ReadString(item, "department_name");
            if (departmentId is null || string.IsNullOrWhiteSpace(departmentName))
            {
                continue;
            }

            departments.Add(new DepartmentOption
            {
                DepartmentId = departmentId.Value,
                CompanyId = ReadLong(item, "company_id") ?? 0,
                DepartmentCode = ReadString(item, "department_code"),
                DepartmentName = departmentName
            });
        }

        return departments;
    }

}
