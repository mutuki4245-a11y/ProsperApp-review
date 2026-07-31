using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Features.Attendance;

public sealed class AttendanceApplicationService(
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreStaffAdminRepository staffAdminRepository,
    IStoreClock storeClock) : IAttendanceApplicationService
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreStaffAdminRepository _staffAdminRepository = staffAdminRepository;
    private readonly IStoreClock _storeClock = storeClock;

    public async Task<AttendancePageState> LoadAsync(
        ClosingAttendanceInputModel input,
        bool preserveInput,
        CancellationToken ct)
    {
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
        var businessDayResult = await _businessDayRepository.GetCurrentAsync(ct, forceRefresh: true);
        if (!businessDayResult.Succeeded)
        {
            return Result<AttendanceSaveOutput>.Failure(
                businessDayResult.FailureKind ?? ResultFailureKind.Unavailable,
                businessDayResult.ErrorMessage ?? "現在営業日を確認できませんでした。");
        }

        var businessDay = businessDayResult.Value;
        if (businessDay is not null &&
            input.BusinessDayId is { } postedBusinessDayId &&
            postedBusinessDayId != businessDay.BusinessDayId)
        {
            return Result<AttendanceSaveOutput>.Failure(
                ResultFailureKind.Conflict,
                "営業日情報が更新されています。画面を再読み込みしてください。");
        }

        if (businessDay is null)
        {
            var ensureResult = await _businessDayRepository.EnsureCurrentAsync(ct);
            if (!ensureResult.Succeeded || ensureResult.BusinessDay is null)
            {
                return Result<AttendanceSaveOutput>.Failure(
                    ResultFailureKind.Unavailable,
                    ensureResult.ErrorMessage ?? "営業日を自動作成できませんでした。");
            }

            businessDay = ensureResult.BusinessDay;
        }

        var attendanceEntries = input.Entries
            .Where(x => AttendancePersonTypes.Normalize(x.PersonType) == AttendancePersonTypes.Cast)
            .Where(x => x.CastId > 0 && (x.IsSelected || (x.IsRegistered && !string.IsNullOrWhiteSpace(x.ClockInTime))))
            .Select(x => new BusinessDayAttendanceInput
            {
                CastId = x.CastId,
                IsSelected = x.IsSelected,
                ClockInTime = x.ClockInTime?.Trim() ?? string.Empty
            })
            .ToArray();

        if (attendanceEntries.Length > 0)
        {
            var attendanceResult = await _businessDayRepository.SaveAttendanceAsync(
                businessDay.BusinessDayId,
                attendanceEntries,
                ct);
            if (!attendanceResult.Succeeded)
            {
                return Result<AttendanceSaveOutput>.Failure(
                    ResultFailureKind.Unavailable,
                    attendanceResult.ErrorMessage ?? "勤怠入力を保存できませんでした。");
            }
        }

        var staffAttendanceEntries = input.Entries
            .Where(x => AttendancePersonTypes.Normalize(x.PersonType) == AttendancePersonTypes.Staff)
            .Where(x => x.StaffId > 0 && (x.IsSelected || (x.IsRegistered && !string.IsNullOrWhiteSpace(x.ClockInTime))))
            .Select(x => new BusinessDayStaffAttendanceInput
            {
                StaffId = x.StaffId,
                IsSelected = x.IsSelected,
                ClockInTime = x.ClockInTime?.Trim() ?? string.Empty
            })
            .ToArray();

        if (staffAttendanceEntries.Length > 0)
        {
            var staffAttendanceResult = await _businessDayRepository.SaveStaffAttendanceAsync(
                businessDay.BusinessDayId,
                staffAttendanceEntries,
                ct);
            if (!staffAttendanceResult.Succeeded)
            {
                return Result<AttendanceSaveOutput>.Failure(
                    ResultFailureKind.Unavailable,
                    staffAttendanceResult.ErrorMessage ?? "勤怠入力を保存できませんでした。");
            }
        }

        var savedAttendance = await _businessDayRepository.GetClosingAttendanceAsync(
            businessDay.BusinessDayId,
            ct);
        if (!savedAttendance.Succeeded)
        {
            return Result<AttendanceSaveOutput>.Failure(
                savedAttendance.FailureKind ?? ResultFailureKind.Unavailable,
                savedAttendance.ErrorMessage ?? "保存後の勤怠入力を取得できませんでした。");
        }

        var attendanceIdByPersonKey = savedAttendance.Value
            .Where(x => x.PersonId > 0)
            .GroupBy(x => x.PersonKey)
            .ToDictionary(x => x.Key, x => x.Last().AttendanceId);
        var clockOutEntries = input.Entries
            .Where(x => x.IsSelected && !string.IsNullOrWhiteSpace(x.ClockOutTime))
            .Select(x =>
            {
                attendanceIdByPersonKey.TryGetValue(AttendancePersonKey.Create(x), out var attendanceId);
                return new BusinessDayClosingAttendanceInput
                {
                    AttendanceId = attendanceId,
                    PersonType = AttendancePersonTypes.Normalize(x.PersonType),
                    DisplayName = x.DisplayName,
                    DepartmentName = x.DepartmentName,
                    ClockInTime = x.ClockInTime?.Trim(),
                    ClockOutTime = x.ClockOutTime?.Trim(),
                    UsesSendService = x.UsesSendService
                };
            })
            .Where(x => x.AttendanceId > 0)
            .ToArray();

        var savedClockOutCount = 0;
        var castClockOutEntries = clockOutEntries
            .Where(x => AttendancePersonTypes.Normalize(x.PersonType) == AttendancePersonTypes.Cast)
            .ToArray();
        if (castClockOutEntries.Length > 0)
        {
            var closingResult = await _businessDayRepository.SaveClosingAttendanceAsync(
                businessDay.BusinessDayId,
                castClockOutEntries,
                ct);
            if (!closingResult.Succeeded)
            {
                return Result<AttendanceSaveOutput>.Failure(
                    ResultFailureKind.Unavailable,
                    closingResult.ErrorMessage ?? "退勤時刻を保存できませんでした。");
            }

            savedClockOutCount += closingResult.SavedCount;
        }

        var staffClockOutEntries = clockOutEntries
            .Where(x => AttendancePersonTypes.Normalize(x.PersonType) == AttendancePersonTypes.Staff)
            .ToArray();
        if (staffClockOutEntries.Length > 0)
        {
            var closingResult = await _businessDayRepository.SaveStaffClosingAttendanceAsync(
                businessDay.BusinessDayId,
                staffClockOutEntries,
                ct);
            if (!closingResult.Succeeded)
            {
                return Result<AttendanceSaveOutput>.Failure(
                    ResultFailureKind.Unavailable,
                    closingResult.ErrorMessage ?? "退勤時刻を保存できませんでした。");
            }

            savedClockOutCount += closingResult.SavedCount;
        }

        return Result<AttendanceSaveOutput>.Success(new AttendanceSaveOutput(
            businessDay,
            input.Entries.Count(x => x.IsSelected),
            savedClockOutCount));
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
