using System.Text.Json;
using Microsoft.Extensions.Options;
using ProsperApp.Options;

namespace ProsperApp.Services;

public class LocalSettingsProvider(
    IHttpContextAccessor httpContextAccessor,
    IOptions<SupabaseOptions> supabaseOptions) : ILocalSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly SupabaseOptions _supabaseOptions = supabaseOptions.Value;

    public LocalSettings GetCurrent()
    {
        var settings = ReadCookieSettings() ?? new LocalSettings();
        return Normalize(settings);
    }

    private LocalSettings? ReadCookieSettings()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null ||
            !request.Cookies.TryGetValue(LocalSettings.CookieName, out var cookieValue) ||
            string.IsNullOrWhiteSpace(cookieValue))
        {
            return null;
        }

        try
        {
            var json = Uri.UnescapeDataString(cookieValue);
            return JsonSerializer.Deserialize<LocalSettings>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private LocalSettings Normalize(LocalSettings settings)
    {
        var storeDepartmentId = settings.StoreDepartmentId > 0
            ? settings.StoreDepartmentId
            : _supabaseOptions.StoreDepartmentId;

        var screenMode = settings.ScreenMode is "sales-management" or "order-entry"
            ? settings.ScreenMode
            : "sales-management";

        var themeMode = settings.ThemeMode is LocalSettings.ThemeModeQuietNavy or LocalSettings.ThemeModeWhite
            ? settings.ThemeMode
            : LocalSettings.ThemeModeQuietNavy;

        return new LocalSettings
        {
            StoreName = string.IsNullOrWhiteSpace(settings.StoreName) ? "店舗" : settings.StoreName.Trim(),
            StoreDepartmentId = storeDepartmentId,
            ScreenMode = screenMode,
            ThemeMode = themeMode
        };
    }
}
