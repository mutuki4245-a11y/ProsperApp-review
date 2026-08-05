using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public class SupabaseStoreSlipRepository(
    ISupabaseRpcClient rpcClient,
    IBusinessDayRepository businessDayRepository,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache,
    IStoreClock storeClock,
    IStoreMasterBootstrapper masterBootstrapper)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreSlipRepository
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
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

    public async Task<Result<IReadOnlyList<CastOption>>> GetCastsAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<IReadOnlyList<CastOption>>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。管理者設定で利用店舗を保存し、RPCキー設定を確認してください。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.StoreCasts(departmentId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<CastOption>? cachedCasts))
        {
            return Result<IReadOnlyList<CastOption>>.Success(cachedCasts ?? []);
        }

        var bootstrap = await _masterBootstrapper.EnsureAsync(ct);
        if (!bootstrap.Succeeded)
        {
            var failure = RpcFailure<IReadOnlyList<CastOption>>(
                bootstrap.ErrorMessage,
                "出勤候補のキャスト情報を取得できません。");
            return Result<IReadOnlyList<CastOption>>.Failure(
                failure.FailureKind ?? ResultFailureKind.Unavailable,
                ToCastLoadFriendlyError(failure.ErrorMessage));
        }

        var casts = StoreBootstrapJson.ReadArray(bootstrap.Value.Row, "casts").Select(row => new CastOption
            {
                CastId = ReadLong(row, "cast_id") ?? 0,
                CastCode = ReadString(row, "cast_code"),
                DepartmentName = ReadString(row, "department_name"),
                DisplayName = ReadString(row, "display_name") ?? string.Empty
            })
            .Where(x => x.CastId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, cacheKey, casts, "キャスト候補");
        return Result<IReadOnlyList<CastOption>>.Success(casts);
    }

    public async Task<BusinessHomeBootstrapResult> GetBusinessHomeBootstrapAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return BusinessHomeBootstrapResult.Failed("店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var bootstrap = await _masterBootstrapper.GetStoreBootstrapAsync(ct);
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
        var businessDay = TryReadObject(row, "business_day", out var businessDayJson)
            ? ParseBusinessDay(businessDayJson)
            : null;
        if (businessDay is { BusinessDayId: <= 0 })
        {
            businessDay = null;
        }

        var tables = ParseTables(StoreBootstrapJson.ReadArray(row, "tables"));
        var nominationOptions = ParseNominationOptions(StoreBootstrapJson.ReadArray(row, "nomination_options"));
        var orderItems = ParseOrderItems(StoreBootstrapJson.ReadArray(row, "order_items"));
        var attendanceCasts = ParseAttendanceCasts(StoreBootstrapJson.ReadArray(row, "attendance_casts"));
        var paymentMethods = ParsePaymentMethods(StoreBootstrapJson.ReadArray(row, "payment_methods"));
        var snapshot = TryReadObject(row, "snapshot", out var snapshotJson)
            ? snapshotJson.Clone()
            : (JsonElement?)null;

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

        if (businessDay is not null)
        {
            StoreMasterCacheKeys.SetRuntime(
                _cache,
                StoreMasterCacheKeys.CurrentBusinessDay(departmentId),
                businessDay,
                "現在営業日");
            StoreMasterCacheKeys.SetRuntime(
                _cache,
                StoreMasterCacheKeys.OrderAttendingCasts(departmentId, businessDay.BusinessDayId),
                attendanceCasts,
                "注文用出勤キャスト");
        }
        else
        {
            StoreMasterCacheKeys.ClearCurrentBusinessDay(_cache, departmentId);
        }

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


    public async Task<BusinessDaySnapshotResult> GetBusinessDaySnapshotAsync(long businessDayId, CancellationToken ct)
    {
        if (!HasRpcAccess() || businessDayId <= 0)
        {
            return BusinessDaySnapshotResult.Failed("営業日を取得できません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_business_day_snapshot",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);

        if (!result.Succeeded || result.Rows.Count == 0 ||
            !result.Rows[0].TryGetProperty("snapshot", out var snapshot) ||
            snapshot.ValueKind is not JsonValueKind.Object)
        {
            return BusinessDaySnapshotResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        return BusinessDaySnapshotResult.Success(snapshot.Clone());
    }

    public async Task<Result<CurrentBusinessHomeSnapshotResult>> GetCurrentBusinessHomeSnapshotAsync(
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
            new { p_department_id = CurrentStoreDepartmentId },
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
        var businessDay = hasBusinessDay && TryReadObject(row, "business_day", out var businessDayJson)
            ? ParseBusinessDay(businessDayJson)
            : null;
        var snapshot = row.TryGetProperty("snapshot", out var snapshotJson) &&
                       snapshotJson.ValueKind == JsonValueKind.Object
            ? snapshotJson.Clone()
            : (JsonElement?)null;

        if (hasBusinessDay && (businessDay is null || snapshot is null))
        {
            return Result<CurrentBusinessHomeSnapshotResult>.Failure(
                ResultFailureKind.InvalidResponse,
                "営業中snapshotの応答形式が正しくありません。");
        }

        return Result<CurrentBusinessHomeSnapshotResult>.Success(new CurrentBusinessHomeSnapshotResult(
            businessDay,
            businessDay?.BusinessDate ?? storeClock.GetCurrentBusinessDate(),
            snapshot));
    }


    public async Task<BusinessHomeChangeFlushResult> FlushBusinessHomeChangesAsync(
        BusinessHomeChangeFlushInput input,
        long businessDayId,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return BusinessHomeChangeFlushResult.Failed("Supabase Edge Function設定が未設定です。営業中の変更を保存できません。");
        }

        if (businessDayId <= 0 || string.IsNullOrWhiteSpace(input.BatchId) || input.BatchId.Length > 100 ||
            input.Operations.Count > 100 || input.KaraokeLines.Count > 100)
        {
            return BusinessHomeChangeFlushResult.Failed("保存内容を確認してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.flush_business_home_changes",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId,
                p_client_batch_id = input.BatchId,
                p_operations = input.Operations.Select(operation => new
                {
                    operation_id = operation.OperationId,
                    slip_id = operation.SlipId,
                    operation_type = operation.OperationType,
                    payload = operation.Payload
                }),
                p_karaoke_lines = input.KaraokeLines.Select(line => new
                {
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
            return BusinessHomeChangeFlushResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var row = result.Rows[0];
        var operationResults = row.TryGetProperty("operation_results", out var operationRows) &&
            operationRows.ValueKind is JsonValueKind.Array
            ? operationRows.Clone()
            : EmptyJsonArray();
        var karaokeResults = row.TryGetProperty("karaoke_results", out var karaokeRows) &&
            karaokeRows.ValueKind is JsonValueKind.Array
            ? karaokeRows.Clone()
            : EmptyJsonArray();

        return BusinessHomeChangeFlushResult.Success(snapshot.Clone(), operationResults, karaokeResults);
    }


    public async Task<CreateSlipResult> CreateSlipAsync(CreateSlipInputModel input, CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return CreateSlipResult.Failed("Supabase Edge Function設定が未設定です。伝票を作成できません。");
        }

        if (input.OpenedAt is null || input.TableId is null)
        {
            return CreateSlipResult.Failed("伝票作成に必要な入力が不足しています。");
        }

        var ensureResult = await _businessDayRepository.EnsureCurrentAsync(ct);
        if (!ensureResult.Succeeded || ensureResult.BusinessDay is null)
        {
            return CreateSlipResult.Failed(ensureResult.ErrorMessage ?? "営業日を自動作成できませんでした。");
        }

        var openedAt = storeClock.ToStoreDateTimeOffset(input.OpenedAt.Value);
        var customerLabels = input.CustomerLabels
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .ToArray();
        var castNominations = input.CastNominations
            .Where(x => x.CastId is not null && !string.IsNullOrWhiteSpace(x.NominationKind))
            .Select(x => new CastNominationPayload(
                x.CastId!.Value,
                x.NominationKind!.Trim(),
                x.NominationPrice))
            .ToArray();

        var result = await RpcClient.PostArrayAsync(
            "store.create_slip",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_table_id = input.TableId.Value,
                p_opened_at = openedAt,
                p_customer_labels = customerLabels,
                p_cast_nominations = castNominations,
                p_memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim()
            },
            ct);

        if (!result.Succeeded)
        {
            return CreateSlipResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var slipId = result.Rows.Count > 0 ? ReadLong(result.Rows[0], "slip_id") : null;
        return slipId is null
            ? CreateSlipResult.Failed("作成した伝票IDを取得できません。")
            : CreateSlipResult.Success(slipId.Value);
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
            Memo = ReadString(row, "memo")
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

        if (rawError.Contains("business_day_not_open", StringComparison.OrdinalIgnoreCase))
        {
            return "営業中の営業日がありません。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を取得できません。設定画面で利用店舗を選択してください。";
        }

        if (rawError.Contains("store_table_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "選択した卓番を利用できません。";
        }

        if (rawError.Contains("store_slip_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "対象の伝票を利用できません。";
        }

        if (rawError.Contains("store_slip_customer_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "退店するお客様を確認してください。";
        }

        if (rawError.Contains("store_order_line_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "対象の注文を確認してください。";
        }

        if (rawError.Contains("store_slip_nomination_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "対象の指名を確認してください。";
        }

        if (rawError.Contains("store_slip_adjustment_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "対象の自由入力明細を確認してください。";
        }

        if (rawError.Contains("invalid_order_quantity", StringComparison.OrdinalIgnoreCase))
        {
            return "注文数量を確認してください。";
        }

        if (rawError.Contains("store_cast_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "選択したキャストを利用できません。";
        }

        if (rawError.Contains("cast_not_selected", StringComparison.OrdinalIgnoreCase))
        {
            return "指名キャストを候補から選択してください。";
        }

        if (rawError.Contains("duplicate_nomination_cast", StringComparison.OrdinalIgnoreCase))
        {
            return "このキャストは既に指名登録されています。";
        }

        if (rawError.Contains("invalid_nomination_type", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("invalid_companion_time", StringComparison.OrdinalIgnoreCase))
        {
            return "指名区分を確認してください。";
        }

        if (rawError.Contains("invalid_nomination_price", StringComparison.OrdinalIgnoreCase))
        {
            return "指名料金を確認してください。";
        }

        if (rawError.Contains("store_nomination_fee_item_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "指名料金の商品設定を確認してください。";
        }

        if (rawError.Contains("invalid_adjustment_name", StringComparison.OrdinalIgnoreCase))
        {
            return "調整明細の名前を確認してください。";
        }

        if (rawError.Contains("invalid_adjustment_amount", StringComparison.OrdinalIgnoreCase))
        {
            return "調整明細の価格を確認してください。";
        }

        if (rawError.Contains("invalid_karaoke_quantity", StringComparison.OrdinalIgnoreCase))
        {
            return "カラオケ回数を確認してください。";
        }

        if (rawError.Contains("store_karaoke_item_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "商品マスタのカラオケ商品を確認してください。";
        }

        if (rawError.Contains("invalid_customer_count", StringComparison.OrdinalIgnoreCase))
        {
            return "追加するお客様情報を確認してください。";
        }

        if (rawError.Contains("invalid_customer_label", StringComparison.OrdinalIgnoreCase))
        {
            return "お客様名は100文字以内で入力してください。";
        }

        if (rawError.Contains("invalid_customer_time", StringComparison.OrdinalIgnoreCase))
        {
            return "入退店時刻は5分単位で、伝票の入店時刻以降かつ現在時刻までで入力してください。";
        }

        if (rawError.Contains("invalid_left_at", StringComparison.OrdinalIgnoreCase))
        {
            return "退店時刻は入店時刻より後にしてください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"伝票を作成できません。{rawError}";
    }

    private static string ToBootstrapFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "営業中トップの初期データを取得できませんでした。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を取得できません。設定画面で利用店舗を選択してください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"営業中トップの初期データを取得できませんでした。{rawError}";
    }

    private static JsonElement EmptyJsonArray()
    {
        using var document = JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }

    private static string ToCastLoadFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "出勤候補のキャスト情報を取得できません。Supabase RPC設定を確認してください。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を取得できません。管理者設定で利用店舗を選択してください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"出勤候補のキャスト情報を取得できません。{rawError}";
    }
}
