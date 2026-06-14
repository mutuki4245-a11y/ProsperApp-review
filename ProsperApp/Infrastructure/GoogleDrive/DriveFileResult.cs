namespace ProsperApp.Services;

public sealed class DriveFileResult
{
    public DriveFileContent? Content { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static DriveFileResult Success(DriveFileContent content) => new() { Content = content };

    public static DriveFileResult Failed(string errorCode, string errorMessage) =>
        new() { ErrorCode = errorCode, ErrorMessage = errorMessage };
}
