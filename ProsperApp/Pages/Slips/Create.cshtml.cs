using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class SlipCreateModel(
    IFeatureGate featureGate,
    IStoreSlipRepository slipRepository,
    IBusinessDayRepository businessDayRepository) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;

    [BindProperty]
    public CreateSlipInputModel Input { get; set; } = new();

    public StoreContext? StoreContext { get; set; }

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public IReadOnlyList<StoreTableOption> Tables { get; set; } = [];

    public IReadOnlyList<CastOption> Casts { get; set; } = [];

    public IReadOnlyList<string> TimeOptions { get; set; } = [];

    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        await LoadOptionsAsync(cancellationToken);
        SetDefaultInput();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        NormalizeInput();
        await LoadOptionsAsync(cancellationToken);
        SetBusinessDayInput();
        ComposeOpenedAt();
        ValidateBusinessRules();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _slipRepository.CreateSlipAsync(Input, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "伝票を作成できませんでした。");
            return Page();
        }

        ModelState.Clear();
        Input = new CreateSlipInputModel();
        await LoadOptionsAsync(cancellationToken);
        SetDefaultInput();
        SuccessMessage = $"伝票を作成しました。伝票ID: {result.SlipId}";
        return Page();
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        StoreContext = await _slipRepository.GetStoreContextAsync(cancellationToken);
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        Tables = await _slipRepository.GetTablesAsync(cancellationToken);
        Casts = await _slipRepository.GetCastsAsync(cancellationToken);
        TimeOptions = BuildTimeOptions();
    }

    private void SetDefaultInput()
    {
        Input.OpenedTime ??= FloorToFiveMinutes(DateTime.Now).ToString("HH:mm");
        SetBusinessDayInput();

        if (Input.CustomerLabels.Count == 0)
        {
            Input.CustomerLabels.Add(null);
        }

        ComposeOpenedAt();
    }

    private void SetBusinessDayInput()
    {
        Input.BusinessDate = CurrentBusinessDay?.BusinessDate;
        Input.BusinessDayId = CurrentBusinessDay?.BusinessDayId;
    }

    private void NormalizeInput()
    {
        Input.CustomerLabels = Input.CustomerLabels
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .ToList();

        if (Input.CustomerLabels.Count == 0)
        {
            Input.CustomerLabels.Add(null);
        }

        Input.Memo = string.IsNullOrWhiteSpace(Input.Memo) ? null : Input.Memo.Trim();
        Input.CastIds = Input.CastIds.Distinct().ToList();
    }

    private void ComposeOpenedAt()
    {
        if (CurrentBusinessDay is null ||
            string.IsNullOrWhiteSpace(Input.OpenedTime) ||
            !TimeOnly.TryParse(Input.OpenedTime, out var openedTime))
        {
            Input.OpenedAt = null;
            return;
        }

        Input.OpenedAt = CurrentBusinessDay.BusinessDate.ToDateTime(openedTime);
    }

    private void ValidateBusinessRules()
    {
        if (StoreContext is null)
        {
            ModelState.AddModelError(string.Empty, "店舗設定を取得できません。Supabase設定とStoreDepartmentIdを確認してください。");
        }

        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業日が開始されていません。開け作業を実行してください。");
        }

        if (Tables.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "卓番マスタが未登録です。store_table_masterにこの店舗の卓番を登録してください。");
        }

        if (Input.TableId is not null && Tables.All(x => x.TableId != Input.TableId.Value))
        {
            ModelState.AddModelError("Input.TableId", "この店舗で利用できない卓番です。");
        }

        var allowedCastIds = Casts.Select(x => x.CastId).ToHashSet();
        foreach (var castId in Input.CastIds)
        {
            if (!allowedCastIds.Contains(castId))
            {
                ModelState.AddModelError("Input.CastIds", "この店舗で利用できないキャストが選択されています。");
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(Input.OpenedTime) || !TimeOptions.Contains(Input.OpenedTime))
        {
            ModelState.AddModelError("Input.OpenedTime", "入店時刻は5分単位で選択してください。");
        }

        if (Input.CustomerLabels.Count is < 1 or > 20)
        {
            ModelState.AddModelError("Input.CustomerLabels", "客情報は1人から20人まで登録できます。");
        }

        if (Input.CustomerLabels.Any(x => x is not null && x.Length > 100))
        {
            ModelState.AddModelError("Input.CustomerLabels", "客名は1人100文字以内で入力してください。");
        }

        if (Input.OpenedAt is not null)
        {
            var now = DateTime.Now;
            if (Input.OpenedAt.Value > now.AddMinutes(5))
            {
                ModelState.AddModelError("Input.OpenedAt", "入店時刻に未来時刻は指定できません。");
            }

            if (Input.OpenedAt.Value < now.AddDays(-2))
            {
                ModelState.AddModelError("Input.OpenedAt", "入店時刻は過去2日以内で入力してください。");
            }
        }
    }

    private static IReadOnlyList<string> BuildTimeOptions()
    {
        var options = new List<string>(24 * 12);
        for (var hour = 0; hour < 24; hour++)
        {
            for (var minute = 0; minute < 60; minute += 5)
            {
                options.Add($"{hour:00}:{minute:00}");
            }
        }

        return options;
    }

    private static DateTime FloorToFiveMinutes(DateTime value)
    {
        var minute = value.Minute - value.Minute % 5;
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, minute, 0);
    }
}
