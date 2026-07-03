using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Models;

namespace ProsperApp.Services;

public sealed class DocumentApiClient(
    HttpClient httpClient,
    IConfiguration configuration) : IDocumentApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly IConfiguration _configuration = configuration;

    public async Task<DocumentApiResult> SaveJournalPayloadAsync(DocumentJournalSavePayload payload, CancellationToken ct)
    {
        var endpointUrl = GetEndpointUrl();
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return DocumentApiResult.Failed("DocManagement連携URLが未設定です。SUPABASE_EDGE_FUNCTION_URL または Supabase:Url を設定してください。");
        }

        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return DocumentApiResult.Failed("DocManagement連携キーが未設定です。DOCUMENT_API_KEY を設定してください。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
        request.Headers.Add("x-document-api-key", apiKey);
        request.Headers.Add("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.UserAgent.ParseAdd("ProsperApp/1.0");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { action = "save_journal_payload", payload }, JsonOptions),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return DocumentApiResult.Success();
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return DocumentApiResult.Failed($"DocManagement連携保存に失敗しました。HTTP {(int)response.StatusCode} {Shorten(body)}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return DocumentApiResult.Failed($"DocManagement連携保存に失敗しました。{ex.GetType().Name}: {ex.Message}");
        }
    }

    private string? GetEndpointUrl()
    {
        var configuredUrl =
            _configuration["SUPABASE_EDGE_FUNCTION_URL"] ??
            _configuration["DocManagement:EdgeFunctionUrl"];
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            return configuredUrl.Trim();
        }

        var supabaseUrl = _configuration["Supabase:Url"];
        return string.IsNullOrWhiteSpace(supabaseUrl)
            ? null
            : $"{supabaseUrl.TrimEnd('/')}/functions/v1/document-api";
    }

    private string? GetApiKey()
    {
        return FirstNonEmpty(
            _configuration["DOCUMENT_API_KEY"],
            _configuration["DocManagement:DocumentApiKey"],
            ReadFirstDocumentApiKey(_configuration["DOCUMENT_API_KEYS"]));
    }

    private static string? ReadFirstDocumentApiKey(string? rawKeys)
    {
        if (string.IsNullOrWhiteSpace(rawKeys))
        {
            return null;
        }

        var trimmed = rawKeys.Trim();
        if (!trimmed.StartsWith("[", StringComparison.Ordinal) &&
            !trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement
                    .EnumerateArray()
                    .Select(element => element.GetString())
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                JsonValueKind.Object => document.RootElement
                    .EnumerateObject()
                    .Select(property => property.Value.GetString())
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string Shorten(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 240 ? normalized : normalized[..240] + "...";
    }
}
