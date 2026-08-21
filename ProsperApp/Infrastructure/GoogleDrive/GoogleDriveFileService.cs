using System.Net.Http.Headers;
using System.Text.Json;
using ProsperApp.Infrastructure.Caching;
using System.Diagnostics;

namespace ProsperApp.Infrastructure.GoogleDrive;

public class GoogleDriveFileService(
    HttpClient httpClient,
    IGoogleDriveAuthService googleDriveAuthService,
    IReceiptRepository receiptRepository,
    IApplicationCache cache,
    ILogger<GoogleDriveFileService> logger) : IDriveFileService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IGoogleDriveAuthService _googleDriveAuthService = googleDriveAuthService;
    private readonly IReceiptRepository _receiptRepository = receiptRepository;
    private readonly IApplicationCache _cache = cache;
    private readonly ILogger<GoogleDriveFileService> _logger = logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<DriveFileResult> GetFileWithDiagnosticsAsync(string driveFileId, CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var startedAt = Stopwatch.GetTimestamp();
        if (string.IsNullOrWhiteSpace(driveFileId))
        {
            return DriveFileResult.Failed(
                "not_allowed",
                "このファイルは表示できません。",
                requestId);
        }

        var pendingAccess = await _receiptRepository.IsPendingDriveFileAllowedAsync(driveFileId, ct);
        if (!pendingAccess.Succeeded)
        {
            return DriveFileResult.Failed(
                "pending_lookup_failed",
                "領収書の確認処理を利用できません。",
                requestId);
        }

        if (!pendingAccess.Value)
        {
            return DriveFileResult.Failed(
                "not_allowed",
                "このファイルは表示できません。",
                requestId);
        }

        if (_cache.TryGetValue(BuildCacheKey(driveFileId), out CachedDriveFile? cachedFile) &&
            cachedFile is not null)
        {
            return DriveFileResult.Success(ToDriveFileContent(cachedFile));
        }

        var accessToken = await _googleDriveAuthService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return DriveFileResult.Failed(
                "missing_access_token",
                "Googleへの再ログインが必要です。",
                requestId);
        }

        var metadataResult = await GetMetadataAsync(driveFileId, accessToken, ct);
        if (metadataResult.Metadata is null)
        {
            return DriveFileResult.Failed(
                metadataResult.ErrorCode ?? "drive_upstream_failed",
                "Google Driveからファイルを取得できません。",
                requestId);
        }

        var metadata = metadataResult.Metadata;

        using var mediaRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(driveFileId)}?alt=media");
        mediaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var mediaResponse = await _httpClient.SendAsync(mediaRequest, ct);
        if (!mediaResponse.IsSuccessStatusCode)
        {
            LogFailure(requestId, (int)mediaResponse.StatusCode, startedAt, "drive_media_failed");
            return DriveFileResult.Failed(
                mediaResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "drive_auth_failed"
                    : "drive_upstream_failed",
                "Google Driveからファイルを取得できません。",
                requestId);
        }

        var bytes = await mediaResponse.Content.ReadAsByteArrayAsync(ct);
        var cached = new CachedDriveFile
        {
            Bytes = bytes,
            ContentType = metadata.MimeType ?? "application/octet-stream",
            FileName = metadata.Name ?? $"{driveFileId}.bin"
        };
        _cache.Set(
            BuildCacheKey(driveFileId),
            cached,
            CacheDuration,
            "Drive",
            cached.FileName);

        _logger.LogInformation(
            "Drive fetch status={Status} duration_ms={DurationMs} request_id={RequestId}",
            200,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            requestId);
        return DriveFileResult.Success(ToDriveFileContent(cached));
    }

    public void RemoveCachedFile(string driveFileId)
    {
        if (!string.IsNullOrWhiteSpace(driveFileId))
        {
            _cache.Remove(BuildCacheKey(driveFileId));
        }
    }

    private static string BuildCacheKey(string driveFileId) => $"drive-preview:{driveFileId}";

    private static DriveFileContent ToDriveFileContent(CachedDriveFile cachedFile)
    {
        return new DriveFileContent(
            new MemoryStream(cachedFile.Bytes, writable: false),
            cachedFile.ContentType,
            cachedFile.FileName);
    }

    private async Task<DriveMetadataResult> GetMetadataAsync(string driveFileId, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(driveFileId)}?fields=id,name,mimeType");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return DriveMetadataResult.Failed(
                response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "drive_auth_failed",
                    System.Net.HttpStatusCode.NotFound => "drive_not_found",
                    _ => "drive_upstream_failed"
                },
                "Google Driveからファイルを取得できません。");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var metadata = await JsonSerializer.DeserializeAsync<DriveFileMetadata>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            ct);
        return DriveMetadataResult.Success(metadata);
    }

    private void LogFailure(string requestId, int status, long startedAt, string errorCode)
    {
        _logger.LogWarning(
            "Drive fetch status={Status} duration_ms={DurationMs} request_id={RequestId} error_code={ErrorCode}",
            status,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            requestId,
            errorCode);
    }

    private sealed class DriveMetadataResult
    {
        public DriveFileMetadata? Metadata { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }

        public static DriveMetadataResult Success(DriveFileMetadata? metadata) => new() { Metadata = metadata };

        public static DriveMetadataResult Failed(string errorCode, string errorMessage) =>
            new() { ErrorCode = errorCode, ErrorMessage = errorMessage };
    }

    private sealed class DriveFileMetadata
    {
        public string? Name { get; set; }
        public string? MimeType { get; set; }
    }
}
