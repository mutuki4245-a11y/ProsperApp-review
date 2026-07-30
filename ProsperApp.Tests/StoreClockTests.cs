using ProsperApp.Services;
using ProsperApp.Models;

namespace ProsperApp.Tests;

public class StoreClockTests
{
    [Theory]
    [InlineData("2026-07-30T02:59:00+00:00", 2026, 7, 29)]
    [InlineData("2026-07-30T03:00:00+00:00", 2026, 7, 30)]
    public void GetCurrentBusinessDate_SwitchesAtNoonInTokyo(
        string utcValue,
        int year,
        int month,
        int day)
    {
        var clock = new StoreClock(new FixedTimeProvider(DateTimeOffset.Parse(utcValue)));

        Assert.Equal(new DateOnly(year, month, day), clock.GetCurrentBusinessDate());
    }

    [Fact]
    public void ComposeBusinessDateTime_MovesEarlyMorningToNextCalendarDate()
    {
        var clock = new StoreClock(new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        var businessDate = new DateOnly(2026, 7, 30);

        Assert.Equal(
            new DateTime(2026, 7, 30, 23, 0, 0),
            clock.ComposeBusinessDateTime(businessDate, new TimeOnly(23, 0)));
        Assert.Equal(
            new DateTime(2026, 7, 31, 2, 0, 0),
            clock.ComposeBusinessDateTime(businessDate, new TimeOnly(2, 0)));
        Assert.Equal("26:00", clock.FormatBusinessTime(new TimeOnly(2, 0)));
    }

    [Fact]
    public void CreateSlipEditor_ComposesEarlyMorningAgainstBusinessDate()
    {
        var clock = new StoreClock(new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var input = CreateSlipEditor.ApplyDefaults(
            new CreateSlipInputModel { OpenedTime = "02:00" },
            null,
            new DateOnly(2026, 7, 30),
            clock);

        Assert.Equal(new DateOnly(2026, 7, 30), input.BusinessDate);
        Assert.Equal(new DateTime(2026, 7, 31, 2, 0, 0), input.OpenedAt);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
