using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class CastSalesAdjustmentModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    ICastSalesAdjustmentRepository castSalesAdjustmentRepository,
    ILocalSettingsProvider localSettingsProvider) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly ICastSalesAdjustmentRepository _castSalesAdjustmentRepository = castSalesAdjustmentRepository;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;

    [BindProperty]
    public CastSalesAdjustmentSaveInput CastSalesAdjustmentInput { get; set; } = new();

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public CastSalesAdjustmentStatus CastSalesAdjustmentStatus { get; private set; } = new();

    public IReadOnlyList<CastSalesAdjustmentSlip> CastSalesAdjustmentSlips { get; private set; } = [];

    public IReadOnlyList<CastSalesAdjustmentDetail> CastSalesAdjustmentDetails { get; private set; } = [];

    public string CastSalesAmountBasis { get; private set; } = LocalSettings.CastSalesAmountBasisTotal;

    public string CastSalesSplitMode { get; private set; } = LocalSettings.CastSalesSplitModeSplit;

    public string? SuccessMessage { get; private set; }

    public long? ShowCastSalesAdjustmentModalSlipId { get; private set; }

    public bool CanConfirmCastSalesAdjustment =>
        CurrentBusinessDay is not null &&
        CastSalesAdjustmentStatus.RequiredSlipCount > 0 &&
        CastSalesAdjustmentSlips.Count > 0 &&
        CastSalesAdjustmentDetails.Count == CastSalesAdjustmentSlips.Count;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveCastSalesAdjustmentAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業中の営業日がありません。");
            return Page();
        }

        if (CastSalesAdjustmentInput.BusinessDayId != CurrentBusinessDay.BusinessDayId)
        {
            ModelState.AddModelError(string.Empty, "営業日情報が更新されています。画面を再読み込みしてください。");
            ShowCastSalesAdjustmentModalSlipId = CastSalesAdjustmentInput.SlipId;
            return Page();
        }

        NormalizeCastSalesAdjustmentInput();
        ValidateCastSalesAdjustmentInput();
        if (!ModelState.IsValid)
        {
            ShowCastSalesAdjustmentModalSlipId = CastSalesAdjustmentInput.SlipId;
            return Page();
        }

        var result = await _castSalesAdjustmentRepository.SaveAsync(CastSalesAdjustmentInput, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "キャスト売上額調整を保存できませんでした。");
            await LoadAsync(cancellationToken);
            ShowCastSalesAdjustmentModalSlipId = CastSalesAdjustmentInput.SlipId;
            return Page();
        }

        SuccessMessage = "キャスト売上額調整を保存しました。";
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業中の営業日がありません。");
            return Page();
        }

        if (CastSalesAdjustmentStatus.RequiredSlipCount == 0)
        {
            TempData["SuccessMessage"] = "キャスト売上額調整の対象はありません。";
            return RedirectToPage("/Closing/Index");
        }

        if (CastSalesAdjustmentDetails.Count != CastSalesAdjustmentSlips.Count)
        {
            ModelState.AddModelError(string.Empty, "確認できない伝票があります。画面を再読み込みしてから再実行してください。");
            return Page();
        }

        foreach (var detail in CastSalesAdjustmentDetails)
        {
            var result = await _castSalesAdjustmentRepository.SaveAsync(
                new CastSalesAdjustmentSaveInput
                {
                    BusinessDayId = CurrentBusinessDay.BusinessDayId,
                    SlipId = detail.SlipId,
                    SourceAmountType = CastSalesAmountBasis,
                    SplitMode = CastSalesSplitMode,
                    Casts = detail.Casts
                        .Select(cast => new CastSalesAdjustmentCastInput
                        {
                            SlipCastId = cast.SlipCastId,
                            SalesAmount = cast.EffectiveSalesAmount
                        })
                        .ToList()
                },
                cancellationToken);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(
                    string.Empty,
                    $"{detail.TableDisplayName} の売上額調整を確認できませんでした。{result.ErrorMessage}");
                await LoadAsync(cancellationToken);
                return Page();
            }
        }

        TempData["SuccessMessage"] = "キャスト売上額調整を確認しました。";
        return RedirectToPage("/Closing/Index");
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = _localSettingsProvider.GetCurrent();
        CastSalesAmountBasis = settings.CastSalesAmountBasis;
        CastSalesSplitMode = settings.CastSalesSplitMode;
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        CastSalesAdjustmentStatus = CurrentBusinessDay is null
            ? new CastSalesAdjustmentStatus()
            : await _castSalesAdjustmentRepository.GetStatusAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        CastSalesAdjustmentSlips = CurrentBusinessDay is null
            ? []
            : await _castSalesAdjustmentRepository.GetSlipsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        CastSalesAdjustmentDetails = CurrentBusinessDay is null
            ? []
            : await LoadCastSalesAdjustmentDetailsAsync(CastSalesAdjustmentSlips, cancellationToken);
    }

    public CastSalesAdjustmentDetail? FindCastSalesAdjustmentDetail(long slipId)
    {
        return CastSalesAdjustmentDetails.FirstOrDefault(x => x.SlipId == slipId);
    }

    public static string FormatAmountValue(decimal amount)
    {
        return amount.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<CastSalesAdjustmentDetail>> LoadCastSalesAdjustmentDetailsAsync(
        IReadOnlyList<CastSalesAdjustmentSlip> slips,
        CancellationToken cancellationToken)
    {
        var details = new List<CastSalesAdjustmentDetail>();
        foreach (var slip in slips)
        {
            var detail = await _castSalesAdjustmentRepository.GetDetailAsync(slip.SlipId, cancellationToken);
            if (detail is null)
            {
                continue;
            }

            ApplyInitialCastSalesAmounts(detail);
            details.Add(detail);
        }

        return details;
    }

    private void ApplyInitialCastSalesAmounts(CastSalesAdjustmentDetail detail)
    {
        if (detail.Casts.Count == 0)
        {
            return;
        }

        var baseAmount = CastSalesAmountBasis == LocalSettings.CastSalesAmountBasisSubtotal
            ? detail.SubtotalAmount
            : detail.TotalAmount;
        var baseAmountYen = (long)decimal.Truncate(baseAmount);

        if (CastSalesSplitMode == LocalSettings.CastSalesSplitModeFull)
        {
            foreach (var row in detail.Casts)
            {
                row.InitialSalesAmount = baseAmountYen;
            }

            return;
        }

        var castCount = detail.Casts.Count;
        var dividedAmount = baseAmountYen / castCount;
        var remainder = baseAmountYen % castCount;
        for (var i = 0; i < detail.Casts.Count; i++)
        {
            detail.Casts[i].InitialSalesAmount = dividedAmount + (i < remainder ? 1 : 0);
        }
    }

    private void NormalizeCastSalesAdjustmentInput()
    {
        CastSalesAdjustmentInput.SourceAmountType =
            CastSalesAdjustmentInput.SourceAmountType == LocalSettings.CastSalesAmountBasisSubtotal
                ? LocalSettings.CastSalesAmountBasisSubtotal
                : LocalSettings.CastSalesAmountBasisTotal;
        CastSalesAdjustmentInput.SplitMode =
            CastSalesAdjustmentInput.SplitMode == LocalSettings.CastSalesSplitModeFull
                ? LocalSettings.CastSalesSplitModeFull
                : LocalSettings.CastSalesSplitModeSplit;
        CastSalesAdjustmentInput.Casts = CastSalesAdjustmentInput.Casts
            .Where(x => x.SlipCastId > 0)
            .GroupBy(x => x.SlipCastId)
            .Select(x => x.Last())
            .ToList();
    }

    private void ValidateCastSalesAdjustmentInput()
    {
        if (CastSalesAdjustmentInput.SlipId is null or <= 0)
        {
            ModelState.AddModelError(string.Empty, "調整する伝票を選択してください。");
        }

        if (CastSalesAdjustmentInput.Casts.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "指名キャストの売上額を入力してください。");
        }

        if (CastSalesAdjustmentInput.Casts.Any(x => x.SalesAmount < 0 || decimal.Truncate(x.SalesAmount) != x.SalesAmount))
        {
            ModelState.AddModelError(string.Empty, "売上額は0円以上の整数で入力してください。");
        }
    }
}
