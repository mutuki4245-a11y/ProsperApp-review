using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProsperApp.Models;
using ProsperApp.Options;

namespace ProsperApp.Services;

public class SupabaseStoreSlipRepository(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options,
    IBusinessDayRepository businessDayRepository,
    ILocalSettingsProvider localSettingsProvider) : IStoreSlipRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = options.Value;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;

    public async Task<StoreContext?> GetStoreContextAsync(CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_store_context",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        if (rows.Count == 0)
        {
            return null;
        }

        var row = rows[0];
        return new StoreContext
        {
            CompanyId = ReadLong(row, "company_id") ?? 0,
            DepartmentId = ReadLong(row, "department_id") ?? CurrentStoreDepartmentId,
            DepartmentName = ReadString(row, "department_name")
        };
    }

    public async Task<IReadOnlyList<StoreTableOption>> GetTablesAsync(CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_store_tables",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        return rows.Select(row => new StoreTableOption
            {
                TableId = ReadLong(row, "table_id") ?? 0,
                TableCode = ReadString(row, "table_code") ?? string.Empty,
                TableName = ReadString(row, "table_name")
            })
            .Where(x => x.TableId > 0 && !string.IsNullOrWhiteSpace(x.TableCode))
            .ToList();
    }

    public async Task<IReadOnlyList<CastOption>> GetCastsAsync(CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_store_casts",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        return rows.Select(row => new CastOption
            {
                CastId = ReadLong(row, "cast_id") ?? 0,
                CastCode = ReadString(row, "cast_code"),
                DisplayName = ReadString(row, "display_name") ?? string.Empty
            })
            .Where(x => x.CastId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
    }

    public async Task<IReadOnlyList<BusinessSlipListItem>> GetBusinessDaySlipsAsync(long businessDayId, CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_business_day_slips",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);

        return rows.Select(row => new BusinessSlipListItem
            {
                SlipId = ReadLong(row, "slip_id") ?? 0,
                SlipNo = ReadString(row, "slip_no"),
                TableId = ReadLong(row, "table_id"),
                TableCode = ReadString(row, "table_code"),
                TableName = ReadString(row, "table_name"),
                OpenedAt = ReadDateTimeOffset(row, "opened_at") ?? DateTimeOffset.MinValue,
                Status = ReadString(row, "status") ?? string.Empty,
                CustomerCount = (int)(ReadLong(row, "customer_count") ?? 0),
                Memo = ReadString(row, "memo")
            })
            .Where(x => x.SlipId > 0)
            .ToList();
    }

    public async Task<CreateSlipResult> CreateSlipAsync(CreateSlipInputModel input, CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return CreateSlipResult.Failed("Supabase設定が不足しています。");
        }

        if (input.OpenedAt is null || input.TableId is null)
        {
            return CreateSlipResult.Failed("伝票作成に必要な入力が不足しています。");
        }

        var businessDay = await _businessDayRepository.GetCurrentAsync(ct);
        if (businessDay is null)
        {
            return CreateSlipResult.Failed("営業日が開始されていません。開け作業を実行してください。");
        }

        var openedAtDateTime = businessDay.BusinessDate.ToDateTime(TimeOnly.FromDateTime(input.OpenedAt.Value));
        var openedAt = ToStoreDateTimeOffset(openedAtDateTime);
        var customerLabels = input.CustomerLabels
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .ToArray();

        var result = await PostRpcArrayWithErrorAsync(
            "create_store_slip",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_table_id = input.TableId.Value,
                p_opened_at = openedAt,
                p_customer_labels = customerLabels,
                p_cast_ids = input.CastIds.Distinct().ToArray(),
                p_memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim()
            },
            ct);

        if (!result.Succeeded)
        {
            return CreateSlipResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var slipId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "slip_id") : null;
        return slipId is null
            ? CreateSlipResult.Failed("作成した伝票IDを取得できません。")
            : CreateSlipResult.Success(slipId.Value);
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
        if (!HasRequiredSettings())
        {
            return [];
        }

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

    private static DateTimeOffset ToStoreDateTimeOffset(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        var timeZone = GetTokyoTimeZone();
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
    }

    private static TimeZoneInfo GetTokyoTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "伝票を作成できません。";
        }

        if (rawError.Contains("business_day_not_open", StringComparison.OrdinalIgnoreCase))
        {
            return "営業日が開始されていません。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を取得できません。設定画面で利用店舗を選択してください。";
        }

        if (rawError.Contains("store_table_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "選択した卓番を利用できません。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return "DBへの実行権限がありません。RPCのgrant設定を確認してください。";
        }

        return $"伝票を作成できません。{rawError}";
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

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
