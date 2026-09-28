using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class ScheduleEventPickersTests
{
    [Fact]
    public void BuildTimeSlotGroups_includes_typical_evening_slot()
    {
        var groups = ScheduleEventPickers.BuildTimeSlotGroups();
        var all = groups.SelectMany(g => g.Slots).ToList();
        all.Should().Contain(s => s.Value == "20:00");
        all.Should().Contain(s => s.Value == "06:00");
        all.Should().Contain(s => s.Value == "21:00");
    }

    [Fact]
    public void BuildTimeSlotGroups_puts_typical_range_first()
    {
        var groups = ScheduleEventPickers.BuildTimeSlotGroups();
        groups[0].Label.Should().Contain("Typical");
        groups[0].Slots.Should().OnlyContain(s =>
            s.Time >= ScheduleEventPickers.TypicalStart && s.Time <= ScheduleEventPickers.TypicalEnd);
    }

    [Fact]
    public void TryParseTimeFromStorage_accepts_hh_mm()
    {
        ScheduleEventPickers.TryParseTimeFromStorage("15:30", out var time).Should().BeTrue();
        time.Should().Be(new TimeSpan(15, 30, 0));
    }

    [Fact]
    public void BuildDateOptionGroups_starts_at_pacific_today()
    {
        var groups = ScheduleEventPickers.BuildDateOptionGroups(daysAhead: 3);
        var all = groups.SelectMany(g => g.Options).ToList();
        all.Should().NotBeEmpty();
        all[0].Value.Should().Be(
            ScheduleEventPickers.FormatDateForInput(WashingtonScheduleTime.PacificToday));
        ScheduleEventPickers.IsSelectableDate(WashingtonScheduleTime.PacificToday.AddDays(-1)).Should().BeFalse();
    }
}
