using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Admin;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ManagementNominationBacksModel(
    IFeatureGate featureGate,
    INominationBackAdminRepository nominationBackAdminRepository,
    IAdminAuthorizationService adminAuthorizationService) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly INominationBackAdminRepository _nominationBackAdminRepository = nominationBackAdminRepository;
    private readonly IAdminAuthorizationService _adminAuthorizationService = adminAuthorizationService;

    [BindProperty]
    public List<NominationBackMasterInputModel> Settings { get; set; } = [];

    public string? SuccessMessage { get; set; }

    public bool IsAdminMode => _adminAuthorizationService.IsAdminMode;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        await LoadSettingsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        if (!IsAdminMode)
        {
            return NotFound();
        }

        if (Settings.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "保存する指名バック設定がありません。");
            await LoadSettingsAsync(cancellationToken);
            return Page();
        }

        NormalizePostedSettings();
        if (!ValidateSettings())
        {
            return Page();
        }

        var result = await _nominationBackAdminRepository.SaveSettingsAsync(Settings, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "指名バック設定を保存できませんでした。");
            return Page();
        }

        SuccessMessage = "指名バック設定を保存しました。";
        await LoadSettingsAsync(cancellationToken);
        return Page();
    }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _nominationBackAdminRepository.GetSettingsAsync(cancellationToken);
        Settings = settings
            .Select(setting => new NominationBackMasterInputModel
            {
                NominationKind = setting.NominationKind,
                NominationType = setting.NominationType,
                DisplayName = setting.DisplayName,
                CompanionTime = setting.CompanionTime,
                BackUnitAmount = setting.BackUnitAmount,
                SortOrder = setting.SortOrder,
                IsActive = setting.IsActive
            })
            .ToList();
    }

    private void NormalizePostedSettings()
    {
        Settings = Settings
            .Select(setting => new NominationBackMasterInputModel
            {
                NominationKind = string.IsNullOrWhiteSpace(setting.NominationKind) ? string.Empty : setting.NominationKind.Trim(),
                NominationType = string.IsNullOrWhiteSpace(setting.NominationType) ? string.Empty : setting.NominationType.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(setting.DisplayName) ? string.Empty : setting.DisplayName.Trim(),
                CompanionTime = string.IsNullOrWhiteSpace(setting.CompanionTime) ? null : setting.CompanionTime.Trim(),
                BackUnitAmount = setting.BackUnitAmount,
                SortOrder = setting.SortOrder,
                IsActive = setting.IsActive
            })
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToList();
    }

    private bool ValidateSettings()
    {
        if (Settings.Select(x => x.NominationKind).Distinct(StringComparer.Ordinal).Count() != Settings.Count)
        {
            ModelState.AddModelError(string.Empty, "指名種別が重複しています。");
        }

        for (var index = 0; index < Settings.Count; index++)
        {
            var setting = Settings[index];
            if (string.IsNullOrWhiteSpace(setting.NominationKind))
            {
                ModelState.AddModelError($"Settings[{index}].NominationType", "指名種別を確認してください。");
            }

            if (string.IsNullOrWhiteSpace(setting.DisplayName))
            {
                ModelState.AddModelError($"Settings[{index}].DisplayName", "指名種別名を確認してください。");
            }

            if (setting.NominationType is not ("nomination" or "in_store" or "companion"))
            {
                ModelState.AddModelError($"Settings[{index}].NominationType", "指名種別を確認してください。");
            }

            if (setting.NominationType == "companion" &&
                (string.IsNullOrWhiteSpace(setting.CompanionTime) || !TimeOnly.TryParse(setting.CompanionTime, out _)))
            {
                ModelState.AddModelError($"Settings[{index}].CompanionTime", "同伴時刻を確認してください。");
            }

            if (setting.BackUnitAmount < 0)
            {
                ModelState.AddModelError($"Settings[{index}].BackUnitAmount", "バック単価は0以上で入力してください。");
            }

            if (setting.SortOrder < 0)
            {
                ModelState.AddModelError($"Settings[{index}].SortOrder", "表示順は0以上で入力してください。");
            }
        }

        return ModelState.IsValid;
    }
}
