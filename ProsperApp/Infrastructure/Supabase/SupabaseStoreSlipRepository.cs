using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseStoreSlipRepository(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache,
    IStoreClock storeClock,
    IStoreMasterBootstrapper masterBootstrapper)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreSlipRepository
{
    private readonly IApplicationCache _cache = cache;
    private readonly IStoreMasterBootstrapper _masterBootstrapper = masterBootstrapper;

    public async Task<Result<StoreContext>> GetStoreContextAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<StoreContext>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.StoreContext(departmentId);
        if (_cache.TryGetValue(cacheKey, out StoreContext? cachedContext))
        {
            return cachedContext is not null
                ? Result<StoreContext>.Success(cachedContext)
                : Result<StoreContext>.Failure(
                    ResultFailureKind.InvalidResponse,
                    "店舗設定のキャッシュを読み取れませんでした。");
        }

        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded || !StoreBootstrapJson.TryReadObject(bootstrap.Value.Row, "store_context", out var contextJson))
        {
            return Result<StoreContext>.Failure(
                bootstrap.FailureKind ?? ResultFailureKind.Unavailable,
                bootstrap.ErrorMessage ?? "店舗設定を取得できませんでした。");
        }

        var context = ParseStoreContext(contextJson, departmentId);
        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, context, "店舗コンテキスト");
        return Result<StoreContext>.Success(context);
    }

    public async Task<Result<IReadOnlyList<StoreTableOption>>> GetTablesAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<IReadOnlyList<StoreTableOption>>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.Tables(departmentId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<StoreTableOption>? cachedTables))
        {
            return Result<IReadOnlyList<StoreTableOption>>.Success(cachedTables ?? []);
        }

        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded)
        {
            return RpcFailure<IReadOnlyList<StoreTableOption>>(
                bootstrap.ErrorMessage,
                "卓番一覧を取得できませんでした。");
        }

        var tables = ParseTables(StoreBootstrapJson.ReadArray(bootstrap.Value.Row, "tables"));
        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, tables, "卓番");
        return Result<IReadOnlyList<StoreTableOption>>.Success(tables);
    }

    public async Task<BusinessHomeBootstrapResult> GetBusinessHomeBootstrapAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return BusinessHomeBootstrapResult.Failed("店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded)
        {
            return BusinessHomeBootstrapResult.Failed(ToBootstrapFriendlyError(bootstrap.ErrorMessage));
        }

        var row = bootstrap.Value.Row;
        if (!TryReadObject(row, "store_context", out var contextJson))
        {
            return BusinessHomeBootstrapResult.Failed("店舗設定の応答形式が正しくありません。");
        }

        var context = ParseStoreContext(contextJson, departmentId);
        var tables = ParseTables(StoreBootstrapJson.ReadArray(row, "tables"));
        var nominationOptions = ParseNominationOptions(StoreBootstrapJson.ReadArray(row, "nomination_options"));
        var orderItems = ParseOrderItems(StoreBootstrapJson.ReadArray(row, "order_items"));
        var paymentMethods = ParsePaymentMethods(StoreBootstrapJson.ReadArray(row, "payment_methods"));
        StoreBusinessDay? businessDay = null;
        IReadOnlyList<StoreOrderAttendanceCastOption> attendanceCasts = [];
        JsonElement? snapshot = null;
        if (bootstrap.Value.WasFetched)
        {
            if (TryReadObject(row, "business_day", out var businessDayJson))
            {
                businessDay = ParseBusinessDay(businessDayJson);
            }
            attendanceCasts = ParseAttendanceCasts(StoreBootstrapJson.ReadArray(row, "attendance_casts"));
            if (TryReadObject(row, "snapshot", out var snapshotJson))
            {
                snapshot = snapshotJson.Clone();
            }
        }

        StoreMasterCacheKeys.SetMaster(
            _cache,
            StoreMasterCacheKeys.StoreContext(departmentId),
            context,
            "店舗コンテキスト");
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.Tables(departmentId), tables, "卓番");
        StoreMasterCacheKeys.SetMaster(
            _cache,
            StoreMasterCacheKeys.NominationBackMaster(departmentId),
            nominationOptions,
            "指名バック設定");
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.OrderItems(departmentId), orderItems, "注文商品");
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.PaymentMethods(departmentId), paymentMethods, "決済方法");

        return BusinessHomeBootstrapResult.Success(
            context,
            businessDay,
            tables,
            nominationOptions,
            orderItems,
            attendanceCasts,
            paymentMethods,
            snapshot);
    }


    public async Task<Result<CurrentBusinessHomeSnapshotResult>> GetCurrentBusinessHomeSnapshotAsync(
        long? knownRevision,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<CurrentBusinessHomeSnapshotResult>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_current_business_home_snapshot",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_known_revision = knownRevision
            },
            ct);
        if (!result.Succeeded || result.Rows.Count == 0)
        {
            var failure = RpcFailure<CurrentBusinessHomeSnapshotResult>(
                result.ErrorMessage,
                "営業中の伝票を取得できませんでした。");
            return Result<CurrentBusinessHomeSnapshotResult>.Failure(
                failure.FailureKind ?? ResultFailureKind.Unavailable,
                failure.ErrorMessage ?? "営業中の伝票を取得できませんでした。");
        }

        var row = result.Rows[0];
        var hasBusinessDay = ReadBool(row, "has_business_day") ?? false;
        var revision = ReadLong(row, "business_day_revision") ?? 0;
        var unchanged = ReadBool(row, "unchanged") ?? false;
        var businessDay = hasBusinessDay && TryReadObject(row, "business_day", out var businessDayJson)
            ? ParseBusinessDay(businessDayJson)
            : null;
        var snapshot = row.TryGetProperty("snapshot", out var snapshotJson) &&
                       snapshotJson.ValueKind == JsonValueKind.Object
            ? snapshotJson.Clone()
            : (JsonElement?)null;

        var attendanceCasts = row.TryGetProperty("attendance_casts", out var attendanceJson) &&
                              attendanceJson.ValueKind == JsonValueKind.Array
            ? attendanceJson.Clone()
            : (JsonElement?)null;

        if ((hasBusinessDay && businessDay is null) || (!unchanged && snapshot is null))
        {
            return Result<CurrentBusinessHomeSnapshotResult>.Failure(
                ResultFailureKind.InvalidResponse,
                "営業中snapshotの応答形式が正しくありません。");
        }

        return Result<CurrentBusinessHomeSnapshotResult>.Success(new CurrentBusinessHomeSnapshotResult(
            businessDay,
            businessDay?.BusinessDate ?? storeClock.GetCurrentBusinessDate(),
            revision,
            unchanged,
            attendanceCasts,
            snapshot));
    }


    public async Task<BusinessHomeChangeFlushResult> FlushBusinessHomeChangesAsync(
        BusinessHomeChangeFlushInput input,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return BusinessHomeChangeFlushResult.Failed(
                "Supabase Edge Function設定が未設定です。営業中の変更を保存できません。",
                ResultFailureKind.NotConfigured,
                "unavailable");
        }

        if (!Guid.TryParse(input.BatchId, out _) ||
            input.Operations.Count > 100 || input.KaraokeLines.Count > 100)
        {
            return BusinessHomeChangeFlushResult.Failed(
                "保存内容を確認してください。",
                ResultFailureKind.InvalidInput,
                "validation_error");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.sync_business_home_changes_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_client_batch_id = input.BatchId,
                p_expected_business_day_id = input.ExpectedBusinessDayId,
                p_expected_business_day_revision = input.ExpectedBusinessDayRevision,
                p_business_date = input.BusinessDate,
                p_operations = input.Operations.Select(operation => new
                {
                    operation_id = operation.OperationId,
                    client_draft_id = operation.ClientDraftId,
                    slip_id = operation.SlipId,
                    operation_type = operation.OperationType,
                    payload = operation.Payload
                }),
                p_karaoke_lines = input.KaraokeLines.Select(line => new
                {
                    operation_id = line.DraftId,
                    draft_id = line.DraftId,
                    slip_id = line.SlipId,
                    quantity = line.Quantity
                })
            },
            ct);

        if (!result.Succeeded || result.Rows.Count == 0 ||
            !result.Rows[0].TryGetProperty("snapshot", out var snapshot) ||
            snapshot.ValueKind is not JsonValueKind.Object)
        {
            var failure = ClassifyFlushFailure(result.ErrorMessage);
            return BusinessHomeChangeFlushResult.Failed(
                failure.Message,
                failure.FailureKind,
                failure.Status);
        }

        var row = result.Rows[0];
        var status = row.TryGetProperty("status", out var statusJson)
            ? statusJson.GetString() ?? "unavailable"
            : "unavailable";
        var businessDay = row.TryGetProperty("business_day", out var businessDayJson)
            ? businessDayJson.Clone()
            : default;
        var revision = row.TryGetProperty("business_day_revision", out var revisionJson) &&
                       revisionJson.TryGetInt64(out var parsedRevision)
            ? parsedRevision
            : 0;
        var operationResults = row.TryGetProperty("operation_results", out var operationRows) &&
            operationRows.ValueKind is JsonValueKind.Array
            ? operationRows.Clone()
            : EmptyJsonArray();
        var karaokeResults = row.TryGetProperty("karaoke_results", out var karaokeRows) &&
            karaokeRows.ValueKind is JsonValueKind.Array
            ? karaokeRows.Clone()
            : EmptyJsonArray();

        return BusinessHomeChangeFlushResult.Success(
            status,
            businessDay,
            revision,
            snapshot.Clone(),
            operationResults,
            karaokeResults);
    }


    private static StoreContext ParseStoreContext(JsonElement row, long fallbackDepartmentId)
    {
        return new StoreContext
        {
            CompanyId = ReadLong(row, "company_id") ?? 0,
            DepartmentId = ReadLong(row, "department_id") ?? fallbackDepartmentId,
            DepartmentName = ReadString(row, "department_name"),
            AttendanceMinuteStep = NormalizeAttendanceMinuteStep(ReadLong(row, "attendance_minute_step")),
            CastSalesAmountBasis = NormalizeCastSalesAmountBasis(ReadString(row, "cast_sales_amount_basis")),
            CastSalesSplitMode = NormalizeCastSalesSplitMode(ReadString(row, "cast_sales_split_mode"))
        };
    }

    private static IReadOnlyList<StoreTableOption> ParseTables(IEnumerable<JsonElement> rows)
    {
        return rows.Select(row => new StoreTableOption
            {
                TableId = ReadLong(row, "table_id") ?? 0,
                TableCode = ReadString(row, "table_code") ?? string.Empty,
                TableName = ReadString(row, "table_name"),
                TableCategoryNo = (int)(ReadLong(row, "table_category_no") ?? 0)
            })
            .Where(x => x.TableId > 0 && !string.IsNullOrWhiteSpace(x.TableCode))
            .ToList();
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

    private static IReadOnlyList<NominationBackMasterItem> ParseNominationOptions(IEnumerable<JsonElement> rows)
    {
        return rows.Select(row => new NominationBackMasterItem
            {
                NominationKind = ReadString(row, "nomination_kind") ?? ReadString(row, "nomination_type") ?? string.Empty,
                NominationType = ReadString(row, "nomination_type") ?? string.Empty,
                DisplayName = ReadString(row, "display_name") ?? string.Empty,
                CompanionTime = ReadString(row, "companion_time"),
                BackType = ReadString(row, "back_type") ?? "nomination",
                BackUnitAmount = ReadDecimal(row, "back_unit_amount") ?? 0,
                SortOrder = (int)(ReadLong(row, "sort_order") ?? 0),
                IsActive = ReadBool(row, "is_active") ?? true
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.NominationKind))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToList();
    }

    private static IReadOnlyList<StoreOrderItemOption> ParseOrderItems(IEnumerable<JsonElement> rows)
    {
        return rows.Select(row => new StoreOrderItemOption
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
    }

    private static IReadOnlyList<StoreOrderAttendanceCastOption> ParseAttendanceCasts(IEnumerable<JsonElement> rows)
    {
        return rows.Select(row => new StoreOrderAttendanceCastOption
            {
                CastId = ReadLong(row, "cast_id") ?? 0,
                DisplayName = ReadString(row, "display_name") ?? string.Empty,
                DrinkMemo = ReadString(row, "drink_memo"),
                DepartmentName = ReadString(row, "department_name"),
                ClockInTime = ReadString(row, "clock_in_time")
            })
            .Where(x => x.CastId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
    }

    private static IReadOnlyList<CheckoutPaymentMethod> ParsePaymentMethods(IEnumerable<JsonElement> rows)
    {
        return rows.Select(row => new CheckoutPaymentMethod
            {
                MethodCode = ReadString(row, "method_code") ?? string.Empty,
                MethodName = ReadString(row, "method_name") ?? string.Empty,
                RequiresReceivedAmount = ReadBool(row, "requires_received_amount") ?? false,
                SortOrder = (int)(ReadLong(row, "sort_order") ?? 0)
            })
            .Where(method =>
                !string.IsNullOrWhiteSpace(method.MethodCode) &&
                !string.IsNullOrWhiteSpace(method.MethodName))
            .OrderBy(method => method.SortOrder)
            .ThenBy(method => method.MethodCode, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement row, string propertyName)
    {
        return row.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.Clone()).ToList()
            : [];
    }

    private static bool TryReadObject(JsonElement row, string propertyName, out JsonElement value)
    {
        value = default;
        if (!row.TryGetProperty(propertyName, out var raw) || raw.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        value = raw.Clone();
        return true;
    }


    private sealed record CastNominationPayload(
        [property: JsonPropertyName("cast_id")] long CastId,
        [property: JsonPropertyName("nomination_kind")] string NominationKind,
        [property: JsonPropertyName("nomination_price")] decimal NominationPrice);



    private static int NormalizeAttendanceMinuteStep(long? value)
    {
        return value is 5L or 10L or 15L or 20L or 30L or 60L
            ? (int)value.Value
            : 15;
    }

    private static string NormalizeCastSalesAmountBasis(string? value)
    {
        return value is LocalSettings.CastSalesAmountBasisSubtotal
            ? LocalSettings.CastSalesAmountBasisSubtotal
            : LocalSettings.CastSalesAmountBasisTotal;
    }

    private static string NormalizeCastSalesSplitMode(string? value)
    {
        return value is LocalSettings.CastSalesSplitModeFull
            ? LocalSettings.CastSalesSplitModeFull
            : LocalSettings.CastSalesSplitModeSplit;
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "伝票を作成できません。";
        }

        if (string.Equals(rawError, "business_day_not_open", StringComparison.Ordinal))
        {
            return "営業中の営業日がありません。";
        }

        if (string.Equals(rawError, "store_department_not_found", StringComparison.Ordinal))
        {
            return "店舗設定を取得できません。設定画面で利用店舗を選択してください。";
        }

        if (string.Equals(rawError, "store_table_not_found", StringComparison.Ordinal))
        {
            return "選択した卓番を利用できません。";
        }

        if (string.Equals(rawError, "store_slip_not_found", StringComparison.Ordinal))
        {
            return "対象の伝票を利用できません。";
        }

        if (string.Equals(rawError, "store_slip_customer_not_found", StringComparison.Ordinal))
        {
            return "退店するお客様を確認してください。";
        }

        if (string.Equals(rawError, "store_order_line_not_found", StringComparison.Ordinal))
        {
            return "対象の注文を確認してください。";
        }

        if (string.Equals(rawError, "store_slip_nomination_not_found", StringComparison.Ordinal))
        {
            return "対象の指名を確認してください。";
        }

        if (string.Equals(rawError, "store_slip_adjustment_not_found", StringComparison.Ordinal))
        {
            return "対象の自由入力明細を確認してください。";
        }

        if (string.Equals(rawError, "invalid_order_quantity", StringComparison.Ordinal))
        {
            return "注文数量を確認してください。";
        }

        if (string.Equals(rawError, "store_cast_not_found", StringComparison.Ordinal))
        {
            return "選択したキャストを利用できません。";
        }

        if (string.Equals(rawError, "cast_not_selected", StringComparison.Ordinal))
        {
            return "指名キャストを候補から選択してください。";
        }

        if (string.Equals(rawError, "duplicate_nomination_cast", StringComparison.Ordinal))
        {
            return "このキャストは既に指名登録されています。";
        }

        if (string.Equals(rawError, "invalid_nomination_type", StringComparison.Ordinal) ||
            string.Equals(rawError, "invalid_companion_time", StringComparison.Ordinal))
        {
            return "指名区分を確認してください。";
        }

        if (string.Equals(rawError, "invalid_nomination_price", StringComparison.Ordinal))
        {
            return "指名料金を確認してください。";
        }

        if (string.Equals(rawError, "store_nomination_fee_item_not_found", StringComparison.Ordinal))
        {
            return "指名料金の商品設定を確認してください。";
        }

        if (string.Equals(rawError, "invalid_adjustment_name", StringComparison.Ordinal))
        {
            return "調整明細の名前を確認してください。";
        }

        if (string.Equals(rawError, "invalid_adjustment_amount", StringComparison.Ordinal))
        {
            return "調整明細の価格を確認してください。";
        }

        if (string.Equals(rawError, "invalid_karaoke_quantity", StringComparison.Ordinal))
        {
            return "カラオケ回数を確認してください。";
        }

        if (string.Equals(rawError, "store_karaoke_item_not_found", StringComparison.Ordinal))
        {
            return "商品マスタのカラオケ商品を確認してください。";
        }

        if (string.Equals(rawError, "invalid_customer_count", StringComparison.Ordinal))
        {
            return "追加するお客様情報を確認してください。";
        }

        if (string.Equals(rawError, "invalid_customer_label", StringComparison.Ordinal))
        {
            return "お客様名は100文字以内で入力してください。";
        }

        if (string.Equals(rawError, "invalid_customer_time", StringComparison.Ordinal))
        {
            return "入退店時刻は5分単位で、伝票の入店時刻以降かつ現在時刻までで入力してください。";
        }

        if (string.Equals(rawError, "invalid_left_at", StringComparison.Ordinal))
        {
            return "退店時刻は入店時刻より後にしてください。";
        }

        if (rawError is "access_denied" or "invalid_signature")
        {
            return PermissionErrorMessage();
        }

        return "伝票を作成できません。";
    }

    private static FlushFailure ClassifyFlushFailure(string? rawError)
    {
        var message = ToFriendlyError(rawError);
        var raw = rawError ?? string.Empty;
        if (raw is "access_denied" or "invalid_signature")
        {
            return new FlushFailure(ResultFailureKind.PermissionDenied, "permission_denied", message);
        }

        if (raw is
            "business_day_revision_conflict" or
            "business_day_closing_required" or
            "business_day_not_open" or
            "business_day_operation_id_reused" or
            "business_home_batch_id_reused" or
            "store_slip_not_found" or
            "store_slip_customer_not_found" or
            "store_order_line_not_found")
        {
            return new FlushFailure(ResultFailureKind.Conflict, "conflict", message);
        }

        if (raw is
            "invalid_business_editor_operation" or
            "invalid_business_editor_payload" or
            "cast_not_selected" or
            "duplicate_nomination_cast")
        {
            return new FlushFailure(ResultFailureKind.InvalidInput, "validation_error", message);
        }

        return new FlushFailure(ResultFailureKind.Unavailable, "unavailable", message);
    }

    private static string ToBootstrapFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "営業中トップの初期データを取得できませんでした。";
        }

        if (string.Equals(rawError, "store_department_not_found", StringComparison.Ordinal))
        {
            return "店舗設定を取得できません。設定画面で利用店舗を選択してください。";
        }

        if (rawError is "access_denied" or "invalid_signature")
        {
            return PermissionErrorMessage();
        }

        return "営業中トップの初期データを取得できませんでした。";
    }

    private static JsonElement EmptyJsonArray()
    {
        using var document = JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }

    private sealed record FlushFailure(ResultFailureKind FailureKind, string Status, string Message);

}
