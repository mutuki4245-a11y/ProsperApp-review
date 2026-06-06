using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProsperApp.Models;
using ProsperApp.Options;

namespace ProsperApp.Services;

public class SupabaseBusinessDayRepository(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options,
    ILocalSettingsProvider localSettingsProvider) : IBusinessDayRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = options.Value;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;

    public async Task<StoreBusinessDay?> GetCurrentAsync(CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return null;
        }

        var rows = await PostRpcArrayAsync(
            "get_current_business_day",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        return rows.Count == 0 ? null : ParseBusinessDay(rows[0]);
    }

    public async Task<BusinessDayOperationResult> OpenAsync(DateOnly businessDate, string? memo, CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return BusinessDayOperationResult.Failed("Supabase設定が不足しています。");
        }

        var result = await PostRpcArrayWithErrorAsync(
            "open_business_day",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_date = businessDate,
                p_memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim()
            },
            ct);

        if (!result.Succeeded)
        {
            return BusinessDayOperationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return BusinessDayOperationResult.Failed("営業日を開始できませんでした。");
        }

        return BusinessDayOperationResult.Success(ParseBusinessDay(result.Rows[0]));
    }

    public async Task<BusinessDayOperationResult> CloseAsync(long businessDayId, string? memo, CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return BusinessDayOperationResult.Failed("Supabase設定が不足しています。");
        }

        var result = await PostRpcArrayWithErrorAsync(
            "close_business_day",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId,
                p_memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim()
            },
            ct);

        if (!result.Succeeded)
        {
            return BusinessDayOperationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return BusinessDayOperationResult.Failed("現在営業中の営業日が見つかりません。");
        }

        return BusinessDayOperationResult.Success(ParseBusinessDay(result.Rows[0]));
    }

    public async Task<int> GetOpenSlipCountAsync(long businessDayId, CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return 0;
        }

        var value = await PostRpcScalarAsync(
            "get_open_slip_count",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);

        return int.TryParse(value, out var count) ? count : 0;
    }

    private string BaseUrl => _options.Url.TrimEnd('/');

    private long CurrentStoreDepartmentId => _localSettingsProvider.GetCurrent().StoreDepartmentId;

    private string AccessKey => !string.IsNullOrWhiteSpace(_options.ServiceRoleKey)
        ? _options.ServiceRoleKey
        : _options.AnonKey;

    private bool HasRequiredSettings()
    {
        return !string.IsNullOrWhiteSpace(_options.Url) &&
               !string.IsNullOrWhiteSpace(AccessKey) &&
               CurrentStoreDepartmentId > 0;
    }

    private async Task<IReadOnlyList<JsonElement>> PostRpcArrayAsync<T>(string functionName, T payload, CancellationToken ct)
    {
        var result = await PostRpcArrayWithErrorAsync(functionName, payload, ct);
        return result.Succeeded ? result.Rows : [];
    }

    private async Task<(bool Succeeded, IReadOnlyList<JsonElement> Rows, string? ErrorMessage)> PostRpcArrayWithErrorAsync<T>(
        string functionName,
        T payload,
        CancellationToken ct)
    {
        using var request = BuildRpcRequest(functionName, payload);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return (false, [], body);
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (true, [], null);
            }

            return (true, doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList(), null);
        }
        catch (Exception ex)
        {
            return (false, [], ex.Message);
        }
    }

    private async Task<string?> PostRpcScalarAsync<T>(string functionName, T payload, CancellationToken ct)
    {
        using var request = BuildRpcRequest(functionName, payload);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return response.IsSuccessStatusCode ? body.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private HttpRequestMessage BuildRpcRequest<T>(string functionName, T payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/rest/v1/rpc/{functionName}")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("apikey", AccessKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessKey);
        return request;
    }

    private static StoreBusinessDay ParseBusinessDay(JsonElement row)
    {
        return new StoreBusinessDay
        {
            BusinessDayId = ReadLong(row, "business_day_id") ?? 0,
            CompanyId = ReadLong(row, "company_id") ?? 0,
            DepartmentId = ReadLong(row, "department_id") ?? 0,
            BusinessDate = ReadDate(row, "business_date") ?? DateOnly.MinValue,
            OpenedAt = ReadDateTimeOffset(row, "opened_at") ?? DateTimeOffset.MinValue,
            ClosedAt = ReadDateTimeOffset(row, "closed_at"),
            Status = ReadString(row, "status") ?? string.Empty,
            Memo = ReadString(row, "memo")
        };
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "DB更新に失敗しました。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を取得できません。設定画面で利用店舗を選択してください。";
        }

        if (rawError.Contains("business_day_already_open", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
        {
            return "既に営業中の営業日があります。";
        }

        if (rawError.Contains("open_slips_exist", StringComparison.OrdinalIgnoreCase))
        {
            return "未会計の伝票があります。すべて会計してから締めてください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return "DBへの実行権限がありません。RPCのgrant設定を確認してください。";
        }

        return $"DB更新に失敗しました。{rawError}";
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

    private static DateOnly? ReadDate(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String &&
               DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
