using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class SettingsModel(
    IFeatureGate featureGate,
    ILocalSettingsProvider localSettingsProvider,
    IStoreSettingsRepository storeSettingsRepository) : PageModel
{
    private const string SettingsPassword = "4245";
    private const string SaveTokenSessionKey = "SettingsSaveToken";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IStoreSettingsRepository _storeSettingsRepository = storeSettingsRepository;

    [BindProperty]
    [Display(Name = "パスワード")]
    public string? Password { get; set; }

    [BindProperty]
    public SettingsInputModel Input { get; set; } = new();

    [BindProperty]
    public string? SaveToken { get; set; }

    public bool IsUnlocked { get; private set; }

    public string? SuccessMessage { get; private set; }

    public string? LocalSettingsJsonForClient { get; private set; }

    public IReadOnlyList<DepartmentOption> Departments { get; private set; } = [];

    public string? StoreSettingsDiagnosticMessage { get; private set; }

    public string? StoreSettingsRpcStatus { get; private set; }

    public string? StoreSettingsTableStatus { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Settings))
        {
            return NotFound();
        }

        LockSettings();
        LoadCurrentSettings();
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

        IsUnlocked = true;
        SaveToken = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString(SaveTokenSessionKey, SaveToken);
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

        var settings = new LocalSettings
        {
            StoreName = selectedDepartment.DisplayName,
            StoreDepartmentId = selectedDepartment.DepartmentId,
            ScreenMode = Input.ScreenMode,
            IsAdminMode = Input.IsAdminMode,
            AttendanceMinuteStep = Input.AttendanceMinuteStep
        };

        WriteSettingsCookie(settings);
        LocalSettingsJsonForClient = JsonSerializer.Serialize(settings, JsonOptions);
        SuccessMessage = "設定をこの端末に保存しました。";
        LockSettings();
        Input = ToInput(settings);
        return Page();
    }

    public async Task<IActionResult> OnPostLockAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Settings))
        {
            return NotFound();
        }

        LockSettings();
        LoadCurrentSettings();
        await LoadDepartmentsAsync(ct);
        return Page();
    }

    private async Task LoadDepartmentsAsync(CancellationToken ct)
    {
        var result = await _storeSettingsRepository.GetDepartmentsAsync(ct);
        Departments = result.Departments;
        StoreSettingsDiagnosticMessage = result.DiagnosticMessage;
        StoreSettingsRpcStatus = result.RpcStatus;
        StoreSettingsTableStatus = result.TableStatus;
    }

    private void LoadCurrentSettings()
    {
        Input = ToInput(_localSettingsProvider.GetCurrent());
    }

    private static SettingsInputModel ToInput(LocalSettings settings)
    {
        return new SettingsInputModel
        {
            StoreName = settings.StoreName,
            StoreDepartmentId = settings.StoreDepartmentId,
            ScreenMode = settings.ScreenMode,
            IsAdminMode = settings.IsAdminMode,
            AttendanceMinuteStep = settings.AttendanceMinuteStep
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

    private DepartmentOption? ValidateSettings()
    {
        Input.ScreenMode = Input.ScreenMode?.Trim() ?? string.Empty;

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

        if (Input.ScreenMode is not "sales-management" and not "order-entry")
        {
            ModelState.AddModelError("Input.ScreenMode", "機能設定を選択してください。");
        }

        if (Input.AttendanceMinuteStep is not 15 and not 30)
        {
            ModelState.AddModelError("Input.AttendanceMinuteStep", "勤怠入力の時刻刻みは15分または30分を選択してください。");
        }

        return selectedDepartment;
    }
}

public class SettingsInputModel
{
    public string? StoreName { get; set; }

    [Display(Name = "利用店舗")]
    public long StoreDepartmentId { get; set; }

    [Display(Name = "機能設定")]
    public string ScreenMode { get; set; } = "sales-management";

    [Display(Name = "管理者モード")]
    public bool IsAdminMode { get; set; }

    [Display(Name = "勤怠入力の時刻刻み")]
    public int AttendanceMinuteStep { get; set; } = 15;
}
