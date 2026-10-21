using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class FamilyEventRulesTests
{
    private static readonly DateTime Today = new(2026, 10, 19);

    private static FamilyEvent MakeEvent(string id, DateTime date, TimeSpan? start = null) =>
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
            MakeEvent("later", Today.AddDays(5))
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
    public void SummarizeRsvps_counts_adults_and_kids_for_going_families_only()
    {
        var summary = FamilyEventRules.SummarizeRsvps(new[]
        {
            new FamilyEventRsvp
            {
                Name = "Leneigh",
                Status = FamilyRsvpStatus.Going,
                Headcount = 4,
                Attendees = new List<FamilyEventAttendee>
                {
                    new FamilyEventAttendee { Name = "Leneigh", Age = FamilyAttendeeAge.Adult },
                    new FamilyEventAttendee { Name = "Sam", Age = "7" },
                    new FamilyEventAttendee { Name = "Baby", Age = "0" },
                    new FamilyEventAttendee { Name = "Unknown" }
                }
            },
            new FamilyEventRsvp
            {
                Name = "Casey",
                Status = FamilyRsvpStatus.Maybe,
                Attendees = new List<FamilyEventAttendee> { new FamilyEventAttendee { Age = "5" } }
            }
        });

        summary.GoingAdults.Should().Be(1);
        summary.GoingKids.Should().Be(2);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("adult", true)]
    [InlineData("0", true)]
    [InlineData("17", true)]
    [InlineData("18", false)]
    [InlineData("-1", false)]
    [InlineData("07", false)]
    [InlineData("teen", false)]
    public void Attendee_age_must_be_adult_or_a_kid_age(string age, bool valid)
    {
        FamilyAttendeeAge.IsValid(age).Should().Be(valid);
    }

    [Fact]
    public void Votes_score_is_ups_minus_downs_with_one_vote_per_name()
    {
        var votes = new Dictionary<string, int>();

        FamilyEventVotes.Apply(votes, "Vickie", FamilyEventVotes.Up);
        FamilyEventVotes.Apply(votes, "Leneigh", FamilyEventVotes.Up);
        FamilyEventVotes.Apply(votes, "Sam", FamilyEventVotes.Down);
        FamilyEventVotes.Apply(votes, " vickie ", FamilyEventVotes.Up);

        votes.Should().HaveCount(3);
        FamilyEventVotes.Score(votes).Should().Be(1);
        FamilyEventVotes.VoteBy(votes, "VICKIE").Should().Be(FamilyEventVotes.Up);
        FamilyEventVotes.VoteBy(votes, "Nobody").Should().Be(0);
    }

    [Fact]
    public void Votes_can_be_switched_or_cleared()
    {
        var votes = new Dictionary<string, int>();
        FamilyEventVotes.Apply(votes, "Sam", FamilyEventVotes.Up);

        FamilyEventVotes.Apply(votes, "sam", FamilyEventVotes.Down);
        FamilyEventVotes.Score(votes).Should().Be(-1);

        FamilyEventVotes.Apply(votes, "Sam", 0);
        votes.Should().BeEmpty();
        FamilyEventVotes.Score(votes).Should().Be(0);
    }

    [Fact]
    public void Posts_saved_before_voting_existed_score_zero()
    {
        FamilyEventVotes.Score(null).Should().Be(0);
        FamilyEventVotes.VoteBy(null, "Sam").Should().Be(0);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(-5, false)]
    public void Only_up_down_or_clear_votes_are_allowed(int vote, bool valid)
    {
        FamilyEventVotes.IsValid(vote).Should().Be(valid);
    }

    [Theory]
    [InlineData("Christmas Party", FamilyEventTheme.Christmas)]
    [InlineData("Hammer XMAS brunch", FamilyEventTheme.Christmas)]
    [InlineData("Mayfield Lake Group Camping", FamilyEventTheme.Camping)]
    [InlineData("Campout at Grandma's", FamilyEventTheme.Camping)]
    [InlineData("Thanksgiving Potluck", FamilyEventTheme.None)]
    public void Theme_is_picked_from_the_event_title(string title, string expectedTheme)
    {
        FamilyEventTheme.For(new FamilyEvent { Title = title }).Should().Be(expectedTheme);
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
            MakeEvent("old", Today.AddDays(-10)),
            MakeEvent("later", Today.AddDays(30)),
            MakeEvent("soon", Today.AddDays(2)),
            MakeEvent("yesterday", Today.AddDays(-1))
        };

        FamilyEventRules.PickDefault(events, Today)!.Id.Should().Be("soon");
    }

    [Fact]
    public void PickDefault_includes_events_today()
    {
        var events = new[] { MakeEvent("today", Today), MakeEvent("next", Today.AddDays(1)) };

        FamilyEventRules.PickDefault(events, Today)!.Id.Should().Be("today");
    }

    [Fact]
    public void PickDefault_falls_back_to_most_recent_past_event()
    {
        var events = new[] { MakeEvent("older", Today.AddDays(-20)), MakeEvent("recent", Today.AddDays(-5)) };

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
            MakeEvent("u2", Today.AddDays(9)),
            MakeEvent("u1-late", Today.AddDays(1), new TimeSpan(18, 0, 0)),
            MakeEvent("u1-early", Today.AddDays(1), new TimeSpan(9, 0, 0)),
            MakeEvent("p2", Today.AddDays(-9)),
            MakeEvent("p1", Today.AddDays(-1))
        };

        FamilyEventRules.Upcoming(events, Today).Select(e => e.Id)
            .Should().Equal("u1-early", "u1-late", "u2");
        FamilyEventRules.Past(events, Today).Select(e => e.Id)
            .Should().Equal("p1", "p2");
    }
}
