namespace ProsperApp.Services;

public interface IDriveFileService
{
    Task<DriveFileContent?> GetFileAsync(string driveFileId, CancellationToken ct);
    Task<DriveFileResult> GetFileWithDiagnosticsAsync(string driveFileId, CancellationToken ct);
    Task<DriveFileResult> PrefetchFileAsync(string driveFileId, CancellationToken ct);
    void RemoveCachedFile(string driveFileId);
    Task<DriveFileOperationResult> TrashFileAsync(string driveFileId, CancellationToken ct);
}
