using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseStoreOrderRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IMemoryCache memoryCache) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreOrderRepository
{
    private readonly IMemoryCache _memoryCache = memoryCache;

    public async Task<IReadOnlyList<StoreOrderSlipOption>> GetOpenSlipsAsync(long businessDayId, CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "store.get_order_entry_slips",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);

        return rows.Select(row => new StoreOrderSlipOption
            {
                SlipId = ReadLong(row, "slip_id") ?? 0,
                TableId = ReadLong(row, "table_id"),
                TableCode = ReadString(row, "table_code"),
                TableName = ReadString(row, "table_name"),
                OpenedAt = ReadDateTimeOffset(row, "opened_at") ?? DateTimeOffset.MinValue,
                CustomerCount = (int)(ReadLong(row, "customer_count") ?? 0),
                CustomerNames = ReadString(row, "customer_names"),
                Memo = ReadString(row, "memo")
            })
            .Where(x => x.SlipId > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<StoreOrderItemOption>> GetItemsAsync(CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return [];
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.OrderItems(departmentId);
        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<StoreOrderItemOption>? cachedItems))
        {
            return cachedItems ?? [];
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_order_items",
            new { p_department_id = departmentId },
            ct);

        if (!result.Succeeded)
        {
            return [];
        }

        var items = result.Rows.Select(row => new StoreOrderItemOption
            {
                ItemId = ReadLong(row, "item_id") ?? 0,
                ItemName = ReadString(row, "item_name") ?? string.Empty,
                ItemType = ReadString(row, "item_type") ?? "standard",
                DefaultPrice = ReadDecimal(row, "default_price") ?? 0,
                CategoryCode = ReadString(row, "category_code"),
                CategoryName = ReadString(row, "category_name") ?? "未分類",
                IsCastBackTarget = ReadBool(row, "is_cast_back_target") ?? false,
                CastBackRegularUnitAmount = ReadDecimal(row, "cast_back_regular_unit_amount") ?? 0,
                CastBackNominationUnitAmount = ReadDecimal(row, "cast_back_nomination_unit_amount") ?? 0,
                CastBackType = ReadString(row, "cast_back_type") ?? "drink"
            })
            .Where(x => x.ItemId > 0 && !string.IsNullOrWhiteSpace(x.ItemName))
            .ToList();
        _memoryCache.Set(cacheKey, items, StoreMasterCacheKeys.CreateOptions());
        return items;
    }

    public async Task<IReadOnlyList<StoreOrderAttendanceCastOption>> GetAttendanceCastsAsync(long businessDayId, CancellationToken ct)
    {
        if (!HasRequiredSettings() || businessDayId <= 0)
        {
            return [];
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.OrderAttendingCasts(departmentId, businessDayId);
        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<StoreOrderAttendanceCastOption>? cachedCasts))
        {
            return cachedCasts ?? [];
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_order_attending_casts",
            new
            {
                p_department_id = departmentId,
                p_business_day_id = businessDayId
            },
            ct);

        if (!result.Succeeded)
        {
            return [];
        }

        var casts = result.Rows.Select(row => new StoreOrderAttendanceCastOption
            {
                CastId = ReadLong(row, "cast_id") ?? 0,
                DisplayName = ReadString(row, "display_name") ?? string.Empty,
                DepartmentName = ReadString(row, "department_name"),
                ClockInTime = ReadString(row, "clock_in_time")
            })
            .Where(x => x.CastId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();

        _memoryCache.Set(cacheKey, casts, StoreMasterCacheKeys.CreateRuntimeOptions());
        return casts;
    }

    public async Task<AddStoreOrderLinesResult> AddOrderLinesAsync(long slipId, IReadOnlyList<OrderQueueInputModel> lines, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return AddStoreOrderLinesResult.Failed("Supabase Edge Function設定が未設定です。注文を登録できません。");
        }

        if ((slipId <= 0 && lines.All(x => x.SlipId is null or <= 0)) || lines.Count == 0)
        {
            return AddStoreOrderLinesResult.Failed("注文登録に必要な入力が不足しています。");
        }

        var payload = lines
            .Where(x => x.ItemId > 0 && x.Quantity > 0)
            .Select(x => new OrderLinePayload(x.SlipId, x.ItemId, x.Quantity, x.CastBackCastId))
            .ToArray();

        if (payload.Length == 0)
        {
            return AddStoreOrderLinesResult.Failed("注文キューに商品がありません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.add_order_lines",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId > 0 ? slipId : (long?)null,
                p_order_lines = payload
            },
            ct);

        if (!result.Succeeded)
        {
            return AddStoreOrderLinesResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var insertedCount = ReadInsertedCount(result);
        if (insertedCount <= 0)
        {
            return AddStoreOrderLinesResult.Failed("注文をDBに登録できませんでした。画面を再読み込みして再度お試しください。");
        }

        return AddStoreOrderLinesResult.Success(insertedCount);
    }

    private sealed record OrderLinePayload(
        [property: JsonPropertyName("slip_id")] long? SlipId,
        [property: JsonPropertyName("item_id")] long ItemId,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("cast_back_cast_id")] long? CastBackCastId);

    private static int ReadInsertedCount(SupabaseRpcResult result)
    {
        if (result.Rows.Count > 0)
        {
            var rowCount = TryReadCount(result.Rows[0]);
            if (rowCount is > 0)
            {
                return (int)rowCount.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(result.Body))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(result.Body);
            return TryReadCount(document.RootElement) is { } count ? (int)count : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static long? TryReadCount(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.GetArrayLength() == 0 ? null : TryReadCount(element[0]);
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "inserted_count", "add_order_lines", "result", "data" })
            {
                if (element.TryGetProperty(propertyName, out var value))
                {
                    var count = TryReadCount(value);
                    if (count is not null)
                    {
                        return count;
                    }
                }
            }

            return null;
        }

        return ReadLongValue(element);
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "注文を登録できません。";
        }

        if (rawError.Contains("store_order_slip_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "選択した伝票は注文登録できません。";
        }

        if (rawError.Contains("store_order_item_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "注文キューに利用できない商品があります。";
        }

        if (rawError.Contains("store_order_item_not_orderable", StringComparison.OrdinalIgnoreCase))
        {
            return "システム商品は注文端末から登録できません。";
        }

        if (rawError.Contains("invalid_order_quantity", StringComparison.OrdinalIgnoreCase))
        {
            return "注文数量を確認してください。";
        }

        if (rawError.Contains("store_order_attendance_cast_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "選択したバック対象キャストは勤怠入力で出勤登録されていません。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"注文を登録できません。{rawError}";
    }
}
