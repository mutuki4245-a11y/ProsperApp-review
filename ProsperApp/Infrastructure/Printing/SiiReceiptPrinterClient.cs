using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProsperApp.Models;
using ProsperApp.Options;

namespace ProsperApp.Services;

public class SiiReceiptPrinterClient(
    HttpClient httpClient,
    IOptions<ReceiptPrinterOptions> options,
    ILogger<SiiReceiptPrinterClient> logger) : IReceiptPrinterClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ReceiptPrinterOptions _options = options.Value;

    public async Task TryPrintCheckoutReceiptAsync(ReceiptPrintRequest request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BridgeUrl))
        {
            return;
        }

        if (!Uri.TryCreate(_options.BridgeUrl, UriKind.Absolute, out var bridgeUri))
        {
            logger.LogWarning("Receipt printer bridge URL is invalid: {BridgeUrl}", _options.BridgeUrl);
            return;
        }

        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(_options.TimeoutMilliseconds, 500, 10000));
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, bridgeUri)
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                message.Headers.TryAddWithoutValidation("X-Receipt-Printer-Key", _options.ApiKey);
            }

            using var response = await httpClient.SendAsync(message, linkedSource.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Receipt printer bridge returned {StatusCode} for checkout {CheckoutId}.",
                    (int)response.StatusCode,
                    request.CheckoutId);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Receipt printer bridge timed out for checkout {CheckoutId}.", request.CheckoutId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Receipt printer bridge request failed for checkout {CheckoutId}.", request.CheckoutId);
        }
    }
}
