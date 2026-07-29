using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProsperApp.Models;
using ProsperApp.Options;
using ProsperApp.Services;
using System.Text.Json;

namespace ProsperApp.Pages;

public class IndexModel(
    IFeatureGate featureGate,
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreOrderRepository orderRepository,
    INominationBackAdminRepository nominationBackRepository,
    ICheckoutRepository checkoutRepository,
    ILocalSettingsProvider localSettingsProvider,
    IOptions<ReceiptPrinterOptions> receiptPrinterOptions,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly INominationBackAdminRepository _nominationBackRepository = nominationBackRepository;
    private readonly ICheckoutRepository _checkoutRepository = checkoutRepository;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly ReceiptPrinterOptions _receiptPrinterOptions = receiptPrinterOptions.Value;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty]
    public CreateSlipInputModel CreateSlipInput { get; set; } = new();

    public StoreBusinessDay? CurrentBusinessDay { get; set; }

    public DateOnly CurrentBusinessDate { get; private set; }

    public StoreContext? StoreContext { get; set; }

    public IReadOnlyList<StoreTableOption> Tables { get; set; } = [];

    public IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts { get; set; } = [];

    public IReadOnlyList<StoreOrderItemOption> OrderItems { get; set; } = [];

    public IReadOnlyList<NominationBackMasterItem> NominationOptions { get; set; } = [];

    public IReadOnlyList<string> TimeOptions { get; set; } = [];

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);
    public const string AttendanceRequiredMessage = CreateSlipEditor.AttendanceRequiredMessage;

    public bool ShowCreateSlipModal { get; private set; }

    public string? SuccessMessage { get; private set; }

    public bool IsReceiptPrinterEnabled => _receiptPrinterOptions.Enabled;

    public object ReceiptPrinterBrowserOptions => new
    {
        host = string.IsNullOrWhiteSpace(_receiptPrinterOptions.BrowserWebSocketHost)
            ? "localhost"
            : _receiptPrinterOptions.BrowserWebSocketHost,
        codePage = _receiptPrinterOptions.BrowserCodePage,
        internationalCharacter = _receiptPrinterOptions.BrowserInternationalCharacter,
        storeName = StoreContext?.DepartmentName,
        lineWidth = _receiptPrinterOptions.LineWidth is >= 24 and <= 64
            ? _receiptPrinterOptions.LineWidth
            : 48
    };

    public string ReceiptPrinterBrowserSdkScriptUrl => _receiptPrinterOptions.BrowserSdkScriptUrl;

    public bool SlipsEnabled => _featureGate.IsEnabled(FeatureNames.Slips);

    public bool OrdersEnabled => _featureGate.IsEnabled(FeatureNames.Orders);

    public bool CheckoutEnabled => _featureGate.IsEnabled(FeatureNames.Checkout);

    public decimal EstimatedSalesAmount => 0;

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

    public bool CanCreateSlip => CanCreateSalesInput && AttendanceCasts.Count > 0;

    public string FormatBusinessTimeOption(string value)
    {
        return TimeOnly.TryParse(value, out var time)
            ? _storeClock.FormatBusinessTime(time)
            : value;
    }

    public bool ShouldShowCreateSlipButton => SlipsEnabled;

    public string? CreateSlipDisabledMessage => !SlipsEnabled
        ? null
        : !CanCreateSalesInput
        ? null
        : !CanCreateSlip
            ? AttendanceRequiredMessage
            : null;

    public bool CanMoveToClosing => HasCurrentBusinessDay;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (OrdersEnabled && _localSettingsProvider.GetCurrent().ScreenMode == "order-entry")
        {
            return RedirectToPage("/Orders/Index");
        }

        await LoadAsync(cancellationToken, includeAttendanceCasts: true);
        SetDefaultCreateSlipInput();
        SuccessMessage = TempData["SuccessMessage"] as string;
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
                estimatedSalesAmount = 0,
                slips = Array.Empty<object>()
            });
        }

        var snapshot = await _slipRepository.GetBusinessDaySnapshotAsync(currentBusinessDay.BusinessDayId, cancellationToken);
        if (!snapshot.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                message = snapshot.ErrorMessage ?? "営業中の伝票を取得できませんでした。"
            });
        }

        return new JsonResult(new { succeeded = true, snapshot = snapshot.Snapshot });
    }

    public async Task<IActionResult> OnPostFlushBusinessHomeChangesAsync(CancellationToken cancellationToken)
    {
        if (!SlipsEnabled)
        {
            return NotFound();
        }

        var input = await ReadCheckoutRequestAsync<BusinessHomeChangeFlushInput>(cancellationToken);
        if (input is null || input.Operations is null || input.KaraokeLines is null ||
            string.IsNullOrWhiteSpace(input.BatchId) || input.BatchId.Length > 100 ||
            input.Operations.Count > 100 || input.KaraokeLines.Count > 100 ||
            input.Operations.Any(operation => operation.SlipId <= 0 || string.IsNullOrWhiteSpace(operation.OperationId) ||
                operation.OperationType is not (
                    "add_customer" or "update_customer" or "leave_customer" or
                    "add_nomination" or "cancel_nomination" or
                    "add_adjustment" or "void_adjustment" or
                    "add_order" or "void_order")) ||
            input.KaraokeLines.Any(line => line.SlipId <= 0 || string.IsNullOrWhiteSpace(line.DraftId) ||
                line.Quantity < 0 || line.Quantity > 999 || line.Quantity != decimal.Truncate(line.Quantity)))
        {
            return BadRequest(new { succeeded = false, message = "保存内容を確認してください。" });
        }

        var currentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        if (currentBusinessDay is null)
        {
            return BadRequest(new { succeeded = false, message = "営業中の営業日がありません。" });
        }

        var result = await _slipRepository.FlushBusinessHomeChangesAsync(
            input,
            currentBusinessDay.BusinessDayId,
            cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, batchId = input.BatchId, message = result.ErrorMessage });
        }

        return new JsonResult(new
        {
            succeeded = true,
            batchId = input.BatchId,
            snapshot = result.Snapshot,
            operationResults = result.OperationResults,
            karaokeResults = result.KaraokeResults
        });
    }

    public async Task<IActionResult> OnPostCreateSlipAsync(CancellationToken cancellationToken)
    {
        if (!SlipsEnabled)
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken, includeAttendanceCasts: true);
        var edit = CreateSlipEditor.Prepare(CreateSlipInput, BuildCreateSlipEditContext(), _storeClock);
        CreateSlipInput = edit.Input;
        AddCreateSlipErrors(edit.Errors);

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
        await LoadAsync(cancellationToken, includeAttendanceCasts: true);
        SetDefaultCreateSlipInput();
        return Page();
    }


    public async Task<IActionResult> OnPostIssueCheckoutStatementAsync(CancellationToken cancellationToken)
    {
        if (!CheckoutEnabled)
        {
            return NotFound();
        }

        var request = await ReadCheckoutRequestAsync<CheckoutStatementIssueRequest>(cancellationToken);
        if (request is null || request.SlipId <= 0 || request.ClosedAt is null)
        {
            return CheckoutJsonError("会計伝票の対象と退店時刻を確認してください。");
        }

        var result = await _checkoutRepository.IssueCheckoutStatementAsync(request.SlipId, request.ClosedAt.Value, cancellationToken);
        return result.Succeeded && result.PrintData is { } printData && result.ReviewData is { } reviewData
            ? new JsonResult(new { succeeded = true, slipId = request.SlipId, printData, reviewData })
            : CheckoutJsonError(result.ErrorMessage ?? "会計伝票を出力できませんでした。");
    }

    public async Task<IActionResult> OnPostGetCheckoutStatementPrintDataAsync(CancellationToken cancellationToken)
    {
        if (!CheckoutEnabled)
        {
            return NotFound();
        }

        var request = await ReadCheckoutRequestAsync<CheckoutSlipRequest>(cancellationToken);
        if (request is null || request.SlipId <= 0)
        {
            return CheckoutJsonError("会計伝票の対象を確認してください。");
        }

        var result = await _checkoutRepository.GetCheckoutStatementPrintDataAsync(request.SlipId, cancellationToken);
        return result.Succeeded && result.PrintData is { } printData && result.ReviewData is { } reviewData
            ? new JsonResult(new { succeeded = true, slipId = request.SlipId, printData, reviewData })
            : CheckoutJsonError(result.ErrorMessage ?? "会計伝票を復旧できませんでした。");
    }

    public async Task<IActionResult> OnPostReleaseCheckoutReadyAsync(CancellationToken cancellationToken)
    {
        if (!CheckoutEnabled)
        {
            return NotFound();
        }

        var request = await ReadCheckoutRequestAsync<CheckoutSlipRequest>(cancellationToken);
        if (request is null || request.SlipId <= 0)
        {
            return CheckoutJsonError("会計伝票の対象を確認してください。");
        }

        var result = await _checkoutRepository.ReleaseCheckoutReadyAsync(request.SlipId, cancellationToken);
        return result.Succeeded
            ? new JsonResult(new { succeeded = true, slipId = request.SlipId })
            : CheckoutJsonError(result.ErrorMessage ?? "会計準備を解除できませんでした。");
    }

    public async Task<IActionResult> OnPostConfirmCheckoutAsync(CancellationToken cancellationToken)
    {
        if (!CheckoutEnabled)
        {
            return NotFound();
        }

        var request = await ReadCheckoutRequestAsync<CheckoutConfirmRequest>(cancellationToken);
        if (request is null || request.SlipId <= 0)
        {
            return CheckoutJsonError("会計伝票の対象を確認してください。");
        }

        var result = await _checkoutRepository.ConfirmCheckoutAsync(
            request.SlipId,
            request.Payments ?? [],
            request.ReceivedAmount,
            cancellationToken);
        return result.Succeeded && result.CheckoutId is { } checkoutId && result.ReceiptPrintData is { } printData
            ? new JsonResult(new
            {
                succeeded = true,
                slipId = request.SlipId,
                checkoutId,
                changeAmount = result.ChangeAmount,
                printData
            })
            : CheckoutJsonError(result.ErrorMessage ?? "会計を確定できませんでした。");
    }

    public async Task<IActionResult> OnPostGetCheckoutReceiptPrintDataAsync(CancellationToken cancellationToken)
    {
        if (!CheckoutEnabled)
        {
            return NotFound();
        }

        var request = await ReadCheckoutRequestAsync<CheckoutSlipRequest>(cancellationToken);
        if (request is null || request.SlipId <= 0)
        {
            return CheckoutJsonError("領収書の対象を確認してください。");
        }

        var result = await _checkoutRepository.GetCheckoutReceiptPrintDataAsync(request.SlipId, cancellationToken);
        return result.Succeeded && result.CheckoutId is { } checkoutId && result.PrintData is { } printData
            ? new JsonResult(new { succeeded = true, checkoutId, printData })
            : CheckoutJsonError(result.ErrorMessage ?? "領収書を取得できませんでした。");
    }

    public async Task<IActionResult> OnPostCancelCheckoutAsync(CancellationToken cancellationToken)
    {
        if (!CheckoutEnabled)
        {
            return NotFound();
        }

        var request = await ReadCheckoutRequestAsync<CheckoutSlipRequest>(cancellationToken);
        if (request is null || request.SlipId <= 0)
        {
            return CheckoutJsonError("会計取消の対象を確認してください。");
        }

        var result = await _checkoutRepository.CancelCheckoutAsync(request.SlipId, cancellationToken);
        return result.Succeeded && result.CheckoutId is { } checkoutId
            ? new JsonResult(new { succeeded = true, slipId = request.SlipId, checkoutId })
            : CheckoutJsonError(result.ErrorMessage ?? "会計を取消できませんでした。");
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool includeAttendanceCasts)
    {
        var storeContextTask = _slipRepository.GetStoreContextAsync(cancellationToken);
        var currentBusinessDayTask = _businessDayRepository.GetCurrentAsync(cancellationToken);
        var tablesTask = _slipRepository.GetTablesAsync(cancellationToken);
        var nominationOptionsTask = _nominationBackRepository.GetSettingsAsync(cancellationToken);
        var orderItemsTask = OrdersEnabled
            ? _orderRepository.GetItemsAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<StoreOrderItemOption>>([]);

        CurrentBusinessDate = _storeClock.GetCurrentBusinessDate();
        TimeOptions = _storeClock.BuildTimeOptions(5);

        await Task.WhenAll(storeContextTask, currentBusinessDayTask, tablesTask, nominationOptionsTask, orderItemsTask);

        StoreContext = await storeContextTask;
        CurrentBusinessDay = await currentBusinessDayTask;
        Tables = await tablesTask;
        NominationOptions = (await nominationOptionsTask)
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToList();
        OrderItems = (await orderItemsTask)
            .Where(x => x.IsStandard)
            .ToList();

        if (CurrentBusinessDay is null)
        {
            AttendanceCasts = [];
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

    }

    private void SetDefaultCreateSlipInput()
    {
        CreateSlipInput = CreateSlipEditor.ApplyDefaults(CreateSlipInput, CurrentBusinessDay, CurrentBusinessDate, _storeClock);
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

    private CreateSlipEditContext BuildCreateSlipEditContext()
    {
        return new CreateSlipEditContext(
            CurrentBusinessDay,
            CurrentBusinessDate,
            StoreContext,
            Tables,
            NominationOptions,
            AttendanceCasts,
            TimeOptions,
            IsPreviousBusinessDayOpen,
            CanCreateSalesInput);
    }

    private void AddCreateSlipErrors(IEnumerable<CreateSlipValidationError> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(error.Key, error.Message);
        }
    }


    private async Task<T?> ReadCheckoutRequestAsync<T>(CancellationToken cancellationToken)
    {
        if (!Request.HasJsonContentType())
        {
            return default;
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<T>(Request.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private IActionResult CheckoutJsonError(string message, int statusCode = 400) =>
        new JsonResult(new { succeeded = false, message }) { StatusCode = statusCode };

    private sealed record CheckoutSlipRequest(long SlipId);
    private sealed record CheckoutStatementIssueRequest(long SlipId, DateTimeOffset? ClosedAt);
    private sealed record CheckoutConfirmRequest(
        long SlipId,
        List<CheckoutPaymentInputModel>? Payments,
        decimal? ReceivedAmount);

    public static string ToSlipStatusDisplay(string status)
    {
        return status switch
        {
            "open" => "在席",
            "checkout_ready" => "会計準備中",
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
            "checkout_ready" => "text-bg-warning",
            "checked_out" => "text-bg-secondary",
            "cancelled" => "text-bg-danger",
            _ => "text-bg-secondary"
        };
    }
}
