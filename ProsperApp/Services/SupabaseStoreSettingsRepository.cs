using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProsperApp.Models;
using ProsperApp.Options;

namespace ProsperApp.Services;

public class SupabaseStoreSettingsRepository(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options) : IStoreSettingsRepository
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = options.Value;

    public async Task<StoreSettingsLoadResult> GetDepartmentsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            return StoreSettingsLoadResult.Failed("Supabase URLが未設定です。Azure App Serviceの環境変数 Supabase__Url を設定してください。");
        }

        if (string.IsNullOrWhiteSpace(AccessKey))
        {
            return StoreSettingsLoadResult.Failed("Supabaseキーが未設定です。Azure App Serviceの環境変数 Supabase__AnonKey を設定してください。");
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
        var endpoint = $"{_options.Url.TrimEnd('/')}/rest/v1/rpc/get_store_departments";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        SetAuthHeaders(request);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return ([], $"RPC failed: HTTP {(int)response.StatusCode} {Shorten(body)}");
            }

            var departments = ParseDepartments(body);
            return departments.Count > 0
                ? (departments, $"RPC ok: {departments.Count}件")
                : ([], "RPC ok but 0件");
        }
        catch (Exception ex)
        {
            return ([], $"RPC exception: {ex.GetType().Name} {ex.Message}");
        }
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

        return "店舗一覧を取得できません。AzureのSupabase__Url / Supabase__AnonKey、最新デプロイ、Supabaseのget_store_departments RPCを確認してください。";
    }

    private static IReadOnlyList<DepartmentOption> ParseDepartments(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var departments = new List<DepartmentOption>();
        foreach (var item in doc.RootElement.EnumerateArray())
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

    private string AccessKey => !string.IsNullOrWhiteSpace(_options.ServiceRoleKey)
        ? _options.ServiceRoleKey
        : _options.AnonKey;

    private void SetAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("apikey", AccessKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessKey);
    }

    private static string Shorten(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var compact = value.ReplaceLineEndings(" ").Trim();
        return compact.Length <= 240 ? compact : compact[..240];
    }

    private static long? ReadLong(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadString(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;
    }
}
