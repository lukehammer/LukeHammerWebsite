using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class WashingtonScheduleTimeTests
{
    [Fact]
    public void IsEventInPast_is_true_for_yesterday()
    {
        var yesterday = WashingtonScheduleTime.PacificToday.AddDays(-1);
        WashingtonScheduleTime.IsEventInPast(yesterday, new TimeSpan(18, 0, 0)).Should().BeTrue();
    }

    [Fact]
    public void IsEventInPast_is_false_for_tomorrow()
    {
        var tomorrow = WashingtonScheduleTime.PacificToday.AddDays(1);
        WashingtonScheduleTime.IsEventInPast(tomorrow, new TimeSpan(8, 0, 0)).Should().BeFalse();
    }

    [Fact]
    public void IsEventInPast_today_without_start_time_is_not_past()
    {
        WashingtonScheduleTime.IsEventInPast(WashingtonScheduleTime.PacificToday, null).Should().BeFalse();
    }

    [Fact]
    public void TryValidateEventNotInPast_rejects_yesterday()
    {
        var yesterday = WashingtonScheduleTime.PacificToday.AddDays(-1);
        WashingtonScheduleTime.TryValidateEventNotInPast(yesterday, null, out var error).Should().BeFalse();
        error.Should().Be(WashingtonScheduleTime.PastEventMessage);
    }
}
