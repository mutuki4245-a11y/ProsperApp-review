using System.Collections.Concurrent;
using System.Text.Json;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Infrastructure.Caching;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public sealed class SupabaseStoreMasterBootstrapper(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider,
    IApplicationCache cache) : IStoreMasterBootstrapper
{
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> DepartmentLocks = new();

    private readonly ISupabaseRpcClient _rpcClient = rpcClient;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;
    private readonly IApplicationCache _cache = cache;

    public Task<Result<StoreBootstrapPayload>> GetStoreBootstrapAsync(CancellationToken ct) =>
        FetchAsync(GetCurrentDepartmentId(), ct);

    public async Task<Result<StoreBootstrapPayload>> EnsureAsync(CancellationToken ct)
    {
        var departmentId = GetCurrentDepartmentId();
        if (!CanLoad(departmentId, out var failure))
        {
            return failure;
        }

        var cacheKey = StoreMasterCacheKeys.BootstrapPayload(departmentId);
        if (_cache.TryGetValue(cacheKey, out StoreBootstrapPayload? cached) && cached is not null)
        {
            return Result<StoreBootstrapPayload>.Success(cached);
        }

        var gate = DepartmentLocks.GetOrAdd(departmentId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return Result<StoreBootstrapPayload>.Success(cached);
            }

            return await FetchAsync(departmentId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Result<StoreBootstrapPayload>> FetchAsync(long departmentId, CancellationToken ct)
    {
        if (!CanLoad(departmentId, out var failure))
        {
            return failure;
        }

        var result = await _rpcClient.PostArrayAsync(
            "store.get_store_bootstrap",
            new { p_department_id = departmentId },
            ct);
        if (!result.Succeeded || result.Rows.Count == 0)
        {
            return Result<StoreBootstrapPayload>.Failure(
                IsPermissionError(result.ErrorMessage)
                    ? ResultFailureKind.PermissionDenied
                    : ResultFailureKind.Unavailable,
                ToFriendlyError(result.ErrorMessage));
        }

        var payload = new StoreBootstrapPayload(departmentId, result.Rows[0].Clone());
        StoreMasterCacheKeys.SetMaster(
            _cache,
            StoreMasterCacheKeys.BootstrapPayload(departmentId),
            payload,
            "店舗マスタ一括取得");
        HydrateCaches(payload);
        return Result<StoreBootstrapPayload>.Success(payload);
    }

    private void HydrateCaches(StoreBootstrapPayload payload)
    {
        var departmentId = payload.DepartmentId;
        var row = payload.Row;

        if (StoreBootstrapJson.TryReadObject(row, "store_context", out var contextRow))
        {
            StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.StoreContext(departmentId), new StoreContext
            {
                CompanyId = ReadLong(contextRow, "company_id") ?? 0,
                DepartmentId = ReadLong(contextRow, "department_id") ?? departmentId,
                DepartmentName = ReadString(contextRow, "department_name"),
                AttendanceMinuteStep = NormalizeAttendanceMinuteStep(ReadLong(contextRow, "attendance_minute_step")),
                CastSalesAmountBasis = NormalizeCastSalesAmountBasis(ReadString(contextRow, "cast_sales_amount_basis")),
                CastSalesSplitMode = NormalizeCastSalesSplitMode(ReadString(contextRow, "cast_sales_split_mode"))
            }, "店舗コンテキスト");
        }

        var departments = StoreBootstrapJson.ReadArray(row, "departments")
            .Select(item => new DepartmentOption
            {
                DepartmentId = ReadLong(item, "department_id") ?? 0,
                CompanyId = ReadLong(item, "company_id") ?? 0,
                DepartmentCode = ReadString(item, "department_code"),
                DepartmentName = ReadString(item, "department_name") ?? string.Empty
            })
            .Where(item => item.DepartmentId > 0 && !string.IsNullOrWhiteSpace(item.DepartmentName))
            .ToList();
        if (departments.Count > 0)
        {
            StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.Departments, departments, "店舗一覧");
        }

        var tables = StoreBootstrapJson.ReadArray(row, "tables")
            .Select(item => new StoreTableOption
            {
                TableId = ReadLong(item, "table_id") ?? 0,
                TableCode = ReadString(item, "table_code") ?? string.Empty,
                TableName = ReadString(item, "table_name"),
                TableCategoryNo = (int)(ReadLong(item, "table_category_no") ?? 0)
            })
            .Where(item => item.TableId > 0 && !string.IsNullOrWhiteSpace(item.TableCode))
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.Tables(departmentId), tables, "卓番");

        var tableAdminList = StoreBootstrapJson.ReadArray(row, "table_admin_list")
            .Select(item => new StoreTableAdminItem
            {
                TableId = ReadLong(item, "table_id") ?? 0,
                TableCode = ReadString(item, "table_code") ?? string.Empty,
                TableName = ReadString(item, "table_name"),
                TableCategoryNo = (int)(ReadLong(item, "table_category_no") ?? 0),
                SortOrder = (int)(ReadLong(item, "sort_order") ?? 0),
                IsActive = ReadBool(item, "is_active") ?? false
            })
            .Where(item => item.TableId > 0)
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.TableAdminList(departmentId), tableAdminList, "卓番管理一覧");

        var casts = StoreBootstrapJson.ReadArray(row, "casts")
            .Select(item => new CastOption
            {
                CastId = ReadLong(item, "cast_id") ?? 0,
                CastCode = ReadString(item, "cast_code"),
                DepartmentName = ReadString(item, "department_name"),
                DisplayName = ReadString(item, "display_name") ?? string.Empty
            })
            .Where(item => item.CastId > 0 && !string.IsNullOrWhiteSpace(item.DisplayName))
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.StoreCasts(departmentId), casts, "キャスト候補");

        var castAdminList = StoreBootstrapJson.ReadArray(row, "casts_admin")
            .Select(item => new StoreCastAdminItem
            {
                CastId = ReadLong(item, "cast_id") ?? 0,
                DisplayName = ReadString(item, "display_name") ?? string.Empty,
                DrinkMemo = ReadString(item, "drink_memo"),
                JoinedOn = ReadDateOnly(item, "joined_on") ?? DateOnly.MinValue
            })
            .Where(item => item.CastId > 0 && !string.IsNullOrWhiteSpace(item.DisplayName))
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.CastAdminList(departmentId), castAdminList, "キャスト管理一覧");

        var staffs = StoreBootstrapJson.ReadArray(row, "staffs")
            .Select(item => new StaffOption
            {
                StaffId = ReadLong(item, "staff_id") ?? 0,
                StaffCode = ReadString(item, "staff_code"),
                DepartmentName = ReadString(item, "department_name"),
                DisplayName = ReadString(item, "display_name") ?? string.Empty,
                EmploymentType = StoreStaffEmploymentTypes.Normalize(ReadString(item, "employment_type"))
            })
            .Where(item => item.StaffId > 0 && !string.IsNullOrWhiteSpace(item.DisplayName))
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.StoreStaffs(departmentId), staffs, "スタッフ候補");

        var staffAdminList = StoreBootstrapJson.ReadArray(row, "staffs_admin")
            .Select(item => new StoreStaffAdminItem
            {
                StaffId = ReadLong(item, "staff_id") ?? 0,
                DisplayName = ReadString(item, "display_name") ?? string.Empty,
                JoinedOn = ReadDateOnly(item, "joined_on") ?? DateOnly.MinValue,
                EmploymentType = StoreStaffEmploymentTypes.Normalize(ReadString(item, "employment_type"))
            })
            .Where(item => item.StaffId > 0 && !string.IsNullOrWhiteSpace(item.DisplayName))
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.StaffAdminList(departmentId), staffAdminList, "スタッフ管理一覧");

        var orderItems = StoreBootstrapJson.ReadArray(row, "order_items")
            .Select(item => new StoreOrderItemOption
            {
                ItemId = ReadLong(item, "item_id") ?? 0,
                ItemName = ReadString(item, "item_name") ?? string.Empty,
                ItemType = ReadString(item, "item_type") ?? "standard",
                DefaultPrice = ReadDecimal(item, "default_price") ?? 0,
                CategoryCode = ReadString(item, "category_code"),
                CategoryName = ReadString(item, "category_name") ?? "未分類",
                IsCastBackTarget = ReadBool(item, "is_cast_back_target") ?? false,
                CastBackRegularUnitAmount = ReadDecimal(item, "cast_back_regular_unit_amount") ?? 0,
                CastBackNominationUnitAmount = ReadDecimal(item, "cast_back_nomination_unit_amount") ?? 0,
                CastBackType = ReadString(item, "cast_back_type") ?? "drink"
            })
            .Where(item => item.ItemId > 0 && !string.IsNullOrWhiteSpace(item.ItemName))
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.OrderItems(departmentId), orderItems, "注文商品");

        var catalogRows = StoreBootstrapJson.ReadArray(row, "item_admin_catalog");
        var itemCatalog = new StoreItemAdminCatalog
        {
            Categories = catalogRows
                .Where(item => string.Equals(ReadString(item, "row_type"), "category", StringComparison.OrdinalIgnoreCase))
                .Select(item => new StoreItemCategoryAdminItem
                {
                    ItemCategoryId = ReadLong(item, "item_category_id") ?? 0,
                    CategoryCode = ReadString(item, "category_code") ?? string.Empty,
                    CategoryName = ReadString(item, "category_name") ?? string.Empty,
                    SortOrder = (int)(ReadLong(item, "sort_order") ?? 0),
                    IsActive = ReadBool(item, "is_active") ?? false
                })
                .Where(item => item.ItemCategoryId > 0)
                .ToList(),
            Items = catalogRows
                .Where(item => string.Equals(ReadString(item, "row_type"), "item", StringComparison.OrdinalIgnoreCase))
                .Select(item => new StoreItemAdminItem
                {
                    ItemId = ReadLong(item, "item_id") ?? 0,
                    ItemCategoryId = ReadLong(item, "item_category_id"),
                    CategoryCode = ReadString(item, "category_code") ?? string.Empty,
                    CategoryName = ReadString(item, "category_name") ?? string.Empty,
                    ItemName = ReadString(item, "item_name") ?? string.Empty,
                    ItemType = ReadString(item, "item_type") ?? "standard",
                    DefaultPrice = ReadDecimal(item, "default_price") ?? 0,
                    SortOrder = (int)(ReadLong(item, "sort_order") ?? 0),
                    IsActive = ReadBool(item, "is_active") ?? false,
                    IsCastBackTarget = ReadBool(item, "is_cast_back_target") ?? false,
                    CastBackRegularUnitAmount = ReadDecimal(item, "cast_back_regular_unit_amount") ?? 0,
                    CastBackNominationUnitAmount = ReadDecimal(item, "cast_back_nomination_unit_amount") ?? 0,
                    CastBackType = ReadString(item, "cast_back_type") ?? "drink"
                })
                .Where(item => item.ItemId > 0)
                .ToList()
        };
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.ItemAdminCatalog(departmentId), itemCatalog, "商品管理カタログ");

        var nominationOptions = StoreBootstrapJson.ReadArray(row, "nomination_options")
            .Select(item => new NominationBackMasterItem
            {
                NominationKind = ReadString(item, "nomination_kind") ?? ReadString(item, "nomination_type") ?? string.Empty,
                NominationType = ReadString(item, "nomination_type") ?? string.Empty,
                DisplayName = ReadString(item, "display_name") ?? string.Empty,
                CompanionTime = ReadString(item, "companion_time"),
                BackType = ReadString(item, "back_type") ?? "nomination",
                BackUnitAmount = ReadDecimal(item, "back_unit_amount") ?? 0,
                SortOrder = (int)(ReadLong(item, "sort_order") ?? 0),
                IsActive = ReadBool(item, "is_active") ?? true
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.NominationKind))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.DisplayName)
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.NominationBackMaster(departmentId), nominationOptions, "指名バック設定");

        var paymentMethods = StoreBootstrapJson.ReadArray(row, "payment_methods")
            .Select(item => new CheckoutPaymentMethod
            {
                MethodCode = ReadString(item, "method_code") ?? string.Empty,
                MethodName = ReadString(item, "method_name") ?? string.Empty,
                RequiresReceivedAmount = ReadBool(item, "requires_received_amount") ?? false,
                SortOrder = (int)(ReadLong(item, "sort_order") ?? 0)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.MethodCode) && !string.IsNullOrWhiteSpace(item.MethodName))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.MethodCode, StringComparer.Ordinal)
            .ToList();
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.PaymentMethods(departmentId), paymentMethods, "決済方法");

        var pricingPlan = StoreBootstrapJson.TryReadObject(row, "pricing_plan", out var pricingPlanRow)
            ? new StorePricingPlanInputModel
            {
                SetMinutes = (int)(ReadLong(pricingPlanRow, "set_minutes") ?? 60),
                SetUnitPriceSingle = ReadDecimal(pricingPlanRow, "set_unit_price_single") ?? 0,
                SetUnitPricePerCustomer = ReadDecimal(pricingPlanRow, "set_unit_price_per_customer") ?? 0,
                ExtensionUnitPriceSingle = ReadDecimal(pricingPlanRow, "extension_unit_price_single") ?? 0,
                ExtensionUnitPricePerCustomer = ReadDecimal(pricingPlanRow, "extension_unit_price_per_customer") ?? 0,
                IsActive = ReadBool(pricingPlanRow, "is_active") ?? false
            }
            : new StorePricingPlanInputModel();
        StoreMasterCacheKeys.SetMaster(_cache, StoreMasterCacheKeys.PricingPlan(departmentId), pricingPlan, "料金設定");

        if (StoreBootstrapJson.TryReadObject(row, "business_day", out var businessDayRow))
        {
            var businessDay = new StoreBusinessDay
            {
                BusinessDayId = ReadLong(businessDayRow, "business_day_id") ?? 0,
                CompanyId = ReadLong(businessDayRow, "company_id") ?? 0,
                DepartmentId = ReadLong(businessDayRow, "department_id") ?? departmentId,
                BusinessDate = ReadDateOnly(businessDayRow, "business_date") ?? DateOnly.MinValue,
                OpenedAt = ReadDateTimeOffset(businessDayRow, "opened_at") ?? DateTimeOffset.MinValue,
                ClosedAt = ReadDateTimeOffset(businessDayRow, "closed_at"),
                Status = ReadString(businessDayRow, "status") ?? string.Empty,
                Memo = ReadString(businessDayRow, "memo")
            };
            if (businessDay.BusinessDayId > 0)
            {
                StoreMasterCacheKeys.SetRuntime(_cache, StoreMasterCacheKeys.CurrentBusinessDay(departmentId), businessDay, "現在営業日");
                var attendanceCasts = StoreBootstrapJson.ReadArray(row, "attendance_casts")
                    .Select(item => new StoreOrderAttendanceCastOption
                    {
                        CastId = ReadLong(item, "cast_id") ?? 0,
                        DisplayName = ReadString(item, "display_name") ?? string.Empty,
                        DrinkMemo = ReadString(item, "drink_memo"),
                        DepartmentName = ReadString(item, "department_name"),
                        ClockInTime = ReadString(item, "clock_in_time")
                    })
                    .Where(item => item.CastId > 0 && !string.IsNullOrWhiteSpace(item.DisplayName))
                    .ToList();
                StoreMasterCacheKeys.SetRuntime(
                    _cache,
                    StoreMasterCacheKeys.OrderAttendingCasts(departmentId, businessDay.BusinessDayId),
                    attendanceCasts,
                    "注文用出勤キャスト");
            }
        }
        else
        {
            StoreMasterCacheKeys.ClearCurrentBusinessDay(_cache, departmentId);
        }
    }

    private static int NormalizeAttendanceMinuteStep(long? value) =>
        value is 5L or 10L or 15L or 20L or 30L or 60L ? (int)value.Value : 15;

    private static string NormalizeCastSalesAmountBasis(string? value) =>
        value is LocalSettings.CastSalesAmountBasisSubtotal
            ? LocalSettings.CastSalesAmountBasisSubtotal
            : LocalSettings.CastSalesAmountBasisTotal;

    private static string NormalizeCastSalesSplitMode(string? value) =>
        value is LocalSettings.CastSalesSplitModeFull
            ? LocalSettings.CastSalesSplitModeFull
            : LocalSettings.CastSalesSplitModeSplit;

    private long GetCurrentDepartmentId() => _localSettingsProvider.GetCurrent().StoreDepartmentId;

    private bool CanLoad(long departmentId, out Result<StoreBootstrapPayload> failure)
    {
        if (!_rpcClient.HasAccess || departmentId <= 0)
        {
            failure = Result<StoreBootstrapPayload>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
            return false;
        }

        failure = default!;
        return true;
    }

    private static bool IsPermissionError(string? error) =>
        !string.IsNullOrWhiteSpace(error) &&
        (error.Contains("401", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("403", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("permission denied", StringComparison.OrdinalIgnoreCase));

    private static string ToFriendlyError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "店舗マスタを一括取得できませんでした。";
        }

        if (rawError.Contains("store_department_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "店舗設定を確認してください。";
        }

        return IsPermissionError(rawError)
            ? "Edge Function経由のRPC実行権限がありません。prosper-rpcのキー設定を確認してください。"
            : $"店舗マスタを一括取得できませんでした。{rawError}";
    }
}
