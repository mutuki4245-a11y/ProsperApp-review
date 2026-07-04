namespace ProsperApp.Models;

public class LocalSettings
{
    public const string CastSalesAmountBasisSubtotal = "subtotal";
    public const string CastSalesAmountBasisTotal = "total";
    public const string CastSalesSplitModeSplit = "split";
    public const string CastSalesSplitModeFull = "full";

    public const string CookieName = "ProsperApp.LocalSettings";
    public const string LocalStorageKey = "ProsperApp.LocalSettings";

    public string StoreName { get; set; } = "店舗";

    public long StoreDepartmentId { get; set; }

    public string ScreenMode { get; set; } = "sales-management";

    public bool IsAdminMode { get; set; }
}
