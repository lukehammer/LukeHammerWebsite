using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class ScheduleDateDisplayTests
{
    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    public void FormatOrdinalDay_uses_expected_suffix(int day, string expected) =>
        ScheduleDateDisplay.FormatOrdinalDay(day).Should().Be(expected);

    [Fact]
    public void FormatScheduleWhen_uses_weekday_month_ordinal_and_twelve_hour_time()
    {
        var date = new DateTime(2026, 9, 27);
        var time = new TimeSpan(15, 0, 0);

        ScheduleDateDisplay.FormatScheduleWhen(date, time)
            .Should()
            .Be("Sun · Sep · 27th · 3:00 PM");
    }

    [Fact]
    public void FormatScheduleWhen_unscheduled_time_is_spelled_out()
    {
        ScheduleDateDisplay.FormatScheduleWhen(new DateTime(2026, 3, 1), null)
            .Should()
            .Be("Sun · Mar · 1st · unscheduled");
    }

    [Fact]
    public void Event_ScheduleMetaLine_matches_display_format()
    {
        var evt = new Event
        {
            Date = new DateTime(2026, 9, 27),
            StartTime = new TimeSpan(15, 0, 0),
        };

        evt.ScheduleMetaLine.Should().Be("Sun · Sep · 27th · 3:00 PM");
        evt.ScheduleMetaParts.Should().Equal("Sun", "Sep", "27th", "3:00 PM");
    }
}
