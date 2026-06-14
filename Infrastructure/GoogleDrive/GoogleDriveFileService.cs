using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace ProsperApp.Services;

public class GoogleDriveFileService(
    HttpClient httpClient,
    IGoogleDriveAuthService googleDriveAuthService,
    IReceiptRepository receiptRepository,
    IMemoryCache memoryCache) : IDriveFileService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IGoogleDriveAuthService _googleDriveAuthService = googleDriveAuthService;
    private readonly IReceiptRepository _receiptRepository = receiptRepository;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<DriveFileContent?> GetFileAsync(string driveFileId, CancellationToken ct)
    {
        var result = await GetFileWithDiagnosticsAsync(driveFileId, ct);
        return result.Content;
    }

    public async Task<DriveFileResult> GetFileWithDiagnosticsAsync(string driveFileId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(driveFileId) ||
            !await _receiptRepository.IsPendingDriveFileAllowedAsync(driveFileId, ct))
        {
            return DriveFileResult.Failed(
                "not_allowed",
                "The drive_file_id is empty or is not included in the current store pending list.");
        }

        if (_memoryCache.TryGetValue(BuildCacheKey(driveFileId), out CachedDriveFile? cachedFile) &&
            cachedFile is not null)
        {
            return DriveFileResult.Success(ToDriveFileContent(cachedFile));
        }

        var accessToken = await _googleDriveAuthService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return DriveFileResult.Failed(
                "missing_access_token",
                "Google access token was not found. Sign in with Google again.");
        }

        var metadataResult = await GetMetadataAsync(driveFileId, accessToken, ct);
        if (metadataResult.Metadata is null)
        {
            return DriveFileResult.Failed(
                metadataResult.ErrorCode ?? "metadata_failed",
                metadataResult.ErrorMessage ?? "Drive metadata request failed. The Google account may not have access to this file.");
        }

        var metadata = metadataResult.Metadata;

        using var mediaRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(driveFileId)}?alt=media");
        mediaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var mediaResponse = await _httpClient.SendAsync(mediaRequest, ct);
        if (!mediaResponse.IsSuccessStatusCode)
        {
            var errorBody = await mediaResponse.Content.ReadAsStringAsync(ct);
            return DriveFileResult.Failed(
                $"media_failed_{(int)mediaResponse.StatusCode}",
                $"Drive media request failed: {errorBody}");
        }

        var bytes = await mediaResponse.Content.ReadAsByteArrayAsync(ct);
        var cached = new CachedDriveFile
        {
            Bytes = bytes,
            ContentType = metadata.MimeType ?? "application/octet-stream",
            FileName = metadata.Name ?? $"{driveFileId}.bin"
        };
        _memoryCache.Set(BuildCacheKey(driveFileId), cached, BuildCacheEntryOptions());

        return DriveFileResult.Success(ToDriveFileContent(cached));
    }

    public Task<DriveFileResult> PrefetchFileAsync(string driveFileId, CancellationToken ct)
    {
        return GetFileWithDiagnosticsAsync(driveFileId, ct);
    }

    public void RemoveCachedFile(string driveFileId)
    {
        if (!string.IsNullOrWhiteSpace(driveFileId))
        {
            _memoryCache.Remove(BuildCacheKey(driveFileId));
        }
    }

    public async Task<DriveFileOperationResult> TrashFileAsync(string driveFileId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(driveFileId) ||
            !await _receiptRepository.IsPendingDriveFileAllowedAsync(driveFileId, ct))
        {
            return DriveFileOperationResult.Failed(
                "not_allowed",
                "The drive_file_id is empty or is not included in the current store pending list.");
        }

        var accessToken = await _googleDriveAuthService.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return DriveFileOperationResult.Failed(
                "missing_access_token",
                "Google access token was not found. Sign in with Google again.");
        }

        var payload = JsonSerializer.Serialize(new { trashed = true });
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(driveFileId)}")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            return DriveFileOperationResult.Failed(
                $"trash_failed_{(int)response.StatusCode}",
                $"Drive trash request failed: {errorBody}");
        }

        return DriveFileOperationResult.Success();
    }

    private static string BuildCacheKey(string driveFileId) => $"drive-preview:{driveFileId}";

    private static MemoryCacheEntryOptions BuildCacheEntryOptions()
    {
        return new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            SlidingExpiration = CacheDuration
        };
    }

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
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            return DriveMetadataResult.Failed(
                $"metadata_failed_{(int)response.StatusCode}",
                $"Drive metadata request failed: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var metadata = await JsonSerializer.DeserializeAsync<DriveFileMetadata>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            ct);
        return DriveMetadataResult.Success(metadata);
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
