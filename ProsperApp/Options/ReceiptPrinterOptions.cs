namespace ProsperApp.Options;

public class ReceiptPrinterOptions
{
    public bool Enabled { get; set; }

    public string BridgeUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutMilliseconds { get; set; } = 1500;
}
