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

    private sealed record AttendanceEntryPayload(
        [property: JsonPropertyName("cast_id")] long CastId,
        [property: JsonPropertyName("clock_in_time")] string ClockInTime,
        [property: JsonPropertyName("is_selected")] bool IsSelected);

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
}
