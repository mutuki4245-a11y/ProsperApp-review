using ProsperApp.Models;

namespace ProsperApp.Tests;

public class ReceiptJournalEntryIdTests
{
    [Fact]
    public void Create_ReturnsStableUuidForSameDocument()
    {
        var first = ReceiptJournalEntryId.Create("document-123");
        var second = ReceiptJournalEntryId.Create(" document-123 ");

        Assert.True(Guid.TryParse(first, out _));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Create_ReturnsDifferentUuidForDifferentDocuments()
    {
        var first = ReceiptJournalEntryId.Create("document-123");
        var second = ReceiptJournalEntryId.Create("document-456");

        Assert.NotEqual(first, second);
    }
}
