using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class IndexModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreOrderRepository orderRepository,
    ILocalSettingsProvider localSettingsProvider,
    IStoreClock storeClock) : PageModel
{
    private static readonly HashSet<string> AllowedNominationKinds = new(StringComparer.Ordinal)
    {
        "companion_18",
        "companion_19",
        "companion_20",
        "nomination",
        "in_store"
    };

    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty]
    public CreateSlipInputModel CreateSlipInput { get; set; } = new();

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public StoreContext? StoreContext { get; set; }

    public IReadOnlyList<BusinessSlipListItem> Slips { get; set; } = [];

    public IReadOnlyList<StoreTableOption> Tables { get; set; } = [];

    public IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts { get; set; } = [];

    public IReadOnlyList<string> TimeOptions { get; set; } = [];

    public bool ShowCreateSlipModal { get; private set; }

    public string? SuccessMessage { get; private set; }

    public bool SlipsEnabled => _featureGate.IsEnabled(FeatureNames.Slips);

    public bool OrdersEnabled => _featureGate.IsEnabled(FeatureNames.Orders);

    public bool CheckoutEnabled => _featureGate.IsEnabled(FeatureNames.Checkout);

    public int OpenSlipCount => Slips.Count(x => x.Status == "open");

    public int CheckedOutSlipCount => Slips.Count(x => x.Status == "checked_out");

    public bool HasAnySlip => Slips.Count > 0;

    public bool CanMoveToClosing => CurrentBusinessDay is not null && HasAnySlip && OpenSlipCount == 0;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (OrdersEnabled && _localSettingsProvider.GetCurrent().ScreenMode == "order-entry")
        {
            return RedirectToPage("/Orders/Index");
        }

        await LoadAsync(cancellationToken);
        SetDefaultCreateSlipInput();
        SuccessMessage = TempData["SuccessMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostCreateSlipAsync(CancellationToken cancellationToken)
    {
        if (!SlipsEnabled)
        {
            return NotFound();
        }

        NormalizeCreateSlipInput();
        await LoadAsync(cancellationToken);
        SetBusinessDayInput();
        ComposeOpenedAt();
        ValidateCreateSlip();

        if (!ModelState.IsValid)
        {
            ShowCreateSlipModal = true;
            return Page();
        }

        var result = await _slipRepository.CreateSlipAsync(CreateSlipInput, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "伝票を作成できませんでした。");
            ShowCreateSlipModal = true;
            return Page();
        }

        SuccessMessage = "伝票を作成しました。";
        ModelState.Clear();
        CreateSlipInput = new CreateSlipInputModel();
        await LoadAsync(cancellationToken);
        SetDefaultCreateSlipInput();
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        StoreContext = await _slipRepository.GetStoreContextAsync(cancellationToken);
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        Tables = await _slipRepository.GetTablesAsync(cancellationToken);
        AttendanceCasts = CurrentBusinessDay is null
            ? []
            : await _orderRepository.GetAttendanceCastsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        TimeOptions = _storeClock.BuildTimeOptions(5);
        Slips = CurrentBusinessDay is null
            ? []
            : await _slipRepository.GetBusinessDaySlipsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
    }

    private void SetDefaultCreateSlipInput()
    {
        CreateSlipInput.OpenedTime ??= _storeClock.FloorToMinuteStep(_storeClock.GetStoreNow(), 5).ToString("HH:mm");
        SetBusinessDayInput();

        if (CreateSlipInput.CustomerLabels.Count == 0)
        {
            CreateSlipInput.CustomerLabels.Add(null);
        }

        if (CreateSlipInput.CastNominations.Count == 0)
        {
            CreateSlipInput.CastNominations.Add(new CastNominationInputModel());
        }

        ComposeOpenedAt();
    }

    private void SetBusinessDayInput()
    {
        CreateSlipInput.BusinessDate = CurrentBusinessDay?.BusinessDate;
        CreateSlipInput.BusinessDayId = CurrentBusinessDay?.BusinessDayId;
    }

    private void NormalizeCreateSlipInput()
    {
        CreateSlipInput.CustomerLabels = CreateSlipInput.CustomerLabels
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .ToList();

        if (CreateSlipInput.CustomerLabels.Count == 0)
        {
            CreateSlipInput.CustomerLabels.Add(null);
        }

        CreateSlipInput.Memo = string.IsNullOrWhiteSpace(CreateSlipInput.Memo) ? null : CreateSlipInput.Memo.Trim();
        CreateSlipInput.CastNominations = CreateSlipInput.CastNominations
            .Select(x => new CastNominationInputModel
            {
                NominationKind = string.IsNullOrWhiteSpace(x.NominationKind) ? null : x.NominationKind.Trim(),
                CastId = x.CastId,
                CastName = string.IsNullOrWhiteSpace(x.CastName) ? null : x.CastName.Trim()
            })
            .Where(x => x.CastId is not null || !string.IsNullOrWhiteSpace(x.CastName) || !string.IsNullOrWhiteSpace(x.NominationKind))
            .ToList();
    }

    private void ComposeOpenedAt()
    {
        if (CurrentBusinessDay is null ||
            string.IsNullOrWhiteSpace(CreateSlipInput.OpenedTime) ||
            !TimeOnly.TryParse(CreateSlipInput.OpenedTime, out var openedTime))
        {
            CreateSlipInput.OpenedAt = null;
            return;
        }

        CreateSlipInput.OpenedAt = _storeClock.ComposeBusinessDateTime(CurrentBusinessDay.BusinessDate, openedTime);
    }

    private void ValidateCreateSlip()
    {
        if (StoreContext is null)
        {
            ModelState.AddModelError(string.Empty, "店舗設定を取得できません。Supabase設定とStoreDepartmentIdを確認してください。");
        }

        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業日が開始されていません。営業準備を実行してください。");
        }

        if (Tables.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "卓番マスタが未登録です。store_table_masterにこの店舗の卓番を登録してください。");
        }

        if (CreateSlipInput.TableId is not null && Tables.All(x => x.TableId != CreateSlipInput.TableId.Value))
        {
            ModelState.AddModelError("CreateSlipInput.TableId", "この店舗で利用できない卓番です。");
        }

        var allowedCastIds = AttendanceCasts.Select(x => x.CastId).ToHashSet();
        for (var i = 0; i < CreateSlipInput.CastNominations.Count; i++)
        {
            var nomination = CreateSlipInput.CastNominations[i];
            if (nomination.CastId is not null && string.IsNullOrWhiteSpace(nomination.CastName))
            {
                nomination.CastName = AttendanceCasts.FirstOrDefault(x => x.CastId == nomination.CastId.Value)?.SearchDisplayName;
            }

            if (string.IsNullOrWhiteSpace(nomination.NominationKind) || !AllowedNominationKinds.Contains(nomination.NominationKind))
            {
                ModelState.AddModelError($"CreateSlipInput.CastNominations[{i}].NominationKind", "指名区分を選択してください。");
            }

            if (nomination.CastId is null)
            {
                ModelState.AddModelError($"CreateSlipInput.CastNominations[{i}].CastName", "候補からキャストを選択してください。");
            }
            else if (!allowedCastIds.Contains(nomination.CastId.Value))
            {
                ModelState.AddModelError($"CreateSlipInput.CastNominations[{i}].CastName", "出勤キャストから選択してください。");
            }

            if (nomination.CastName is not null && nomination.CastName.Length > 160)
            {
                ModelState.AddModelError($"CreateSlipInput.CastNominations[{i}].CastName", "キャスト名は160文字以内で入力してください。");
            }
        }

        if (string.IsNullOrWhiteSpace(CreateSlipInput.OpenedTime) || !TimeOptions.Contains(CreateSlipInput.OpenedTime))
        {
            ModelState.AddModelError("CreateSlipInput.OpenedTime", "入店時刻は5分単位で選択してください。");
        }

        if (CreateSlipInput.CustomerLabels.Count is < 1 or > 20)
        {
            ModelState.AddModelError("CreateSlipInput.CustomerLabels", "客情報は1人から20人まで登録できます。");
        }

        if (CreateSlipInput.CustomerLabels.Any(x => x is not null && x.Length > 100))
        {
            ModelState.AddModelError("CreateSlipInput.CustomerLabels", "客名は1人100文字以内で入力してください。");
        }

        if (CreateSlipInput.OpenedAt is not null)
        {
            var now = _storeClock.GetStoreNow();
            if (CreateSlipInput.OpenedAt.Value > now.AddMinutes(5))
            {
                ModelState.AddModelError("CreateSlipInput.OpenedAt", "入店時刻に未来時刻は指定できません。");
            }

            if (CreateSlipInput.OpenedAt.Value < now.AddDays(-2))
            {
                ModelState.AddModelError("CreateSlipInput.OpenedAt", "入店時刻は過去2日以内で入力してください。");
            }
        }
    }

    public static string ToSlipStatusDisplay(string status)
    {
        return status switch
        {
            "open" => "在席",
            "checked_out" => "会計済み",
            "cancelled" => "取消",
            _ => status
        };
    }

    public static string ToSlipStatusBadgeClass(string status)
    {
        return status switch
        {
            "open" => "text-bg-success",
            "checked_out" => "text-bg-secondary",
            "cancelled" => "text-bg-danger",
            _ => "text-bg-secondary"
        };
    }
}
