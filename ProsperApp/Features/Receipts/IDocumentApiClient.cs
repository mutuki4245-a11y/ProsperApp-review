using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IDocumentApiClient
{
    Task<DocumentApiResult> SaveJournalPayloadAsync(DocumentJournalSavePayload payload, CancellationToken ct);
}

public sealed class DocumentApiResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }

    public static DocumentApiResult Success() => new() { Succeeded = true };

    public static DocumentApiResult Failed(string message) => new() { Succeeded = false, ErrorMessage = message };
}
