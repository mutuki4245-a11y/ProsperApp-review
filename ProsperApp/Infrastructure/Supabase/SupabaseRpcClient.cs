using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProsperApp.Options;

namespace ProsperApp.Infrastructure.Supabase;

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

    Task<SupabaseRpcResult> BindUserAccessAsync(
        string email,
        string googleSubject,
        bool emailVerified,
        CancellationToken ct) => Task.FromResult(
            SupabaseRpcResult.Failed("access_denied", "access_denied", 403));
}

public sealed class SupabaseRpcClient(
    HttpClient httpClient,
    IConfiguration configuration,
    IOptions<SupabaseOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<SupabaseRpcClient> logger) : ISupabaseRpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> ReadOnlyFunctions = new HashSet<string>(StringComparer.Ordinal)
    {
        "store.get_departments",
        "store.get_business_home_bootstrap_v2",
        "store.get_management_master_snapshot",
        "store.get_current_business_home_snapshot",
        "store.get_current_order_entry_candidates",
        "store.get_business_day_daily_report",
        "store.get_sales_history_page",
        "store.get_sales_history_correction_editor",
        "store.get_current_receipt_work_queue",
        "store.is_pending_receipt_drive_file_allowed",
        "store.get_current_closing_dashboard",
        "store.get_current_drink_delivery_editor",
        "store.get_current_cast_sales_adjustment_overview",
        "store.get_current_drink_back_editor",
        "store.get_attendance_editor_bootstrap_v2",
        "store.get_current_attendance_editor_snapshot",
        "store.get_checkout_statement_print_data",
        "store.get_checkout_receipt_print_data"
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly IConfiguration _configuration = configuration;
    private readonly SupabaseOptions _options = options.Value;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ILogger<SupabaseRpcClient> _logger = logger;

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
        catch (JsonException)
        {
            return SupabaseRpcResult.Failed("invalid_response", "invalid_response", 502);
        }
    }

    public Task<SupabaseRpcResult> PostScalarAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct)
    {
        return SendAsync(functionName, payload, ct);
    }

    public async Task<SupabaseRpcResult> BindUserAccessAsync(
        string email,
        string googleSubject,
        bool emailVerified,
        CancellationToken ct)
    {
        var result = await SendEnvelopeAsync(
            "store.bind_app_user_access",
            new
            {
                operation = "bind_access",
                actor = new
                {
                    email,
                    google_subject = googleSubject,
                    email_verified = emailVerified
                }
            },
            isReadOnly: false,
            ct);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Body))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(result.Body);
            return TryReadRows(document.RootElement, out var rows)
                ? result with { Rows = rows }
                : result with { Rows = [] };
        }
        catch (JsonException)
        {
            return SupabaseRpcResult.Failed("invalid_response", "invalid_response", 502, result.RequestId);
        }
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

        var principal = _httpContextAccessor.HttpContext?.User;
        var email = principal?.FindFirst(ClaimTypes.Email)?.Value;
        var subject = principal?.FindFirst(ProsperAccessClaims.GoogleSubject)?.Value;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(subject))
        {
            return SupabaseRpcResult.Failed("access_denied", "access_denied", 403);
        }

        var envelope = new
        {
            operation = "rpc",
            actor = new { email, google_subject = subject },
            function_name = functionName,
            payload
        };
        return await SendEnvelopeAsync(functionName, envelope, ReadOnlyFunctions.Contains(functionName), ct);
    }

    private async Task<SupabaseRpcResult> SendEnvelopeAsync<TEnvelope>(
        string functionName,
        TEnvelope envelope,
        bool isReadOnly,
        CancellationToken ct)
    {
        var edgeFunctionUrl = GetRpcEdgeFunctionUrl();
        var accessKey = GetRpcEdgeFunctionKey();
        if (string.IsNullOrWhiteSpace(edgeFunctionUrl) || string.IsNullOrWhiteSpace(accessKey))
        {
            return SupabaseRpcResult.Failed("service_not_configured", "service_not_configured");
        }

        var operationId = TryGetOperationId(envelope);
        var startedAt = Stopwatch.GetTimestamp();
        var attempts = isReadOnly ? 3 : 1;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            using var request = BuildEdgeFunctionRequest(edgeFunctionUrl, envelope, accessKey);
            try
            {
                using var response = await _httpClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                var parsed = ParseResponseMetadata(body);
                LogRpc(functionName, operationId, (int)response.StatusCode, startedAt, parsed.RequestId);
                if (!response.IsSuccessStatusCode)
                {
                    var code = parsed.ErrorCode ?? "upstream_error";
                    return SupabaseRpcResult.Failed(code, code, (int)response.StatusCode, parsed.RequestId);
                }

                return SupabaseRpcResult.Success(NormalizeEdgeFunctionBody(body), (int)response.StatusCode, parsed.RequestId);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt + 1 < attempts)
            {
                await DelayBeforeRetryAsync(attempt, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return SupabaseRpcResult.Failed("cancelled", "cancelled", 499);
            }
            catch (HttpRequestException) when (attempt + 1 < attempts)
            {
                await DelayBeforeRetryAsync(attempt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RPC transport failure for {FunctionName}; operation_id={OperationId}", functionName, operationId);
                LogRpc(functionName, operationId, 503, startedAt, null);
                return SupabaseRpcResult.Failed("rpc_unavailable", "rpc_unavailable", 503);
            }
        }

        LogRpc(functionName, operationId, 504, startedAt, null);
        return SupabaseRpcResult.Failed("rpc_timeout", "rpc_timeout", 504);
    }

    private static HttpRequestMessage BuildEdgeFunctionRequest<TEnvelope>(
        string edgeFunctionUrl,
        TEnvelope envelope,
        string accessKey)
    {
        var body = JsonSerializer.Serialize(envelope, JsonOptions);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(accessKey),
                Encoding.UTF8.GetBytes($"{timestamp}\n{body}")));
        var request = new HttpRequestMessage(HttpMethod.Post, edgeFunctionUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-prosper-timestamp", timestamp);
        request.Headers.Add("x-prosper-signature", signature);
        return request;
    }

    private static async Task DelayBeforeRetryAsync(int attempt, CancellationToken ct)
    {
        var baseDelay = attempt == 0 ? 250 : 750;
        await Task.Delay(baseDelay + Random.Shared.Next(25, 126), ct);
    }

    private void LogRpc(string functionName, string? operationId, int status, long startedAt, string? requestId)
    {
        _logger.LogInformation(
            "RPC {FunctionName} operation_id={OperationId} status={Status} duration_ms={DurationMs} request_id={RequestId}",
            functionName,
            operationId,
            status,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            requestId);
    }

    private static string? TryGetOperationId<TEnvelope>(TEnvelope envelope)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(envelope, JsonOptions));
        return doc.RootElement.TryGetProperty("payload", out var payload) &&
               payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty("p_operation_id", out var operationId)
            ? operationId.GetString()
            : null;
    }

    private static (string? ErrorCode, string? RequestId) ParseResponseMetadata(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var errorCode = root.TryGetProperty("error_code", out var error) ? error.GetString() : null;
            var requestId = root.TryGetProperty("request_id", out var request) ? request.GetString() : null;
            return (errorCode, requestId);
        }
        catch (JsonException)
        {
            return (null, null);
        }
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
    IReadOnlyList<JsonElement> Rows,
    string? ErrorCode,
    int? HttpStatus,
    string? RequestId)
{
    public static SupabaseRpcResult Success(string? body) => Success(body, 200, null);

    public static SupabaseRpcResult Success(string? body, int? httpStatus, string? requestId) =>
        new(true, body, null, "RPC ok", [], null, httpStatus, requestId);

    public static SupabaseRpcResult Failed(string? errorCode) => Failed(errorCode, errorCode, null, null);

    public static SupabaseRpcResult Failed(string? errorCode, string? status) =>
        Failed(errorCode, status, null, null);

    public static SupabaseRpcResult Failed(string? errorCode, string? status, int? httpStatus) =>
        Failed(errorCode, status, httpStatus, null);

    public static SupabaseRpcResult Failed(
        string? errorCode,
        string? status,
        int? httpStatus,
        string? requestId) =>
        new(false, null, errorCode, status ?? errorCode, [], errorCode, httpStatus, requestId);
}
