using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseBusinessDayRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IBusinessDayRepository
{
    public async Task<StoreBusinessDay?> GetCurrentAsync(CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return null;
        }

        var rows = await PostRpcArrayAsync(
            "get_current_business_day",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        return rows.Count == 0 ? null : ParseBusinessDay(rows[0]);
    }

    public async Task<BusinessDayOperationResult> OpenAsync(
        DateOnly businessDate,
        string? memo,
        IReadOnlyCollection<BusinessDayAttendanceInput>? attendanceEntries,
        CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return BusinessDayOperationResult.Failed("Supabase SecretKeyが未設定です。営業日を更新できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "open_business_day_with_attendance",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_date = businessDate,
                p_attendance_entries = attendanceEntries?
                    .Where(x => x.CastId > 0 && x.IsSelected && !string.IsNullOrWhiteSpace(x.ClockInTime))
                    .GroupBy(x => x.CastId)
                    .Select(x => x.First())
                    .Select(x => new AttendanceEntryPayload(x.CastId, x.ClockInTime, x.IsSelected))
                    .ToArray() ?? [],
                p_memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim()
            },
            requireSecretKey: true,
            ct);

        if (!result.Succeeded)
        {
            return BusinessDayOperationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return BusinessDayOperationResult.Failed("営業日を開始できませんでした。");
        }

        return BusinessDayOperationResult.Success(ParseBusinessDay(result.Rows[0]));
    }

    public async Task<BusinessDayOperationResult> CloseAsync(long businessDayId, string? memo, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return BusinessDayOperationResult.Failed("Supabase SecretKeyが未設定です。営業日を更新できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "close_business_day",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId,
                p_memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim()
            },
            requireSecretKey: true,
            ct);

        if (!result.Succeeded)
        {
            return BusinessDayOperationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return BusinessDayOperationResult.Failed("現在営業中の営業日が見つかりません。");
        }

        return BusinessDayOperationResult.Success(ParseBusinessDay(result.Rows[0]));
    }

    public async Task<BusinessDayOperationResult> SaveAttendanceAsync(
        long businessDayId,
        IReadOnlyCollection<BusinessDayAttendanceInput> attendanceEntries,
        CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return BusinessDayOperationResult.Failed("Supabase SecretKeyが未設定です。出勤登録を更新できません。");
        }

        var payload = attendanceEntries
            .Where(x => x.CastId > 0 && (x.IsSelected || !string.IsNullOrWhiteSpace(x.ClockInTime)))
            .GroupBy(x => x.CastId)
            .Select(x => x.Last())
            .Select(x => new AttendanceEntryPayload(x.CastId, x.ClockInTime, x.IsSelected))
            .ToArray();

        if (payload.Length == 0)
        {
            return BusinessDayOperationResult.Failed("出勤キャストを選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "save_business_day_attendance",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId,
                p_attendance_entries = payload
            },
            requireSecretKey: true,
            ct);

        if (!result.Succeeded)
        {
            return BusinessDayOperationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return BusinessDayOperationResult.Failed("出勤登録を更新できませんでした。");
        }

        return BusinessDayOperationResult.Success(ParseBusinessDay(result.Rows[0]));
    }

    public async Task<int> GetOpenSlipCountAsync(long businessDayId, CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return 0;
        }

        var result = await RpcClient.PostScalarAsync(
            "get_open_slip_count",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            requireSecretKey: false,
            ct);
        var value = result.Succeeded ? result.Body?.Trim() : null;

        return int.TryParse(value, out var count) ? count : 0;
    }

    public async Task<decimal> GetDrinkDeliveryAmountAsync(long businessDayId, CancellationToken ct)
    {
        return (await GetDrinkDeliveryStatusAsync(businessDayId, ct)).Amount;
    }

    public async Task<BusinessDayDrinkDeliveryStatus> GetDrinkDeliveryStatusAsync(long businessDayId, CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return new BusinessDayDrinkDeliveryStatus();
        }

        var rows = await PostRpcArrayAsync(
            "get_business_day_drink_delivery_status",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);

        if (rows.Count == 0)
        {
            return new BusinessDayDrinkDeliveryStatus();
        }

        return new BusinessDayDrinkDeliveryStatus
        {
            Amount = ReadDecimal(rows[0], "drink_delivery_amount") ?? 0,
            IsEntered = ReadBool(rows[0], "is_entered") ?? false
        };
    }

    public async Task<BusinessDayAmountSaveResult> SaveDrinkDeliveryAmountAsync(
        long businessDayId,
        decimal amount,
        CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return BusinessDayAmountSaveResult.Failed("Supabase SecretKeyが未設定です。納品額を保存できません。");
        }

        if (amount < 0 || decimal.Truncate(amount) != amount)
        {
            return BusinessDayAmountSaveResult.Failed("納品額は0円以上の整数で入力してください。");
        }

        var result = await RpcClient.PostScalarAsync(
            "save_business_day_drink_delivery_amount",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId,
                p_drink_delivery_amount = amount
            },
            requireSecretKey: true,
            ct);

        if (!result.Succeeded)
        {
            return BusinessDayAmountSaveResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var value = NormalizeScalarBody(result.Body);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var savedAmount)
            ? BusinessDayAmountSaveResult.Success(savedAmount)
            : BusinessDayAmountSaveResult.Failed("納品額を保存できませんでした。");
    }

    public async Task<IReadOnlyList<BusinessDayClosingAttendanceItem>> GetClosingAttendanceAsync(
        long businessDayId,
        CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return [];
        }

        var rows = await PostRpcArrayAsync(
            "get_business_day_closing_attendance",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);

        return rows
            .Select(ParseClosingAttendanceItem)
            .Where(x => x.AttendanceId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
    }

    public async Task<BusinessDayAttendanceSaveResult> SaveClosingAttendanceAsync(
        long businessDayId,
        IReadOnlyCollection<BusinessDayClosingAttendanceInput> attendanceEntries,
        CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return BusinessDayAttendanceSaveResult.Failed("Supabase SecretKeyが未設定です。勤怠入力を保存できません。");
        }

        var payload = attendanceEntries
            .Where(x => x.AttendanceId > 0)
            .GroupBy(x => x.AttendanceId)
            .Select(x => x.Last())
            .Select(x => new ClosingAttendanceEntryPayload(
                x.AttendanceId,
                x.ClockInTime?.Trim() ?? string.Empty,
                x.ClockOutTime?.Trim() ?? string.Empty,
                x.UsesSendService))
            .ToArray();

        if (payload.Length == 0)
        {
            return BusinessDayAttendanceSaveResult.Failed("退勤情報を1名以上入力してください。");
        }

        var result = await RpcClient.PostScalarAsync(
            "save_business_day_closing_attendance",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId,
                p_attendance_entries = payload
            },
            requireSecretKey: true,
            ct);

        if (!result.Succeeded)
        {
            return BusinessDayAttendanceSaveResult.Failed(ToClosingAttendanceFriendlyError(result.ErrorMessage));
        }

        var value = NormalizeScalarBody(result.Body);
        return int.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var savedCount)
            ? BusinessDayAttendanceSaveResult.Success(savedCount)
            : BusinessDayAttendanceSaveResult.Failed("勤怠入力を保存できませんでした。");
    }

    private sealed record AttendanceEntryPayload(
        [property: JsonPropertyName("cast_id")] long CastId,
        [property: JsonPropertyName("clock_in_time")] string ClockInTime,
        [property: JsonPropertyName("is_selected")] bool IsSelected);

    private sealed record ClosingAttendanceEntryPayload(
        [property: JsonPropertyName("attendance_id")] long AttendanceId,
        [property: JsonPropertyName("clock_in_time")] string ClockInTime,
        [property: JsonPropertyName("clock_out_time")] string ClockOutTime,
        [property: JsonPropertyName("uses_send_service")] bool UsesSendService);

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
            Memo = ReadString(row, "memo")
        };
    }

    private static BusinessDayClosingAttendanceItem ParseClosingAttendanceItem(JsonElement row)
    {
        return new BusinessDayClosingAttendanceItem
        {
            AttendanceId = ReadLong(row, "attendance_id") ?? 0,
            CastId = ReadLong(row, "cast_id") ?? 0,
            DisplayName = ReadString(row, "cast_display_name") ?? string.Empty,
            DepartmentName = ReadString(row, "cast_department_name"),
            AttendanceStatus = ReadString(row, "attendance_status") ?? string.Empty,
            ClockInAt = ReadDateTimeOffset(row, "clock_in_at"),
            ClockOutAt = ReadDateTimeOffset(row, "clock_out_at"),
            UsesSendService = ReadBool(row, "uses_send_service") ?? false
        };
    }

    private static string? NormalizeScalarBody(string? body)
    {
        return string.IsNullOrWhiteSpace(body)
            ? null
            : body.Trim().Trim('"');
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

        if (rawError.Contains("invalid_drink_delivery_amount", StringComparison.OrdinalIgnoreCase))
        {
            return "納品額は0円以上の整数で入力してください。";
        }

        if (rawError.Contains("open_slips_exist", StringComparison.OrdinalIgnoreCase))
        {
            return "未会計の伝票があります。すべて会計してから締めてください。";
        }

        if (rawError.Contains("attendance_required", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤キャストを1名以上入力してください。";
        }

        if (rawError.Contains("invalid_attendance_clock_in_time", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤時刻を確認してください。";
        }

        if (rawError.Contains("store_attendance_cast_not_found", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("attendance_cast_required", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤キャストの選択内容を確認してください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"DB更新に失敗しました。{rawError}";
    }

    private static string ToClosingAttendanceFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "勤怠入力を保存できませんでした。";
        }

        if (rawError.Contains("invalid_attendance_clock_out_time", StringComparison.OrdinalIgnoreCase))
        {
            return "退勤時刻を確認してください。";
        }

        if (rawError.Contains("invalid_attendance_clock_in_time", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤時刻を確認してください。";
        }

        if (rawError.Contains("attendance_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "出勤登録済みキャストの選択内容を確認してください。";
        }

        if (rawError.Contains("attendance_required", StringComparison.OrdinalIgnoreCase))
        {
            return "退勤情報を1名以上入力してください。";
        }

        return ToFriendlyError(rawError);
    }
}
