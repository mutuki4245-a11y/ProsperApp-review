using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProsperApp.Features.BusinessHome;
using ProsperApp.Features.Shared;
using ProsperApp.Options;
using ProsperApp.Services;
using System.Text.Json;

namespace ProsperApp.Pages;

public class IndexModel(
    IFeatureGate featureGate,
    IBusinessHomeApplicationService businessHomeApplicationService,
    ILocalSettingsProvider localSettingsProvider,
    IOptions<ReceiptPrinterOptions> receiptPrinterOptions,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IBusinessHomeApplicationService _businessHomeApplicationService = businessHomeApplicationService;
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

    public IReadOnlyList<CheckoutPaymentMethod> PaymentMethods { get; private set; } = [];

    public string? PaymentMethodsLoadError { get; private set; }

    public IReadOnlyList<PageLoadIssue> LoadIssues { get; private set; } = [];

    public DateTimeOffset? LastUpdatedAt { get; private set; }

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
            : 48,
        logoImageUrl = _receiptPrinterOptions.LogoImageUrl,
        paperWidthMillimeters = _receiptPrinterOptions.PaperWidthMillimeters is 58 or 80
            ? _receiptPrinterOptions.PaperWidthMillimeters
            : 80,
        logoMaxWidthDots = _receiptPrinterOptions.LogoMaxWidthDots > 0
            ? _receiptPrinterOptions.LogoMaxWidthDots
            : 384,
        logoMaxHeightDots = _receiptPrinterOptions.LogoMaxHeightDots > 0
            ? _receiptPrinterOptions.LogoMaxHeightDots
            : 160,
        logoThreshold = _receiptPrinterOptions.LogoThreshold is >= 0 and <= 255
            ? _receiptPrinterOptions.LogoThreshold
            : 180
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

        var result = await _businessHomeApplicationService.GetAttendanceCastsAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                failureKind = result.FailureKind?.ToString(),
                message = result.ErrorMessage ?? "出勤キャストを取得できませんでした。"
            });
        }

        Response.Headers["X-Last-Updated"] = DateTimeOffset.UtcNow.ToString("O");
        return new JsonResult(result.Value.Select(cast => new
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

        var result = await _businessHomeApplicationService.GetSnapshotAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                message = result.ErrorMessage ?? "営業中の伝票を取得できませんでした。"
            });
        }

        var state = result.Value;
        if (state.BusinessDay is null)
        {
            return new JsonResult(new
            {
                succeeded = true,
                businessDayId = (long?)null,
                businessDate = state.BusinessDate.ToString("yyyy-MM-dd"),
                businessDateDisplay = $"{state.BusinessDate:yyyy-MM-dd} / 自動作成待ち",
                hasBusinessDay = false,
                openSlipCount = 0,
                checkedOutSlipCount = 0,
                estimatedSalesAmount = 0,
                slips = Array.Empty<object>()
            });
        }

        return new JsonResult(new { succeeded = true, snapshot = state.Snapshot });
    }

    public async Task<IActionResult> OnPostFlushBusinessHomeChangesAsync(CancellationToken cancellationToken)
    {
        if (!SlipsEnabled)
        {
            return NotFound();
        }

        var input = await ReadCheckoutRequestAsync<BusinessHomeChangeFlushInput>(cancellationToken);
        if (input is null)
        {
            return BadRequest(new { succeeded = false, message = "保存内容を確認してください。" });
        }

        var result = await _businessHomeApplicationService.FlushAsync(input, cancellationToken);
        if (!result.Succeeded)
        {
            var error = new
            {
                succeeded = false,
                batchId = input.BatchId,
                message = result.ErrorMessage ?? "営業中の変更を保存できませんでした。"
            };
            return result.FailureKind is ResultFailureKind.Unavailable or ResultFailureKind.NotConfigured
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, error)
                : BadRequest(error);
        }

        var output = result.Value;
        return new JsonResult(new
        {
            succeeded = true,
            batchId = output.BatchId,
            snapshot = output.Snapshot,
            operationResults = output.OperationResults,
            karaokeResults = output.KaraokeResults
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

        var result = await _businessHomeApplicationService.CreateSlipAsync(CreateSlipInput, cancellationToken);
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

        var result = await _businessHomeApplicationService.IssueCheckoutStatementAsync(
            request.SlipId,
            request.ClosedAt.Value,
            cancellationToken);
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

        var result = await _businessHomeApplicationService.GetCheckoutStatementPrintDataAsync(
            request.SlipId,
            cancellationToken);
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

        var result = await _businessHomeApplicationService.ReleaseCheckoutReadyAsync(
            request.SlipId,
            cancellationToken);
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

        var result = await _businessHomeApplicationService.ConfirmCheckoutAsync(
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

        var result = await _businessHomeApplicationService.GetCheckoutReceiptPrintDataAsync(
            request.SlipId,
            cancellationToken);
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

        var result = await _businessHomeApplicationService.CancelCheckoutAsync(
            request.SlipId,
            cancellationToken);
        return result.Succeeded && result.CheckoutId is { } checkoutId
            ? new JsonResult(new { succeeded = true, slipId = request.SlipId, checkoutId })
            : CheckoutJsonError(result.ErrorMessage ?? "会計を取消できませんでした。");
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool includeAttendanceCasts)
    {
        var state = await _businessHomeApplicationService.LoadPageAsync(
            OrdersEnabled,
            CheckoutEnabled,
            includeAttendanceCasts,
            cancellationToken);

        StoreContext = state.StoreContext;
        CurrentBusinessDay = state.BusinessDay;
        CurrentBusinessDate = state.BusinessDate;
        Tables = state.Tables;
        NominationOptions = state.NominationOptions;
        OrderItems = state.OrderItems;
        AttendanceCasts = state.AttendanceCasts;
        PaymentMethods = state.PaymentMethods;
        LoadIssues = state.LoadIssues;
        LastUpdatedAt = state.LastUpdatedAt;
        PaymentMethodsLoadError = state.LoadIssues
            .FirstOrDefault(issue => string.Equals(issue.Area, "決済方法", StringComparison.Ordinal))
            ?.Message;
        TimeOptions = _storeClock.BuildTimeOptions(5);
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
