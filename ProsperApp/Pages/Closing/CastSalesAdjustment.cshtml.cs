using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class CastSalesAdjustmentModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    ICastSalesAdjustmentRepository castSalesAdjustmentRepository,
    IStoreSlipRepository slipRepository,
    IStoreClock storeClock,
    IStoreMasterBootstrapper masterBootstrapper) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly ICastSalesAdjustmentRepository _castSalesAdjustmentRepository = castSalesAdjustmentRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    [BindProperty]
    public CastSalesAdjustmentSaveInput CastSalesAdjustmentInput { get; set; } = new();

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public CastSalesAdjustmentStatus CastSalesAdjustmentStatus { get; private set; } = new();

    public IReadOnlyList<CastSalesAdjustmentSlip> CastSalesAdjustmentSlips { get; private set; } = [];

    public IReadOnlyList<CastSalesAdjustmentDetail> CastSalesAdjustmentDetails { get; private set; } = [];

    public string CastSalesAmountBasis { get; private set; } = LocalSettings.CastSalesAmountBasisTotal;

    public string CastSalesSplitMode { get; private set; } = LocalSettings.CastSalesSplitModeSplit;

    public string? SuccessMessage { get; private set; }

    public string? LoadErrorMessage { get; private set; }

    public PageLoadStatus? LoadStatus { get; private set; }

    public long? ShowCastSalesAdjustmentModalSlipId { get; private set; }

    public bool CanConfirmCastSalesAdjustment =>
        CurrentBusinessDay is not null &&
        CastSalesAdjustmentStatus.RequiredSlipCount > 0 &&
        CastSalesAdjustmentSlips.Count > 0 &&
        CastSalesAdjustmentDetails.Count == CastSalesAdjustmentSlips.Count;

    public string FormatBusinessTime(DateTimeOffset value) => _storeClock.FormatBusinessTime(value);

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

        var inputs = CastSalesAdjustmentDetails
            .Select(detail => new CastSalesAdjustmentSaveInput
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
            })
            .ToList();
        var result = await _castSalesAdjustmentRepository.SaveBatchAsync(
            CurrentBusinessDay.BusinessDayId,
            inputs,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(
                string.Empty,
                result.ErrorMessage ?? "キャスト売上額調整を確認できませんでした。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["SuccessMessage"] = "キャスト売上額調整を確認しました。";
        return RedirectToPage("/Closing/Index");
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _masterBootstrapper.EnsureAsync(cancellationToken);
        var storeContextTask = _slipRepository.GetStoreContextAsync(cancellationToken);
        var currentBusinessDayTask = _businessDayRepository.GetCurrentAsync(cancellationToken);
        await Task.WhenAll(storeContextTask, currentBusinessDayTask);

        var storeContext = await storeContextTask;
        var currentBusinessDay = await currentBusinessDayTask;
        if (!storeContext.Succeeded || !currentBusinessDay.Succeeded)
        {
            var failureKind = !storeContext.Succeeded
                ? storeContext.FailureKind
                : currentBusinessDay.FailureKind;
            LoadErrorMessage = !storeContext.Succeeded
                ? storeContext.ErrorMessage
                : currentBusinessDay.ErrorMessage;
            LoadStatus = PageLoadStatus.Failure(
                failureKind ?? ResultFailureKind.Unavailable,
                LoadErrorMessage ?? "キャスト売上額調整に必要な情報を取得できませんでした。");
            CurrentBusinessDay = null;
            CastSalesAdjustmentStatus = new CastSalesAdjustmentStatus();
            CastSalesAdjustmentSlips = [];
            CastSalesAdjustmentDetails = [];
            return;
        }

        CastSalesAmountBasis = storeContext.Value.CastSalesAmountBasis;
        CastSalesSplitMode = storeContext.Value.CastSalesSplitMode;
        CurrentBusinessDay = currentBusinessDay.Value;
        if (CurrentBusinessDay is null)
        {
            LoadStatus = PageLoadStatus.Success(
                _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()));
            CastSalesAdjustmentStatus = new CastSalesAdjustmentStatus();
            CastSalesAdjustmentSlips = [];
            CastSalesAdjustmentDetails = [];
            return;
        }

        var overviewResult = await _castSalesAdjustmentRepository.GetOverviewAsync(
            CurrentBusinessDay.BusinessDayId,
            cancellationToken);
        if (!overviewResult.Succeeded)
        {
            LoadErrorMessage = overviewResult.ErrorMessage ?? "キャスト売上額調整を取得できませんでした。";
            LoadStatus = PageLoadStatus.Failure(
                overviewResult.FailureKind ?? ResultFailureKind.Unavailable,
                LoadErrorMessage);
            CastSalesAdjustmentStatus = new CastSalesAdjustmentStatus
            {
                RequiredSlipCount = 1,
                MissingSlipCount = 1
            };
            CastSalesAdjustmentSlips = [];
            CastSalesAdjustmentDetails = [];
            return;
        }

        CastSalesAdjustmentStatus = overviewResult.Value.Status;
        LoadStatus = PageLoadStatus.Success(
            _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()));
        CastSalesAdjustmentSlips = overviewResult.Value.Slips;
        foreach (var detail in overviewResult.Value.Details)
        {
            ApplyInitialCastSalesAmounts(detail);
        }

        CastSalesAdjustmentDetails = overviewResult.Value.Details;
    }

    public CastSalesAdjustmentDetail? FindCastSalesAdjustmentDetail(long slipId)
    {
        return CastSalesAdjustmentDetails.FirstOrDefault(x => x.SlipId == slipId);
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

        var suggestedSalesAmounts = CastSalesAmountBasis == LocalSettings.CastSalesAmountBasisSubtotal
            ? detail.Casts.Select(row => row.SuggestedSubtotalSalesAmount).ToArray()
            : detail.Casts.Select(row => row.SuggestedTotalSalesAmount).ToArray();
        var fallbackReason = CastSalesAmountBasis == LocalSettings.CastSalesAmountBasisSubtotal
            ? detail.Casts.Select(row => row.SubtotalSuggestionFallbackReason).FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason))
            : detail.Casts.Select(row => row.TotalSuggestionFallbackReason).FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));

        if (string.IsNullOrWhiteSpace(fallbackReason) && suggestedSalesAmounts.All(amount => amount is not null))
        {
            for (var i = 0; i < detail.Casts.Count; i++)
            {
                detail.Casts[i].InitialSalesAmount = suggestedSalesAmounts[i]!.Value;
            }

            detail.UsesTimeBasedInitialSalesAmount = true;
            return;
        }

        var castCount = detail.Casts.Count;
        var dividedAmount = baseAmountYen / castCount;
        var remainder = baseAmountYen % castCount;
        for (var i = 0; i < detail.Casts.Count; i++)
        {
            detail.Casts[i].InitialSalesAmount = dividedAmount + (i < remainder ? 1 : 0);
        }

        detail.InitialSalesAmountFallbackReason = GetInitialSalesAmountFallbackMessage(fallbackReason);
    }

    private static string GetInitialSalesAmountFallbackMessage(string? fallbackReason)
    {
        return fallbackReason switch
        {
            "missing_nomination_start_time" => "指名開始時刻が不足しているため、均等配分を初期表示しています。",
            "checkout_snapshot_mismatch" => "会計額と明細の整合性を確認できないため、均等配分を初期表示しています。",
            "unallocated_sales_event" => "指名開始前の売上があるため、均等配分を初期表示しています。",
            "negative_cast_sales_amount" => "値引きによりキャスト別売上額が負になるため、均等配分を初期表示しています。",
            _ => "売上発生時点の配分を計算できないため、均等配分を初期表示しています。"
        };
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
