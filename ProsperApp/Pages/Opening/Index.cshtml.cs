using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class OpeningModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty]
    [Display(Name = "営業日")]
    [Required(ErrorMessage = "営業日を入力してください。")]
    public DateOnly? BusinessDate { get; set; }

    [BindProperty]
    [Display(Name = "メモ")]
    [StringLength(500, ErrorMessage = "メモは500文字以内で入力してください。")]
    public string? Memo { get; set; }

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        BusinessDate ??= DateOnly.FromDateTime(_storeClock.GetStoreNow());
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        SuccessMessage = TempData["SuccessMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        BusinessDate ??= DateOnly.FromDateTime(_storeClock.GetStoreNow());
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (CurrentBusinessDay is not null)
        {
            ModelState.AddModelError(string.Empty, $"営業日 {CurrentBusinessDay.BusinessDate:yyyy-MM-dd} は既に営業中です。");
            return Page();
        }

        if (!ModelState.IsValid || BusinessDate is null)
        {
            return Page();
        }

        var today = DateOnly.FromDateTime(_storeClock.GetStoreNow());
        if (BusinessDate.Value > today)
        {
            ModelState.AddModelError(nameof(BusinessDate), "未来日は営業日に指定できません。");
            return Page();
        }

        if (BusinessDate.Value < today.AddDays(-2))
        {
            ModelState.AddModelError(nameof(BusinessDate), "営業日は過去2日以内で指定してください。");
            return Page();
        }

        var result = await _businessDayRepository.OpenAsync(BusinessDate.Value, Memo, [], cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "営業日を開始できませんでした。");
            return Page();
        }

        CurrentBusinessDay = result.BusinessDay;
        TempData["SuccessMessage"] = $"営業日 {CurrentBusinessDay?.BusinessDate:yyyy-MM-dd} を開始しました。";
        return RedirectToPage("/Index");
    }
}
