using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Attendance;

public interface IAttendanceApplicationService
{
    Task<AttendancePageState> LoadAsync(
        ClosingAttendanceInputModel input,
        bool preserveInput,
        CancellationToken ct);

    IReadOnlyList<AttendanceValidationError> Validate(
        AttendancePageState state,
        ClosingAttendanceInputModel input);

    Task<Result<AttendanceSaveOutput>> SaveAsync(
        ClosingAttendanceInputModel input,
        CancellationToken ct);
}

public sealed record AttendancePageState(
    StoreContext? StoreContext,
    StoreBusinessDay? BusinessDay,
    DateOnly BusinessDate,
    IReadOnlyList<AttendanceTimeOption> ClockInTimeOptions,
    IReadOnlyList<AttendanceTimeOption> ClockOutTimeOptions,
    string DefaultClockInTime,
    string DefaultClockOutTime,
    ClosingAttendanceInputModel Input,
    IReadOnlyList<PageLoadIssue> LoadIssues,
    DateTimeOffset? LastUpdatedAt);

public sealed record AttendanceSaveOutput(
    StoreBusinessDay BusinessDay,
    int SelectedCount,
    int SavedClockOutCount);
