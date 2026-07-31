using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ManagementItemsModel(
    IFeatureGate featureGate,
    IStoreItemAdminRepository itemAdminRepository,
    IStoreClock storeClock,
    IAdminModeService adminModeService) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IStoreItemAdminRepository _itemAdminRepository = itemAdminRepository;
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IAdminModeService _adminModeService = adminModeService;

    [BindProperty]
    public StoreItemCategoryInputModel CategoryInput { get; set; } = new();

    [BindProperty]
    public StoreItemInputModel ItemInput { get; set; } = new();

    [BindProperty]
    public long? DeleteItemId { get; set; }

    [BindProperty]
    public long? DeleteCategoryId { get; set; }

    [BindProperty]
    public List<StoreItemOrderInputModel> ReorderItems { get; set; } = [];

    public StoreItemAdminCatalog Catalog { get; set; } = new();

    public string? SuccessMessage { get; set; }

    public PageLoadStatus? LoadStatus { get; private set; }

    public bool IsAdminMode => _adminModeService.IsEnabled;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        await LoadCatalogAsync(cancellationToken);
        ResetInputs();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveCategoryAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        if (!await EnsureCategoryAdminModeAsync(cancellationToken))
        {
            return Page();
        }

        ModelState.Clear();
        if (!TryValidateModel(CategoryInput, nameof(CategoryInput)))
        {
            await LoadCatalogAsync(cancellationToken);
            PrepareMissingInputs();
            return Page();
        }

        var result = await _itemAdminRepository.SaveCategoryAsync(CategoryInput, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "カテゴリを保存できませんでした。");
            await LoadCatalogAsync(cancellationToken);
            PrepareMissingInputs();
            return Page();
        }

        SuccessMessage = "カテゴリを保存しました。";
        await LoadCatalogAsync(cancellationToken);
        ResetInputs();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveItemAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        ItemInput.ItemId = null;
        ItemInput.IsActive = true;
        ModelState.Clear();
        if (!TryValidateModel(ItemInput, nameof(ItemInput)))
        {
            await LoadCatalogAsync(cancellationToken);
            PrepareMissingInputs();
            return Page();
        }

        var result = await _itemAdminRepository.SaveItemAsync(ItemInput, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "商品を保存できませんでした。");
            await LoadCatalogAsync(cancellationToken);
            PrepareMissingInputs();
            return Page();
        }

        SuccessMessage = "商品を追加しました。";
        await LoadCatalogAsync(cancellationToken);
        ResetInputs();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteItemAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        if (DeleteItemId is null or <= 0)
        {
            ModelState.AddModelError(string.Empty, "削除する商品を選択してください。");
            await LoadCatalogAsync(cancellationToken);
            PrepareMissingInputs();
            return Page();
        }

        var result = await _itemAdminRepository.DeleteItemAsync(DeleteItemId.Value, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "商品を削除できませんでした。");
            await LoadCatalogAsync(cancellationToken);
            PrepareMissingInputs();
            return Page();
        }

        SuccessMessage = "商品を削除しました。";
        await LoadCatalogAsync(cancellationToken);
        ResetInputs();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        if (!await EnsureCategoryAdminModeAsync(cancellationToken))
        {
            return Page();
        }

        if (DeleteCategoryId is null or <= 0)
        {
            ModelState.AddModelError(string.Empty, "削除するカテゴリを選択してください。");
            await LoadCatalogAsync(cancellationToken);
            PrepareMissingInputs();
            return Page();
        }

        var result = await _itemAdminRepository.DeleteCategoryAsync(DeleteCategoryId.Value, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "カテゴリを削除できませんでした。");
            await LoadCatalogAsync(cancellationToken);
            PrepareMissingInputs();
            return Page();
        }

        SuccessMessage = "カテゴリを削除しました。";
        await LoadCatalogAsync(cancellationToken);
        ResetInputs();
        return Page();
    }

    public async Task<IActionResult> OnPostReorderItemsAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        ModelState.Clear();
        if (ReorderItems.Count == 0 || !TryValidateModel(ReorderItems, nameof(ReorderItems)))
        {
            ModelState.AddModelError(string.Empty, "商品の並び順を確認してください。");
            await LoadCatalogAsync(cancellationToken);
            PrepareMissingInputs();
            return Page();
        }

        var result = await _itemAdminRepository.ReorderItemsAsync(ReorderItems, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "商品の並び順を保存できませんでした。");
            await LoadCatalogAsync(cancellationToken);
            PrepareMissingInputs();
            return Page();
        }

        SuccessMessage = "商品の並び順を保存しました。";
        await LoadCatalogAsync(cancellationToken);
        ResetInputs();
        return Page();
    }

    private async Task LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var result = await _itemAdminRepository.GetCatalogAsync(cancellationToken);
        Catalog = result.Succeeded ? result.Value : new StoreItemAdminCatalog();
        LoadStatus = result.Succeeded
            ? PageLoadStatus.Success(_storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()))
            : PageLoadStatus.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "商品マスタを取得できませんでした。");
    }

    private async Task<bool> EnsureCategoryAdminModeAsync(CancellationToken cancellationToken)
    {
        if (IsAdminMode)
        {
            return true;
        }

        ModelState.AddModelError(string.Empty, "カテゴリを変更するには管理者設定で管理者モードを有効にしてください。");
        await LoadCatalogAsync(cancellationToken);
        PrepareMissingInputs();
        return false;
    }

    private void ResetInputs()
    {
        CategoryInput = new StoreItemCategoryInputModel
        {
            IsActive = true,
            SortOrder = NextCategorySortOrder()
        };

        ItemInput = new StoreItemInputModel
        {
            IsActive = true,
            ItemCategoryId = Catalog.Categories.FirstOrDefault(x => x.IsActive)?.ItemCategoryId ??
                             Catalog.Categories.FirstOrDefault()?.ItemCategoryId
        };
    }

    private void PrepareMissingInputs()
    {
        CategoryInput.IsActive = CategoryInput.ItemCategoryId is null || CategoryInput.IsActive;
        ItemInput.IsActive = ItemInput.ItemId is null || ItemInput.IsActive;
    }

    private int NextCategorySortOrder()
    {
        return Catalog.Categories.Count == 0 ? 10 : Catalog.Categories.Max(x => x.SortOrder) + 10;
    }

}
