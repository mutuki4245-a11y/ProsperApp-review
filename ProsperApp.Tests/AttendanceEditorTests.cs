using ProsperApp.Features.Attendance;
using ProsperApp.Services;

namespace ProsperApp.Tests;

public class AttendanceEditorTests
{
    [Fact]
    public void MergeSelectedEntries_ReportsMalformedJson()
    {
        var result = AttendanceEditor.MergeSelectedEntries(new ClosingAttendanceInputModel
        {
            SelectedEntriesJson = "{broken"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("選択した勤怠行を読み取れませんでした。画面を再読み込みしてください。", result.ErrorMessage);
    }

    [Fact]
    public void Validate_AllowsClockOutAfterMidnight()
    {
        var input = CreateInput("20:00", "02:00");
        var options = AttendanceEditor.BuildTimeOptions(18, 0, 15);

        var errors = AttendanceEditor.Validate(
            input,
            options,
            options,
            new DateOnly(2026, 7, 30),
            CreateClock(),
            null);

        Assert.Empty(errors);
    }

    [Fact]
    public void BuildTimeOptions_UsesBusinessHourLabelForOneAmClockOutDefault()
    {
        var options = AttendanceEditor.BuildTimeOptions(24, 0, 15);

        var oneAm = Assert.Single(options, option => option.Value == "01:00");
        Assert.Equal("25:00", oneAm.Label);
        Assert.Equal("01:00", AttendanceEditor.ResolveDefaultTime(options, "01:00"));
    }

    [Fact]
    public void Validate_RejectsClockOutBeforeClockInOnSameBusinessDate()
    {
        var input = CreateInput("02:00", "01:00");
        var options = AttendanceEditor.BuildTimeOptions(18, 0, 15);

        var errors = AttendanceEditor.Validate(
            input,
            options,
            options,
            new DateOnly(2026, 7, 30),
            CreateClock(),
            null);

        Assert.Contains(errors, error => error.Field.EndsWith(".ClockOutTime", StringComparison.Ordinal));
    }

    private static ClosingAttendanceInputModel CreateInput(string clockIn, string clockOut)
    {
        return new ClosingAttendanceInputModel
        {
            SelectedCastIds = "10",
            Entries =
            [
                new BusinessDayAttendanceEntryInput
                {
                    CastId = 10,
                    DisplayName = "テスト",
                    ClockInTime = clockIn,
                    ClockOutTime = clockOut
                }
            ]
        };
    }

    private static StoreClock CreateClock() => new(new FixedTimeProvider());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
    }
}
