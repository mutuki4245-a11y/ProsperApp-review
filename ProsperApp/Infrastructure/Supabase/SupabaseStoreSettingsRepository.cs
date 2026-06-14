using System.Text.Json;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseStoreSettingsRepository(ISupabaseRpcClient rpcClient)
    : SupabaseRepositoryBase(rpcClient), IStoreSettingsRepository
{
    public async Task<StoreSettingsLoadResult> GetDepartmentsAsync(CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return StoreSettingsLoadResult.Failed("Supabaseキーが未設定です。Azure App Serviceの環境変数 Supabase__PublishableKey または Supabase__SecretKey を設定してください。");
        }

        var rpcResult = await GetDepartmentsFromRpcAsync(ct);
        if (rpcResult.Departments.Count > 0)
        {
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
            requireSecretKey: false,
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
            return "店舗一覧取得RPCの実行権限がありません。grant execute設定を確認してください。";
        }

        if (rpcStatus.Contains("0件", StringComparison.OrdinalIgnoreCase))
        {
            return "有効な店舗が0件です。department_masterのis_active=trueの店舗を確認してください。";
        }

        return "店舗一覧を取得できません。AzureのSupabase__Url / Supabase__PublishableKey または Supabase__SecretKey、最新デプロイ、Supabaseのget_store_departments RPCを確認してください。";
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
