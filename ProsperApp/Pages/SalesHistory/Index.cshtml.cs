using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.DailyReports;
using ProsperApp.Features.SalesHistory;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages.SalesHistory;

public sealed class IndexModel(
    IFeatureGate featureGate,
    ISalesHistoryApplicationService salesHistoryApplicationService,
    IDailyReportApplicationService dailyReportApplicationService,
    IStoreClock storeClock) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ISalesHistoryApplicationService _salesHistoryApplicationService = salesHistoryApplicationService;
    private readonly IDailyReportApplicationService _dailyReportApplicationService = dailyReportApplicationService;
    private readonly IStoreClock _storeClock = storeClock;

    [BindProperty(SupportsGet = true)]
    public long? BusinessDayId { get; set; }

    public IActionResult OnGet()
    {
        return IsEnabled() ? Page() : NotFound();
    }

    public async Task<IActionResult> OnGetHistoryPageAsync(
        DateOnly? fromBusinessDate,
        DateOnly? toBusinessDate,
        DateOnly? beforeBusinessDate,
        long? beforeBusinessDayId,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        var result = await _salesHistoryApplicationService.GetPageAsync(
            new SalesHistoryQuery(
                fromBusinessDate,
                toBusinessDate,
                beforeBusinessDate,
                beforeBusinessDayId),
            _storeClock.GetCurrentBusinessDate(),
            cancellationToken);
        return ToJsonResult(result);
    }

    public async Task<IActionResult> OnGetCorrectionEditorAsync(
        long businessDayId,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        var result = await _salesHistoryApplicationService.GetCorrectionEditorAsync(
            businessDayId,
            _storeClock.GetCurrentBusinessDate(),
            cancellationToken);
        return ToJsonResult(result);
    }

    public async Task<IActionResult> OnPostCorrectionSaveAsync(
        [FromBody] SalesHistoryCorrectionMutation? mutation,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        if (mutation is null)
        {
            return BadRequest(new
            {
                succeeded = false,
                status = "validation_error",
                errorMessage = "補正内容を確認してください。"
            });
        }

        var actorEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(actorEmail))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                succeeded = false,
                status = "permission_denied",
                errorMessage = "ログインユーザーを確認できません。再ログインしてください。"
            });
        }

        var result = await _salesHistoryApplicationService.SaveCorrectionAsync(
            mutation with
            {
                ActorEmail = actorEmail,
                CurrentBusinessDate = _storeClock.GetCurrentBusinessDate()
            },
            cancellationToken);
        if (!result.Succeeded)
        {
            return StatusCode(ToStatusCode(result.FailureKind), new
            {
                succeeded = false,
                status = ToSaveStatus(result.FailureKind),
                failureKind = result.FailureKind?.ToString(),
                errorMessage = result.ErrorMessage
            });
        }

        return new JsonResult(result.Value);
    }

    public async Task<IActionResult> OnGetDailyReportAsync(
        long? businessDayId,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        var result = await _dailyReportApplicationService.LoadAsync(
            businessDayId ?? BusinessDayId,
            cancellationToken);
        return ToJsonResult(result);
    }

    private bool IsEnabled() => _featureGate.IsEnabled(FeatureNames.SalesHistory);

    private IActionResult ToJsonResult<T>(Result<T> result)
    {
        if (result.Succeeded)
        {
            return new JsonResult(result.Value);
        }

        return StatusCode(ToStatusCode(result.FailureKind), new
        {
            succeeded = false,
            failureKind = result.FailureKind?.ToString(),
            errorMessage = result.ErrorMessage
        });
    }

    private static int ToStatusCode(ResultFailureKind? failureKind) => failureKind switch
    {
        ResultFailureKind.InvalidInput => StatusCodes.Status400BadRequest,
        ResultFailureKind.NotFound => StatusCodes.Status404NotFound,
        ResultFailureKind.Conflict => StatusCodes.Status409Conflict,
        ResultFailureKind.PermissionDenied => StatusCodes.Status403Forbidden,
        ResultFailureKind.NotConfigured => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static string ToSaveStatus(ResultFailureKind? failureKind) => failureKind switch
    {
        ResultFailureKind.InvalidInput => "validation_error",
        ResultFailureKind.Conflict => "conflict",
        ResultFailureKind.PermissionDenied => "permission_denied",
        _ => "unavailable"
    };
}
