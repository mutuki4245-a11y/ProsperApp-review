using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ManagementCastsModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreCastAdminRepository castAdminRepository,
    IStoreClock storeClock,
    IStoreMasterBootstrapper masterBootstrapper) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreCastAdminRepository _castAdminRepository = castAdminRepository;
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public IReadOnlyList<StoreCastAdminItem> Casts { get; set; } = [];

    [BindProperty]
    public StoreCastCreateInputModel Input { get; set; } = new();

    [BindProperty]
    public long? DeleteCastId { get; set; }

    [BindProperty]
    public StoreCastDrinkMemoInputModel DrinkMemoInput { get; set; } = new();

    public long? EditingDrinkMemoCastId { get; private set; }

    public string? SuccessMessage { get; set; }
    public PageLoadStatus? LoadStatus { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        if (!await LoadAsync(cancellationToken))
        {
            return Page();
        }
        ModelState.Clear();
        if (!TryValidateModel(Input, nameof(Input)))
        {
            return Page();
        }

        var result = await _castAdminRepository.CreateCastAsync(Input, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "キャストを登録できませんでした。");
            return Page();
        }

        ModelState.Clear();
        Input = new StoreCastCreateInputModel();
        SuccessMessage = "キャストを登録しました。";
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        if (!await LoadAsync(cancellationToken))
        {
            return Page();
        }
        if (DeleteCastId is null or <= 0)
        {
            ModelState.AddModelError(string.Empty, "削除するキャストを選択してください。");
            return Page();
        }

        var result = await _castAdminRepository.DeleteCastAsync(DeleteCastId.Value, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "キャストを削除できませんでした。");
            return Page();
        }

        ModelState.Clear();
        Input = new StoreCastCreateInputModel();
        SuccessMessage = "キャストを削除しました。";
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateDrinkMemoAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        if (!await LoadAsync(cancellationToken))
        {
            return Page();
        }
        ModelState.Clear();
        if (!TryValidateModel(DrinkMemoInput, nameof(DrinkMemoInput)))
        {
            EditingDrinkMemoCastId = DrinkMemoInput.CastId;
            return Page();
        }

        var result = await _castAdminRepository.UpdateDrinkMemoAsync(
            DrinkMemoInput,
            CurrentBusinessDay?.BusinessDayId,
            cancellationToken);
        if (!result.Succeeded)
        {
            EditingDrinkMemoCastId = DrinkMemoInput.CastId;
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "ドリンクメモを更新できませんでした。");
            return Page();
        }

        ModelState.Clear();
        Input = new StoreCastCreateInputModel();
        DrinkMemoInput = new StoreCastDrinkMemoInputModel();
        SuccessMessage = "ドリンクメモを更新しました。";
        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        await _masterBootstrapper.EnsureAsync(cancellationToken);
        var businessDayTask = _businessDayRepository.GetCurrentAsync(cancellationToken);
        var castsTask = _castAdminRepository.GetCastsAsync(cancellationToken);
        await Task.WhenAll(businessDayTask, castsTask);

        var businessDay = await businessDayTask;
        var casts = await castsTask;
        CurrentBusinessDay = businessDay.Succeeded ? businessDay.Value : null;
        Casts = casts.Succeeded ? casts.Value : [];

        if (!businessDay.Succeeded || !casts.Succeeded)
        {
            var failureKind = !businessDay.Succeeded ? businessDay.FailureKind : casts.FailureKind;
            var message = !businessDay.Succeeded ? businessDay.ErrorMessage : casts.ErrorMessage;
            LoadStatus = PageLoadStatus.Failure(
                failureKind ?? ResultFailureKind.Unavailable,
                message ?? "キャスト管理に必要な情報を取得できませんでした。");
            ModelState.AddModelError(string.Empty, LoadStatus.ErrorMessage!);
            return false;
        }

        LoadStatus = PageLoadStatus.Success(
            _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()));
        return true;
    }

}
