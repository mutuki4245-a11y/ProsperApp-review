namespace ProsperApp.Options;

public class ReceiptPrinterOptions
{
    public bool Enabled { get; set; }

    public string BrowserSdkScriptUrl { get; set; } = string.Empty;

    public string BrowserWebSocketHost { get; set; } = "localhost";

    public string BrowserCodePage { get; set; } = string.Empty;

    public string BrowserInternationalCharacter { get; set; } = string.Empty;
}
