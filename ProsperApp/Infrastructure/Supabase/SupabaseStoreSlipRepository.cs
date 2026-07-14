using Microsoft.Extensions.Caching.Memory;
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
        if (!HasRequiredSettings())
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
        if (!HasRequiredSettings())
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
                TableName = ReadString(row, "table_name")
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
        if (!HasRequiredSettings())
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

    public async Task<IReadOnlyList<BusinessSlipListItem>> GetBusinessDaySlipsAsync(long businessDayId, CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "store.get_business_day_slips",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId
            },
            ct);

        return rows.Select(row => new BusinessSlipListItem
            {
                SlipId = ReadLong(row, "slip_id") ?? 0,
                SlipNo = ReadString(row, "slip_no"),
                TableId = ReadLong(row, "table_id"),
                TableCode = ReadString(row, "table_code"),
                TableName = ReadString(row, "table_name"),
                OpenedAt = ReadDateTimeOffset(row, "opened_at") ?? DateTimeOffset.MinValue,
                Status = ReadString(row, "status") ?? string.Empty,
                CustomerCount = (int)(ReadLong(row, "customer_count") ?? 0),
                CustomerNames = ReadString(row, "customer_names"),
                CastNames = NormalizeCastDisplayNameList(ReadString(row, "cast_names")),
                AccountingAmount = ReadDecimal(row, "accounting_amount") ?? 0,
                KaraokeQuantity = ReadDecimal(row, "karaoke_quantity") ?? 0,
                Memo = ReadString(row, "memo")
            })
            .Where(x => x.SlipId > 0)
            .ToList();
    }

    public async Task<SlipDetail?> GetSlipDetailAsync(long slipId, CancellationToken ct)
    {
        if (slipId <= 0)
        {
            return null;
        }

        var rows = await PostRpcArrayAsync(
            "store.get_slip_detail",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId
            },
            ct);

        SlipDetail? detail = null;
        foreach (var row in rows)
        {
            var rowType = ReadString(row, "row_type");
            if (rowType == "slip")
            {
                detail = new SlipDetail
                {
                    SlipId = ReadLong(row, "slip_id") ?? 0,
                    SlipNo = ReadString(row, "slip_no"),
                    BusinessDate = ReadDateOnly(row, "business_date") ?? default,
                    TableId = ReadLong(row, "table_id"),
                    TableCode = ReadString(row, "table_code"),
                    TableName = ReadString(row, "table_name"),
                    OpenedAt = ReadDateTimeOffset(row, "opened_at") ?? DateTimeOffset.MinValue,
                    Status = ReadString(row, "status") ?? string.Empty,
                    CustomerCount = (int)(ReadLong(row, "customer_count") ?? 0),
                    Memo = ReadString(row, "memo")
                };
                continue;
            }

            if (detail is null)
            {
                continue;
            }

            if (rowType == "customer")
            {
                detail.Customers.Add(new SlipDetailCustomer
                {
                    SlipCustomerId = ReadLong(row, "slip_customer_id") ?? 0,
                    LineNo = (int)(ReadLong(row, "line_no") ?? 0),
                    CustomerLabel = ReadString(row, "customer_label"),
                    EnteredAt = ReadDateTimeOffset(row, "entered_at") ?? DateTimeOffset.MinValue,
                    LeftAt = ReadDateTimeOffset(row, "left_at"),
                    Status = ReadString(row, "customer_status") ?? string.Empty
                });
            }
            else if (rowType == "nomination")
            {
                detail.Nominations.Add(new SlipDetailNomination
                {
                    SlipCastId = ReadLong(row, "slip_cast_id") ?? 0,
                    CastId = ReadLong(row, "cast_id") ?? 0,
                    DisplayName = ReadString(row, "cast_display_name") ?? string.Empty,
                    DepartmentName = ReadString(row, "cast_department_name"),
                    NominationKind = ReadString(row, "nomination_kind") ?? string.Empty,
                    NominationType = ReadString(row, "nomination_type") ?? string.Empty,
                    NominationDisplayNameFromMaster = ReadString(row, "nomination_display_name"),
                    NominationPrice = ReadDecimal(row, "nomination_price") ?? 0,
                    StartedAt = ReadDateTimeOffset(row, "started_at") ?? DateTimeOffset.MinValue,
                    Status = ReadString(row, "nomination_status") ?? string.Empty
                });
            }
            else if (rowType == "order")
            {
                detail.Orders.Add(new SlipDetailOrderLine
                {
                    OrderLineId = ReadLong(row, "order_line_id") ?? 0,
                    LineNo = (int)(ReadLong(row, "line_no") ?? 0),
                    ItemNameSnapshot = ReadString(row, "item_name_snapshot") ?? string.Empty,
                    ItemType = ReadString(row, "item_type") ?? "standard",
                    Quantity = ReadDecimal(row, "quantity") ?? 0,
                    UnitPrice = ReadDecimal(row, "unit_price") ?? 0,
                    Amount = ReadDecimal(row, "amount") ?? 0,
                    OrderedAt = ReadDateTimeOffset(row, "ordered_at") ?? DateTimeOffset.MinValue,
                    Status = ReadString(row, "order_status") ?? string.Empty,
                    BackCastId = ReadLong(row, "order_back_cast_id"),
                    BackCastDisplayName = ReadString(row, "order_back_cast_display_name"),
                    BackCastDepartmentName = ReadString(row, "order_back_cast_department_name")
                });
            }
            else if (rowType == "charge")
            {
                detail.ChargeLines.Add(new SlipDetailChargeLine
                {
                    ChargeLineId = ReadLong(row, "charge_line_id") ?? 0,
                    LineNo = (int)(ReadLong(row, "line_no") ?? 0),
                    ChargeType = ReadString(row, "charge_type") ?? string.Empty,
                    LineName = ReadString(row, "item_name_snapshot") ?? string.Empty,
                    Quantity = ReadDecimal(row, "quantity") ?? 0,
                    UnitPrice = ReadDecimal(row, "unit_price") ?? 0,
                    Amount = ReadDecimal(row, "amount") ?? 0,
                    CreatedAt = ReadDateTimeOffset(row, "ordered_at") ?? DateTimeOffset.MinValue,
                    Status = ReadString(row, "order_status") ?? string.Empty
                });
            }
        }

        if (detail is not null)
        {
            detail.CustomerCount = detail.Customers.Count(x => !string.Equals(x.Status, "cancelled", StringComparison.Ordinal));
        }

        return detail?.SlipId > 0 ? detail : null;
    }

    public async Task<CreateSlipResult> CreateSlipAsync(CreateSlipInputModel input, CancellationToken ct)
    {
        if (!HasMutationSettings())
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

    public async Task<SlipMutationResult> AddSlipCustomersAsync(long slipId, IReadOnlyList<string?> customerLabels, DateTime enteredAt, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return SlipMutationResult.Failed("Supabase Edge Function設定が未設定です。伝票を更新できません。");
        }

        var labels = customerLabels
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .ToArray();

        if (slipId <= 0 || labels.Length == 0)
        {
            return SlipMutationResult.Failed("客追加に必要な入力が不足しています。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.add_slip_customers",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId,
                p_customer_labels = labels,
                p_entered_at = storeClock.ToStoreDateTimeOffset(enteredAt)
            },
            ct);

        if (!result.Succeeded)
        {
            return SlipMutationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var insertedCount = result.Rows.Count > 0 ? (int)(ReadLong(result.Rows[0], "inserted_count") ?? 0) : 0;
        return SlipMutationResult.Success(insertedCount);
    }

    public async Task<SlipMutationResult> AddSlipNominationsAsync(long slipId, IReadOnlyList<CastNominationInputModel> nominations, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return SlipMutationResult.Failed("Supabase Edge Function設定が未設定です。伝票を更新できません。");
        }

        var payload = nominations
            .Where(x => x.CastId is not null && !string.IsNullOrWhiteSpace(x.NominationKind))
            .Select(x => new CastNominationPayload(
                x.CastId!.Value,
                x.NominationKind!.Trim(),
                x.NominationPrice))
            .ToArray();

        if (slipId <= 0 || payload.Length == 0)
        {
            return SlipMutationResult.Failed("指名追加に必要な入力が不足しています。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.add_slip_nominations",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId,
                p_cast_nominations = payload
            },
            ct);

        if (!result.Succeeded)
        {
            return SlipMutationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var insertedCount = result.Rows.Count > 0 ? (int)(ReadLong(result.Rows[0], "inserted_count") ?? 0) : 0;
        return SlipMutationResult.Success(insertedCount);
    }

    public async Task<SlipMutationResult> LeaveSlipCustomerAsync(long slipCustomerId, DateTime leftAt, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return SlipMutationResult.Failed("Supabase Edge Function設定が未設定です。伝票を更新できません。");
        }

        if (slipCustomerId <= 0)
        {
            return SlipMutationResult.Failed("退店する客を選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.leave_slip_customer",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_customer_id = slipCustomerId,
                p_left_at = storeClock.ToStoreDateTimeOffset(leftAt)
            },
            ct);

        if (!result.Succeeded)
        {
            return SlipMutationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        return SlipMutationResult.Success(1);
    }

    public async Task<SlipMutationResult> UpdateSlipCustomerLabelAsync(long slipCustomerId, string? customerLabel, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return SlipMutationResult.Failed("Supabase Edge Function設定が未設定です。伝票を更新できません。");
        }

        if (slipCustomerId <= 0)
        {
            return SlipMutationResult.Failed("客を選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.update_slip_customer_label",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_customer_id = slipCustomerId,
                p_customer_label = string.IsNullOrWhiteSpace(customerLabel) ? null : customerLabel.Trim()
            },
            ct);

        if (!result.Succeeded)
        {
            return SlipMutationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        return SlipMutationResult.Success(1);
    }

    public async Task<SlipMutationResult> VoidOrderLineAsync(long orderLineId, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return SlipMutationResult.Failed("Supabase Edge Function設定が未設定です。伝票を更新できません。");
        }

        if (orderLineId <= 0)
        {
            return SlipMutationResult.Failed("削除する注文を選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.void_order_line",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_order_line_id = orderLineId
            },
            ct);

        if (!result.Succeeded)
        {
            return SlipMutationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        return SlipMutationResult.Success(1);
    }

    public async Task<SlipMutationResult> SaveOrderLineQuantitiesAsync(long slipId, IReadOnlyList<OrderLineQuantityInputModel> orderLines, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return SlipMutationResult.Failed("Supabase Edge Function設定が未設定です。注文数量を保存できません。");
        }

        if (slipId <= 0)
        {
            return SlipMutationResult.Failed("注文数量を保存する伝票を確認してください。");
        }

        var payload = orderLines
            .Where(x => x.OrderLineId > 0 && x.Quantity >= 0)
            .Select(x => new OrderLineQuantityPayload(x.OrderLineId, x.Quantity))
            .ToArray();

        if (payload.Length == 0)
        {
            return SlipMutationResult.Failed("保存する注文数量がありません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.save_order_line_quantities",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId,
                p_order_lines = payload
            },
            ct);

        if (!result.Succeeded)
        {
            return SlipMutationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var savedCount = result.Rows.Count > 0 ? (int)(ReadLong(result.Rows[0], "saved_count") ?? 0) : 0;
        return SlipMutationResult.Success(savedCount);
    }

    public async Task<SlipMutationResult> AddSlipAdjustmentAsync(long slipId, SlipAdjustmentInputModel adjustment, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return SlipMutationResult.Failed("Supabase Edge Function設定が未設定です。調整明細を保存できません。");
        }

        if (slipId <= 0)
        {
            return SlipMutationResult.Failed("調整明細を保存する伝票を確認してください。");
        }

        if (string.IsNullOrWhiteSpace(adjustment.LineName))
        {
            return SlipMutationResult.Failed("追加する調整明細を入力してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.add_slip_adjustment",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId,
                p_line_name = adjustment.LineName.Trim(),
                p_amount = adjustment.Amount
            },
            ct);

        if (!result.Succeeded)
        {
            return SlipMutationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var insertedCount = result.Rows.Count > 0 ? (int)(ReadLong(result.Rows[0], "inserted_count") ?? 0) : 0;
        return SlipMutationResult.Success(insertedCount);
    }

    public async Task<SlipMutationResult> SaveKaraokeLinesAsync(long businessDayId, IReadOnlyList<KaraokeQuantityInputModel> karaokeLines, CancellationToken ct)
    {
        if (!HasMutationSettings())
        {
            return SlipMutationResult.Failed("Supabase Edge Function設定が未設定です。カラオケ回数を保存できません。");
        }

        if (businessDayId <= 0)
        {
            return SlipMutationResult.Failed("営業日を確認してください。");
        }

        var payload = karaokeLines
            .Where(x => x.SlipId > 0 && x.Quantity >= 0)
            .Select(x => new KaraokeLinePayload(x.SlipId, x.Quantity))
            .ToArray();

        if (payload.Length == 0)
        {
            return SlipMutationResult.Failed("保存するカラオケ回数がありません。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.save_karaoke_lines",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_business_day_id = businessDayId,
                p_karaoke_lines = payload
            },
            ct);

        if (!result.Succeeded)
        {
            return SlipMutationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        var savedCount = result.Rows.Count > 0 ? (int)(ReadLong(result.Rows[0], "saved_count") ?? 0) : 0;
        return SlipMutationResult.Success(savedCount);
    }

    private sealed record CastNominationPayload(
        [property: JsonPropertyName("cast_id")] long CastId,
        [property: JsonPropertyName("nomination_kind")] string NominationKind,
        [property: JsonPropertyName("nomination_price")] decimal NominationPrice);

    private sealed record KaraokeLinePayload(
        [property: JsonPropertyName("slip_id")] long SlipId,
        [property: JsonPropertyName("quantity")] decimal Quantity);

    private sealed record OrderLineQuantityPayload(
        [property: JsonPropertyName("order_line_id")] long OrderLineId,
        [property: JsonPropertyName("quantity")] decimal Quantity);

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
            return "退店する客を確認してください。";
        }

        if (rawError.Contains("store_order_line_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "対象の注文を確認してください。";
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
            return "追加する客情報を確認してください。";
        }

        if (rawError.Contains("invalid_left_at", StringComparison.OrdinalIgnoreCase))
        {
            return "退店時刻は入店時刻以降で入力してください。";
        }

        if (rawError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionErrorMessage();
        }

        return $"伝票を作成できません。{rawError}";
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
