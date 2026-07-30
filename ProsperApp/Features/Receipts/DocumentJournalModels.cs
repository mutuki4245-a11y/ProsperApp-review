using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ProsperApp.Models;

public sealed class DocumentJournalSavePayload
{
    [JsonPropertyName("journal_entries")]
    public List<DocumentJournalEntryRecord> JournalEntries { get; } = [];

    [JsonPropertyName("journal_entry_lines")]
    public List<DocumentJournalEntryLineRecord> JournalEntryLines { get; } = [];

    [JsonPropertyName("document_journal_links")]
    public List<DocumentJournalLinkRecord> DocumentJournalLinks { get; } = [];
}

public sealed class DocumentJournalEntryRecord
{
    [JsonPropertyName("journal_entry_id")]
    public string JournalEntryId { get; init; } = string.Empty;

    [JsonPropertyName("journal_date")]
    public DateOnly JournalDate { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "confirmed";
}

public sealed class DocumentJournalEntryLineRecord
{
    [JsonPropertyName("journal_entry_id")]
    public string JournalEntryId { get; init; } = string.Empty;

    [JsonPropertyName("line_no")]
    public int LineNo { get; init; }

    [JsonPropertyName("side")]
    public string Side { get; init; } = string.Empty;

    [JsonPropertyName("account_code")]
    public string AccountCode { get; init; } = string.Empty;

    [JsonPropertyName("company_id")]
    public long? CompanyId { get; init; }

    [JsonPropertyName("department_id")]
    public long? DepartmentId { get; init; }

    [JsonPropertyName("department_code")]
    public string? DepartmentCode { get; init; }

    [JsonPropertyName("is_reduced_tax_rate")]
    public bool IsReducedTaxRate { get; init; }

    [JsonPropertyName("invoice_category_code")]
    public string? InvoiceCategoryCode { get; init; }

    [JsonPropertyName("line_memo")]
    public string? LineMemo { get; init; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }
}

public sealed class DocumentJournalLinkRecord
{
    [JsonPropertyName("journal_entry_id")]
    public string JournalEntryId { get; init; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; init; } = string.Empty;
}

public static class ReceiptJournalEntryId
{
    public static string Create(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        var source = Encoding.UTF8.GetBytes($"prosper-receipt:{documentId.Trim()}");
        Span<byte> idBytes = stackalloc byte[16];
        SHA256.HashData(source).AsSpan(0, idBytes.Length).CopyTo(idBytes);
        return new Guid(idBytes).ToString();
    }
}
