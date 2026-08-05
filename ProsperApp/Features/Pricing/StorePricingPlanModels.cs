using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Features.Pricing;

public class StorePricingPlanInputModel
{
    [Display(Name = "セット時間")]
    [Range(5, 480, ErrorMessage = "セット時間は5〜480分で入力してください。")]
    public int SetMinutes { get; set; } = 60;

    [Display(Name = "延長時間")]
    [Range(5, 480, ErrorMessage = "延長時間は5〜480分で入力してください。")]
    public int ExtensionMinutes { get; set; } = 30;

    [Display(Name = "1人時セット料金")]
    [Range(0, 99999999, ErrorMessage = "料金は0〜99,999,999円で入力してください。")]
    public decimal SetUnitPriceSingle { get; set; }

    [Display(Name = "複数人時セット料金（1人あたり）")]
    [Range(0, 99999999, ErrorMessage = "料金は0〜99,999,999円で入力してください。")]
    public decimal SetUnitPricePerCustomer { get; set; }

    [Display(Name = "1人時延長料金")]
    [Range(0, 99999999, ErrorMessage = "料金は0〜99,999,999円で入力してください。")]
    public decimal ExtensionUnitPriceSingle { get; set; }

    [Display(Name = "複数人時延長料金（1人あたり）")]
    [Range(0, 99999999, ErrorMessage = "料金は0〜99,999,999円で入力してください。")]
    public decimal ExtensionUnitPricePerCustomer { get; set; }

    [Display(Name = "時間料金を有効にする")]
    public bool IsActive { get; set; }
}

public class StorePricingPlanSaveResult
{
    public bool Succeeded { get; init; }

    public int PlanVersion { get; init; }

    public string? ErrorMessage { get; init; }

    public static StorePricingPlanSaveResult Success(int planVersion) => new() { Succeeded = true, PlanVersion = planVersion };

    public static StorePricingPlanSaveResult Failed(string message) => new() { Succeeded = false, ErrorMessage = message };
}
