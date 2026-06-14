namespace ProsperApp.Services;

public sealed class DriveFileOperationResult
{
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static DriveFileOperationResult Success() => new() { Succeeded = true };

    public static DriveFileOperationResult Failed(string errorCode, string errorMessage) =>
        new() { Succeeded = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}
