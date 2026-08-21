using ProsperApp.Infrastructure.Caching;

namespace ProsperApp.Infrastructure.Supabase;

internal static class StoreMasterCacheKeys
{
    public static readonly TimeSpan RuntimeCacheTtl = TimeSpan.FromSeconds(30);

    public static string Departments => "store-master:departments";

    public static string StoreContext(long departmentId) => $"store-master:{departmentId}:context";

    public static string Tables(long departmentId) => $"store-master:{departmentId}:tables";

    public static string TableAdminList(long departmentId) => $"store-master:{departmentId}:table-admin-list";

    public static string StoreCasts(long departmentId) => $"store-master:{departmentId}:casts";

    public static string StoreStaffs(long departmentId) => $"store-master:{departmentId}:staffs";

    public static string OrderItems(long departmentId) => $"store-master:{departmentId}:order-items";

    public static string ItemAdminCatalog(long departmentId) => $"store-master:{departmentId}:item-admin-catalog";

    public static string PaymentMethods(long departmentId) => $"store-master:{departmentId}:payment-methods";

    public static string PricingPlan(long departmentId) => $"store-master:{departmentId}:pricing-plan";

    public static string CastAdminList(long departmentId) => $"store-master:{departmentId}:cast-admin-list";

    public static string StaffAdminList(long departmentId) => $"store-master:{departmentId}:staff-admin-list";

    public static string NominationBackMaster(long departmentId) => $"store-master:{departmentId}:nomination-back-master";

    public static string CurrentBusinessDay(long departmentId) => $"store-runtime:{departmentId}:current-business-day";

    public static string OrderAttendingCasts(long departmentId, long businessDayId) => $"store-runtime:{departmentId}:{businessDayId}:order-attending-casts";

    public static string BootstrapPayload(long departmentId) => $"store-master:{departmentId}:bootstrap-payload";

    public static void SetMaster<T>(
        IApplicationCache cache,
        string key,
        T value,
        string displayName)
    {
        cache.Set(key, value, null, "マスタ", displayName);
    }

    public static void SetRuntime<T>(
        IApplicationCache cache,
        string key,
        T value,
        string displayName)
    {
        cache.Set(key, value, RuntimeCacheTtl, "営業中", displayName);
    }

    public static void ClearItems(IApplicationCache cache, long departmentId)
    {
        cache.Remove(OrderItems(departmentId));
        cache.Remove(ItemAdminCatalog(departmentId));
        ClearBootstrapPayload(cache, departmentId);
    }

    public static void ClearTables(IApplicationCache cache, long departmentId)
    {
        cache.Remove(Tables(departmentId));
        cache.Remove(TableAdminList(departmentId));
        ClearBootstrapPayload(cache, departmentId);
    }

    public static void ClearCasts(IApplicationCache cache, long departmentId)
    {
        cache.Remove(StoreCasts(departmentId));
        cache.Remove(CastAdminList(departmentId));
        ClearBootstrapPayload(cache, departmentId);
    }

    public static void ClearStaffs(IApplicationCache cache, long departmentId)
    {
        cache.Remove(StoreStaffs(departmentId));
        cache.Remove(StaffAdminList(departmentId));
        ClearBootstrapPayload(cache, departmentId);
    }

    public static void ClearNominationBacks(IApplicationCache cache, long departmentId)
    {
        cache.Remove(NominationBackMaster(departmentId));
        ClearBootstrapPayload(cache, departmentId);
    }

    public static void ClearPricingPlan(IApplicationCache cache, long departmentId)
    {
        cache.Remove(PricingPlan(departmentId));
        ClearBootstrapPayload(cache, departmentId);
    }

    public static void ClearBootstrapPayload(IApplicationCache cache, long departmentId) =>
        cache.Remove(BootstrapPayload(departmentId));

    public static void ClearCurrentBusinessDay(IApplicationCache cache, long departmentId)
    {
        cache.Remove(CurrentBusinessDay(departmentId));
    }

    public static void ClearOrderAttendingCasts(IApplicationCache cache, long departmentId, long businessDayId)
    {
        if (businessDayId <= 0)
        {
            return;
        }

        cache.Remove(OrderAttendingCasts(departmentId, businessDayId));
    }
}
