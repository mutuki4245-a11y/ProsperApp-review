using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public sealed class ManagementTablesModel(
    IFeatureGate featureGate,
    IStoreTableAdminRepository tableAdminRepository,
    IStoreClock storeClock,
    IAdminModeService adminModeService) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IStoreTableAdminRepository _tableAdminRepository = tableAdminRepository;
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IAdminModeService _adminModeService = adminModeService;

    [BindProperty]
    public StoreTableInputModel Input { get; set; } = new();

    [BindProperty]
    public long? DeleteTableId { get; set; }

    public IReadOnlyList<StoreTableAdminItem> Tables { get; private set; } = [];

    public string? SuccessMessage { get; private set; }

    public PageLoadStatus? LoadStatus { get; private set; }

    public bool IsAdminMode => _adminModeService.IsEnabled;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        await LoadAsync(ct);
        ResetInput();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        if (!IsAdminMode)
        {
            return await AdminModeRequiredAsync(ct);
        }

        ModelState.Clear();
        if (!TryValidateModel(Input, nameof(Input)))
        {
            await LoadAsync(ct);
            return Page();
        }

        var result = await _tableAdminRepository.SaveTableAsync(Input, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "卓番を保存できませんでした。");
            await LoadAsync(ct);
            return Page();
        }

        SuccessMessage = Input.TableId is > 0 ? "卓番を更新しました。" : "卓番を追加しました。";
        await LoadAsync(ct);
        ResetInput();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        if (!IsAdminMode)
        {
            return await AdminModeRequiredAsync(ct);
        }

        if (DeleteTableId is null or <= 0)
        {
            ModelState.AddModelError(string.Empty, "削除する卓番を選択してください。");
            await LoadAsync(ct);
            ResetInput();
            return Page();
        }

        var result = await _tableAdminRepository.DeleteTableAsync(DeleteTableId.Value, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "卓番を削除できませんでした。");
            await LoadAsync(ct);
            ResetInput();
            return Page();
        }

        SuccessMessage = "卓番を削除しました。既存伝票は保存済みの卓番表示を使用します。";
        await LoadAsync(ct);
        ResetInput();
        return Page();
    }

    private async Task<IActionResult> AdminModeRequiredAsync(CancellationToken ct)
    {
        ModelState.AddModelError(string.Empty, "変更するには管理者設定で管理者モードを有効にしてください。");
        await LoadAsync(ct);
        ResetInput();
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var result = await _tableAdminRepository.GetTablesAsync(ct);
        Tables = result.Succeeded ? result.Value : [];
        LoadStatus = result.Succeeded
            ? PageLoadStatus.Success(_storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()))
            : PageLoadStatus.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "卓番マスタを取得できませんでした。");
    }

    private void ResetInput()
    {
        Input = new StoreTableInputModel
        {
            IsActive = true,
            SortOrder = Tables.Count == 0 ? 10 : Tables.Max(table => table.SortOrder) + 10
        };
    }
}
