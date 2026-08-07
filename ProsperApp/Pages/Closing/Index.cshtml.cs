using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Closing;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class ClosingModel(
    IFeatureGate featureGate,
    IClosingApplicationService closingApplicationService,
    IAdminModeService adminModeService,
    IDailyReportApplicationService dailyReportApplicationService,
    ILocalSettingsProvider localSettingsProvider) : PageModel
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IClosingApplicationService _closingApplicationService = closingApplicationService;
    private readonly IAdminModeService _adminModeService = adminModeService;
    private readonly IDailyReportApplicationService _dailyReportApplicationService = dailyReportApplicationService;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;

    [BindProperty(SupportsGet = true)]
    public long? ReportBusinessDayId { get; set; }

    public bool ReceiptsEnabled => _featureGate.IsEnabled(FeatureNames.Receipts);

    public bool IsAdminMode => _adminModeService.IsEnabled;

    public long DepartmentId => _localSettingsProvider.GetCurrent().StoreDepartmentId;

    public IActionResult OnGet()
    {
        return _featureGate.IsEnabled(FeatureNames.Closing) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnGetDashboardAsync(
        string? knownCastMasterRevision,
        CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var result = await _closingApplicationService.GetDashboardAsync(
            knownCastMasterRevision,
            cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                failureKind = result.FailureKind?.ToString(),
                errorMessage = result.ErrorMessage
            });
        }

        var dashboard = result.Value;
        return new JsonResult(new
        {
            succeeded = true,
            dashboard.DepartmentId,
            dashboard.HasBusinessDay,
            dashboard.BusinessDayId,
            dashboard.BusinessDayRevision,
            dashboard.BusinessDate,
            dashboard.Memo,
            dashboard.OpenSlipCount,
            dashboard.DrinkDeliveryAmount,
            dashboard.IsDrinkDeliveryAmountEntered,
            dashboard.AttendanceCount,
            dashboard.MissingClockOutCount,
            dashboard.CastSalesRequiredSlipCount,
            dashboard.CastSalesCompletedSlipCount,
            dashboard.CastSalesMissingSlipCount,
            dashboard.DrinkBackRequiredCastCount,
            dashboard.DrinkBackCompletedCastCount,
            dashboard.DrinkBackMissingCastCount,
            dashboard.DrinkBackTotalAmount,
            dashboard.DrinkBackEditor,
            dashboard.CastMasterRevision,
            dashboard.ActiveCasts,
            dashboard.PendingReceiptCount,
            dashboard.CanClose,
            dashboard.BlockReasons,
            dashboard.CheckedAt
        });
    }

    public async Task<IActionResult> OnPostCloseV2Async(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        CloseBusinessDayRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CloseBusinessDayRequest>(
                Request.Body,
                RequestJsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null ||
            !Guid.TryParse(request.OperationId, out var operationId) ||
            request.ExpectedBusinessDayId is null or <= 0 ||
            request.ExpectedBusinessDayRevision is null or < 0 ||
            request.Memo?.Length > 500)
        {
            return BadRequest(new
            {
                succeeded = false,
                status = "validation_error",
                operationId = request?.OperationId,
                message = "営業日情報または締めメモが正しくありません。"
            });
        }

        if (request.IgnoreClosingRequirements && !IsAdminMode)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                succeeded = false,
                status = "permission_denied",
                operationId = operationId.ToString("D"),
                message = "締め条件を無視するには管理者モードを有効にしてください。"
            });
        }

        var result = await _closingApplicationService.CloseAsync(
            new CurrentBusinessDayCloseMutation(
                operationId.ToString("D"),
                request.ExpectedBusinessDayId,
                request.ExpectedBusinessDayRevision,
                request.Memo,
                request.IgnoreClosingRequirements && IsAdminMode),
            cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                succeeded = false,
                status = "unavailable",
                operationId = operationId.ToString("D"),
                message = result.ErrorMessage ?? "営業日を締められませんでした。"
            });
        }

        var output = result.Value;
        var payload = new
        {
            succeeded = output.Status == "confirmed",
            output.OperationId,
            output.Status,
            output.Message,
            output.ClosedBusinessDayId,
            output.BusinessDate,
            output.ClosedAt,
            output.ReportBusinessDayId,
            output.Dashboard
        };
        return output.Status switch
        {
            "confirmed" => new JsonResult(payload),
            "conflict" => StatusCode(StatusCodes.Status409Conflict, payload),
            _ => StatusCode(StatusCodes.Status400BadRequest, payload)
        };
    }

    public async Task<IActionResult> OnGetDailyReportAsync(
        long? businessDayId,
        CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var result = await _dailyReportApplicationService.LoadAsync(
            businessDayId ?? ReportBusinessDayId,
            cancellationToken);
        if (!result.Succeeded)
        {
            var statusCode = result.FailureKind switch
            {
                ResultFailureKind.InvalidInput => StatusCodes.Status400BadRequest,
                ResultFailureKind.NotFound => StatusCodes.Status404NotFound,
                ResultFailureKind.PermissionDenied => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status503ServiceUnavailable
            };
            return StatusCode(statusCode, new
            {
                succeeded = false,
                failureKind = result.FailureKind?.ToString(),
                errorMessage = result.ErrorMessage
            });
        }

        return new JsonResult(result.Value);
    }

    private sealed record CloseBusinessDayRequest(
        string? OperationId,
        long? ExpectedBusinessDayId,
        long? ExpectedBusinessDayRevision,
        string? Memo,
        bool IgnoreClosingRequirements);
}
