using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProsperApp.Models;
using ProsperApp.Options;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class IndexModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreOrderRepository orderRepository,
    INominationBackAdminRepository nominationBackRepository,
    ILocalSettingsProvider localSettingsProvider,
    IOptions<ReceiptPrinterOptions> receiptPrinterOptions,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly INominationBackAdminRepository _nominationBackRepository = nominationBackRepository;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly ReceiptPrinterOptions _receiptPrinterOptions = receiptPrinterOptions.Value;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty]
    public CreateSlipInputModel CreateSlipInput { get; set; } = new();

    [BindProperty]
    public List<KaraokeQuantityInputModel> KaraokeLines { get; set; } = [];

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public DateOnly CurrentBusinessDate { get; private set; }

    public StoreContext? StoreContext { get; set; }

    public IReadOnlyList<BusinessSlipListItem> Slips { get; set; } = [];

    public IReadOnlyList<StoreTableOption> Tables { get; set; } = [];

    public IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts { get; set; } = [];

    public IReadOnlyList<NominationBackMasterItem> NominationOptions { get; set; } = [];

    public IReadOnlyList<string> TimeOptions { get; set; } = [];

    public bool ShowCreateSlipModal { get; private set; }

    public string? SuccessMessage { get; private set; }

    public string? PendingReceiptPrintRequestJson { get; private set; }

    public bool ShouldRunBrowserReceiptPrint => _receiptPrinterOptions.Enabled &&
        !string.IsNullOrWhiteSpace(PendingReceiptPrintRequestJson);

    public string ReceiptPrinterBrowserSdkScriptUrl => _receiptPrinterOptions.BrowserSdkScriptUrl;

    public string ReceiptPrinterBrowserWebSocketHost => string.IsNullOrWhiteSpace(_receiptPrinterOptions.BrowserWebSocketHost)
        ? "localhost"
        : _receiptPrinterOptions.BrowserWebSocketHost;

    public string ReceiptPrinterBrowserCodePage => _receiptPrinterOptions.BrowserCodePage;

    public string ReceiptPrinterBrowserInternationalCharacter => _receiptPrinterOptions.BrowserInternationalCharacter;

    public bool SlipsEnabled => _featureGate.IsEnabled(FeatureNames.Slips);

    public bool OrdersEnabled => _featureGate.IsEnabled(FeatureNames.Orders);

    public bool CheckoutEnabled => _featureGate.IsEnabled(FeatureNames.Checkout);

    public int OpenSlipCount => Slips.Count(x => x.Status == "open");

    public int CheckedOutSlipCount => Slips.Count(x => x.Status == "checked_out");

    public bool HasAnySlip => Slips.Count > 0;

    public bool HasCurrentBusinessDay => HasValidBusinessDate(CurrentBusinessDay);

    public DateOnly DisplayBusinessDate => GetSafeBusinessDate(CurrentBusinessDay);

    public string DisplayBusinessDateText => HasCurrentBusinessDay
        ? DisplayBusinessDate.ToString("yyyy-MM-dd")
        : $"{DisplayBusinessDate:yyyy-MM-dd} / 自動作成待ち";

    public long? CurrentBusinessDayId => HasCurrentBusinessDay
        ? CurrentBusinessDay?.BusinessDayId
        : null;

    public bool IsPreviousBusinessDayOpen => CurrentBusinessDay is not null &&
        HasValidBusinessDate(CurrentBusinessDay) &&
        CurrentBusinessDay.BusinessDate < CurrentBusinessDate;

    public bool CanCreateSalesInput => SlipsEnabled && !IsPreviousBusinessDayOpen;

    public bool CanMoveToClosing => HasCurrentBusinessDay;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (OrdersEnabled && _localSettingsProvider.GetCurrent().ScreenMode == "order-entry")
        {
            return RedirectToPage("/Orders/Index");
        }

        await LoadAsync(cancellationToken, includeAttendanceCasts: false);
        SetDefaultCreateSlipInput();
        SuccessMessage = TempData["SuccessMessage"] as string;
        PendingReceiptPrintRequestJson = TempData[ReceiptPrintTempDataKeys.PendingCheckoutReceipt] as string;
        return Page();
    }

    public async Task<IActionResult> OnGetAttendanceCastsAsync(CancellationToken cancellationToken)
    {
        if (!SlipsEnabled)
        {
            return NotFound();
        }

        var currentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (currentBusinessDay is null)
        {
            return new JsonResult(Array.Empty<object>());
        }

        var casts = await _orderRepository.GetAttendanceCastsAsync(currentBusinessDay.BusinessDayId, cancellationToken);
        return new JsonResult(casts.Select(cast => new
        {
            id = cast.CastId,
            name = cast.DisplayName,
            display = cast.SearchDisplayName,
            department = cast.DepartmentName
        }));
    }

    public async Task<IActionResult> OnGetBusinessSlipsAsync(CancellationToken cancellationToken)
    {
        if (!SlipsEnabled)
        {
            return NotFound();
        }

        var currentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (currentBusinessDay is null)
        {
            var currentBusinessDate = _storeClock.GetCurrentBusinessDate();
            return new JsonResult(new
            {
                succeeded = true,
                businessDayId = (long?)null,
                businessDate = currentBusinessDate.ToString("yyyy-MM-dd"),
                businessDateDisplay = $"{currentBusinessDate:yyyy-MM-dd} / 自動作成待ち",
                hasBusinessDay = false,
                openSlipCount = 0,
                checkedOutSlipCount = 0,
                slips = Array.Empty<object>()
            });
        }

        var slips = await _slipRepository.GetBusinessDaySlipsAsync(currentBusinessDay.BusinessDayId, cancellationToken);
        var businessDate = GetSafeBusinessDate(currentBusinessDay);
        var hasValidBusinessDate = HasValidBusinessDate(currentBusinessDay);
        return new JsonResult(new
        {
            succeeded = true,
            businessDayId = hasValidBusinessDate ? (long?)currentBusinessDay.BusinessDayId : null,
            businessDate = businessDate.ToString("yyyy-MM-dd"),
            businessDateDisplay = hasValidBusinessDate
                ? businessDate.ToString("yyyy-MM-dd")
                : $"{businessDate:yyyy-MM-dd} / 自動作成待ち",
            hasBusinessDay = hasValidBusinessDate,
            openSlipCount = slips.Count(x => x.Status == "open"),
            checkedOutSlipCount = slips.Count(x => x.Status == "checked_out"),
            slips = slips.Select(slip => new
            {
                id = slip.SlipId,
                tableDisplay = slip.TableDisplayName,
                openedTime = StoreBusinessTime.FormatStoreTime(slip.OpenedAt),
                status = slip.Status,
                statusDisplay = ToSlipStatusDisplay(slip.Status),
                statusBadgeClass = ToSlipStatusBadgeClass(slip.Status),
                customerNames = string.IsNullOrWhiteSpace(slip.CustomerNames) ? "客名なし" : slip.CustomerNames,
                castNames = string.IsNullOrWhiteSpace(slip.CastNames) ? "指名なし" : slip.CastNames,
                memo = string.IsNullOrWhiteSpace(slip.Memo) ? "-" : slip.Memo,
                accountingAmount = slip.AccountingAmount,
                karaokeQuantity = slip.KaraokeQuantity
            })
        });
    }

    public async Task<IActionResult> OnPostCreateSlipAsync(CancellationToken cancellationToken)
    {
        if (!SlipsEnabled)
        {
            return NotFound();
        }

        NormalizeCreateSlipInput();
        await LoadAsync(cancellationToken, includeAttendanceCasts: CreateSlipInput.CastNominations.Count > 0);
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
        await LoadAsync(cancellationToken, includeAttendanceCasts: false);
        SetDefaultCreateSlipInput();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveKaraokeAsync(CancellationToken cancellationToken)
    {
        var isAsyncRequest = IsAsyncKaraokeRequest();
        if (!SlipsEnabled)
        {
            return isAsyncRequest ? KaraokeJsonError("カラオケ入力は利用できません。", 404) : NotFound();
        }

        await LoadAsync(cancellationToken, includeAttendanceCasts: false);
        if (CurrentBusinessDay is null)
        {
            ModelState.AddModelError(string.Empty, "営業中の営業日がありません。");
            if (isAsyncRequest)
            {
                return KaraokeJsonError(GetFirstModelError("営業中の営業日がありません。"));
            }

            SetDefaultCreateSlipInput();
            return Page();
        }

        RemoveModelStateEntries(nameof(CreateSlipInput));
        NormalizeKaraokeLines();
        ValidateKaraokeLines();
        if (!ModelState.IsValid)
        {
            if (isAsyncRequest)
            {
                return KaraokeJsonError(GetFirstModelError("カラオケ回数を保存できませんでした。"));
            }

            SetDefaultCreateSlipInput();
            return Page();
        }

        var result = await _slipRepository.SaveKaraokeLinesAsync(
            CurrentBusinessDay.BusinessDayId,
            KaraokeLines,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "カラオケ回数を保存できませんでした。");
            if (isAsyncRequest)
            {
                return KaraokeJsonError(GetFirstModelError("カラオケ回数を保存できませんでした。"));
            }

            SetDefaultCreateSlipInput();
            return Page();
        }

        if (isAsyncRequest)
        {
            ModelState.Clear();
            return new JsonResult(new { succeeded = true, savedCount = KaraokeLines.Count });
        }

        TempData["SuccessMessage"] = "カラオケ回数を保存しました。";
        ModelState.Clear();
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool includeAttendanceCasts)
    {
        var storeContextTask = _slipRepository.GetStoreContextAsync(cancellationToken);
        var currentBusinessDayTask = _businessDayRepository.GetCurrentAsync(cancellationToken);
        var tablesTask = _slipRepository.GetTablesAsync(cancellationToken);
        var nominationOptionsTask = _nominationBackRepository.GetSettingsAsync(cancellationToken);

        CurrentBusinessDate = _storeClock.GetCurrentBusinessDate();
        TimeOptions = _storeClock.BuildTimeOptions(5);

        await Task.WhenAll(storeContextTask, currentBusinessDayTask, tablesTask, nominationOptionsTask);

        StoreContext = await storeContextTask;
        CurrentBusinessDay = await currentBusinessDayTask;
        Tables = await tablesTask;
        NominationOptions = (await nominationOptionsTask)
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToList();

        if (CurrentBusinessDay is null)
        {
            AttendanceCasts = [];
            Slips = [];
            return;
        }

        if (includeAttendanceCasts)
        {
            var attendanceCastsTask = _orderRepository.GetAttendanceCastsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
            AttendanceCasts = await attendanceCastsTask;
        }
        else
        {
            AttendanceCasts = [];
        }

        Slips = [];
    }

    private void SetDefaultCreateSlipInput()
    {
        CreateSlipInput.OpenedTime ??= _storeClock.FloorToMinuteStep(_storeClock.GetStoreNow(), 5).ToString("HH:mm");
        SetBusinessDayInput();

        if (CreateSlipInput.CustomerLabels.Count == 0)
        {
            CreateSlipInput.CustomerLabels.Add(null);
        }

        ComposeOpenedAt();
    }

    private void SetBusinessDayInput()
    {
        CreateSlipInput.BusinessDate = GetSafeBusinessDate(CurrentBusinessDay);
        CreateSlipInput.BusinessDayId = HasValidBusinessDate(CurrentBusinessDay)
            ? CurrentBusinessDay?.BusinessDayId
            : null;
    }

    private DateOnly GetSafeBusinessDate(StoreBusinessDay? businessDay)
    {
        return HasValidBusinessDate(businessDay)
            ? businessDay!.BusinessDate
            : CurrentBusinessDate == default
                ? _storeClock.GetCurrentBusinessDate()
                : CurrentBusinessDate;
    }

    private static bool HasValidBusinessDate(StoreBusinessDay? businessDay)
    {
        return businessDay is { BusinessDayId: > 0, BusinessDate: var businessDate } && businessDate != default;
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
                NominationKind = ResolveNominationKind(x),
                NominationPrice = x.NominationPrice,
                CastId = x.CastId,
                CastName = string.IsNullOrWhiteSpace(x.CastName) ? null : x.CastName.Trim()
            })
            .Where(HasNominationCast)
            .ToList();
    }

    private static bool HasNominationCast(CastNominationInputModel nomination)
    {
        return nomination.CastId is not null || !string.IsNullOrWhiteSpace(nomination.CastName);
    }

    private static string? ResolveNominationKind(CastNominationInputModel nomination)
    {
        if (!string.IsNullOrWhiteSpace(nomination.NominationKind))
        {
            return nomination.NominationKind.Trim();
        }

        return null;
    }

    private void ComposeOpenedAt()
    {
        if (string.IsNullOrWhiteSpace(CreateSlipInput.OpenedTime) ||
            CreateSlipInput.BusinessDate is null ||
            !TimeOnly.TryParse(CreateSlipInput.OpenedTime, out var openedTime))
        {
            CreateSlipInput.OpenedAt = null;
            return;
        }

        CreateSlipInput.OpenedAt = _storeClock.ComposeBusinessDateTime(CreateSlipInput.BusinessDate.Value, openedTime);
    }

    private void ValidateCreateSlip()
    {
        if (StoreContext is null)
        {
            ModelState.AddModelError(string.Empty, "店舗設定を取得できません。Supabase設定とStoreDepartmentIdを確認してください。");
        }

        if (IsPreviousBusinessDayOpen)
        {
            ModelState.AddModelError(string.Empty, $"前回営業日 {CurrentBusinessDay?.BusinessDate:yyyy-MM-dd} の締め作業が未完了です。締め作業を完了してから新しい営業入力を開始してください。");
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
        var allowedNominationKinds = NominationOptions
            .Select(x => x.NominationKind)
            .ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < CreateSlipInput.CastNominations.Count; i++)
        {
            var nomination = CreateSlipInput.CastNominations[i];
            if (nomination.CastId is not null && string.IsNullOrWhiteSpace(nomination.CastName))
            {
                nomination.CastName = AttendanceCasts.FirstOrDefault(x => x.CastId == nomination.CastId.Value)?.SearchDisplayName;
            }

            if (allowedNominationKinds.Count == 0)
            {
                ModelState.AddModelError($"CreateSlipInput.CastNominations[{i}].NominationKind", "指名種別マスタを登録してください。");
            }
            else if (string.IsNullOrWhiteSpace(nomination.NominationKind) || !allowedNominationKinds.Contains(nomination.NominationKind))
            {
                ModelState.AddModelError($"CreateSlipInput.CastNominations[{i}].NominationKind", "指名区分を選択してください。");
            }

            if (!IsValidNominationPrice(nomination.NominationPrice))
            {
                ModelState.AddModelError($"CreateSlipInput.CastNominations[{i}].NominationPrice", "指名料金を選択してください。");
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

    private void NormalizeKaraokeLines()
    {
        KaraokeLines = KaraokeLines
            .Where(x => x.SlipId > 0)
            .GroupBy(x => x.SlipId)
            .Select(x => new KaraokeQuantityInputModel
            {
                SlipId = x.Key,
                Quantity = x.Last().Quantity
            })
            .ToList();
    }

    private void ValidateKaraokeLines()
    {
        if (KaraokeLines.Count == 0)
        {
            ModelState.AddModelError(nameof(KaraokeLines), "保存するカラオケ回数がありません。");
            return;
        }

        for (var i = 0; i < KaraokeLines.Count; i++)
        {
            var line = KaraokeLines[i];
            if (line.SlipId <= 0)
            {
                ModelState.AddModelError($"KaraokeLines[{i}].SlipId", "営業中の卓を確認してください。");
            }

            if (line.Quantity < 0 || line.Quantity > 999 || line.Quantity != decimal.Truncate(line.Quantity))
            {
                ModelState.AddModelError($"KaraokeLines[{i}].Quantity", "カラオケ回数を確認してください。");
            }
        }
    }

    private void RemoveModelStateEntries(string prefix)
    {
        var keys = ModelState.Keys
            .Where(key => string.Equals(key, prefix, StringComparison.Ordinal) ||
                          key.StartsWith($"{prefix}.", StringComparison.Ordinal) ||
                          key.StartsWith($"{prefix}[", StringComparison.Ordinal))
            .ToArray();

        foreach (var key in keys)
        {
            ModelState.Remove(key);
        }
    }

    private bool IsAsyncKaraokeRequest()
    {
        return string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }

    private IActionResult KaraokeJsonError(string message, int statusCode = 400)
    {
        return new JsonResult(new { succeeded = false, message }) { StatusCode = statusCode };
    }

    private string GetFirstModelError(string fallback)
    {
        return ModelState.Values
            .SelectMany(x => x.Errors)
            .Select(x => x.ErrorMessage)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? fallback;
    }

    private static bool IsValidNominationPrice(decimal price)
    {
        return price is >= 1000 and <= 20000 && price % 1000 == 0;
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
