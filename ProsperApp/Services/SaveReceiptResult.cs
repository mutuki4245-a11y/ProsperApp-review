namespace ProsperApp.Services;

public class SaveReceiptResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ExternalId { get; init; }

    public static SaveReceiptResult Success(string? externalId = null) =>
        new() { Succeeded = true, ExternalId = externalId };

    public static SaveReceiptResult Failed(string errorMessage) =>
        new() { Succeeded = false, ErrorMessage = errorMessage };
}
