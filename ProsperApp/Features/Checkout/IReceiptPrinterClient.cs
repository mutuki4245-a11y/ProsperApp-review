using ProsperApp.Models;

namespace ProsperApp.Services;

public interface IReceiptPrinterClient
{
    Task TryPrintCheckoutReceiptAsync(ReceiptPrintRequest request, CancellationToken cancellationToken);
}
