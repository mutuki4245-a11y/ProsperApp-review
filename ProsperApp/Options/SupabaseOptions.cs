namespace ProsperApp.Options;

public class SupabaseOptions
{
    public string Url { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string PendingListEndpointName { get; set; } = string.Empty;
    public string QuickEntryUpdateEndpointName { get; set; } = string.Empty;
    public string Mode { get; set; } = "rpc";
    public string PendingStatus { get; set; } = "unprocessed";
    public string CompletedStatus { get; set; } = "quick_entered";
    public string ScanMistakeStatus { get; set; } = "excluded";
    public string DocumentIdColumn { get; set; } = "id";
    public string DrivePreviewUrlTemplate { get; set; } = "https://drive.google.com/file/d/{id}/preview";
    public long StoreDepartmentId { get; set; }
}
