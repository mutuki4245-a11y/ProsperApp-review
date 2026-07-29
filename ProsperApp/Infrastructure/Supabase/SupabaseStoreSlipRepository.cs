using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseStoreSlipRepository(
    ISupabaseRpcClient rpcClient,
    IBusinessDayRepository businessDayRepository,
    ILocalSettingsProvider localSettingsProvider,
    IMemoryCache memoryCache,
    IStoreClock storeClock)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreSlipRepository
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IMemoryCache _memoryCache = memoryCache;

    public async Task<StoreContext?> GetStoreContextAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return null;
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.StoreContext(departmentId);
        if (_memoryCache.TryGetValue(cacheKey, out StoreContext? cachedContext))
        {
            return cachedContext;
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_context",
            new { p_department_id = departmentId },
            ct);

        if (!result.Succeeded || result.Rows.Count == 0)
        {
            return null;
        }

        var row = result.Rows[0];
        var context = new StoreContext
        {
            CompanyId = ReadLong(row, "company_id") ?? 0,
            DepartmentId = ReadLong(row, "department_id") ?? departmentId,
            DepartmentName = ReadString(row, "department_name"),
            AttendanceMinuteStep = NormalizeAttendanceMinuteStep(ReadLong(row, "attendance_minute_step")),
            CastSalesAmountBasis = NormalizeCastSalesAmountBasis(ReadString(row, "cast_sales_amount_basis")),
            CastSalesSplitMode = NormalizeCastSalesSplitMode(ReadString(row, "cast_sales_split_mode"))
        };
        _memoryCache.Set(cacheKey, context, StoreMasterCacheKeys.CreateOptions());
        return context;
    }

    public async Task<IReadOnlyList<StoreTableOption>> GetTablesAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return [];
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.Tables(departmentId);
        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<StoreTableOption>? cachedTables))
        {
            return cachedTables ?? [];
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_tables",
            new { p_department_id = departmentId },
            ct);

        if (!result.Succeeded)
        {
            return [];
        }

        var tables = result.Rows.Select(row => new StoreTableOption
            {
                TableId = ReadLong(row, "table_id") ?? 0,
                TableCode = ReadString(row, "table_code") ?? string.Empty,
                TableName = ReadString(row, "table_name"),
                TableCategoryNo = (int)(ReadLong(row, "table_category_no") ?? 0)
            })
            .Where(x => x.TableId > 0 && !string.IsNullOrWhiteSpace(x.TableCode))
            .ToList();
        _memoryCache.Set(cacheKey, tables, StoreMasterCacheKeys.CreateOptions());
        return tables;
    }

    public async Task<IReadOnlyList<CastOption>> GetCastsAsync(CancellationToken ct)
    {
        var result = await GetCastsResultAsync(ct);
        return result.Succeeded ? result.Casts : [];
    }

    public async Task<CastOptionsLoadResult> GetCastsResultAsync(CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return CastOptionsLoadResult.Failed("店舗設定またはSupabase Edge Function設定が未設定です。管理者設定で利用店舗を保存し、RPCキー設定を確認してください。");
        }

        var departmentId = CurrentStoreDepartmentId;
        var cacheKey = StoreMasterCacheKeys.StoreCasts(departmentId);
        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<CastOption>? cachedCasts))
        {
            return CastOptionsLoadResult.Success(cachedCasts ?? []);
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_casts",
            new { p_department_id = departmentId },
            ct);

        if (!result.Succeeded)
        {
            return CastOptionsLoadResult.Failed(ToCastLoadFriendlyError(result.ErrorMessage));
        }

        var casts = result.Rows.Select(row => new CastOption
            {
                CastId = ReadLong(row, "cast_id") ?? 0,
                CastCode = ReadString(row, "cast_code"),
                DepartmentName = ReadString(row, "department_name"),
                DisplayName = ReadString(row, "display_name") ?? string.Empty
            })
            .Where(x => x.CastId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
        _memoryCache.Set(cacheKey, casts, StoreMasterCacheKeys.CreateOptions());
        return CastOptionsLoadResult.Success(casts);
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
