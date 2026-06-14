namespace ProsperApp.Services;

public interface IStoreClock
{
    DateTime GetStoreNow();

    DateTime FloorToMinuteStep(DateTime value, int minuteStep);

    DateTime ComposeBusinessDateTime(DateOnly businessDate, TimeOnly inputTime);

    DateTime ToStoreDateTime(DateTimeOffset value);

    DateTimeOffset ToStoreDateTimeOffset(DateTime value);

    IReadOnlyList<string> BuildTimeOptions(int minuteStep);

    string FormatStoreTime(DateTimeOffset value);

    string FormatStoreTime(DateTimeOffset? value, string fallback = "-");
}
