using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Attendance;
using ProsperApp.Features.Closing;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class ClosingModel(
    IFeatureGate featureGate,
    IClosingApplicationService closingApplicationService,
    IAttendanceApplicationService attendanceApplicationService,
    IBusinessDayRepository businessDayRepository,
    ICastSalesAdjustmentRepository castSalesAdjustmentRepository,
    IDrinkBackRepository drinkBackRepository,
    IAdminModeService adminModeService,
    IDailyReportApplicationService dailyReportApplicationService,
    ILocalSettingsProvider localSettingsProvider,
    IStoreClock storeClock) : PageModel
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IClosingApplicationService _closingApplicationService = closingApplicationService;
    private readonly IAttendanceApplicationService _attendanceApplicationService = attendanceApplicationService;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly ICastSalesAdjustmentRepository _castSalesAdjustmentRepository = castSalesAdjustmentRepository;
    private readonly IDrinkBackRepository _drinkBackRepository = drinkBackRepository;
    private readonly IAdminModeService _adminModeService = adminModeService;
    private readonly IDailyReportApplicationService _dailyReportApplicationService = dailyReportApplicationService;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty(SupportsGet = true)]
    public long? ReportBusinessDayId { get; set; }

    public bool ReceiptsEnabled => _featureGate.IsEnabled(FeatureNames.Receipts);

    public bool IsAdminMode => _adminModeService.IsEnabled;

    public long DepartmentId => _localSettingsProvider.GetCurrent().StoreDepartmentId;

    public DateOnly CurrentBusinessDate => _storeClock.GetCurrentBusinessDate();

    public AttendanceModalViewModel? AttendanceModal { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadAttendanceModalAsync(cancellationToken);
        return Page();
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
            return StatusCode(ToStatusCode(result.FailureKind), new
            {
                succeeded = false,
                status = ToSaveStatus(result.FailureKind),
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
            "validation_error" => StatusCode(StatusCodes.Status400BadRequest, payload),
            "permission_denied" => StatusCode(StatusCodes.Status403Forbidden, payload),
            "stale_work_item" => StatusCode(StatusCodes.Status409Conflict, payload),
            "unavailable" => StatusCode(StatusCodes.Status503ServiceUnavailable, payload),
            _ => StatusCode(StatusCodes.Status400BadRequest, payload)
        };
    }

    public async Task<IActionResult> OnGetAttendanceCurrentAsync(
        long? knownBusinessDayId,
        long? knownBusinessDayRevision,
        CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var result = await _attendanceApplicationService.ReadCurrentAsync(
            knownBusinessDayId,
            knownBusinessDayRevision,
            cancellationToken);
        if (!result.Succeeded)
        {
            return new JsonResult(new
            {
                status = ToSaveStatus(result.FailureKind),
                message = result.ErrorMessage ?? "現在の勤怠入力を取得できませんでした。"
            })
            {
                StatusCode = ToStatusCode(result.FailureKind)
            };
        }

        return new JsonResult(result.Value);
    }

    public async Task<IActionResult> OnPostAttendanceSaveAsync(
        [FromBody] ClosingAttendanceInputModel input,
        CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var result = await _attendanceApplicationService.SaveAsync(input, cancellationToken);
        if (!result.Succeeded)
        {
            return new JsonResult(new
            {
                status = ToSaveStatus(result.FailureKind),
                message = result.ErrorMessage ?? "勤怠入力を保存できませんでした。"
            })
            {
                StatusCode = ToStatusCode(result.FailureKind)
            };
        }

        var output = result.Value;
        return new JsonResult(output)
        {
            StatusCode = output.Status switch
            {
                "conflict" => StatusCodes.Status409Conflict,
                "validation_error" => StatusCodes.Status400BadRequest,
                "permission_denied" => StatusCodes.Status403Forbidden,
                "stale_work_item" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status200OK
            }
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

    private async Task LoadAttendanceModalAsync(CancellationToken cancellationToken)
    {
        var state = await _attendanceApplicationService.LoadShellAsync(cancellationToken);
        AttendanceModal = new AttendanceModalViewModel(
            state,
            Url.Page("/Closing/Index", "AttendanceCurrent") ?? string.Empty,
            Url.Page("/Closing/Index", "AttendanceSave") ?? string.Empty,
            IsClosingContext: true);
    }

    private static int ToStatusCode(ResultFailureKind? failureKind) => failureKind switch
    {
        ResultFailureKind.InvalidInput => StatusCodes.Status400BadRequest,
        ResultFailureKind.Conflict => StatusCodes.Status409Conflict,
        ResultFailureKind.NotConfigured => StatusCodes.Status503ServiceUnavailable,
        ResultFailureKind.PermissionDenied => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static string ToSaveStatus(ResultFailureKind? failureKind) => failureKind switch
    {
        ResultFailureKind.InvalidInput => "validation_error",
        ResultFailureKind.Conflict => "conflict",
        ResultFailureKind.PermissionDenied => "permission_denied",
        _ => "unavailable"
    };

    private sealed record CloseBusinessDayRequest(
        string? OperationId,
        long? ExpectedBusinessDayId,
        long? ExpectedBusinessDayRevision,
        string? Memo,
        bool IgnoreClosingRequirements);
}
