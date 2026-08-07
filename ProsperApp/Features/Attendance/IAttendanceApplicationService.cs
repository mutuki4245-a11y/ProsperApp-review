using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Attendance;

public interface IAttendanceApplicationService
{
    Task<AttendancePageState> LoadShellAsync(CancellationToken ct);

    Task<Result<AttendanceEditorSnapshot>> ReadCurrentAsync(
        long? knownBusinessDayId,
        long? knownBusinessDayRevision,
        CancellationToken ct);

    Task<Result<AttendanceSaveOutput>> SaveAsync(
        ClosingAttendanceInputModel input,
        CancellationToken ct);
}

public sealed record AttendancePageState(
    StoreContext? StoreContext,
    DateOnly BusinessDate,
    IReadOnlyList<AttendanceTimeOption> ClockInTimeOptions,
    IReadOnlyList<AttendanceTimeOption> ClockOutTimeOptions,
    string DefaultClockInTime,
    string DefaultClockOutTime,
    ClosingAttendanceInputModel Input,
    AttendanceEditorSnapshot? InitialSnapshot,
    IReadOnlyList<PageLoadIssue> LoadIssues,
    bool WasFetched);

public sealed record AttendanceSaveOutput(
    string OperationId,
    string Status,
    string Message,
    AttendanceEditorSnapshot Snapshot,
    int SavedCount,
    int SavedClockOutCount,
    IReadOnlyList<AttendanceSaveRowResult> RowResults);
