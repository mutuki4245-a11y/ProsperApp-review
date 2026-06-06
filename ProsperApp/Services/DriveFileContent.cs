namespace ProsperApp.Services;

public sealed class DriveFileContent(Stream stream, string contentType, string fileName)
{
    public Stream Stream { get; } = stream;
    public string ContentType { get; } = contentType;
    public string FileName { get; } = fileName;
}
