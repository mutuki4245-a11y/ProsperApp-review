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
            return Result<StoreBootstrapPayload>.Success(cached with { WasFetched = false });
        }

        var gate = DepartmentLocks.GetOrAdd(departmentId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return Result<StoreBootstrapPayload>.Success(cached with { WasFetched = false });
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
            "store.get_business_home_bootstrap_v2",
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

        var payload = new StoreBootstrapPayload(departmentId, result.Rows[0].Clone(), false);
        StoreMasterCacheKeys.SetMaster(
            _cache,
            StoreMasterCacheKeys.BootstrapPayload(departmentId),
            payload,
            "店舗マスタ一括取得");
        HydrateCaches(payload);
        return Result<StoreBootstrapPayload>.Success(payload with { WasFetched = true });
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
