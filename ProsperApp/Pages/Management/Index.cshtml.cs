using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Synchronization;
using ProsperApp.Infrastructure.Caching;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ManagementIndexModel(
    IFeatureGate featureGate,
    ILocalSettingsProvider localSettingsProvider,
    IAdminModeService adminModeService,
    IApplicationCache applicationCache,
    IManagementMasterSynchronization managementMasterSynchronization) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IAdminModeService _adminModeService = adminModeService;
    private readonly IApplicationCache _applicationCache = applicationCache;
    private readonly IManagementMasterSynchronization _managementMasterSynchronization = managementMasterSynchronization;

    [BindProperty]
    [Display(Name = "使用する画面")]
    public string ScreenMode { get; set; } = "sales-management";

    [BindProperty]
    [Display(Name = "配色")]
    public string ThemeMode { get; set; } = LocalSettings.ThemeModeQuietNavy;

    public bool IsAdminMode => _adminModeService.IsEnabled;

    public IReadOnlyList<ApplicationCacheStatus> CacheStatuses { get; private set; } = [];

    public IActionResult OnGet()
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        LoadCurrentSettings();
        return Page();
    }

    public IActionResult OnPostSaveDisplaySettings()
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        ScreenMode = ScreenMode?.Trim() ?? string.Empty;
        ThemeMode = ThemeMode?.Trim() ?? string.Empty;
        var currentSettings = _localSettingsProvider.GetCurrent();
        if (ScreenMode is not "sales-management" and not "order-entry")
        {
            ScreenMode = currentSettings.ScreenMode;
            ModelState.AddModelError(nameof(ScreenMode), "使用する画面を選択してください。");
        }

        if (ThemeMode is not LocalSettings.ThemeModeQuietNavy and not LocalSettings.ThemeModeWhite)
        {
            ThemeMode = currentSettings.ThemeMode;
            ModelState.AddModelError(nameof(ThemeMode), "配色を選択してください。");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        WriteSettingsCookie(new LocalSettings
        {
            StoreName = currentSettings.StoreName,
            StoreDepartmentId = currentSettings.StoreDepartmentId,
            ScreenMode = ScreenMode,
            ThemeMode = ThemeMode
        });

        TempData["SuccessMessage"] = "画面設定をこの端末に保存しました。";
        return ScreenMode == "order-entry"
            ? RedirectToPage("/Orders/Index")
            : RedirectToPage("/Index");
    }

    public Task<IActionResult> OnPostClearCacheAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return Task.FromResult<IActionResult>(NotFound());
        }

        ct.ThrowIfCancellationRequested();
        var clearedCount = _applicationCache.ClearAll();
        TempData["SuccessMessage"] = $"アプリ内キャッシュを {clearedCount} 件削除しました。";
        return Task.FromResult<IActionResult>(RedirectToPage());
    }

    public async Task<IActionResult> OnGetMasterSnapshotAsync(
        string? knownRevision,
        CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        var result = await _managementMasterSynchronization.GetSnapshotAsync(knownRevision, cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                status = "unavailable",
                message = result.ErrorMessage ?? "管理マスタを取得できませんでした。"
            });
        }

        var snapshot = result.Value;
        return new JsonResult(new
        {
            succeeded = true,
            departmentId = snapshot.DepartmentId,
            revision = snapshot.Revision,
            unchanged = snapshot.Unchanged,
            payload = snapshot.Payload
        });
    }

    private void LoadCurrentSettings()
    {
        var currentSettings = _localSettingsProvider.GetCurrent();
        ScreenMode = currentSettings.ScreenMode;
        ThemeMode = currentSettings.ThemeMode;
        CacheStatuses = _applicationCache.GetStatuses();
    }

    private void WriteSettingsCookie(LocalSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Response.Cookies.Append(
            LocalSettings.CookieName,
            Uri.EscapeDataString(json),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = false,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Path = "/"
            });
    }
}
