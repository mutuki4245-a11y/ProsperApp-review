using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class SettingsModel(
    IFeatureGate featureGate,
    ILocalSettingsProvider localSettingsProvider,
    IStoreSettingsRepository storeSettingsRepository,
    IAdminModeService adminModeService,
    ICurrentUserAccess currentUserAccess) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IStoreSettingsRepository _storeSettingsRepository = storeSettingsRepository;
    private readonly IAdminModeService _adminModeService = adminModeService;
    private readonly ICurrentUserAccess _currentUserAccess = currentUserAccess;

    [BindProperty]
    public SettingsInputModel Input { get; set; } = new();

    [BindProperty]
    public string? DeleteConfirmation { get; set; }

    public string? SuccessMessage { get; private set; }

    public IReadOnlyList<DebugDeletedTableCount> DebugDeletedTableCounts { get; private set; } = [];

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

        if (!_currentUserAccess.IsAdministrator)
        {
            return Forbid();
        }

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

        if (!_currentUserAccess.IsAdministrator)
        {
            return Forbid();
        }

        await LoadDepartmentsAsync(ct);
        var selectedDepartment = ValidateSettings();
        if (!ModelState.IsValid || selectedDepartment is null)
        {
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
        return RedirectToPage("/Management/Index");
    }

    public async Task<IActionResult> OnPostLockAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Settings))
        {
            return NotFound();
        }

        if (!_currentUserAccess.IsAdministrator)
        {
            return Forbid();
        }

        LoadCurrentSettings();
        _adminModeService.SetEnabled(false);

        await LoadDepartmentsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteNonMasterRecordsAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Settings))
        {
            return NotFound();
        }

        if (!_currentUserAccess.IsAdministrator)
        {
            return Forbid();
        }

        await LoadDepartmentsAsync(ct);
        var selectedDepartment = ValidateSettings();
        if (!ModelState.IsValid || selectedDepartment is null)
        {
            return Page();
        }

        var expectedConfirmation = BuildDeleteConfirmation(selectedDepartment.DisplayName);
        if (!string.Equals(DeleteConfirmation, expectedConfirmation, StringComparison.Ordinal))
        {
            ModelState.AddModelError(
                nameof(DeleteConfirmation),
                $"確認欄に「{expectedConfirmation}」と入力してください。");
            return Page();
        }

        var result = await _storeSettingsRepository.DeleteNonMasterRecordsAsync(selectedDepartment.DepartmentId, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "マスタ以外のレコード削除に失敗しました。");
            return Page();
        }

        DebugDeletedTableCounts = result.TableCounts;
        Input.StoreName = selectedDepartment.DisplayName;
        SuccessMessage = $"{selectedDepartment.DisplayName} のマスタ以外のレコードを {result.DeletedCount} 件削除しました。";
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
        if (Departments.Count == 0)
        {
            ModelState.AddModelError("Input.StoreDepartmentId", StoreSettingsDiagnosticMessage ?? "店舗マスタを取得できません。");
            return null;
        }

        var selectedDepartment = Departments.FirstOrDefault(x =>
            x.DepartmentId == Input.StoreDepartmentId &&
            _currentUserAccess.CanAccessDepartment(x.DepartmentId));
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
