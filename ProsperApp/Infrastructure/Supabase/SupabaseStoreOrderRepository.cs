using System.Text.Json.Serialization;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseStoreOrderRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreOrderRepository
{
    public async Task<IReadOnlyList<StoreOrderSlipOption>> GetOpenSlipsAsync(long businessDayId, CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_order_entry_slips",
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
                Memo = ReadString(row, "memo")
            })
            .Where(x => x.SlipId > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<StoreOrderItemOption>> GetItemsAsync(CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_store_order_items",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        return rows.Select(row => new StoreOrderItemOption
            {
                ItemId = ReadLong(row, "item_id") ?? 0,
                ItemName = ReadString(row, "item_name") ?? string.Empty,
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
    }

    public async Task<IReadOnlyList<StoreOrderAttendanceCastOption>> GetAttendanceCastsAsync(long businessDayId, CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_order_attending_casts",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);

        return rows.Select(row => new StoreOrderAttendanceCastOption
            {
                CastId = ReadLong(row, "cast_id") ?? 0,
                DisplayName = ReadString(row, "display_name") ?? string.Empty,
                DepartmentName = ReadString(row, "department_name"),
                ClockInTime = ReadString(row, "clock_in_time")
            })
            .Where(x => x.CastId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
    }

    public async Task<AddStoreOrderLinesResult> AddOrderLinesAsync(long slipId, IReadOnlyList<OrderQueueInputModel> lines, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return AddStoreOrderLinesResult.Failed("Supabase SecretKeyが未設定です。注文を登録できません。");
        }

        if (slipId <= 0 || lines.Count == 0)
        {
            return AddStoreOrderLinesResult.Failed("注文登録に必要な入力が不足しています。");
        }

        var payload = lines
            .Where(x => x.ItemId > 0 && x.Quantity > 0)
            .Select(x => new OrderLinePayload(x.ItemId, x.Quantity, x.CastBackCastId))
            .ToArray();

        if (payload.Length == 0)
        {
            return AddStoreOrderLinesResult.Failed("注文キューに商品がありません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "add_store_order_lines",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId,
                p_order_lines = payload
            },
            requireSecretKey: true,
            ct);

        if (!result.Succeeded)
        {
            return AddStoreOrderLinesResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var insertedCount = result.Rows.Count > 0 ? (int)(ReadLong(result.Rows[0], "inserted_count") ?? 0) : 0;
        return AddStoreOrderLinesResult.Success(insertedCount);
    }

    private sealed record OrderLinePayload(
        [property: JsonPropertyName("item_id")] long ItemId,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("cast_back_cast_id")] long? CastBackCastId);

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

        if (rawError.Contains("invalid_order_quantity", StringComparison.OrdinalIgnoreCase))
        {
            return "注文数量を確認してください。";
        }

        if (rawError.Contains("cast_back_cast_required", StringComparison.OrdinalIgnoreCase))
        {
            return "バック対象商品のキャストを選択してください。";
        }

        if (rawError.Contains("store_order_attendance_cast_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "選択したバック対象キャストは出勤登録されていません。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"注文を登録できません。{rawError}";
    }
}
