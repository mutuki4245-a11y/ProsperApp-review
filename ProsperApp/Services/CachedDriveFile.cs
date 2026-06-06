namespace ProsperApp.Services;

public sealed class CachedDriveFile
{
    public required byte[] Bytes { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
}
