namespace ProsperApp.Options;

public class ReceiptPrinterOptions
{
    public bool Enabled { get; set; }

    public string Provider { get; set; } = "epson-epos";

    public string PrinterAddress { get; set; } = string.Empty;

    public string DeviceId { get; set; } = "local_printer";

    public bool UseHttps { get; set; } = true;

    public string EndpointPath { get; set; } = "/cgi-bin/epos/service.cgi";

    public int TimeoutMilliseconds { get; set; } = 10000;

    public string TextLang { get; set; } = "mul";

    public int LineWidth { get; set; } = 48;
}
