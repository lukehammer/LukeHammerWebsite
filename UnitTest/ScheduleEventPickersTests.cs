using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class ScheduleEventPickersTests
{
    [Fact]
    public void BuildTimeSlotGroups_includes_early_typical_and_late_slots()
    {
        var groups = ScheduleEventPickers.BuildTimeSlotGroups();
        var all = groups.SelectMany(g => g.Slots).ToList();
        all.Should().Contain(s => s.Value == "06:00");
        all.Should().Contain(s => s.Value == "19:30");
        all.Should().Contain(s => s.Value == "20:00");
        all.Should().Contain(s => s.Value == "21:00");
    }

    [Fact]
    public void BuildTimeSlotGroups_puts_typical_range_first()
    {
        var groups = ScheduleEventPickers.BuildTimeSlotGroups();
        groups[0].Label.Should().Contain("Typical");
        groups[0].Slots.Should().OnlyContain(s =>
            s.Time >= ScheduleEventPickers.TypicalStart && s.Time < ScheduleEventPickers.LateStart);
    }

    [Fact]
    public void BuildTimeSlotGroups_puts_eight_pm_in_late_group()
    {
        var groups = ScheduleEventPickers.BuildTimeSlotGroups();
        var late = groups.First(g => g.Label.Contains("Late", StringComparison.Ordinal));
        late.Slots.Should().Contain(s => s.Value == "20:00");
        late.Slots.Should().Contain(s => s.Value == "21:00");
        groups.First(g => g.Label.Contains("Typical", StringComparison.Ordinal)).Slots
            .Should().NotContain(s => s.Value == "20:00");
    }

    [Fact]
    public void TryParseTimeFromStorage_accepts_hh_mm()
    {
        ScheduleEventPickers.TryParseTimeFromStorage("15:30", out var time).Should().BeTrue();
        time.Should().Be(new TimeSpan(15, 30, 0));
    }

    [Fact]
    public void FormatDatePickerLabel_uses_numeric_date_em_dash_and_padded_weekday()
    {
        var label = ScheduleEventPickers.FormatDatePickerLabel(new DateTime(2026, 9, 27));
        label.Should().StartWith("09/27/2026 — ");
        label.Length.Should().Be("09/27/2026 — ".Length + 9);
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
