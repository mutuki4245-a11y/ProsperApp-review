using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ProsperApp.Features.Shared;
using ProsperApp.Infrastructure.Caching;
using ProsperApp.Options;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseBusinessDayRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IStoreClock storeClock,
    IApplicationCache cache,
    IOptions<SupabaseOptions> options) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IBusinessDayRepository
{
    private const string IgnoreClosingRequirementsStatus = "__ignore_closing_requirements__";
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IApplicationCache _cache = cache;
    private readonly SupabaseOptions _options = options.Value;

    public async Task<Result<StoreBusinessDay?>> GetCurrentAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (!HasRpcAccess())
        {
            return Result<StoreBusinessDay?>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.CurrentBusinessDay(departmentId);
        if (!forceRefresh && _cache.TryGetValue(cacheKey, out StoreBusinessDay? cachedBusinessDay))
        {
            if (IsValidBusinessDay(cachedBusinessDay))
            {
                return Result<StoreBusinessDay?>.Success(cachedBusinessDay);
            }

            _cache.Remove(cacheKey);
        }

        var result = await PostRpcArrayResultAsync(
            "store.get_current_business_day",
            new { p_department_id = departmentId },
            ct);
        if (!result.Succeeded)
        {
            return Result<StoreBusinessDay?>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "現在営業日を取得できませんでした。");
        }

        if (result.Value.Count == 0)
        {
            _cache.Remove(cacheKey);
            return Result<StoreBusinessDay?>.Success(null);
        }

        var businessDay = ParseBusinessDay(result.Value[0]);
        if (!IsValidBusinessDay(businessDay))
        {
            _cache.Remove(cacheKey);
            return Result<StoreBusinessDay?>.Failure(
                ResultFailureKind.InvalidResponse,
                "現在営業日の応答形式が正しくありません。");
        }

        StoreMasterCacheKeys.SetRuntime(_cache, cacheKey, businessDay, "現在営業日");
        return Result<StoreBusinessDay?>.Success(businessDay);
    }

    public async Task<BusinessDayEnsureResult> EnsureCurrentAsync(CancellationToken ct)
    {
        var currentBusinessDate = _storeClock.GetCurrentBusinessDate();
        var currentResult = await GetCurrentAsync(ct);
        if (!currentResult.Succeeded)
        {
            return BusinessDayEnsureResult.Failed(
                currentResult.ErrorMessage ?? "現在営業日を確認できませんでした。",
                currentBusinessDate);
        }

        var current = currentResult.Value;
        if (current is not null)
        {
            if (current.BusinessDate == currentBusinessDate)
            {
                return BusinessDayEnsureResult.Success(current, currentBusinessDate);
            }

            if (current.BusinessDate < currentBusinessDate)
            {
                return BusinessDayEnsureResult.ClosingRequired(current, currentBusinessDate);
            }

            return BusinessDayEnsureResult.Failed(
                $"営業日 {current.BusinessDate:yyyy-MM-dd} が現在時刻から見た営業日 {currentBusinessDate:yyyy-MM-dd} より未来です。店舗設定と時刻を確認してください。",
                currentBusinessDate);
        }

        var openResult = await OpenAsync(currentBusinessDate, null, [], ct);
        if (openResult.Succeeded && openResult.BusinessDay is not null)
        {
            return BusinessDayEnsureResult.Success(openResult.BusinessDay, currentBusinessDate);
        }

        var afterOpenResult = await GetCurrentAsync(ct);
        var afterOpen = afterOpenResult.Succeeded ? afterOpenResult.Value : null;
        if (afterOpen is not null)
        {
            if (afterOpen.BusinessDate == currentBusinessDate)
            {
                return BusinessDayEnsureResult.Success(afterOpen, currentBusinessDate);
            }

            if (afterOpen.BusinessDate < currentBusinessDate)
            {
                return BusinessDayEnsureResult.ClosingRequired(afterOpen, currentBusinessDate);
            }
        }

        return BusinessDayEnsureResult.Failed(
            openResult.ErrorMessage ?? "営業日を自動作成できませんでした。",
            currentBusinessDate);
    }

    public async Task<BusinessDayOperationResult> OpenAsync(
        DateOnly businessDate,
        string? memo,
        IReadOnlyCollection<BusinessDayAttendanceInput>? attendanceEntries,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return BusinessDayOperationResult.Failed("Supabase Edge Function設定が未設定です。営業日を更新できません。");
        }

        var attendancePayload = attendanceEntries?
            .Where(x => x.CastId > 0 && x.IsSelected && !string.IsNullOrWhiteSpace(x.ClockInTime))
            .GroupBy(x => x.CastId)
            .Select(x => x.First())
            .Select(x => new AttendanceEntryPayload(x.CastId, x.ClockInTime, x.IsSelected))
            .ToArray() ?? [];
        var trimmedMemo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
        var departmentId = CurrentStoreDepartmentId;

        var result = attendancePayload.Length == 0
            ? await RpcClient.PostArrayAsync(
                "store.open_business_day",
                new
                {
                    p_department_id = departmentId,
                    p_business_date = businessDate,
                    p_memo = trimmedMemo
                },
                ct)
            : await RpcClient.PostArrayAsync(
                "store.open_business_day_with_attendance",
                new
                {
                    p_department_id = departmentId,
                    p_business_date = businessDate,
                    p_attendance_entries = attendancePayload,
                    p_memo = trimmedMemo
                },
                ct);

        if (!result.Succeeded)
        {
            return BusinessDayOperationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return BusinessDayOperationResult.Failed("営業日を開始できませんでした。");
        }

        var businessDay = ParseBusinessDay(result.Rows[0]);
        StoreMasterCacheKeys.SetRuntime(
            _cache,
            StoreMasterCacheKeys.CurrentBusinessDay(departmentId),
            businessDay,
            "現在営業日");
        StoreMasterCacheKeys.ClearNominationBacks(_cache, departmentId);
        StoreMasterCacheKeys.ClearOrderAttendingCasts(_cache, departmentId, businessDay.BusinessDayId);
        return BusinessDayOperationResult.Success(businessDay);
    }

    public async Task<BusinessDayOperationResult> CloseAsync(
        long businessDayId,
        string? memo,
        bool includePendingReceipts,
        bool ignoreClosingRequirements,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return BusinessDayOperationResult.Failed("Supabase Edge Function設定が未設定です。営業日を更新できません。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var result = await RpcClient.PostArrayAsync(
            "store.close_business_day",
            new
            {
                p_department_id = departmentId,
                p_business_day_id = businessDayId,
                p_memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim(),
                p_pending_receipt_status = ignoreClosingRequirements
                    ? IgnoreClosingRequirementsStatus
                    : includePendingReceipts ? _options.PendingStatus : null,
                p_ignore_closing_requirements = ignoreClosingRequirements
            },
            ct);

        if (!result.Succeeded)
        {
            return BusinessDayOperationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return BusinessDayOperationResult.Failed("現在営業中の営業日が見つかりません。");
        }

        StoreMasterCacheKeys.ClearCurrentBusinessDay(_cache, departmentId);
        StoreMasterCacheKeys.ClearNominationBacks(_cache, departmentId);
        StoreMasterCacheKeys.ClearOrderAttendingCasts(_cache, departmentId, businessDayId);
        return BusinessDayOperationResult.Success(ParseBusinessDay(result.Rows[0]));
    }

    public async Task<Result<BusinessDayClosingReadiness>> GetClosingReadinessAsync(
        StoreBusinessDay businessDay,
        bool includePendingReceipts,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<BusinessDayClosingReadiness>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。締め条件を確認できません。");
        }

        if (!IsValidBusinessDay(businessDay))
        {
            return Result<BusinessDayClosingReadiness>.Failure(
                ResultFailureKind.InvalidInput,
                "営業日情報が正しくありません。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.get_business_day_closing_readiness",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDay.BusinessDayId,
                p_pending_receipt_status = includePendingReceipts ? _options.PendingStatus : null
            },
            ct);

        if (!result.Succeeded)
        {
            return await GetLegacyClosingReadinessAsync(businessDay, includePendingReceipts, ct);
        }

        if (result.Value.Count == 0)
        {
            return Result<BusinessDayClosingReadiness>.Failure(
                ResultFailureKind.NotFound,
                "営業中の営業日が見つかりません。");
        }

        var row = result.Value[0];
        return Result<BusinessDayClosingReadiness>.Success(new BusinessDayClosingReadiness
        {
            BusinessDay = businessDay,
            OpenSlipCount = (int)(ReadLong(row, "open_slip_count") ?? 0),
            DrinkDeliveryAmount = ReadDecimal(row, "drink_delivery_amount") ?? 0,
            IsDrinkDeliveryAmountEntered = ReadBool(row, "is_drink_delivery_amount_entered") ?? false,
            AttendanceCount = (int)(ReadLong(row, "attendance_count") ?? 0),
            MissingClockOutCount = (int)(ReadLong(row, "missing_clock_out_count") ?? 0),
            CastSalesRequiredSlipCount = (int)(ReadLong(row, "cast_sales_required_slip_count") ?? 0),
            CastSalesCompletedSlipCount = (int)(ReadLong(row, "cast_sales_completed_slip_count") ?? 0),
            CastSalesMissingSlipCount = (int)(ReadLong(row, "cast_sales_missing_slip_count") ?? 0),
            ChampagneBackRequiredCastCount = (int)(ReadLong(row, "champagne_back_required_cast_count") ?? 0),
            ChampagneBackCompletedCastCount = (int)(ReadLong(row, "champagne_back_completed_cast_count") ?? 0),
            ChampagneBackMissingCastCount = (int)(ReadLong(row, "champagne_back_missing_cast_count") ?? 0),
            ChampagneBackTotalAmount = ReadDecimal(row, "champagne_back_total_amount") ?? 0,
            PendingReceiptCount = (int)(ReadLong(row, "pending_receipt_count") ?? 0),
            ReceiptsEnabled = includePendingReceipts,
            CanCloseFromStore = ReadBool(row, "can_close") ?? false,
            BlockReasonsFromStore = ReadStringArray(row, "block_reasons"),
            CheckedAt = ReadDateTimeOffset(row, "checked_at")
        });
    }

    public async Task<BusinessDayOperationResult> SaveAttendanceAsync(
        long businessDayId,
        IReadOnlyCollection<BusinessDayAttendanceInput> attendanceEntries,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return BusinessDayOperationResult.Failed("Supabase Edge Function設定が未設定です。勤怠入力を更新できません。");
        }

        var payload = attendanceEntries
            .Where(x => x.CastId > 0)
            .GroupBy(x => x.CastId)
            .Select(x => x.Last())
            .Select(x => new AttendanceEntryPayload(x.CastId, x.ClockInTime, x.IsSelected))
            .ToArray();

        if (payload.Length == 0)
        {
            return BusinessDayOperationResult.Failed("出勤キャストを選択してください。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var result = await RpcClient.PostArrayAsync(
            "store.save_business_day_attendance",
            new
            {
                p_department_id = departmentId,
                p_business_day_id = businessDayId,
                p_attendance_entries = payload
            },
            ct);

        if (!result.Succeeded)
        {
            return BusinessDayOperationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        if (result.Rows.Count == 0)
        {
            return BusinessDayOperationResult.Failed("勤怠入力を更新できませんでした。");
        }

        StoreMasterCacheKeys.ClearOrderAttendingCasts(_cache, departmentId, businessDayId);
        return BusinessDayOperationResult.Success(ParseBusinessDay(result.Rows[0]));
    }

    public async Task<Result<int>> GetOpenSlipCountAsync(long businessDayId, CancellationToken ct)
    {
        if (businessDayId <= 0)
        {
            return Result<int>.Failure(ResultFailureKind.InvalidInput, "営業日情報が正しくありません。");
        }

        var result = await PostRpcScalarResultAsync(
            "store.get_open_slip_count",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);
        if (!result.Succeeded)
        {
            return Result<int>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "未会計伝票数を取得できませんでした。");
        }

        var value = NormalizeScalarBody(result.Value);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? Result<int>.Success(count)
            : Result<int>.Failure(
                ResultFailureKind.InvalidResponse,
                "未会計伝票数の応答形式が正しくありません。");
    }

    public async Task<Result<BusinessDayDrinkDeliveryStatus>> GetDrinkDeliveryStatusAsync(
        long businessDayId,
        CancellationToken ct)
    {
        if (businessDayId <= 0)
        {
            return Result<BusinessDayDrinkDeliveryStatus>.Failure(
                ResultFailureKind.InvalidInput,
                "営業日情報が正しくありません。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.get_business_day_drink_delivery_status",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);
        if (!result.Succeeded)
        {
            return Result<BusinessDayDrinkDeliveryStatus>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "酒代入力状況を取得できませんでした。");
        }

        if (result.Value.Count == 0)
        {
            return Result<BusinessDayDrinkDeliveryStatus>.Failure(
                ResultFailureKind.InvalidResponse,
                "酒代入力状況の応答がありません。");
        }

        return Result<BusinessDayDrinkDeliveryStatus>.Success(new BusinessDayDrinkDeliveryStatus
        {
            Amount = ReadDecimal(result.Value[0], "drink_delivery_amount") ?? 0,
            IsEntered = ReadBool(result.Value[0], "is_entered") ?? false
        });
    }

    public async Task<BusinessDayAmountSaveResult> SaveDrinkDeliveryAmountAsync(
        long businessDayId,
        decimal amount,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return BusinessDayAmountSaveResult.Failed("Supabase Edge Function設定が未設定です。納品額を保存できません。");
        }

        if (amount < 0 || decimal.Truncate(amount) != amount)
        {
            return BusinessDayAmountSaveResult.Failed("納品額は0円以上の整数で入力してください。");
        }

        var result = await RpcClient.PostScalarAsync(
            "store.save_business_day_drink_delivery_amount",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId,
                p_drink_delivery_amount = amount
            },
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

    public async Task<Result<IReadOnlyList<BusinessDayClosingAttendanceItem>>> GetClosingAttendanceAsync(
        long businessDayId,
        CancellationToken ct)
    {
        if (businessDayId <= 0)
        {
            return Result<IReadOnlyList<BusinessDayClosingAttendanceItem>>.Failure(
                ResultFailureKind.InvalidInput,
                "営業日情報が正しくありません。");
        }

        var result = await PostRpcArrayResultAsync(
            "store.get_business_day_closing_attendance",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);
        if (!result.Succeeded)
        {
            return Result<IReadOnlyList<BusinessDayClosingAttendanceItem>>.Failure(
                result.FailureKind ?? ResultFailureKind.Unavailable,
                result.ErrorMessage ?? "勤怠入力を取得できませんでした。");
        }

        var attendance = result.Value
            .Select(ParseClosingAttendanceItem)
            .Where(x => x.AttendanceId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
        return Result<IReadOnlyList<BusinessDayClosingAttendanceItem>>.Success(attendance);
    }

    public async Task<BusinessDayAttendanceSaveResult> SaveClosingAttendanceAsync(
        long businessDayId,
        IReadOnlyCollection<BusinessDayClosingAttendanceInput> attendanceEntries,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return BusinessDayAttendanceSaveResult.Failed("Supabase Edge Function設定が未設定です。勤怠入力を保存できません。");
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

        var departmentId = CurrentStoreDepartmentId;
        var result = await RpcClient.PostScalarAsync(
            "store.save_business_day_closing_attendance",
            new
            {
                p_department_id = departmentId,
                p_business_day_id = businessDayId,
                p_attendance_entries = payload
            },
            ct);

        if (!result.Succeeded)
        {
            return BusinessDayAttendanceSaveResult.Failed(ToClosingAttendanceFriendlyError(result.ErrorMessage));
        }

        var value = NormalizeScalarBody(result.Body);
        if (!int.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var savedCount))
        {
            return BusinessDayAttendanceSaveResult.Failed("勤怠入力を保存できませんでした。");
        }

        StoreMasterCacheKeys.ClearOrderAttendingCasts(_cache, departmentId, businessDayId);
        return BusinessDayAttendanceSaveResult.Success(savedCount);
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

    private static bool IsValidBusinessDay(StoreBusinessDay? businessDay)
    {
        return businessDay is { BusinessDayId: > 0 } && businessDay.BusinessDate != DateOnly.MinValue;
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

    private async Task<Result<BusinessDayClosingReadiness>> GetLegacyClosingReadinessAsync(
        StoreBusinessDay businessDay,
        bool includePendingReceipts,
        CancellationToken ct)
    {
        var departmentId = CurrentStoreDepartmentId;
        var openSlipsTask = RpcClient.PostScalarAsync(
            "store.get_open_slip_count",
            new { p_department_id = departmentId, p_business_day_id = businessDay.BusinessDayId },
            ct);
        var drinkTask = RpcClient.PostArrayAsync(
            "store.get_business_day_drink_delivery_status",
            new { p_department_id = departmentId, p_business_day_id = businessDay.BusinessDayId },
            ct);
        var attendanceTask = RpcClient.PostArrayAsync(
            "store.get_business_day_closing_attendance",
            new { p_department_id = departmentId, p_business_day_id = businessDay.BusinessDayId },
            ct);
        var castTask = RpcClient.PostArrayAsync(
            "store.get_business_day_cast_sales_adjustment_status",
            new { p_department_id = departmentId, p_business_day_id = businessDay.BusinessDayId },
            ct);
        var receiptsTask = includePendingReceipts
            ? RpcClient.PostArrayAsync(
                "store.get_pending_receipts",
                new { p_department_id = departmentId, p_status = _options.PendingStatus },
                ct)
            : Task.FromResult(SupabaseRpcResult.Success("[]") with { Rows = [] });

        await Task.WhenAll(openSlipsTask, drinkTask, attendanceTask, castTask, receiptsTask);

        var openSlips = await openSlipsTask;
        var drink = await drinkTask;
        var attendance = await attendanceTask;
        var cast = await castTask;
        var receipts = await receiptsTask;
        if (!openSlips.Succeeded ||
            !drink.Succeeded ||
            !attendance.Succeeded ||
            !cast.Succeeded ||
            !receipts.Succeeded)
        {
            return Result<BusinessDayClosingReadiness>.Failure(
                ResultFailureKind.Unavailable,
                "締め条件の一部を取得できませんでした。時間をおいて再表示してください。");
        }

        if (!int.TryParse(
                NormalizeScalarBody(openSlips.Body),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var openSlipCount))
        {
            return Result<BusinessDayClosingReadiness>.Failure(
                ResultFailureKind.InvalidResponse,
                "未会計伝票数を確認できませんでした。");
        }

        var drinkRow = drink.Rows.FirstOrDefault();
        var castRow = cast.Rows.FirstOrDefault();
        if (drink.Rows.Count == 0 || cast.Rows.Count == 0)
        {
            return Result<BusinessDayClosingReadiness>.Failure(
                ResultFailureKind.InvalidResponse,
                "締め条件の応答が不足しています。");
        }

        return Result<BusinessDayClosingReadiness>.Success(new BusinessDayClosingReadiness
        {
            BusinessDay = businessDay,
            OpenSlipCount = openSlipCount,
            DrinkDeliveryAmount = ReadDecimal(drinkRow, "drink_delivery_amount") ?? 0,
            IsDrinkDeliveryAmountEntered = ReadBool(drinkRow, "is_entered") ?? false,
            AttendanceCount = attendance.Rows.Count,
            MissingClockOutCount = attendance.Rows.Count(row => ReadDateTimeOffset(row, "clock_out_at") is null),
            CastSalesRequiredSlipCount = (int)(ReadLong(castRow, "required_slip_count") ?? 0),
            CastSalesCompletedSlipCount = (int)(ReadLong(castRow, "completed_slip_count") ?? 0),
            CastSalesMissingSlipCount = (int)(ReadLong(castRow, "missing_slip_count") ?? 0),
            PendingReceiptCount = receipts.Rows.Count,
            ReceiptsEnabled = includePendingReceipts
        });
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
            return "出勤キャストを1名以上入力してください。";
        }

        if (rawError.Contains("attendance_clock_out_required", StringComparison.OrdinalIgnoreCase))
        {
            return "退勤時刻が未入力のキャストがいます。";
        }

        if (rawError.Contains("cast_sales_adjustment_required", StringComparison.OrdinalIgnoreCase))
        {
            return "キャスト売上額調整を完了してください。";
        }

        if (rawError.Contains("champagne_back_required", StringComparison.OrdinalIgnoreCase))
        {
            return "シャンパンバックを入力してください。0円の場合も保存してください。";
        }

        if (rawError.Contains("pending_receipts_exist", StringComparison.OrdinalIgnoreCase))
        {
            return "未入力領収書があります。領収書入力を確認してください。";
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
            return "勤怠入力のキャスト選択内容を確認してください。";
        }

        if (rawError.Contains("attendance_required", StringComparison.OrdinalIgnoreCase))
        {
            return "退勤情報を1名以上入力してください。";
        }

        return ToFriendlyError(rawError);
    }
}
