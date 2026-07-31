using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public sealed class ClosingChampagneBacksModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IChampagneBackRepository champagneBackRepository,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IChampagneBackRepository _champagneBackRepository = champagneBackRepository;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty]
    public ChampagneBackSaveInput Input { get; set; } = new();

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public ChampagneBackStatus Status { get; private set; } = new();

    public IReadOnlyList<ChampagneBackCastRow> Casts { get; private set; } = [];

    public PageLoadStatus? LoadStatus { get; private set; }

    public string? SuccessMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        SuccessMessage = TempData["SuccessMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        NormalizeInput();
        await LoadAsync(cancellationToken, preserveInput: true);
        if (LoadStatus is { Succeeded: false })
        {
            ModelState.AddModelError(string.Empty, "必要な情報を取得できないためシャンパンバックを保存できません。再試行してください。");
            return Page();
        }

        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業中の営業日がありません。先に勤怠を保存してください。");
            return Page();
        }

        if (Input.BusinessDayId != CurrentBusinessDay.BusinessDayId)
        {
            ModelState.AddModelError(string.Empty, "営業日情報が更新されています。画面を再読み込みしてください。");
            return Page();
        }

        ValidateInput();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _champagneBackRepository.SaveAsync(Input, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "シャンパンバックを保存できませんでした。");
            await LoadAsync(cancellationToken, preserveInput: true);
            return Page();
        }

        TempData["SuccessMessage"] = $"{result.SavedCastCount}名分のシャンパン追加バック（合計 {StoreUiText.Yen(result.TotalBackAmount)}）を保存しました。";
        return RedirectToPage("/Closing/Index");
    }

    public ChampagneBackCastRow? FindCast(long castId)
    {
        return Casts.FirstOrDefault(cast => cast.CastId == castId);
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool preserveInput = false)
    {
        var businessDayResult = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (!businessDayResult.Succeeded)
        {
            LoadStatus = PageLoadStatus.Failure(
                businessDayResult.FailureKind ?? ResultFailureKind.Unavailable,
                businessDayResult.ErrorMessage ?? "現在営業日を取得できませんでした。");
            CurrentBusinessDay = null;
            Status = new ChampagneBackStatus();
            Casts = [];
            return;
        }

        CurrentBusinessDay = businessDayResult.Value;
        if (CurrentBusinessDay is null)
        {
            LoadStatus = PageLoadStatus.Success(
                _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()));
            Status = new ChampagneBackStatus();
            Casts = [];
            if (!preserveInput)
            {
                Input = new ChampagneBackSaveInput();
            }

            return;
        }

        var overview = await _champagneBackRepository.GetOverviewAsync(
            CurrentBusinessDay.BusinessDayId,
            cancellationToken);
        if (!overview.Succeeded)
        {
            LoadStatus = PageLoadStatus.Failure(
                overview.FailureKind ?? ResultFailureKind.Unavailable,
                overview.ErrorMessage ?? "シャンパンバックを取得できませんでした。");
            Status = new ChampagneBackStatus { RequiredCastCount = 1, MissingCastCount = 1 };
            Casts = [];
            return;
        }

        LoadStatus = PageLoadStatus.Success(
            _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()));
        Status = overview.Value.Status;
        Casts = overview.Value.Casts;

        if (!preserveInput)
        {
            Input = new ChampagneBackSaveInput
            {
                BusinessDayId = CurrentBusinessDay.BusinessDayId,
                Casts = Casts
                    .Select(cast => new ChampagneBackCastInput
                    {
                        CastId = cast.CastId,
                        BackAmount = cast.BackAmount ?? 0
                    })
                    .ToList()
            };
        }
    }

    private void NormalizeInput()
    {
        Input.Casts = Input.Casts
            .Where(cast => cast.CastId > 0)
            .GroupBy(cast => cast.CastId)
            .Select(group => group.Last())
            .ToList();
    }

    private void ValidateInput()
    {
        if (Input.Casts.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "出勤キャスト全員のシャンパン追加バックを入力してください。");
        }

        if (Input.Casts.Any(cast => cast.BackAmount < 0 || decimal.Truncate(cast.BackAmount) != cast.BackAmount))
        {
            ModelState.AddModelError(string.Empty, "シャンパン追加バックは0円以上の整数で入力してください。");
        }
    }
}
