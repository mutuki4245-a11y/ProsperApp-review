namespace ProsperApp.Models;

public class LocalSettings
{
    public const string CookieName = "ProsperApp.LocalSettings";
    public const string LocalStorageKey = "ProsperApp.LocalSettings";

    public string StoreName { get; set; } = "店舗";

    public long StoreDepartmentId { get; set; }

    public string ScreenMode { get; set; } = "sales-management";

    public bool IsAdminMode { get; set; }

    public int AttendanceMinuteStep { get; set; } = 15;
}
