using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProsperApp.Options;

namespace ProsperApp.Services;

public interface ISupabaseRpcClient
{
    bool HasReadAccess { get; }

    bool HasSecretAccess { get; }

    Task<SupabaseRpcResult> PostArrayAsync<TPayload>(
        string functionName,
        TPayload payload,
        bool requireSecretKey,
        CancellationToken ct);

    Task<SupabaseRpcResult> PostScalarAsync<TPayload>(
        string functionName,
        TPayload payload,
        bool requireSecretKey,
        CancellationToken ct);
}

public sealed class SupabaseRpcClient(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options) : ISupabaseRpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = options.Value;

    public bool HasReadAccess => SupabaseApiKeyHeaders.HasReadAccess(_options);

    public bool HasSecretAccess => SupabaseApiKeyHeaders.HasMutationAccess(_options);

    public async Task<SupabaseRpcResult> PostArrayAsync<TPayload>(
        string functionName,
        TPayload payload,
        bool requireSecretKey,
        CancellationToken ct)
    {
        var result = await SendAsync(functionName, payload, requireSecretKey, ct);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Body))
        {
            return result with { Rows = [] };
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result with { Rows = [] };
            }

            return result with { Rows = doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList() };
        }
        catch (JsonException ex)
        {
            return SupabaseRpcResult.Failed($"RPC response parse error: {ex.Message}");
        }
    }

    public Task<SupabaseRpcResult> PostScalarAsync<TPayload>(
        string functionName,
        TPayload payload,
        bool requireSecretKey,
        CancellationToken ct)
    {
        return SendAsync(functionName, payload, requireSecretKey, ct);
    }

    private async Task<SupabaseRpcResult> SendAsync<TPayload>(
        string functionName,
        TPayload payload,
        bool requireSecretKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            return SupabaseRpcResult.Failed("Supabase URLが未設定です。");
        }

        var accessKey = requireSecretKey
            ? SupabaseApiKeyHeaders.MutationAccessKey(_options)
            : SupabaseApiKeyHeaders.ReadAccessKey(_options);
        if (string.IsNullOrWhiteSpace(accessKey))
        {
            return SupabaseRpcResult.Failed(requireSecretKey
                ? "Supabase SecretKeyが未設定です。更新系RPCを実行できません。"
                : "Supabaseキーが未設定です。");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.Url.TrimEnd('/')}/rest/v1/rpc/{functionName}")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        SupabaseApiKeyHeaders.Apply(request, accessKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return SupabaseRpcResult.Failed(body, $"HTTP {(int)response.StatusCode} {Shorten(body)}");
            }

            return SupabaseRpcResult.Success(body);
        }
        catch (Exception ex)
        {
            return SupabaseRpcResult.Failed($"RPC exception: {ex.GetType().Name} {ex.Message}");
        }
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
