using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProsperApp.Models;
using ProsperApp.Options;

namespace ProsperApp.Services;

public class SupabaseReceiptRepository(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options,
    ILocalSettingsProvider localSettingsProvider) : IReceiptRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = options.Value;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;

    public async Task<IReadOnlyList<PendingReceiptItem>> GetPendingAsync(CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return [];
        }

        var result = await PostRpcArrayWithErrorAsync(
            "get_pending_receipts",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_status = _options.PendingStatus
            },
            ct);

        return result.Succeeded ? ParsePendingItems(result.Rows) : [];
    }

    public async Task<bool> IsPendingDriveFileAllowedAsync(string driveFileId, CancellationToken ct)
    {
        if (!HasRequiredSettings() || string.IsNullOrWhiteSpace(driveFileId))
        {
            return false;
        }

        var pending = await GetPendingAsync(ct);
        return pending.Any(x => x.DriveFileId == driveFileId);
    }

    public async Task<SaveReceiptResult> SaveQuickEntryAsync(QuickEntryInputModel input, CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return SaveReceiptResult.Failed("Supabase設定が不足しています。");
        }

        var result = await PostRpcArrayWithErrorAsync(
            "quick_enter_receipt",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_document_id = input.DocumentId,
                p_payment_date = input.PaymentDate,
                p_amount = input.Amount,
                p_account_subject = input.AccountSubject.Trim(),
                p_description = input.Description.Trim(),
                p_group_code = input.GroupCode,
                p_status = _options.CompletedStatus
            },
            ct);

        if (!result.Succeeded)
        {
            return SaveReceiptResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        return result.Rows.Count == 0
            ? SaveReceiptResult.Failed("対象の領収書を更新できません。店舗設定またはステータスを確認してください。")
            : SaveReceiptResult.Success(input.DocumentId);
    }

    public async Task<SaveReceiptResult> MarkScanMistakeAsync(string documentId, CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return SaveReceiptResult.Failed("Supabase設定が不足しています。");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return SaveReceiptResult.Failed("DocumentId is required.");
        }

        var result = await PostRpcArrayWithErrorAsync(
            "mark_receipt_scan_mistake",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_document_id = documentId,
                p_status = _options.ScanMistakeStatus
            },
            ct);

        if (!result.Succeeded)
        {
            return SaveReceiptResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        return result.Rows.Count == 0
            ? SaveReceiptResult.Failed("対象の領収書を更新できません。店舗設定またはステータスを確認してください。")
            : SaveReceiptResult.Success(documentId);
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

    private async Task<(bool Succeeded, IReadOnlyList<JsonElement> Rows, string? ErrorMessage)> PostRpcArrayWithErrorAsync<T>(
        string functionName,
        T payload,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/rest/v1/rpc/{functionName}")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("apikey", AccessKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessKey);

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

    private static IReadOnlyList<PendingReceiptItem> ParsePendingItems(IReadOnlyList<JsonElement> rows)
    {
        return rows.Select(item => new PendingReceiptItem
            {
                Id = ReadAsString(item, "document_id") ?? string.Empty,
                DocumentNo = ReadAsString(item, "document_id"),
                FileName = ReadAsString(item, "file_name"),
                FilePath = ReadAsString(item, "drive_url") ?? ReadAsString(item, "storage_path"),
                DriveFileId = ReadAsString(item, "drive_file_id"),
                PreviewUrl = BuildPreviewUrl(ReadAsString(item, "drive_file_id")),
                PaymentDate = ReadAsDateOnly(item, "document_date"),
                Amount = ReadAsDecimal(item, "amount")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToList();
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "領収書の更新に失敗しました。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return "DBへの実行権限がありません。RPCのgrant設定を確認してください。";
        }

        return $"領収書の更新に失敗しました。{rawError}";
    }

    private static string? ReadAsString(JsonElement obj, string name)
    {
        return obj.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;
    }

    private static decimal? ReadAsDecimal(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateOnly? ReadAsDateOnly(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String &&
               DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? BuildPreviewUrl(string? driveFileId)
    {
        if (string.IsNullOrWhiteSpace(driveFileId))
        {
            return null;
        }

        return $"/DrivePreview/{Uri.EscapeDataString(driveFileId)}";
    }
}
