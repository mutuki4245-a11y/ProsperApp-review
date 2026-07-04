using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ManagementNominationBacksModel(
    IFeatureGate featureGate,
    INominationBackAdminRepository nominationBackAdminRepository) : PageModel
{
    private static readonly NominationBackDefinition[] Definitions =
    [
        new("nomination", "本指名", 10),
        new("in_store", "場内指名", 20),
        new("companion", "同伴", 30)
    ];

    private readonly IFeatureGate _featureGate = featureGate;
    private readonly INominationBackAdminRepository _nominationBackAdminRepository = nominationBackAdminRepository;

    [BindProperty]
    public List<NominationBackMasterInputModel> Settings { get; set; } = [];

    public string? SuccessMessage { get; set; }

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

    public string GetDisplayName(string nominationType)
    {
        return Definitions.FirstOrDefault(x => x.NominationType == nominationType)?.DisplayName ?? nominationType;
    }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _nominationBackAdminRepository.GetSettingsAsync(cancellationToken);
        Settings = Definitions
            .Select(definition =>
            {
                var current = settings.FirstOrDefault(x => x.NominationType == definition.NominationType);
                return new NominationBackMasterInputModel
                {
                    NominationType = definition.NominationType,
                    BackUnitAmount = current?.BackUnitAmount ?? 0,
                    SortOrder = current?.SortOrder ?? definition.SortOrder,
                    IsActive = current?.IsActive ?? true
                };
            })
            .ToList();
    }

    private void NormalizePostedSettings()
    {
        Settings = Definitions
            .Select(definition =>
            {
                var posted = Settings.FirstOrDefault(x => x.NominationType == definition.NominationType);
                return new NominationBackMasterInputModel
                {
                    NominationType = definition.NominationType,
                    BackUnitAmount = posted?.BackUnitAmount ?? 0,
                    SortOrder = posted?.SortOrder ?? definition.SortOrder,
                    IsActive = posted?.IsActive ?? true
                };
            })
            .ToList();
    }

    private bool ValidateSettings()
    {
        for (var index = 0; index < Settings.Count; index++)
        {
            var setting = Settings[index];
            if (!Definitions.Any(x => x.NominationType == setting.NominationType))
            {
                ModelState.AddModelError($"Settings[{index}].NominationType", "指名種別を確認してください。");
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

    private sealed record NominationBackDefinition(string NominationType, string DisplayName, int SortOrder);
}
