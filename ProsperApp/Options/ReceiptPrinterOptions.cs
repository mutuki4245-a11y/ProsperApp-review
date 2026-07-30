namespace ProsperApp.Options;

public class ReceiptPrinterOptions
{
    public bool Enabled { get; set; }

    public string BrowserSdkScriptUrl { get; set; } = "https://www.sii-ps.com/sample/websdk/siiWebSdk.js";

    public string BrowserWebSocketHost { get; set; } = "localhost";

    public string BrowserCodePage { get; set; } = string.Empty;

    public string BrowserInternationalCharacter { get; set; } = string.Empty;

    public int LineWidth { get; set; } = 48;
}
