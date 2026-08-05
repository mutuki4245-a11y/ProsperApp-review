using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Services;

namespace ProsperApp.Features.Attendance;

public sealed class AttendanceApplicationService(
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreStaffAdminRepository staffAdminRepository,
    IStoreClock storeClock,
    IStoreMasterBootstrapper masterBootstrapper) : IAttendanceApplicationService
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreStaffAdminRepository _staffAdminRepository = staffAdminRepository;
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public async Task<AttendancePageState> LoadAsync(
        ClosingAttendanceInputModel input,
        bool preserveInput,
        CancellationToken ct)
    {
        await _masterBootstrapper.EnsureAsync(ct);
        var contextTask = _slipRepository.GetStoreContextAsync(ct);
        var businessDayTask = _businessDayRepository.GetCurrentAsync(ct);
        var castsTask = _slipRepository.GetCastsAsync(ct);
        var staffsTask = _staffAdminRepository.GetStaffOptionsAsync(ct);
        await Task.WhenAll(contextTask, businessDayTask, castsTask, staffsTask);

        var context = await contextTask;
        var businessDay = await businessDayTask;
        var casts = await castsTask;
        var staffs = await staffsTask;
        var issues = new List<PageLoadIssue>();
        AddIssue(issues, "店舗設定", context);
        AddIssue(issues, "営業日", businessDay);
        AddIssue(issues, "キャスト", casts);
        AddIssue(issues, "スタッフ", staffs);

        var minuteStep = context.Succeeded ? context.Value.AttendanceMinuteStep : 15;
        var clockInOptions = AttendanceEditor.BuildTimeOptions(19, 0, minuteStep);
        var clockOutOptions = AttendanceEditor.BuildTimeOptions(24, 0, minuteStep);
        var defaultClockIn = AttendanceEditor.ResolveDefaultTime(clockInOptions, "19:00");
        var currentBusinessDay = businessDay.Succeeded ? businessDay.Value : null;

        Result<IReadOnlyList<BusinessDayClosingAttendanceItem>> attendance =
            Result<IReadOnlyList<BusinessDayClosingAttendanceItem>>.Success([]);
        if (currentBusinessDay is not null)
        {
            attendance = await _businessDayRepository.GetClosingAttendanceAsync(
                currentBusinessDay.BusinessDayId,
                ct);
            AddIssue(issues, "勤怠", attendance);
        }

        var rebuiltInput = RebuildInput(
            input,
            preserveInput,
            currentBusinessDay,
            casts.Succeeded ? casts.Value : [],
            staffs.Succeeded ? staffs.Value : [],
            attendance.Succeeded ? attendance.Value : [],
            defaultClockIn);

        return new AttendancePageState(
            context.Succeeded ? context.Value : null,
            currentBusinessDay,
            _storeClock.GetCurrentBusinessDate(),
            clockInOptions,
            clockOutOptions,
            defaultClockIn,
            string.Empty,
            rebuiltInput,
            issues,
            issues.Count == 0
                ? _storeClock.ToStoreDateTimeOffset(_storeClock.GetStoreNow())
                : null);
    }

    public IReadOnlyList<AttendanceValidationError> Validate(
        AttendancePageState state,
        ClosingAttendanceInputModel input)
    {
        var loadError = state.LoadIssues.Count == 0
            ? null
            : string.Join(" ", state.LoadIssues.Select(issue => issue.Message));
        return AttendanceEditor.Validate(
            input,
            state.ClockInTimeOptions,
            state.ClockOutTimeOptions,
            state.BusinessDay?.BusinessDate ?? state.BusinessDate,
            _storeClock,
            loadError);
    }

    public async Task<Result<AttendanceSaveOutput>> SaveAsync(
        ClosingAttendanceInputModel input,
        CancellationToken ct)
    {
        var castEntries = input.Entries
            .Where(x => AttendancePersonTypes.Normalize(x.PersonType) == AttendancePersonTypes.Cast)
            .Where(x => x.CastId > 0 && (x.IsSelected || (x.IsRegistered && !string.IsNullOrWhiteSpace(x.ClockInTime))))
            .Select(x => new CurrentBusinessDayAttendanceEntry(
                x.CastId,
                x.IsSelected,
                x.ClockInTime?.Trim() ?? string.Empty,
                x.ClockOutTime?.Trim(),
                x.UsesSendService))
            .ToArray();
        var staffEntries = input.Entries
            .Where(x => AttendancePersonTypes.Normalize(x.PersonType) == AttendancePersonTypes.Staff)
            .Where(x => x.StaffId > 0 && (x.IsSelected || (x.IsRegistered && !string.IsNullOrWhiteSpace(x.ClockInTime))))
            .Select(x => new CurrentBusinessDayAttendanceEntry(
                x.StaffId,
                x.IsSelected,
                x.ClockInTime?.Trim() ?? string.Empty,
                x.ClockOutTime?.Trim(),
                x.UsesSendService))
            .ToArray();

        var result = await _businessDayRepository.SaveCurrentAttendanceAsync(
            new CurrentBusinessDayAttendanceMutation(
                input.BusinessDayId,
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

        return Result<AttendanceSaveOutput>.Success(new AttendanceSaveOutput(
            result.Value.BusinessDay,
            input.Entries.Count(x => x.IsSelected),
            result.Value.SavedClockOutCount));
    }

    private ClosingAttendanceInputModel RebuildInput(
        ClosingAttendanceInputModel input,
        bool preserveInput,
        StoreBusinessDay? businessDay,
        IReadOnlyList<CastOption> casts,
        IReadOnlyList<StaffOption> staffs,
        IReadOnlyList<BusinessDayClosingAttendanceItem> attendanceItems,
        string defaultClockIn)
    {
        foreach (var entry in input.Entries)
        {
            entry.PersonType = AttendancePersonTypes.Normalize(entry.PersonType);
        }

        var postedByPersonKey = preserveInput
            ? input.Entries
                .Where(x => x.PersonId > 0)
                .GroupBy(AttendancePersonKey.Create)
                .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal)
            : new Dictionary<string, BusinessDayAttendanceEntryInput>(StringComparer.Ordinal);
        var postedSelectedCastIds = preserveInput
            ? AttendanceEditor.ParseSelectedCastIds(input.SelectedCastIds)
            : [];
        var postedSelectedAttendanceKeys = preserveInput
            ? AttendanceEditor.ParseSelectedAttendanceKeys(input.SelectedAttendanceKeys)
            : [];
        var attendanceByPersonKey = attendanceItems
            .Where(x => x.PersonId > 0)
            .GroupBy(x => x.PersonKey)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);

        var entries = new List<BusinessDayAttendanceEntryInput>();
        entries.AddRange(casts.Select(cast =>
            {
                var personKey = AttendancePersonKey.Create(AttendancePersonTypes.Cast, cast.CastId);
                postedByPersonKey.TryGetValue(personKey, out var posted);
                attendanceByPersonKey.TryGetValue(personKey, out var attendance);
                var clockInTime = posted?.ClockInTime ??
                    _storeClock.FormatStoreTime(attendance?.ClockInAt, string.Empty);
                if (attendance is null && posted is null && string.IsNullOrWhiteSpace(clockInTime))
                {
                    clockInTime = defaultClockIn;
                }

                return new BusinessDayAttendanceEntryInput
                {
                    PersonType = AttendancePersonTypes.Cast,
                    CastId = cast.CastId,
                    AttendanceId = attendance?.AttendanceId ?? posted?.AttendanceId ?? 0,
                    DisplayName = cast.SearchDisplayName,
                    DepartmentName = cast.DepartmentName,
                    IsSelected = posted?.IsSelected ?? (attendance is not null && !string.IsNullOrWhiteSpace(clockInTime)),
                    IsRegistered = attendance is not null,
                    ClockInTime = clockInTime,
                    ClockOutTime = posted?.ClockOutTime ??
                        _storeClock.FormatStoreTime(attendance?.ClockOutAt, string.Empty),
                    UsesSendService = posted?.UsesSendService ?? attendance?.UsesSendService ?? false
                };
            }));
        entries.AddRange(staffs.Select(staff =>
            {
                var personKey = AttendancePersonKey.Create(AttendancePersonTypes.Staff, staff.StaffId);
                postedByPersonKey.TryGetValue(personKey, out var posted);
                attendanceByPersonKey.TryGetValue(personKey, out var attendance);
                var clockInTime = posted?.ClockInTime ??
                    _storeClock.FormatStoreTime(attendance?.ClockInAt, string.Empty);
                if (attendance is null && posted is null && string.IsNullOrWhiteSpace(clockInTime))
                {
                    clockInTime = defaultClockIn;
                }

                return new BusinessDayAttendanceEntryInput
                {
                    PersonType = AttendancePersonTypes.Staff,
                    StaffId = staff.StaffId,
                    AttendanceId = attendance?.AttendanceId ?? posted?.AttendanceId ?? 0,
                    DisplayName = staff.SearchDisplayName,
                    DepartmentName = staff.DepartmentName,
                    IsSelected = posted?.IsSelected ?? (attendance is not null && !string.IsNullOrWhiteSpace(clockInTime)),
                    IsRegistered = attendance is not null,
                    ClockInTime = clockInTime,
                    ClockOutTime = posted?.ClockOutTime ??
                        _storeClock.FormatStoreTime(attendance?.ClockOutAt, string.Empty),
                    UsesSendService = posted?.UsesSendService ?? attendance?.UsesSendService ?? false
                };
            }));
        var listedPersonKeys = entries
            .Select(AttendancePersonKey.Create)
            .ToHashSet(StringComparer.Ordinal);
        entries.AddRange(attendanceItems
            .Where(item => !listedPersonKeys.Contains(item.PersonKey))
            .Select(item =>
            {
                postedByPersonKey.TryGetValue(item.PersonKey, out var posted);
                return new BusinessDayAttendanceEntryInput
                {
                    PersonType = AttendancePersonTypes.Normalize(item.PersonType),
                    CastId = item.CastId,
                    StaffId = item.StaffId,
                    AttendanceId = item.AttendanceId,
                    DisplayName = item.SearchDisplayName,
                    DepartmentName = item.DepartmentName,
                    IsSelected = posted?.IsSelected ??
                        !string.IsNullOrWhiteSpace(posted?.ClockInTime ??
                            _storeClock.FormatStoreTime(item.ClockInAt, string.Empty)),
                    IsRegistered = true,
                    ClockInTime = posted?.ClockInTime ??
                        _storeClock.FormatStoreTime(item.ClockInAt, string.Empty),
                    ClockOutTime = posted?.ClockOutTime ??
                        _storeClock.FormatStoreTime(item.ClockOutAt, string.Empty),
                    UsesSendService = posted?.UsesSendService ?? item.UsesSendService
                };
            }));

        if (postedSelectedCastIds.Count > 0 || postedSelectedAttendanceKeys.Count > 0)
        {
            foreach (var entry in entries)
            {
                entry.PersonType = AttendancePersonTypes.Normalize(entry.PersonType);
                entry.IsSelected = entry.IsSelected ||
                    postedSelectedAttendanceKeys.Contains(AttendancePersonKey.Create(entry)) ||
                    (entry.PersonType == AttendancePersonTypes.Cast && postedSelectedCastIds.Contains(entry.CastId));
            }
        }

        return new ClosingAttendanceInputModel
        {
            BusinessDayId = businessDay?.BusinessDayId,
            SelectedCastIds = string.Join(
                ',',
                entries.Where(x => x.IsSelected && x.PersonType == AttendancePersonTypes.Cast)
                    .Select(x => x.CastId)),
            SelectedAttendanceKeys = string.Join(
                ',',
                entries.Where(x => x.IsSelected).Select(AttendancePersonKey.Create)),
            SelectedEntriesJson = input.SelectedEntriesJson,
            Entries = entries
        };
    }

    private static void AddIssue<T>(
        ICollection<PageLoadIssue> issues,
        string area,
        Result<T> result)
    {
        if (!result.Succeeded)
        {
            issues.Add(new PageLoadIssue(
                area,
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? $"{area}を取得できませんでした。"));
        }
    }
}
