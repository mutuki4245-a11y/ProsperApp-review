using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Features.Attendance;

public sealed class AttendanceApplicationService(
    IBusinessDayRepository businessDayRepository,
    IStoreSlipRepository slipRepository,
    IStoreClock storeClock) : IAttendanceApplicationService
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IStoreClock _storeClock = storeClock;

    public async Task<AttendancePageState> LoadAsync(
        ClosingAttendanceInputModel input,
        bool preserveInput,
        CancellationToken ct)
    {
        var contextTask = _slipRepository.GetStoreContextAsync(ct);
        var businessDayTask = _businessDayRepository.GetCurrentAsync(ct);
        var castsTask = _slipRepository.GetCastsAsync(ct);
        await Task.WhenAll(contextTask, businessDayTask, castsTask);

        var context = await contextTask;
        var businessDay = await businessDayTask;
        var casts = await castsTask;
        var issues = new List<PageLoadIssue>();
        AddIssue(issues, "店舗設定", context);
        AddIssue(issues, "営業日", businessDay);
        AddIssue(issues, "キャスト", casts);

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
            .Where(x => x.IsSelected || (x.IsRegistered && !string.IsNullOrWhiteSpace(x.ClockInTime)))
            .Select(x => new BusinessDayAttendanceInput
            {
                CastId = x.CastId,
                IsSelected = x.IsSelected,
                ClockInTime = x.ClockInTime?.Trim() ?? string.Empty
            })
            .ToArray();
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

        var savedAttendance = await _businessDayRepository.GetClosingAttendanceAsync(
            businessDay.BusinessDayId,
            ct);
        if (!savedAttendance.Succeeded)
        {
            return Result<AttendanceSaveOutput>.Failure(
                savedAttendance.FailureKind ?? ResultFailureKind.Unavailable,
                savedAttendance.ErrorMessage ?? "保存後の勤怠入力を取得できませんでした。");
        }

        var attendanceIdByCastId = savedAttendance.Value
            .GroupBy(x => x.CastId)
            .ToDictionary(x => x.Key, x => x.Last().AttendanceId);
        var clockOutEntries = input.Entries
            .Where(x => x.IsSelected && !string.IsNullOrWhiteSpace(x.ClockOutTime))
            .Select(x =>
            {
                attendanceIdByCastId.TryGetValue(x.CastId, out var attendanceId);
                return new BusinessDayClosingAttendanceInput
                {
                    AttendanceId = attendanceId,
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
        if (clockOutEntries.Length > 0)
        {
            var closingResult = await _businessDayRepository.SaveClosingAttendanceAsync(
                businessDay.BusinessDayId,
                clockOutEntries,
                ct);
            if (!closingResult.Succeeded)
            {
                return Result<AttendanceSaveOutput>.Failure(
                    ResultFailureKind.Unavailable,
                    closingResult.ErrorMessage ?? "退勤時刻を保存できませんでした。");
            }

            savedClockOutCount = closingResult.SavedCount;
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
        IReadOnlyList<BusinessDayClosingAttendanceItem> attendanceItems,
        string defaultClockIn)
    {
        var postedByCastId = preserveInput
            ? input.Entries
                .Where(x => x.CastId > 0)
                .GroupBy(x => x.CastId)
                .ToDictionary(x => x.Key, x => x.Last())
            : [];
        var postedSelectedCastIds = preserveInput
            ? AttendanceEditor.ParseSelectedCastIds(input.SelectedCastIds)
            : [];
        var attendanceByCastId = attendanceItems
            .GroupBy(x => x.CastId)
            .ToDictionary(x => x.Key, x => x.Last());
        var entries = casts
            .Select(cast =>
            {
                postedByCastId.TryGetValue(cast.CastId, out var posted);
                attendanceByCastId.TryGetValue(cast.CastId, out var attendance);
                var clockInTime = posted?.ClockInTime ??
                    _storeClock.FormatStoreTime(attendance?.ClockInAt, string.Empty);
                if (attendance is null && posted is null && string.IsNullOrWhiteSpace(clockInTime))
                {
                    clockInTime = defaultClockIn;
                }

                return new BusinessDayAttendanceEntryInput
                {
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
            })
            .ToList();
        var listedCastIds = entries.Select(x => x.CastId).ToHashSet();
        entries.AddRange(attendanceItems
            .Where(item => !listedCastIds.Contains(item.CastId))
            .Select(item =>
            {
                postedByCastId.TryGetValue(item.CastId, out var posted);
                return new BusinessDayAttendanceEntryInput
                {
                    CastId = item.CastId,
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

        if (postedSelectedCastIds.Count > 0)
        {
            foreach (var entry in entries)
            {
                entry.IsSelected = entry.IsSelected || postedSelectedCastIds.Contains(entry.CastId);
            }
        }

        return new ClosingAttendanceInputModel
        {
            BusinessDayId = businessDay?.BusinessDayId,
            SelectedCastIds = string.Join(',', entries.Where(x => x.IsSelected).Select(x => x.CastId)),
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
