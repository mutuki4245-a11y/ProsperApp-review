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
    private AttendancePageState? _pageState;

    [BindProperty]
    public ClosingAttendanceInputModel Input { get; set; } = new();

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public StoreContext? StoreContext { get; private set; }

    public DateOnly CurrentBusinessDate { get; private set; }

    public IReadOnlyList<AttendanceTimeOption> ClockInTimeOptions { get; private set; } = [];

    public IReadOnlyList<AttendanceTimeOption> ClockOutTimeOptions { get; private set; } = [];

    public string DefaultClockInTime { get; private set; } = "19:00";

    public string DefaultClockOutTime { get; private set; } = string.Empty;

    public string? SuccessMessage { get; private set; }

    public string? CastLoadErrorMessage { get; private set; }

    public IReadOnlyList<PageLoadIssue> LoadIssues { get; private set; } = [];

    public DateTimeOffset? LastUpdatedAt { get; private set; }

    public bool IsClosingContext => Request.Path.StartsWithSegments("/Closing/Attendance", StringComparison.OrdinalIgnoreCase);

    public string WorkflowLabel => IsClosingContext ? "締め作業" : "営業中";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        SuccessMessage = TempData["SuccessMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Closing))
        {
            return NotFound();
        }

        var mergeResult = AttendanceEditor.MergeSelectedEntries(Input);
        if (!mergeResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, mergeResult.ErrorMessage ?? "勤怠入力を読み取れませんでした。");
        }
        await LoadAsync(cancellationToken, preserveInput: true);
        if (LoadIssues.Count > 0 || _pageState is null)
        {
            ModelState.AddModelError(string.Empty, "必要な情報を取得できないため勤怠を保存できません。再試行してください。");
            return Page();
        }

        AddAttendanceErrors(_attendanceApplicationService.Validate(_pageState, Input));
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var saveResult = await _attendanceApplicationService.SaveAsync(Input, cancellationToken);
        if (!saveResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, saveResult.ErrorMessage ?? "勤怠入力を保存できませんでした。");
            await LoadAsync(cancellationToken, preserveInput: true);
            return Page();
        }

        TempData["SuccessMessage"] =
            $"勤怠入力を保存しました。出勤 {saveResult.Value.SelectedCount}名 / 退勤 {saveResult.Value.SavedClockOutCount}名";
        return IsClosingContext
            ? RedirectToPage("/Closing/Index")
            : RedirectToCurrentPath();
    }

    private IActionResult RedirectToCurrentPath()
    {
        return LocalRedirect(Request.PathBase.Add(Request.Path).Value ?? "/Attendance");
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool preserveInput = false)
    {
        _pageState = await _attendanceApplicationService.LoadAsync(Input, preserveInput, cancellationToken);
        StoreContext = _pageState.StoreContext;
        CurrentBusinessDay = _pageState.BusinessDay;
        CurrentBusinessDate = _pageState.BusinessDate;
        ClockInTimeOptions = _pageState.ClockInTimeOptions;
        ClockOutTimeOptions = _pageState.ClockOutTimeOptions;
        DefaultClockInTime = _pageState.DefaultClockInTime;
        DefaultClockOutTime = _pageState.DefaultClockOutTime;
        Input = _pageState.Input;
        LoadIssues = _pageState.LoadIssues;
        LastUpdatedAt = _pageState.LastUpdatedAt;
        CastLoadErrorMessage = _pageState.LoadIssues
            .FirstOrDefault(issue => string.Equals(issue.Area, "キャスト", StringComparison.Ordinal))
            ?.Message;
    }

    private void AddAttendanceErrors(IEnumerable<AttendanceValidationError> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(error.Field, error.Message);
        }
    }
}
