namespace ProsperApp.Services;

public sealed class StoreClock : IStoreClock
{
    private static readonly TimeOnly BusinessDaySwitchTime = new(12, 0);

    public DateTime GetStoreNow()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetStoreTimeZone());
    }

    public DateTime FloorToMinuteStep(DateTime value, int minuteStep)
    {
        if (minuteStep <= 0)
        {
            minuteStep = 5;
        }

        var minute = value.Minute - value.Minute % minuteStep;
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, minute, 0);
    }

    public DateTime ComposeBusinessDateTime(DateOnly businessDate, TimeOnly inputTime)
    {
        var actualDate = inputTime < BusinessDaySwitchTime
            ? businessDate.AddDays(1)
            : businessDate;

        return actualDate.ToDateTime(inputTime);
    }

    public DateTime ToStoreDateTime(DateTimeOffset value)
    {
        return TimeZoneInfo.ConvertTime(value, GetStoreTimeZone()).DateTime;
    }

    public DateTimeOffset ToStoreDateTimeOffset(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, GetStoreTimeZone().GetUtcOffset(unspecified));
    }

    public IReadOnlyList<string> BuildTimeOptions(int minuteStep)
    {
        if (minuteStep <= 0 || 60 % minuteStep != 0)
        {
            minuteStep = 5;
        }

        var options = new List<string>(24 * 60 / minuteStep);
        for (var hour = 0; hour < 24; hour++)
        {
            for (var minute = 0; minute < 60; minute += minuteStep)
            {
                options.Add($"{hour:00}:{minute:00}");
            }
        }

        return options;
    }

    public string FormatStoreTime(DateTimeOffset value)
    {
        return ToStoreDateTime(value).ToString("HH:mm");
    }

    public string FormatStoreTime(DateTimeOffset? value, string fallback = "-")
    {
        return value is null ? fallback : FormatStoreTime(value.Value);
    }

    private static TimeZoneInfo GetStoreTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
    }
}
