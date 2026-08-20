namespace ProsperApp.Infrastructure.GoogleDrive;

public sealed class DriveFileResult
{
    public DriveFileContent? Content { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RequestId { get; init; }

    public static DriveFileResult Success(DriveFileContent content) => new() { Content = content };

    public static DriveFileResult Failed(string errorCode, string errorMessage, string? requestId = null) =>
        new() { ErrorCode = errorCode, ErrorMessage = errorMessage, RequestId = requestId };
}
