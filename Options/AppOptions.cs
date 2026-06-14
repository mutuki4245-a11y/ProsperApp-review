namespace ProsperApp.Options;

public class AppOptions
{
    public string Mode { get; set; } = "Store";
    public string[] EnabledFeatures { get; set; } = [];
}
