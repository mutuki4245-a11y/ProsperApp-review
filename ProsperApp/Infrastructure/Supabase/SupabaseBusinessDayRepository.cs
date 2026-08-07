using System.Text.Json;
using ProsperApp.Features.Closing;
using ProsperApp.Features.Shared;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseBusinessDayRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IBusinessDayRepository
{
    private readonly IApplicationCache _cache = cache;

    public async Task<Result<CurrentBusinessDayCloseOutput>> CloseCurrentBusinessDayAsync(
        CurrentBusinessDayCloseMutation input,
        CancellationToken ct)
    {
        var departmentId = CurrentStoreDepartmentId;
        var result = await PostRpcArrayResultAsync(
            "store.close_business_day_v2",
            new
            {
                p_department_id = departmentId,
                p_operation_id = input.OperationId,
                p_expected_business_day_id = input.ExpectedBusinessDayId,
                p_expected_business_day_revision = input.ExpectedBusinessDayRevision,
                p_memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim(),
                p_ignore_closing_requirements = input.IgnoreClosingRequirements
            },
            ct);
        if (!result.Succeeded)
        {
            return Result<CurrentBusinessDayCloseOutput>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "営業日を締められませんでした。");
        }

        if (result.Value.Count != 1)
        {
            return Result<CurrentBusinessDayCloseOutput>.Failure(
                ResultFailureKind.InvalidResponse,
                "営業日締めの応答形式が正しくありません。");
        }

        var row = result.Value[0];
        if (!row.TryGetProperty("dashboard", out var dashboardElement) ||
            dashboardElement.ValueKind != JsonValueKind.Object ||
            !dashboardElement.TryGetProperty("drink_back_editor", out var editorElement) ||
            !SupabaseDrinkBackRepository.TryParseEditor(editorElement, out var editor))
        {
            return Result<CurrentBusinessDayCloseOutput>.Failure(
                ResultFailureKind.InvalidResponse,
                "営業日締め後の画面状態が正しくありません。");
        }

        var status = ReadString(row, "status") ?? "validation_error";
        var output = new CurrentBusinessDayCloseOutput(
            ReadString(row, "operation_id") ?? input.OperationId,
            status,
            ReadString(row, "message"),
            ReadLong(row, "closed_business_day_id"),
            ReadDateOnly(row, "business_date"),
            ReadDateTimeOffset(row, "closed_at"),
            ReadLong(row, "report_business_day_id"),
            ParseCurrentClosingDashboard(dashboardElement, editor));

        if (status == "confirmed")
        {
            StoreMasterCacheKeys.ClearCurrentBusinessDay(_cache, departmentId);
            if (output.ClosedBusinessDayId is long businessDayId)
            {
                StoreMasterCacheKeys.ClearOrderAttendingCasts(_cache, departmentId, businessDayId);
            }
        }

        return Result<CurrentBusinessDayCloseOutput>.Success(output);
    }

    public async Task<Result<CurrentClosingDashboard>> GetCurrentClosingDashboardAsync(
        string? knownCastMasterRevision,
        CancellationToken ct)
    {
        var result = await PostRpcArrayResultAsync(
            "store.get_current_closing_dashboard",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_known_cast_master_revision = string.IsNullOrWhiteSpace(knownCastMasterRevision)
                    ? null
                    : knownCastMasterRevision
            },
            ct);
        if (!result.Succeeded)
        {
            return Result<CurrentClosingDashboard>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "締め状況を取得できませんでした。");
        }

        if (result.Value.Count != 1)
        {
            return Result<CurrentClosingDashboard>.Failure(
                ResultFailureKind.InvalidResponse,
                "締め状況の応答形式が正しくありません。");
        }

        var row = result.Value[0];
        if (!row.TryGetProperty("drink_back_editor", out var editorElement) ||
            !SupabaseDrinkBackRepository.TryParseEditor(editorElement, out var editor))
        {
            return Result<CurrentClosingDashboard>.Failure(
                ResultFailureKind.InvalidResponse,
                "締め状況のドリンクバック調整情報が正しくありません。");
        }

        return Result<CurrentClosingDashboard>.Success(ParseCurrentClosingDashboard(row, editor));
    }

    public async Task<Result<AttendanceShellBootstrap>> GetAttendanceShellAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<AttendanceShellBootstrap>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        if (_cache.TryGetValue(StoreMasterCacheKeys.StoreContext(departmentId), out StoreContext? context) &&
            context is not null &&
            _cache.TryGetValue(StoreMasterCacheKeys.StoreCasts(departmentId), out IReadOnlyList<CastOption>? casts) &&
            casts is not null &&
            _cache.TryGetValue(StoreMasterCacheKeys.StoreStaffs(departmentId), out IReadOnlyList<StaffOption>? staffs) &&
            staffs is not null)
        {
            return Result<AttendanceShellBootstrap>.Success(new AttendanceShellBootstrap(
                context,
                BuildAttendanceRoster(casts, staffs),
                null,
                false));
        }

        var result = await PostRpcArrayResultAsync(
            "store.get_attendance_editor_bootstrap_v2",
            new { p_department_id = departmentId },
            ct);
        if (!result.Succeeded || result.Value.Count == 0)
        {
            return Result<AttendanceShellBootstrap>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "勤怠入力の名簿を取得できませんでした。");
        }

        var row = result.Value[0];
        if (!StoreBootstrapJson.TryReadObject(row, "store_context", out var contextRow))
        {
            return Result<AttendanceShellBootstrap>.Failure(
                ResultFailureKind.InvalidResponse,
                "勤怠入力の店舗情報が正しくありません。");
        }

        context = ParseAttendanceStoreContext(contextRow, departmentId);
        casts = StoreBootstrapJson.ReadArray(row, "casts")
            .Select(ParseAttendanceCast)
            .Where(cast => cast.CastId > 0 && !string.IsNullOrWhiteSpace(cast.DisplayName))
            .ToList();
        staffs = StoreBootstrapJson.ReadArray(row, "staffs")
            .Select(ParseAttendanceStaff)
            .Where(staff => staff.StaffId > 0 && !string.IsNullOrWhiteSpace(staff.DisplayName))
            .ToList();

        StoreMasterCacheKeys.SetMaster(
            _cache,
            StoreMasterCacheKeys.StoreContext(departmentId),
            context,
            "店舗コンテキスト");
        StoreMasterCacheKeys.SetMaster(
            _cache,
            StoreMasterCacheKeys.StoreCasts(departmentId),
            casts,
            "キャスト候補");
        StoreMasterCacheKeys.SetMaster(
            _cache,
            StoreMasterCacheKeys.StoreStaffs(departmentId),
            staffs,
            "スタッフ候補");

        var snapshot = ParseAttendanceEditorSnapshot(row);
        ApplyAttendanceRuntimeCache(departmentId, snapshot, null);
        return Result<AttendanceShellBootstrap>.Success(new AttendanceShellBootstrap(
            context,
            BuildAttendanceRoster(casts, staffs),
            snapshot,
            true));
    }

    public async Task<Result<AttendanceEditorSnapshot>> GetCurrentAttendanceEditorAsync(
        long? knownBusinessDayId,
        long? knownBusinessDayRevision,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<AttendanceEditorSnapshot>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。勤怠入力を取得できません。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var result = await PostRpcArrayResultAsync(
            "store.get_current_attendance_editor_snapshot",
            new
            {
                p_department_id = departmentId,
                p_known_business_day_id = knownBusinessDayId,
                p_known_business_day_revision = knownBusinessDayRevision
            },
            ct);
        if (!result.Succeeded || result.Value.Count == 0)
        {
            return Result<AttendanceEditorSnapshot>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "現在の勤怠入力を取得できませんでした。");
        }

        var snapshot = ParseAttendanceEditorSnapshot(result.Value[0]);
        ApplyAttendanceRuntimeCache(departmentId, snapshot, knownBusinessDayId);
        return Result<AttendanceEditorSnapshot>.Success(snapshot);
    }

    public async Task<Result<CurrentBusinessDayAttendanceSaveOutput>> SaveCurrentAttendanceAsync(
        CurrentBusinessDayAttendanceMutation input,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<CurrentBusinessDayAttendanceSaveOutput>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。勤怠入力を更新できません。");
        }

        if (input.BusinessDate == default ||
            input.CastEntries.Any(entry => entry.PersonId <= 0) ||
            input.StaffEntries.Any(entry => entry.PersonId <= 0))
        {
            return Result<CurrentBusinessDayAttendanceSaveOutput>.Failure(
                ResultFailureKind.InvalidInput,
                "勤怠入力を確認してください。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var result = await PostRpcArrayResultAsync(
            "store.save_current_business_day_attendance_v2",
            new
            {
                p_department_id = departmentId,
                p_operation_id = input.OperationId,
                p_expected_business_day_id = input.ExpectedBusinessDayId,
                p_expected_business_day_revision = input.ExpectedBusinessDayRevision,
                p_business_date = input.BusinessDate,
                p_cast_entries = input.CastEntries.Select(entry => new
                {
                    cast_id = entry.PersonId,
                    is_selected = entry.IsSelected,
                    clock_in_time = entry.ClockInTime?.Trim(),
                    clock_out_time = entry.ClockOutTime?.Trim(),
                    uses_send_service = entry.UsesSendService,
                    expected_version = entry.ExpectedVersion
                }),
                p_staff_entries = input.StaffEntries.Select(entry => new
                {
                    staff_id = entry.PersonId,
                    is_selected = entry.IsSelected,
                    clock_in_time = entry.ClockInTime?.Trim(),
                    clock_out_time = entry.ClockOutTime?.Trim(),
                    uses_send_service = entry.UsesSendService,
                    expected_version = entry.ExpectedVersion
                })
            },
            ct);

        if (!result.Succeeded || result.Value.Count == 0)
        {
            return Result<CurrentBusinessDayAttendanceSaveOutput>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                ToFriendlyError(result.ErrorMessage));
        }

        var row = result.Value[0];
        var status = ReadString(row, "status") ?? "validation_error";
        var snapshot = ParseAttendanceEditorSnapshot(row);
        var savedCount = (int)(ReadLong(row, "saved_count") ?? 0);
        var savedClockOutCount = (int)(ReadLong(row, "saved_clock_out_count") ?? 0);
        var rowResults = StoreBootstrapJson.ReadArray(row, "row_results")
            .Select(resultRow => new AttendanceSaveRowResult(
                AttendancePersonTypes.Normalize(ReadString(resultRow, "person_type")),
                ReadLong(resultRow, "person_id") ?? 0,
                ReadString(resultRow, "status") ?? status,
                ReadString(resultRow, "message")))
            .Where(resultRow => resultRow.PersonId > 0)
            .ToList();

        ApplyAttendanceRuntimeCache(departmentId, snapshot, input.ExpectedBusinessDayId);

        return Result<CurrentBusinessDayAttendanceSaveOutput>.Success(
            new CurrentBusinessDayAttendanceSaveOutput(
                ReadString(row, "operation_id") ?? input.OperationId,
                status,
                ReadString(row, "message") ?? (status == "confirmed"
                    ? "勤怠入力を保存しました。"
                    : "勤怠入力を保存できませんでした。"),
                snapshot,
                savedCount,
                savedClockOutCount,
                rowResults));
    }

    public async Task<Result<CurrentDrinkDeliveryEditorState>> GetCurrentDrinkDeliveryEditorAsync(
        CancellationToken ct)
    {
        var result = await PostRpcArrayResultAsync(
            "store.get_current_drink_delivery_editor",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);
        if (!result.Succeeded)
        {
            return Result<CurrentDrinkDeliveryEditorState>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "納品額入力状況を取得できませんでした。");
        }

        if (result.Value.Count != 1)
        {
            return Result<CurrentDrinkDeliveryEditorState>.Failure(
                ResultFailureKind.InvalidResponse,
                "納品額入力状況の応答形式が正しくありません。");
        }

        return Result<CurrentDrinkDeliveryEditorState>.Success(
            ParseCurrentDrinkDeliveryEditor(result.Value[0]));
    }

    public async Task<Result<CurrentBusinessDayDrinkDeliverySaveOutput>> SaveCurrentDrinkDeliveryAmountAsync(
        CurrentBusinessDayDrinkDeliveryMutation input,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<CurrentBusinessDayDrinkDeliverySaveOutput>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。納品額を保存できません。");
        }

        if (input.BusinessDate == default || input.Amount < 0 ||
            input.Amount > 999999999999m || decimal.Truncate(input.Amount) != input.Amount)
        {
            return Result<CurrentBusinessDayDrinkDeliverySaveOutput>.Failure(
                ResultFailureKind.InvalidInput,
                "納品額は0円以上の整数で入力してください。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.save_current_business_day_drink_delivery_amount_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = input.OperationId,
                p_expected_business_day_id = input.ExpectedBusinessDayId,
                p_expected_business_day_revision = input.ExpectedBusinessDayRevision,
                p_business_date = input.BusinessDate,
                p_drink_delivery_amount = input.Amount
            },
            ct);
        if (!result.Succeeded)
        {
            return Result<CurrentBusinessDayDrinkDeliverySaveOutput>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                ToFriendlyError(result.ErrorMessage));
        }

        if (result.Value.Count != 1 ||
            !result.Value[0].TryGetProperty("editor", out var editorJson) ||
            editorJson.ValueKind != JsonValueKind.Object)
        {
            return Result<CurrentBusinessDayDrinkDeliverySaveOutput>.Failure(
                ResultFailureKind.InvalidResponse,
                "納品額保存結果の応答形式が正しくありません。");
        }

        var row = result.Value[0];
        StoreBusinessDay? businessDay = null;
        if (row.TryGetProperty("business_day", out var businessDayJson) &&
            businessDayJson.ValueKind == JsonValueKind.Object)
        {
            businessDay = ParseBusinessDay(businessDayJson);
            if (!IsValidBusinessDay(businessDay))
            {
                businessDay = null;
            }
        }

        if (businessDay is not null)
        {
            StoreMasterCacheKeys.SetRuntime(
                _cache,
                StoreMasterCacheKeys.CurrentBusinessDay(CurrentStoreDepartmentId),
                businessDay,
                "現在営業日");
        }

        ClosingDashboardDrinkDelta? dashboardDelta = null;
        if (row.TryGetProperty("dashboard_delta", out var dashboardDeltaJson) &&
            dashboardDeltaJson.ValueKind == JsonValueKind.Object)
        {
            dashboardDelta = ParseClosingDashboardDrinkDelta(dashboardDeltaJson);
        }

        var status = ReadString(row, "status") ?? "validation_error";
        var message = ReadString(row, "message");
        return Result<CurrentBusinessDayDrinkDeliverySaveOutput>.Success(
            new CurrentBusinessDayDrinkDeliverySaveOutput(
                status,
                ReadString(row, "operation_id") ?? input.OperationId,
                string.IsNullOrWhiteSpace(message) ? null : ToFriendlyError(message),
                ParseCurrentDrinkDeliveryEditor(editorJson),
                dashboardDelta,
                businessDay));
    }

    private static StoreContext ParseAttendanceStoreContext(JsonElement row, long departmentId) =>
        new()
        {
            CompanyId = ReadLong(row, "company_id") ?? 0,
            DepartmentId = ReadLong(row, "department_id") ?? departmentId,
            DepartmentName = ReadString(row, "department_name"),
            AttendanceMinuteStep = NormalizeAttendanceMinuteStep(ReadLong(row, "attendance_minute_step"))
        };

    private static CastOption ParseAttendanceCast(JsonElement row) =>
        new()
        {
            CastId = ReadLong(row, "cast_id") ?? 0,
            CastCode = ReadString(row, "cast_code"),
            DisplayName = ReadString(row, "display_name") ?? string.Empty,
            DepartmentName = ReadString(row, "department_name")
        };

    private static StaffOption ParseAttendanceStaff(JsonElement row) =>
        new()
        {
            StaffId = ReadLong(row, "staff_id") ?? 0,
            StaffCode = ReadString(row, "staff_code"),
            DisplayName = ReadString(row, "display_name") ?? string.Empty,
            DepartmentName = ReadString(row, "department_name"),
            EmploymentType = StoreStaffEmploymentTypes.Normalize(ReadString(row, "employment_type"))
        };

    private static IReadOnlyList<AttendanceRosterPerson> BuildAttendanceRoster(
        IReadOnlyList<CastOption> casts,
        IReadOnlyList<StaffOption> staffs) =>
        casts.Select(cast => new AttendanceRosterPerson(
                AttendancePersonTypes.Cast,
                cast.CastId,
                cast.DisplayName,
                cast.DepartmentName,
                true))
            .Concat(staffs.Select(staff => new AttendanceRosterPerson(
                AttendancePersonTypes.Staff,
                staff.StaffId,
                staff.DisplayName,
                staff.DepartmentName,
                true)))
            .ToList();

    private static AttendanceEditorSnapshot ParseAttendanceEditorSnapshot(JsonElement row)
    {
        var hasBusinessDay = ReadBool(row, "has_business_day") ?? false;
        StoreBusinessDay? businessDay = null;
        if (hasBusinessDay &&
            row.TryGetProperty("business_day", out var businessDayJson) &&
            businessDayJson.ValueKind == JsonValueKind.Object)
        {
            var parsed = ParseBusinessDay(businessDayJson);
            if (IsValidBusinessDay(parsed))
            {
                businessDay = parsed;
            }
        }

        var attendanceEntries = StoreBootstrapJson.ReadArray(row, "attendance_entries")
            .Select(entry => new AttendanceEditorEntrySnapshot(
                ReadLong(entry, "attendance_id") ?? 0,
                AttendancePersonTypes.Normalize(ReadString(entry, "person_type")),
                ReadLong(entry, "person_id") ?? 0,
                ReadString(entry, "display_name") ?? string.Empty,
                ReadString(entry, "department_name"),
                ReadString(entry, "attendance_status") ?? string.Empty,
                ReadString(entry, "clock_in_time") ?? string.Empty,
                ReadString(entry, "clock_out_time"),
                ReadBool(entry, "uses_send_service") ?? false,
                ReadString(entry, "version") ?? string.Empty))
            .Where(entry => entry.AttendanceId > 0 && entry.PersonId > 0)
            .ToList();
        var attendingCasts = StoreBootstrapJson.ReadArray(row, "attending_casts")
            .Select(entry => new StoreOrderAttendanceCastOption
            {
                CastId = ReadLong(entry, "cast_id") ?? 0,
                DisplayName = ReadString(entry, "display_name") ?? string.Empty,
                DrinkMemo = ReadString(entry, "drink_memo"),
                DepartmentName = ReadString(entry, "department_name"),
                ClockInTime = ReadString(entry, "clock_in_time")
            })
            .Where(entry => entry.CastId > 0 && !string.IsNullOrWhiteSpace(entry.DisplayName))
            .ToList();

        return new AttendanceEditorSnapshot(
            hasBusinessDay && businessDay is not null,
            ReadBool(row, "unchanged") ?? false,
            businessDay,
            ReadLong(row, "business_day_revision") ?? businessDay?.BusinessUiRevision ?? 0,
            attendanceEntries,
            attendingCasts);
    }

    private void ApplyAttendanceRuntimeCache(
        long departmentId,
        AttendanceEditorSnapshot snapshot,
        long? previousBusinessDayId)
    {
        if (!snapshot.HasBusinessDay || snapshot.BusinessDay is null)
        {
            StoreMasterCacheKeys.ClearCurrentBusinessDay(_cache, departmentId);
            if (previousBusinessDayId is > 0)
            {
                StoreMasterCacheKeys.ClearOrderAttendingCasts(_cache, departmentId, previousBusinessDayId.Value);
            }

            return;
        }

        StoreMasterCacheKeys.SetRuntime(
            _cache,
            StoreMasterCacheKeys.CurrentBusinessDay(departmentId),
            snapshot.BusinessDay,
            "現在営業日");
        if (!snapshot.Unchanged)
        {
            StoreMasterCacheKeys.SetRuntime(
                _cache,
                StoreMasterCacheKeys.OrderAttendingCasts(departmentId, snapshot.BusinessDay.BusinessDayId),
                snapshot.AttendingCasts,
                "出勤キャスト");
        }
    }

    private static int NormalizeAttendanceMinuteStep(long? value) =>
        value is 5L or 10L or 15L or 20L or 30L or 60L ? (int)value.Value : 15;

    private static CurrentClosingDashboard ParseCurrentClosingDashboard(
        JsonElement row,
        DrinkBackEditorFragment drinkBackEditor)
    {
        IReadOnlyList<DrinkBackCastOption>? activeCasts = null;
        if (row.TryGetProperty("active_casts", out var activeCastsElement) &&
            activeCastsElement.ValueKind == JsonValueKind.Array)
        {
            activeCasts = activeCastsElement
                .EnumerateArray()
                .Select(SupabaseDrinkBackRepository.ParseCastOption)
                .Where(cast => cast.CastId > 0 && !string.IsNullOrWhiteSpace(cast.DisplayName))
                .ToList();
        }

        return new CurrentClosingDashboard(
            ReadLong(row, "department_id") ?? 0,
            ReadBool(row, "has_business_day") ?? false,
            ReadLong(row, "business_day_id"),
            ReadLong(row, "business_day_revision") ?? 0,
            ReadDateOnly(row, "business_date"),
            ReadString(row, "memo"),
            (int)(ReadLong(row, "open_slip_count") ?? 0),
            ReadDecimal(row, "drink_delivery_amount") ?? 0,
            ReadBool(row, "is_drink_delivery_amount_entered") ?? false,
            (int)(ReadLong(row, "attendance_count") ?? 0),
            (int)(ReadLong(row, "missing_clock_out_count") ?? 0),
            (int)(ReadLong(row, "cast_sales_required_slip_count") ?? 0),
            (int)(ReadLong(row, "cast_sales_completed_slip_count") ?? 0),
            (int)(ReadLong(row, "cast_sales_missing_slip_count") ?? 0),
            (int)(ReadLong(row, "drink_back_required_cast_count") ?? 0),
            (int)(ReadLong(row, "drink_back_completed_cast_count") ?? 0),
            (int)(ReadLong(row, "drink_back_missing_cast_count") ?? 0),
            ReadLong(row, "drink_back_total_amount") ?? 0,
            drinkBackEditor,
            ReadString(row, "cast_master_revision") ?? string.Empty,
            activeCasts,
            (int)(ReadLong(row, "pending_receipt_count") ?? 0),
            ReadBool(row, "can_close") ?? false,
            ReadStringArray(row, "block_reasons"),
            ReadDateTimeOffset(row, "checked_at") ?? DateTimeOffset.UtcNow);
    }

    private static CurrentDrinkDeliveryEditorState ParseCurrentDrinkDeliveryEditor(JsonElement row)
    {
        return new CurrentDrinkDeliveryEditorState(
            ReadLong(row, "department_id") ?? 0,
            ReadBool(row, "has_business_day") ?? false,
            ReadLong(row, "business_day_id"),
            ReadLong(row, "business_day_revision") ?? 0,
            ReadDateOnly(row, "business_date"),
            ReadDecimal(row, "drink_delivery_amount") ?? 0,
            ReadBool(row, "is_entered") ?? false,
            ReadDateTimeOffset(row, "checked_at") ?? DateTimeOffset.UtcNow);
    }

    private static ClosingDashboardDrinkDelta ParseClosingDashboardDrinkDelta(JsonElement row)
    {
        return new ClosingDashboardDrinkDelta(
            ReadLong(row, "department_id") ?? 0,
            ReadLong(row, "business_day_id") ?? 0,
            ReadLong(row, "business_day_revision") ?? 0,
            ReadDecimal(row, "drink_delivery_amount") ?? 0,
            ReadBool(row, "is_drink_delivery_amount_entered") ?? false,
            ReadDateTimeOffset(row, "checked_at") ?? DateTimeOffset.UtcNow);
    }

    private static StoreBusinessDay ParseBusinessDay(JsonElement row)
    {
        return new StoreBusinessDay
        {
            BusinessDayId = ReadLong(row, "business_day_id") ?? 0,
            CompanyId = ReadLong(row, "company_id") ?? 0,
            DepartmentId = ReadLong(row, "department_id") ?? 0,
            BusinessDate = ReadDateOnly(row, "business_date") ?? DateOnly.MinValue,
            OpenedAt = ReadDateTimeOffset(row, "opened_at") ?? DateTimeOffset.MinValue,
            ClosedAt = ReadDateTimeOffset(row, "closed_at"),
            Status = ReadString(row, "status") ?? string.Empty,
            Memo = ReadString(row, "memo"),
            BusinessUiRevision = ReadLong(row, "business_ui_revision") ?? 0
        };
    }

    private static bool IsValidBusinessDay(StoreBusinessDay? businessDay)
    {
        return businessDay is { BusinessDayId: > 0 } && businessDay.BusinessDate != DateOnly.MinValue;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement row, string propertyName)
    {
        if (!row.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "DB更新に失敗しました。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を取得できません。設定画面で利用店舗を選択してください。";
        }

        if (rawError.Contains("business_day_already_open", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
        {
            return "既に営業中の営業日があります。";
        }

        if (rawError.Contains("business_day_not_open", StringComparison.OrdinalIgnoreCase))
        {
            return "営業中の営業日がありません。";
        }

        if (rawError.Contains("closing_override_disabled", StringComparison.OrdinalIgnoreCase))
        {
            return "現在のDBでは締め条件無視が無効です。SQL定義を適用してください。";
        }

        if (rawError.Contains("invalid_drink_delivery_amount", StringComparison.OrdinalIgnoreCase))
        {
            return "納品額は0円以上の整数で入力してください。";
        }

        if (rawError.Contains("business_day_revision_conflict", StringComparison.OrdinalIgnoreCase))
        {
            return "営業日情報が更新されました。最新の納品額を確認してもう一度保存してください。";
        }

        if (rawError.Contains("business_day_closing_required", StringComparison.OrdinalIgnoreCase))
        {
            return "対象の営業日が切り替わりました。最新の納品額を確認してください。";
        }

        if (rawError.Contains("open_slips_exist", StringComparison.OrdinalIgnoreCase))
        {
            return "未会計の伝票があります。すべて会計してから締めてください。";
        }

        if (rawError.Contains("drink_delivery_required", StringComparison.OrdinalIgnoreCase))
        {
            return "酒代を入力してください。酒代がない場合も0円で保存してください。";
        }

        if (rawError.Contains("attendance_required", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤者を1名以上入力してください。";
        }

        if (rawError.Contains("attendance_clock_out_required", StringComparison.OrdinalIgnoreCase))
        {
            return "退勤時刻が未入力の出勤者がいます。";
        }

        if (rawError.Contains("cast_sales_adjustment_required", StringComparison.OrdinalIgnoreCase))
        {
            return "キャスト売上額調整を完了してください。";
        }

        if (rawError.Contains("drink_back_required", StringComparison.OrdinalIgnoreCase))
        {
            return "ドリンクバック調整を入力してください。0円の場合も保存してください。";
        }

        if (rawError.Contains("invalid_attendance_clock_in_time", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤時刻を確認してください。";
        }

        if (rawError.Contains("store_attendance_cast_not_found", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("attendance_cast_required", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("store_attendance_staff_not_found", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("attendance_staff_required", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤者の選択内容を確認してください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"DB更新に失敗しました。{rawError}";
    }

}
