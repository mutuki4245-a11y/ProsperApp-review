using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProsperApp.Options;

namespace ProsperApp.Services;

public interface ISupabaseRpcClient
{
    bool HasAccess { get; }

    Task<SupabaseRpcResult> PostArrayAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct);

    Task<SupabaseRpcResult> PostScalarAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct);
}

public sealed class SupabaseRpcClient(
    HttpClient httpClient,
    IConfiguration configuration,
    IOptions<SupabaseOptions> options) : ISupabaseRpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly IConfiguration _configuration = configuration;
    private readonly SupabaseOptions _options = options.Value;

    public bool HasAccess => !string.IsNullOrWhiteSpace(GetRpcEdgeFunctionUrl()) &&
                             !string.IsNullOrWhiteSpace(GetRpcEdgeFunctionKey());

    public async Task<SupabaseRpcResult> PostArrayAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct)
    {
        var result = await SendAsync(functionName, payload, ct);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Body))
        {
            return result with { Rows = [] };
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            if (!TryReadRows(doc.RootElement, out var rows))
            {
                return result with { Rows = [] };
            }

            return result with { Rows = rows };
        }
        catch (JsonException ex)
        {
            return SupabaseRpcResult.Failed($"RPC response parse error: {ex.Message}");
        }
    }

    public Task<SupabaseRpcResult> PostScalarAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct)
    {
        return SendAsync(functionName, payload, ct);
    }

    private async Task<SupabaseRpcResult> SendAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct)
    {
        var edgeFunctionUrl = GetRpcEdgeFunctionUrl();
        if (string.IsNullOrWhiteSpace(edgeFunctionUrl))
        {
            return SupabaseRpcResult.Failed("Supabase Edge Function URLが未設定です。");
        }

        var accessKey = GetRpcEdgeFunctionKey();
        if (string.IsNullOrWhiteSpace(accessKey))
        {
            return SupabaseRpcResult.Failed("Supabase Edge Functionキーが未設定です。RPCを実行できません。");
        }

        using var request = BuildEdgeFunctionRequest(edgeFunctionUrl, functionName, payload, accessKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return SupabaseRpcResult.Failed(body, $"HTTP {(int)response.StatusCode} {Shorten(body)}");
            }

            return SupabaseRpcResult.Success(NormalizeEdgeFunctionBody(body));
        }
        catch (Exception ex)
        {
            return SupabaseRpcResult.Failed($"RPC exception: {ex.GetType().Name} {ex.Message}");
        }
    }

    private static HttpRequestMessage BuildEdgeFunctionRequest<TPayload>(
        string edgeFunctionUrl,
        string functionName,
        TPayload payload,
        string accessKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, edgeFunctionUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new
                    {
                        function_name = functionName,
                        payload
                    },
                    JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("apikey", accessKey);
        request.Headers.Add("x-prosper-rpc-api-key", accessKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessKey);
        return request;
    }

    private string? GetRpcEdgeFunctionUrl()
    {
        var configuredUrl = FirstNonEmpty(
            _configuration["SUPABASE_RPC_EDGE_FUNCTION_URL"],
            _configuration["Supabase:RpcEdgeFunctionUrl"],
            _options.RpcEdgeFunctionUrl);
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            return configuredUrl.Trim();
        }

        var functionName = FirstNonEmpty(_options.RpcProxyFunctionName, "prosper-rpc");
        return string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(functionName)
            ? null
            : $"{_options.Url.TrimEnd('/')}/functions/v1/{functionName}";
    }

    private string? GetRpcEdgeFunctionKey()
    {
        return FirstNonEmpty(
            _configuration["Supabase_Edge_Key"],
            _configuration["SUPABASE_EDGE_KEY"],
            _configuration["SUPABASE_RPC_EDGE_FUNCTION_KEY"],
            _configuration["Supabase:RpcEdgeFunctionKey"],
            _options.RpcEdgeFunctionKey);
    }

    private static string NormalizeEdgeFunctionBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return body;
            }

            if (document.RootElement.TryGetProperty("data", out var data))
            {
                return data.GetRawText();
            }

            if (document.RootElement.TryGetProperty("result", out var result))
            {
                return result.GetRawText();
            }
        }
        catch (JsonException)
        {
            return body;
        }

        return body;
    }

    private static bool TryReadRows(JsonElement element, out IReadOnlyList<JsonElement> rows)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            rows = element.EnumerateArray().Select(x => x.Clone()).ToList();
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("data", out var data) && TryReadRows(data, out rows))
            {
                return true;
            }

            if (element.TryGetProperty("result", out var result) && TryReadRows(result, out rows))
            {
                return true;
            }

            rows = [element.Clone()];
            return true;
        }

        rows = [];
        return false;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
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
}

public sealed record SupabaseRpcResult(
    bool Succeeded,
    string? Body,
    string? ErrorMessage,
    string? Status,
    IReadOnlyList<JsonElement> Rows)
{
    public static SupabaseRpcResult Success(string? body) => new(true, body, null, "RPC ok", []);

    public static SupabaseRpcResult Failed(string? errorMessage, string? status = null) =>
        new(false, null, errorMessage, status ?? errorMessage, []);
}
