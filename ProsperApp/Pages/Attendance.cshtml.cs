using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Features.Attendance;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public class AttendanceModel(
    IFeatureGate featureGate,
    IAttendanceApplicationService attendanceApplicationService) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IAttendanceApplicationService _attendanceApplicationService = attendanceApplicationService;

    public ClosingAttendanceInputModel Input { get; private set; } = new();

    public StoreBusinessDay? CurrentBusinessDay => InitialSnapshot?.BusinessDay;

    public StoreContext? StoreContext { get; private set; }

    public DateOnly CurrentBusinessDate { get; private set; }

    public IReadOnlyList<AttendanceTimeOption> ClockInTimeOptions { get; private set; } = [];

    public IReadOnlyList<AttendanceTimeOption> ClockOutTimeOptions { get; private set; } = [];

    public string DefaultClockInTime { get; private set; } = "19:00";

    public string DefaultClockOutTime { get; private set; } = "01:00";

    public AttendanceEditorSnapshot? InitialSnapshot { get; private set; }

    public IReadOnlyList<PageLoadIssue> LoadIssues { get; private set; } = [];

    public bool IsClosingContext => Request.Path.StartsWithSegments(
        "/Closing/Attendance",
        StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var state = await _attendanceApplicationService.LoadShellAsync(cancellationToken);
        StoreContext = state.StoreContext;
        CurrentBusinessDate = state.BusinessDate;
        ClockInTimeOptions = state.ClockInTimeOptions;
        ClockOutTimeOptions = state.ClockOutTimeOptions;
        DefaultClockInTime = state.DefaultClockInTime;
        DefaultClockOutTime = state.DefaultClockOutTime;
        Input = state.Input;
        InitialSnapshot = state.InitialSnapshot;
        LoadIssues = state.LoadIssues;
        return Page();
    }

    public async Task<IActionResult> OnGetCurrentAsync(
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
                status = "error",
                message = result.ErrorMessage ?? "現在の勤怠入力を取得できませんでした。"
            })
            {
                StatusCode = ToStatusCode(result.FailureKind)
            };
        }

        return new JsonResult(result.Value);
    }

    public async Task<IActionResult> OnPostSaveV2Async(
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
                status = "error",
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
                _ => StatusCodes.Status200OK
            }
        };
    }

    private static int ToStatusCode(ResultFailureKind? failureKind) => failureKind switch
    {
        ResultFailureKind.InvalidInput => StatusCodes.Status400BadRequest,
        ResultFailureKind.Conflict => StatusCodes.Status409Conflict,
        ResultFailureKind.NotConfigured => StatusCodes.Status503ServiceUnavailable,
        ResultFailureKind.PermissionDenied => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status503ServiceUnavailable
    };
}
