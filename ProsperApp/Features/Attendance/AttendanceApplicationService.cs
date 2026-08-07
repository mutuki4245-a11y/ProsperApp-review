using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Features.Attendance;

public sealed class AttendanceApplicationService(
    IBusinessDayRepository businessDayRepository,
    IStoreClock storeClock) : IAttendanceApplicationService
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreClock _storeClock = storeClock;

    public async Task<AttendancePageState> LoadShellAsync(CancellationToken ct)
    {
        var shell = await _businessDayRepository.GetAttendanceShellAsync(ct);
        var issues = new List<PageLoadIssue>();
        var minuteStep = 15;
        StoreContext? context = null;
        IReadOnlyList<AttendanceRosterPerson> roster = [];
        AttendanceEditorSnapshot? initialSnapshot = null;
        var wasFetched = false;

        if (shell.Succeeded)
        {
            context = shell.Value.StoreContext;
            roster = shell.Value.Roster;
            initialSnapshot = shell.Value.InitialSnapshot;
            wasFetched = shell.Value.WasFetched;
            minuteStep = context.AttendanceMinuteStep;
        }
        else
        {
            issues.Add(new PageLoadIssue(
                "勤怠shell",
                shell.FailureKind ?? ResultFailureKind.Unavailable,
                shell.ErrorMessage ?? "勤怠入力の名簿を取得できませんでした。"));
        }

        var clockInOptions = AttendanceEditor.BuildTimeOptions(19, 0, minuteStep);
        var clockOutOptions = AttendanceEditor.BuildTimeOptions(24, 0, minuteStep);
        var defaultClockIn = AttendanceEditor.ResolveDefaultTime(clockInOptions, "19:00");
        var defaultClockOut = AttendanceEditor.ResolveDefaultTime(clockOutOptions, "01:00");
        var entries = roster
            .Where(person => person.IsActive && person.PersonId > 0)
            .Select(person => new BusinessDayAttendanceEntryInput
            {
                PersonType = AttendancePersonTypes.Normalize(person.PersonType),
                CastId = AttendancePersonTypes.Normalize(person.PersonType) == AttendancePersonTypes.Cast
                    ? person.PersonId
                    : 0,
                StaffId = AttendancePersonTypes.Normalize(person.PersonType) == AttendancePersonTypes.Staff
                    ? person.PersonId
                    : 0,
                DisplayName = person.DisplayName,
                DepartmentName = person.DepartmentName,
                ClockInTime = defaultClockIn,
                ClockOutTime = defaultClockOut
            })
            .ToList();

        return new AttendancePageState(
            context,
            _storeClock.GetCurrentBusinessDate(),
            clockInOptions,
            clockOutOptions,
            defaultClockIn,
            defaultClockOut,
            new ClosingAttendanceInputModel { Entries = entries },
            initialSnapshot,
            issues,
            wasFetched);
    }

    public Task<Result<AttendanceEditorSnapshot>> ReadCurrentAsync(
        long? knownBusinessDayId,
        long? knownBusinessDayRevision,
        CancellationToken ct) =>
        _businessDayRepository.GetCurrentAttendanceEditorAsync(
            knownBusinessDayId,
            knownBusinessDayRevision,
            ct);

    public async Task<Result<AttendanceSaveOutput>> SaveAsync(
        ClosingAttendanceInputModel input,
        CancellationToken ct)
    {
        var castEntries = input.Entries
            .Where(entry => AttendancePersonTypes.Normalize(entry.PersonType) == AttendancePersonTypes.Cast)
            .Where(entry => entry.CastId > 0 && (entry.IsSelected || entry.IsRegistered))
            .GroupBy(entry => entry.CastId)
            .Select(group => group.Last())
            .Select(entry => ToMutationEntry(entry.CastId, entry))
            .ToArray();
        var staffEntries = input.Entries
            .Where(entry => AttendancePersonTypes.Normalize(entry.PersonType) == AttendancePersonTypes.Staff)
            .Where(entry => entry.StaffId > 0 && (entry.IsSelected || entry.IsRegistered))
            .GroupBy(entry => entry.StaffId)
            .Select(group => group.Last())
            .Select(entry => ToMutationEntry(entry.StaffId, entry))
            .ToArray();

        var result = await _businessDayRepository.SaveCurrentAttendanceAsync(
            new CurrentBusinessDayAttendanceMutation(
                EnsureOperationId(input.OperationId),
                input.BusinessDayId,
                input.BusinessDayRevision,
                _storeClock.GetCurrentBusinessDate(),
                castEntries,
                staffEntries),
            ct);
        if (!result.Succeeded)
        {
            return Result<AttendanceSaveOutput>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "勤怠入力を保存できませんでした。");
        }

        var output = result.Value;
        return Result<AttendanceSaveOutput>.Success(new AttendanceSaveOutput(
            output.OperationId,
            output.Status,
            output.Message,
            output.Snapshot,
            output.SavedCount,
            output.SavedClockOutCount,
            output.RowResults));
    }

    private static CurrentBusinessDayAttendanceEntry ToMutationEntry(
        long personId,
        BusinessDayAttendanceEntryInput entry) =>
        new(
            personId,
            entry.IsSelected,
            entry.ClockInTime?.Trim() ?? string.Empty,
            entry.ClockOutTime?.Trim(),
            entry.UsesSendService,
            entry.ExpectedVersion);

    private static string EnsureOperationId(string? operationId) =>
        Guid.TryParse(operationId, out var parsed)
            ? parsed.ToString("D")
            : Guid.NewGuid().ToString("D");
}
