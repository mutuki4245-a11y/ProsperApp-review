namespace ProsperApp.Features.Shared;

public sealed class StoreClock : IStoreClock
{
    private static readonly TimeOnly BusinessDaySwitchTime = new(12, 0);
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _storeTimeZone;

    public StoreClock(TimeProvider timeProvider)
        : this(timeProvider, TimeZoneInfo.FindSystemTimeZoneById)
    {
    }

    internal StoreClock(
        TimeProvider timeProvider,
        Func<string, TimeZoneInfo> timeZoneResolver)
    {
        _timeProvider = timeProvider;
        _storeTimeZone = GetStoreTimeZone(timeZoneResolver);
    }

    public DateTime GetStoreNow()
    {
        return TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _storeTimeZone).DateTime;
    }

    public DateOnly GetCurrentBusinessDate()
    {
        var now = GetStoreNow();
        var businessDate = now.TimeOfDay < BusinessDaySwitchTime.ToTimeSpan()
            ? now.AddDays(-1)
            : now;

        return DateOnly.FromDateTime(businessDate);
    }

    public DateOnly GetStoreToday()
    {
        return DateOnly.FromDateTime(GetStoreNow());
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
        return TimeZoneInfo.ConvertTime(value, _storeTimeZone).DateTime;
    }

    public DateTimeOffset ToStoreDateTimeOffset(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, _storeTimeZone.GetUtcOffset(unspecified));
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

    public string FormatBusinessTime(TimeOnly value)
    {
        var displayHour = value.Hour < BusinessDaySwitchTime.Hour
            ? value.Hour + 24
            : value.Hour;
        return $"{displayHour:00}:{value.Minute:00}";
    }

    public string FormatBusinessTime(DateTimeOffset value)
    {
        return FormatBusinessTime(TimeOnly.FromDateTime(ToStoreDateTime(value)));
    }

    public string FormatBusinessTime(DateTimeOffset? value, string fallback = "-")
    {
        return value is null ? fallback : FormatBusinessTime(value.Value);
    }

    private static TimeZoneInfo GetStoreTimeZone(
        Func<string, TimeZoneInfo> timeZoneResolver)
    {
        try
        {
            return timeZoneResolver("Tokyo Standard Time");
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            try
            {
                return timeZoneResolver("Asia/Tokyo");
            }
            catch (Exception fallbackException) when (
                fallbackException is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "ProsperApp-Tokyo-Fallback",
                    TimeSpan.FromHours(9),
                    "ProsperApp Tokyo (UTC+09:00)",
                    "ProsperApp Tokyo (UTC+09:00)");
            }
        }
    }
}
