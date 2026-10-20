using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class FamilyEventRulesTests
{
    private static readonly DateTime Today = new(2026, 10, 19);

    private static FamilyEvent Evt(string id, DateTime date, TimeSpan? start = null) =>
        new() { Id = id, Title = id, Date = date, StartTime = start };

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(-3, false)]
    [InlineData(-4, true)]
    [InlineData(-30, true)]
    [InlineData(5, false)]
    public void IsLocked_after_three_days_past_event(int offsetDays, bool expected)
    {
        FamilyEventRules.IsLocked(Today.AddDays(offsetDays), Today).Should().Be(expected);
    }

    [Theory]
    [InlineData("Stevie", "stevie ", true)]
    [InlineData("Stevie", "1123", true)]
    [InlineData("Stevie", "Alaina", false)]
    [InlineData("Stevie", "Luke", false)]
    [InlineData("Stevie", "", false)]
    public void CanDeletePost_allows_author_or_organizer_code(string author, string requestedBy, bool expected)
    {
        FamilyEventRules.CanDeletePost(author, requestedBy).Should().Be(expected);
    }

    [Fact]
    public void Multi_day_event_locks_three_days_after_last_day()
    {
        var camping = new FamilyEvent { Id = "camp", Date = Today.AddDays(-10), EndDate = Today.AddDays(-3) };

        FamilyEventRules.IsLocked(camping, Today).Should().BeFalse();
        FamilyEventRules.IsLocked(camping, Today.AddDays(1)).Should().BeTrue();
    }

    [Fact]
    public void In_progress_multi_day_event_is_upcoming_and_default()
    {
        var events = new[]
        {
            new FamilyEvent { Id = "camp", Date = Today.AddDays(-1), EndDate = Today.AddDays(2) },
            Evt("later", Today.AddDays(5))
        };

        FamilyEventRules.Upcoming(events, Today).Select(e => e.Id).Should().Equal("camp", "later");
        FamilyEventRules.PickDefault(events, Today)!.Id.Should().Be("camp");
    }

    [Fact]
    public void SummarizeRsvps_counts_going_families_and_people()
    {
        var summary = FamilyEventRules.SummarizeRsvps(new[]
        {
            new FamilyEventRsvp { Name = "Leneigh", Status = FamilyRsvpStatus.Going, Headcount = 4 },
            new FamilyEventRsvp { Name = "Vickie", Status = FamilyRsvpStatus.Going },
            new FamilyEventRsvp { Name = "Casey", Status = FamilyRsvpStatus.Maybe, Headcount = 3 },
            new FamilyEventRsvp { Name = "Steve", Status = FamilyRsvpStatus.No }
        });

        summary.GoingFamilies.Should().Be(2);
        summary.GoingPeople.Should().Be(4);
        summary.MaybeFamilies.Should().Be(1);
        summary.NotGoingFamilies.Should().Be(1);
    }

    [Fact]
    public void IsPast_is_false_on_event_day()
    {
        FamilyEventRules.IsPast(Today, Today).Should().BeFalse();
        FamilyEventRules.IsPast(Today.AddDays(-1), Today).Should().BeTrue();
    }

    [Fact]
    public void PickDefault_returns_next_upcoming_event()
    {
        var events = new[]
        {
            Evt("old", Today.AddDays(-10)),
            Evt("later", Today.AddDays(30)),
            Evt("soon", Today.AddDays(2)),
            Evt("yesterday", Today.AddDays(-1))
        };

        FamilyEventRules.PickDefault(events, Today)!.Id.Should().Be("soon");
    }

    [Fact]
    public void PickDefault_includes_events_today()
    {
        var events = new[] { Evt("today", Today), Evt("next", Today.AddDays(1)) };

        FamilyEventRules.PickDefault(events, Today)!.Id.Should().Be("today");
    }

    [Fact]
    public void PickDefault_falls_back_to_most_recent_past_event()
    {
        var events = new[] { Evt("older", Today.AddDays(-20)), Evt("recent", Today.AddDays(-5)) };

        FamilyEventRules.PickDefault(events, Today)!.Id.Should().Be("recent");
    }

    [Fact]
    public void PickDefault_is_null_without_events()
    {
        FamilyEventRules.PickDefault(Array.Empty<FamilyEvent>(), Today).Should().BeNull();
    }

    [Fact]
    public void Upcoming_and_past_are_ordered_toward_today()
    {
        var events = new[]
        {
            Evt("u2", Today.AddDays(9)),
            Evt("u1-late", Today.AddDays(1), new TimeSpan(18, 0, 0)),
            Evt("u1-early", Today.AddDays(1), new TimeSpan(9, 0, 0)),
            Evt("p2", Today.AddDays(-9)),
            Evt("p1", Today.AddDays(-1))
        };

        FamilyEventRules.Upcoming(events, Today).Select(e => e.Id)
            .Should().Equal("u1-early", "u1-late", "u2");
        FamilyEventRules.Past(events, Today).Select(e => e.Id)
            .Should().Equal("p1", "p2");
    }
}
