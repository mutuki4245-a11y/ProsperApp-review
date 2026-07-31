using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Infrastructure.Caching;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class SettingsModel(
    IFeatureGate featureGate,
    ILocalSettingsProvider localSettingsProvider,
    IStoreSettingsRepository storeSettingsRepository,
    IApplicationCache applicationCache,
    IAdminModeService adminModeService) : PageModel
{
    private const string SettingsPassword = "4245";
    private const string SaveTokenSessionKey = "SettingsSaveToken";
    private const string LegacyAdminCookieName = "ProsperApp.AdminSession";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IStoreSettingsRepository _storeSettingsRepository = storeSettingsRepository;
    private readonly IApplicationCache _applicationCache = applicationCache;
    private readonly IAdminModeService _adminModeService = adminModeService;

    [BindProperty]
    [Display(Name = "パスワード")]
    public string? Password { get; set; }

    [BindProperty]
    public SettingsInputModel Input { get; set; } = new();

    [BindProperty]
    public string? SaveToken { get; set; }

    [BindProperty]
    public string? DeleteConfirmation { get; set; }

    public bool IsUnlocked { get; private set; }

    public string? SuccessMessage { get; private set; }

    public IReadOnlyList<DebugDeletedTableCount> DebugDeletedTableCounts { get; private set; } = [];

    public string? LocalSettingsJsonForClient { get; private set; }

    public IReadOnlyList<DepartmentOption> Departments { get; private set; } = [];

    public string? StoreSettingsDiagnosticMessage { get; private set; }

    public string? StoreSettingsRpcStatus { get; private set; }

    public string? StoreSettingsTableStatus { get; private set; }

    public IReadOnlyList<ApplicationCacheStatus> CacheStatuses { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Settings))
        {
            return NotFound();
        }

        DeleteLegacyAdminCookie();
        LoadCurrentSettings();
        LockSettings();
        await LoadDepartmentsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostUnlockAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Settings))
        {
            return NotFound();
        }

        if (Password != SettingsPassword)
        {
            IsUnlocked = false;
            LoadCurrentSettings();
            await LoadDepartmentsAsync(ct);
            ModelState.AddModelError(nameof(Password), "パスワードが違います。");
            return Page();
        }

        RefreshSaveToken();
        LoadCurrentSettings();
        await LoadDepartmentsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Settings))
        {
            return NotFound();
        }

        IsUnlocked = IsValidSaveToken();
        if (!IsUnlocked)
        {
            LockSettings();
            LoadCurrentSettings();
            await LoadDepartmentsAsync(ct);
            ModelState.AddModelError(string.Empty, "設定を変更するには、もう一度パスワードを入力してください。");
            return Page();
        }

        await LoadDepartmentsAsync(ct);
        var selectedDepartment = ValidateSettings();
        if (!ModelState.IsValid || selectedDepartment is null)
        {
            SaveToken = Guid.NewGuid().ToString("N");
            HttpContext.Session.SetString(SaveTokenSessionKey, SaveToken);
            return Page();
        }

        var currentSettings = _localSettingsProvider.GetCurrent();
        var settings = new LocalSettings
        {
            StoreName = selectedDepartment.DisplayName,
            StoreDepartmentId = selectedDepartment.DepartmentId,
            ScreenMode = currentSettings.ScreenMode,
            ThemeMode = currentSettings.ThemeMode
        };

        WriteSettingsCookie(settings);
        _adminModeService.SetEnabled(Input.AdminMode);
        TempData["SuccessMessage"] = Input.AdminMode
            ? "設定を保存し、管理者モードを有効にしました。"
            : "設定を保存し、管理者モードを無効にしました。";
        LockSettings();
        return RedirectToPage("/Management/Index");
    }

    public async Task<IActionResult> OnPostLockAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Settings))
        {
            return NotFound();
        }

        LoadCurrentSettings();
        _adminModeService.SetEnabled(false);
        LockSettings();

        await LoadDepartmentsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteNonMasterRecordsAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Settings))
        {
            return NotFound();
        }

        IsUnlocked = IsValidSaveToken();
        if (!IsUnlocked)
        {
            LockSettings();
            LoadCurrentSettings();
            await LoadDepartmentsAsync(ct);
            ModelState.AddModelError(string.Empty, "削除するには、もう一度設定ページを開いてください。");
            return Page();
        }

        await LoadDepartmentsAsync(ct);
        var selectedDepartment = ValidateSettings();
        if (!ModelState.IsValid || selectedDepartment is null)
        {
            RefreshSaveToken();
            return Page();
        }

        var expectedConfirmation = BuildDeleteConfirmation(selectedDepartment.DisplayName);
        if (!string.Equals(DeleteConfirmation, expectedConfirmation, StringComparison.Ordinal))
        {
            ModelState.AddModelError(
                nameof(DeleteConfirmation),
                $"確認欄に「{expectedConfirmation}」と入力してください。");
            RefreshSaveToken();
            return Page();
        }

        var result = await _storeSettingsRepository.DeleteNonMasterRecordsAsync(selectedDepartment.DepartmentId, ct);
        RefreshSaveToken();
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "マスタ以外のレコード削除に失敗しました。");
            return Page();
        }

        DebugDeletedTableCounts = result.TableCounts;
        Input.StoreName = selectedDepartment.DisplayName;
        SuccessMessage = $"{selectedDepartment.DisplayName} のマスタ以外のレコードを {result.DeletedCount} 件削除しました。";
        LoadCacheStatuses();
        return Page();
    }

    public async Task<IActionResult> OnPostClearCacheAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Settings))
        {
            return NotFound();
        }

        IsUnlocked = IsValidSaveToken();
        if (!IsUnlocked)
        {
            LockSettings();
            LoadCurrentSettings();
            await LoadDepartmentsAsync(ct);
            ModelState.AddModelError(string.Empty, "キャッシュを削除するには、もう一度設定ページを開いてください。");
            return Page();
        }

        await LoadDepartmentsAsync(ct);
        var clearedCount = _applicationCache.ClearAll();
        RefreshSaveToken();
        LoadCacheStatuses();
        SuccessMessage = $"アプリ内キャッシュを {clearedCount} 件削除しました。";
        return Page();
    }

    private async Task LoadDepartmentsAsync(CancellationToken ct)
    {
        var result = await _storeSettingsRepository.GetDepartmentsAsync(ct);
        Departments = result.Departments;
        StoreSettingsDiagnosticMessage = result.DiagnosticMessage;
        StoreSettingsRpcStatus = result.RpcStatus;
        StoreSettingsTableStatus = result.TableStatus;
        LoadCacheStatuses();
    }

    private void LoadCacheStatuses()
    {
        CacheStatuses = _applicationCache.GetStatuses();
    }

    private void LoadCurrentSettings()
    {
        Input = ToInput(_localSettingsProvider.GetCurrent());
        Input.AdminMode = _adminModeService.IsEnabled;
    }

    private static SettingsInputModel ToInput(LocalSettings settings)
    {
        return new SettingsInputModel
        {
            StoreName = settings.StoreName,
            StoreDepartmentId = settings.StoreDepartmentId
        };
    }

    private bool IsValidSaveToken()
    {
        var sessionToken = HttpContext.Session.GetString(SaveTokenSessionKey);
        return !string.IsNullOrWhiteSpace(SaveToken) &&
               !string.IsNullOrWhiteSpace(sessionToken) &&
               string.Equals(SaveToken, sessionToken, StringComparison.Ordinal);
    }

    private void LockSettings()
    {
        IsUnlocked = false;
        SaveToken = null;
        HttpContext.Session.Remove(SaveTokenSessionKey);
    }

    private void RefreshSaveToken()
    {
        IsUnlocked = true;
        SaveToken = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString(SaveTokenSessionKey, SaveToken);
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

    private void DeleteLegacyAdminCookie()
    {
        if (!Request.Cookies.ContainsKey(LegacyAdminCookieName))
        {
            return;
        }

        Response.Cookies.Delete(
            LegacyAdminCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Path = "/"
            });
    }

    private DepartmentOption? ValidateSettings()
    {
        if (Departments.Count == 0)
        {
            ModelState.AddModelError("Input.StoreDepartmentId", StoreSettingsDiagnosticMessage ?? "店舗マスタを取得できません。");
            return null;
        }

        var selectedDepartment = Departments.FirstOrDefault(x => x.DepartmentId == Input.StoreDepartmentId);
        if (selectedDepartment is null)
        {
            ModelState.AddModelError("Input.StoreDepartmentId", "店舗マスタから店舗を選択してください。");
        }

        return selectedDepartment;
    }

    public static string BuildDeleteConfirmation(string storeName)
    {
        return $"削除 {storeName}";
    }
}

public class SettingsInputModel
{
    public string? StoreName { get; set; }

    [Display(Name = "利用店舗")]
    public long StoreDepartmentId { get; set; }

    [Display(Name = "管理者モード")]
    public bool AdminMode { get; set; }
}
