using System.Text.Json.Serialization;
using ProsperApp.Models;
using static ProsperApp.Services.SupabaseJson;

namespace ProsperApp.Services;

public class SupabaseStoreSlipRepository(
    ISupabaseRpcClient rpcClient,
    IBusinessDayRepository businessDayRepository,
    ILocalSettingsProvider localSettingsProvider,
    IStoreClock storeClock)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IStoreSlipRepository
{
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;

    public async Task<StoreContext?> GetStoreContextAsync(CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_store_context",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        if (rows.Count == 0)
        {
            return null;
        }

        var row = rows[0];
        return new StoreContext
        {
            CompanyId = ReadLong(row, "company_id") ?? 0,
            DepartmentId = ReadLong(row, "department_id") ?? CurrentStoreDepartmentId,
            DepartmentName = ReadString(row, "department_name")
        };
    }

    public async Task<IReadOnlyList<StoreTableOption>> GetTablesAsync(CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_store_tables",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        return rows.Select(row => new StoreTableOption
            {
                TableId = ReadLong(row, "table_id") ?? 0,
                TableCode = ReadString(row, "table_code") ?? string.Empty,
                TableName = ReadString(row, "table_name")
            })
            .Where(x => x.TableId > 0 && !string.IsNullOrWhiteSpace(x.TableCode))
            .ToList();
    }

    public async Task<IReadOnlyList<CastOption>> GetCastsAsync(CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_store_casts",
            new { p_department_id = CurrentStoreDepartmentId },
            ct);

        return rows.Select(row => new CastOption
            {
                CastId = ReadLong(row, "cast_id") ?? 0,
                CastCode = ReadString(row, "cast_code"),
                DepartmentName = ReadString(row, "department_name"),
                DisplayName = ReadString(row, "display_name") ?? string.Empty
            })
            .Where(x => x.CastId > 0 && !string.IsNullOrWhiteSpace(x.DisplayName))
            .ToList();
    }

    public async Task<IReadOnlyList<BusinessSlipListItem>> GetBusinessDaySlipsAsync(long businessDayId, CancellationToken ct)
    {
        var rows = await PostRpcArrayAsync(
            "get_business_day_slips",
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
                AccountingAmount = ReadDecimal(row, "accounting_amount") ?? 0,
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
            "get_store_slip_detail",
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
                    NominationType = ReadString(row, "nomination_type") ?? string.Empty,
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
                    Quantity = ReadDecimal(row, "quantity") ?? 0,
                    UnitPrice = ReadDecimal(row, "unit_price") ?? 0,
                    Amount = ReadDecimal(row, "amount") ?? 0,
                    OrderedAt = ReadDateTimeOffset(row, "ordered_at") ?? DateTimeOffset.MinValue,
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
            return CreateSlipResult.Failed("Supabase SecretKeyが未設定です。伝票を作成できません。");
        }

        if (input.OpenedAt is null || input.TableId is null)
        {
            return CreateSlipResult.Failed("伝票作成に必要な入力が不足しています。");
        }

        var businessDay = await _businessDayRepository.GetCurrentAsync(ct);
        if (businessDay is null)
        {
            return CreateSlipResult.Failed("営業日が開始されていません。営業準備を実行してください。");
        }

        var openedAt = storeClock.ToStoreDateTimeOffset(input.OpenedAt.Value);
        var customerLabels = input.CustomerLabels
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .ToArray();
        var castNominations = input.CastNominations
            .Where(x => x.CastId is not null && !string.IsNullOrWhiteSpace(x.NominationKind))
            .Select(x => new CastNominationPayload(
                x.CastId!.Value,
                ToNominationType(x.NominationKind!),
                ToCompanionTime(x.NominationKind!)))
            .ToArray();

        var result = await RpcClient.PostArrayAsync(
            "create_store_slip",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_table_id = input.TableId.Value,
                p_opened_at = openedAt,
                p_customer_labels = customerLabels,
                p_cast_nominations = castNominations,
                p_memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim()
            },
            requireSecretKey: true,
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
            return SlipMutationResult.Failed("Supabase SecretKeyが未設定です。伝票を更新できません。");
        }

        var labels = customerLabels
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .ToArray();

        if (slipId <= 0 || labels.Length == 0)
        {
            return SlipMutationResult.Failed("客追加に必要な入力が不足しています。");
        }

        var result = await RpcClient.PostArrayAsync(
            "add_store_slip_customers",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId,
                p_customer_labels = labels,
                p_entered_at = storeClock.ToStoreDateTimeOffset(enteredAt)
            },
            requireSecretKey: true,
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
            return SlipMutationResult.Failed("Supabase SecretKeyが未設定です。伝票を更新できません。");
        }

        var payload = nominations
            .Where(x => x.CastId is not null && !string.IsNullOrWhiteSpace(x.NominationKind))
            .Select(x => new CastNominationPayload(
                x.CastId!.Value,
                ToNominationType(x.NominationKind!),
                ToCompanionTime(x.NominationKind!)))
            .ToArray();

        if (slipId <= 0 || payload.Length == 0)
        {
            return SlipMutationResult.Failed("指名追加に必要な入力が不足しています。");
        }

        var result = await RpcClient.PostArrayAsync(
            "add_store_slip_nominations",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_id = slipId,
                p_cast_nominations = payload
            },
            requireSecretKey: true,
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
            return SlipMutationResult.Failed("Supabase SecretKeyが未設定です。伝票を更新できません。");
        }

        if (slipCustomerId <= 0)
        {
            return SlipMutationResult.Failed("退店する客を選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "leave_store_slip_customer",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_customer_id = slipCustomerId,
                p_left_at = storeClock.ToStoreDateTimeOffset(leftAt)
            },
            requireSecretKey: true,
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
            return SlipMutationResult.Failed("Supabase SecretKeyが未設定です。伝票を更新できません。");
        }

        if (slipCustomerId <= 0)
        {
            return SlipMutationResult.Failed("客を選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "update_store_slip_customer_label",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_slip_customer_id = slipCustomerId,
                p_customer_label = string.IsNullOrWhiteSpace(customerLabel) ? null : customerLabel.Trim()
            },
            requireSecretKey: true,
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
            return SlipMutationResult.Failed("Supabase SecretKeyが未設定です。伝票を更新できません。");
        }

        if (orderLineId <= 0)
        {
            return SlipMutationResult.Failed("削除する注文を選択してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "void_store_order_line",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_order_line_id = orderLineId
            },
            requireSecretKey: true,
            ct);

        if (!result.Succeeded)
        {
            return SlipMutationResult.Failed(ToFriendlyError(result.ErrorMessage));
        }

        return SlipMutationResult.Success(1);
    }

    private sealed record CastNominationPayload(
        [property: JsonPropertyName("cast_id")] long CastId,
        [property: JsonPropertyName("nomination_type")] string NominationType,
        [property: JsonPropertyName("companion_time")] string? CompanionTime);

    private static string ToNominationType(string nominationKind)
    {
        return nominationKind switch
        {
            "companion_18" or "companion_19" or "companion_20" => "companion",
            "in_store" => "in_store",
            _ => "nomination"
        };
    }

    private static string? ToCompanionTime(string nominationKind)
    {
        return nominationKind switch
        {
            "companion_18" => "18:00",
            "companion_19" => "19:00",
            "companion_20" => "20:00",
            _ => null
        };
    }

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "伝票を作成できません。";
        }

        if (rawError.Contains("business_day_not_open", StringComparison.OrdinalIgnoreCase))
        {
            return "営業日が開始されていません。";
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
            return "削除する注文を確認してください。";
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
}
