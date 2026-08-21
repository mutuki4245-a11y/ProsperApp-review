using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseStoreOrderRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache,
    IStoreMasterBootstrapper masterBootstrapper) : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreOrderRepository
{
    private readonly IApplicationCache _cache = cache;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public async Task<Result<OrderEntryCandidates>> GetCurrentCandidatesAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<OrderEntryCandidates>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_current_order_entry_candidates",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);
        if (!result.Succeeded || result.Rows.Count == 0)
        {
            var failure = RpcFailure<OrderEntryCandidates>(
                result.ErrorMessage,
                "注文対象の伝票を取得できませんでした。");
            return Result<OrderEntryCandidates>.Failure(
                failure.FailureKind ?? ResultFailureKind.Unavailable,
                failure.ErrorMessage ?? "注文対象の伝票を取得できませんでした。");
        }

        var row = result.Rows[0];
        var hasBusinessDay = ReadBool(row, "has_business_day") ?? false;
        var businessDay = hasBusinessDay && TryReadJsonProperty(row, "business_day", JsonValueKind.Object, out var businessDayRow)
            ? new StoreBusinessDay
            {
                BusinessDayId = ReadLong(businessDayRow, "business_day_id") ?? 0,
                CompanyId = ReadLong(businessDayRow, "company_id") ?? 0,
                DepartmentId = ReadLong(businessDayRow, "department_id") ?? CurrentStoreDepartmentId,
                BusinessDate = ReadDateOnly(businessDayRow, "business_date") ?? DateOnly.MinValue,
                OpenedAt = ReadDateTimeOffset(businessDayRow, "opened_at") ?? DateTimeOffset.MinValue,
                ClosedAt = ReadDateTimeOffset(businessDayRow, "closed_at"),
                Status = ReadString(businessDayRow, "status") ?? string.Empty,
                Memo = ReadString(businessDayRow, "memo"),
                BusinessUiRevision = ReadLong(businessDayRow, "business_ui_revision") ?? 0
            }
            : null;
        if (hasBusinessDay && businessDay is null)
        {
            return Result<OrderEntryCandidates>.Failure(
                ResultFailureKind.InvalidResponse,
                "注文候補の営業日情報を取得できませんでした。");
        }

        var slips = TryReadJsonProperty(row, "slips", JsonValueKind.Array, out var slipRows)
            ? ParseOpenSlips(slipRows.EnumerateArray())
            : [];
        var casts = TryReadJsonProperty(row, "attendance_casts", JsonValueKind.Array, out var castRows)
            ? ParseAttendanceCasts(castRows.EnumerateArray())
            : [];
        var revision = ReadString(row, "revision") ?? string.Empty;

        return Result<OrderEntryCandidates>.Success(new OrderEntryCandidates(
            businessDay,
            revision,
            slips,
            casts));
    }

    public async Task<Result<IReadOnlyList<StoreOrderItemOption>>> GetItemsAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<IReadOnlyList<StoreOrderItemOption>>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.OrderItems(departmentId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<StoreOrderItemOption>? cachedItems))
        {
            return Result<IReadOnlyList<StoreOrderItemOption>>.Success(cachedItems ?? []);
        }

        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded)
        {
            return Result<IReadOnlyList<StoreOrderItemOption>>.Failure(
                bootstrap.FailureKind ?? ResultFailureKind.Unavailable,
                bootstrap.ErrorMessage ?? "商品一覧を取得できませんでした。");
        }

        var items = StoreBootstrapJson.ReadArray(bootstrap.Value.Row, "order_items").Select(row => new StoreOrderItemOption
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
        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, items, "注文商品");
        return Result<IReadOnlyList<StoreOrderItemOption>>.Success(items);
    }

    public async Task<Result<OrderEntrySubmitResult>> SubmitCurrentAsync(
        OrderEntrySubmitInput input,
        CancellationToken ct)
    {
        if (!HasRpcAccess() ||
            !Guid.TryParse(input.OperationId, out var operationId) ||
            input.ExpectedBusinessDayId <= 0 ||
            input.ExpectedBusinessDayRevision < 0 ||
            input.Lines.Count is < 1 or > 200 ||
            input.Lines.Any(line =>
                !Guid.TryParse(line.ClientLineId, out _) ||
                line.SlipId is null or <= 0 ||
                line.ItemId <= 0 ||
                line.Quantity <= 0))
        {
            return Result<OrderEntrySubmitResult>.Failure(
                ResultFailureKind.InvalidInput,
                "注文内容を確認してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.submit_current_order_entry_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = operationId.ToString("D"),
                p_expected_business_day_id = input.ExpectedBusinessDayId,
                p_expected_business_day_revision = input.ExpectedBusinessDayRevision,
                p_lines = input.Lines.Select(line => new
                {
                    client_line_id = Guid.Parse(line.ClientLineId).ToString("D"),
                    slip_id = line.SlipId,
                    item_id = line.ItemId,
                    quantity = line.Quantity,
                    cast_back_cast_id = line.CastBackCastId
                })
            },
            ct);
        if (!result.Succeeded || result.Rows.Count == 0)
        {
            var failure = RpcFailure<OrderEntrySubmitResult>(result.ErrorMessage, "注文を登録できませんでした。");
            return Result<OrderEntrySubmitResult>.Failure(
                failure.FailureKind ?? ResultFailureKind.Unavailable,
                failure.ErrorMessage ?? "注文を登録できませんでした。");
        }

        var row = result.Rows[0];
        if (!TryReadJsonProperty(row, "inserted_lines", JsonValueKind.Array, out var insertedLines))
        {
            return Result<OrderEntrySubmitResult>.Failure(
                ResultFailureKind.InvalidResponse,
                "注文登録後の状態を取得できませんでした。");
        }

        var status = ReadString(row, "status") ?? "unavailable";
        var recoveryCandidates = row.TryGetProperty("recovery_candidates", out var recoveryJson) &&
                                 recoveryJson.ValueKind == JsonValueKind.Object
            ? recoveryJson.Clone()
            : (JsonElement?)null;
        return Result<OrderEntrySubmitResult>.Success(new OrderEntrySubmitResult(
            status,
            operationId.ToString("D"),
            (int)(ReadLong(row, "inserted_count") ?? 0),
            insertedLines.Clone(),
            ReadLong(row, "business_day_id"),
            ReadLong(row, "business_day_revision") ?? 0,
            ReadString(row, "message") ?? "注文を登録しました。",
            recoveryCandidates));
    }

    private static IReadOnlyList<StoreOrderSlipOption> ParseOpenSlips(IEnumerable<JsonElement> rows) =>
        rows.Select(row => new StoreOrderSlipOption
            {
                SlipId = ReadLong(row, "slip_id") ?? 0,
                TableId = ReadLong(row, "table_id"),
                TableCode = ReadString(row, "table_code"),
                TableName = ReadString(row, "table_name"),
                OpenedAt = ReadDateTimeOffset(row, "opened_at") ?? DateTimeOffset.MinValue,
                CustomerCount = (int)(ReadLong(row, "customer_count") ?? 0),
                CustomerNames = ReadString(row, "customer_names"),
                NominationCastIds = ReadString(row, "nomination_cast_ids"),
                NominationCastNames = ReadString(row, "nomination_cast_names"),
                Memo = ReadString(row, "memo")
            })
            .Where(x => x.SlipId > 0)
            .ToList();

    private static IReadOnlyList<StoreOrderAttendanceCastOption> ParseAttendanceCasts(IEnumerable<JsonElement> rows) =>
        rows.Select(row => new StoreOrderAttendanceCastOption
            {
                CastId = ReadLong(row, "cast_id") ?? 0,
                DisplayName = ReadString(row, "display_name") ?? string.Empty,
                DrinkMemo = ReadString(row, "drink_memo"),
                DepartmentName = ReadString(row, "department_name"),
                ClockInTime = ReadString(row, "clock_in_time")
            })
            .Where(x => x.CastId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();

    private static bool TryReadJsonProperty(
        JsonElement row,
        string propertyName,
        JsonValueKind expectedKind,
        out JsonElement value)
    {
        if (row.TryGetProperty(propertyName, out value) && value.ValueKind == expectedKind)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "注文を登録できません。";
        }

        if (string.Equals(rawError, "store_order_slip_not_found", StringComparison.Ordinal))
        {
            return "選択した伝票は注文登録できません。";
        }

        if (string.Equals(rawError, "store_order_item_not_found", StringComparison.Ordinal))
        {
            return "注文キューに利用できない商品があります。";
        }

        if (string.Equals(rawError, "store_order_item_not_orderable", StringComparison.Ordinal))
        {
            return "システム商品は注文端末から登録できません。";
        }

        if (string.Equals(rawError, "invalid_order_quantity", StringComparison.Ordinal))
        {
            return "注文数量を確認してください。";
        }

        if (string.Equals(rawError, "store_order_attendance_cast_not_found", StringComparison.Ordinal))
        {
            return "選択したバック対象キャストは勤怠入力で出勤登録されていません。";
        }

        if (rawError is "access_denied" or "invalid_signature")
        {
            return PermissionErrorMessage();
        }

        return "注文を登録できません。";
    }
}
