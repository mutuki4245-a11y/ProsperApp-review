namespace ProsperApp.Features.Drive;

public interface IDriveFileService
{
    Task<DriveFileResult> GetFileWithDiagnosticsAsync(string driveFileId, CancellationToken ct);
    void RemoveCachedFile(string driveFileId);
}
