using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ManagementStaffsModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreStaffAdminRepository staffAdminRepository,
    IStoreClock storeClock,
    IStoreMasterBootstrapper masterBootstrapper) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreStaffAdminRepository _staffAdminRepository = staffAdminRepository;
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public IReadOnlyList<StoreStaffAdminItem> Staffs { get; set; } = [];

    [BindProperty]
    public StoreStaffCreateInputModel Input { get; set; } = new();

    [BindProperty]
    public long? DeleteStaffId { get; set; }

    [BindProperty]
    public long? UpdateStaffId { get; set; }

    [BindProperty]
    public string EmploymentType { get; set; } = StoreStaffEmploymentTypes.Employee;

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

        var result = await _staffAdminRepository.CreateStaffAsync(Input, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "スタッフを登録できませんでした。");
            return Page();
        }

        ModelState.Clear();
        Input = new StoreStaffCreateInputModel();
        SuccessMessage = "スタッフを登録しました。";
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

        if (DeleteStaffId is null or <= 0)
        {
            ModelState.AddModelError(string.Empty, "削除するスタッフを選択してください。");
            return Page();
        }

        var result = await _staffAdminRepository.DeleteStaffAsync(DeleteStaffId.Value, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "スタッフを削除できませんでした。");
            return Page();
        }

        ModelState.Clear();
        Input = new StoreStaffCreateInputModel();
        SuccessMessage = "スタッフを削除しました。";
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateEmploymentTypeAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Opening))
        {
            return NotFound();
        }

        if (!await LoadAsync(cancellationToken))
        {
            return Page();
        }

        if (UpdateStaffId is null or <= 0 || !StoreStaffEmploymentTypes.IsValid(EmploymentType))
        {
            ModelState.AddModelError(string.Empty, "変更するスタッフと雇用区分を確認してください。");
            return Page();
        }

        var result = await _staffAdminRepository.UpdateStaffEmploymentTypeAsync(
            UpdateStaffId.Value,
            EmploymentType,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "雇用区分を変更できませんでした。");
            return Page();
        }

        ModelState.Clear();
        Input = new StoreStaffCreateInputModel();
        SuccessMessage = "雇用区分を変更しました。";
        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        await _masterBootstrapper.EnsureAsync(cancellationToken);
        var businessDayTask = _businessDayRepository.GetCurrentAsync(cancellationToken);
        var staffsTask = _staffAdminRepository.GetStaffsAsync(cancellationToken);
        await Task.WhenAll(businessDayTask, staffsTask);

        var businessDay = await businessDayTask;
        var staffs = await staffsTask;
        CurrentBusinessDay = businessDay.Succeeded ? businessDay.Value : null;
        Staffs = staffs.Succeeded ? staffs.Value : [];

        if (!businessDay.Succeeded || !staffs.Succeeded)
        {
            var failureKind = !businessDay.Succeeded ? businessDay.FailureKind : staffs.FailureKind;
            var message = !businessDay.Succeeded ? businessDay.ErrorMessage : staffs.ErrorMessage;
            LoadStatus = PageLoadStatus.Failure(
                failureKind ?? ResultFailureKind.Unavailable,
                message ?? "スタッフ管理に必要な情報を取得できませんでした。");
            ModelState.AddModelError(string.Empty, LoadStatus.ErrorMessage!);
            return false;
        }

        LoadStatus = PageLoadStatus.Success(
            _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow()));
        return true;
    }
}
