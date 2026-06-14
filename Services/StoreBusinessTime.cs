namespace ProsperApp.Services;

public static class StoreBusinessTime
{
    private static readonly IStoreClock Clock = new StoreClock();

    public static DateTime ComposeBusinessDateTime(DateOnly businessDate, TimeOnly inputTime)
    {
        return Clock.ComposeBusinessDateTime(businessDate, inputTime);
    }

    public static DateTime ToStoreDateTime(DateTimeOffset value)
    {
        return Clock.ToStoreDateTime(value);
    }

    public static string FormatStoreTime(DateTimeOffset value)
    {
        return Clock.FormatStoreTime(value);
    }

    public static string FormatStoreTime(DateTimeOffset? value, string fallback = "-")
    {
        return Clock.FormatStoreTime(value, fallback);
    }
}
