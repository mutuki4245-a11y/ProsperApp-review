using Microsoft.Extensions.Caching.Memory;

namespace ProsperApp.Services;

internal static class StoreMasterCacheKeys
{
    public static string Departments => "store-master:departments";

    public static string StoreContext(long departmentId) => $"store-master:{departmentId}:context";

    public static string Tables(long departmentId) => $"store-master:{departmentId}:tables";

    public static string StoreCasts(long departmentId) => $"store-master:{departmentId}:casts";

    public static string OrderItems(long departmentId) => $"store-master:{departmentId}:order-items";

    public static string ItemAdminCatalog(long departmentId) => $"store-master:{departmentId}:item-admin-catalog";

    public static string CastAdminList(long departmentId) => $"store-master:{departmentId}:cast-admin-list";

    public static string NominationBackMaster(long departmentId) => $"store-runtime:{departmentId}:nomination-back-master";

    public static string CurrentBusinessDay(long departmentId) => $"store-runtime:{departmentId}:current-business-day";

    public static string OrderAttendingCasts(long departmentId, long businessDayId) => $"store-runtime:{departmentId}:{businessDayId}:order-attending-casts";

    public static MemoryCacheEntryOptions CreateOptions()
    {
        return new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.NeverRemove
        };
    }

    public static MemoryCacheEntryOptions CreateRuntimeOptions()
    {
        return new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.NeverRemove
        };
    }

    public static void ClearItems(IMemoryCache memoryCache, long departmentId)
    {
        memoryCache.Remove(OrderItems(departmentId));
        memoryCache.Remove(ItemAdminCatalog(departmentId));
    }

    public static void ClearCasts(IMemoryCache memoryCache, long departmentId)
    {
        memoryCache.Remove(StoreCasts(departmentId));
        memoryCache.Remove(CastAdminList(departmentId));
    }

    public static void ClearNominationBacks(IMemoryCache memoryCache, long departmentId)
    {
        memoryCache.Remove(NominationBackMaster(departmentId));
    }

    public static void ClearCurrentBusinessDay(IMemoryCache memoryCache, long departmentId)
    {
        memoryCache.Remove(CurrentBusinessDay(departmentId));
    }

    public static void ClearOrderAttendingCasts(IMemoryCache memoryCache, long departmentId, long businessDayId)
    {
        if (businessDayId <= 0)
        {
            return;
        }

        memoryCache.Remove(OrderAttendingCasts(departmentId, businessDayId));
    }
}
