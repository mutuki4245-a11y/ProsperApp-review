namespace ProsperApp.Options;

public class GoogleDriveOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = ["https://www.googleapis.com/auth/drive"];
}
