using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class SportsSchedulesMergeTests
{
    [Fact]
    public void Merge_keeps_events_only_on_live()
    {
        var seed = new SportsSchedulesData
        {
            Events = new List<Event>
            {
                SeedEvent("aaa", "Football", new DateTime(2026, 10, 1))
            }
        };
        var live = new SportsSchedulesData
        {
            Events = new List<Event>
            {
                SeedEvent("aaa", "Football", new DateTime(2026, 10, 1)),
                SeedEvent("bbb", "Soccer", new DateTime(2026, 10, 2), "Added on site")
            }
        };

        var merged = SportsSchedules.MergeDeployedSeed(seed, live);
        merged.Events.Should().HaveCount(2);
        merged.Events.Should().ContainSingle(e => e.Id == "bbb" && e.Name == "Added on site");
    }

    [Fact]
    public void Merge_adds_events_only_in_seed()
    {
        var seed = new SportsSchedulesData
        {
            Events = new List<Event>
            {
                SeedEvent("new-id", "Baseball", new DateTime(2027, 3, 1), "From repo")
            }
        };
        var live = new SportsSchedulesData { Events = new List<Event>() };

        var merged = SportsSchedules.MergeDeployedSeed(seed, live);
        merged.Events.Should().ContainSingle(e => e.Id == "new-id");
    }

    [Fact]
    public void Merge_keeps_live_when_last_modified_is_newer()
    {
        var seed = new SportsSchedulesData
        {
            Events = new List<Event>
            {
                SeedEvent("same", "Football", new DateTime(2026, 10, 1), "Seed name",
                    WashingtonScheduleTime.AssumePacific(new DateTime(2026, 9, 27, 12, 0, 0)))
            }
        };
        var live = new SportsSchedulesData
        {
            Events = new List<Event>
            {
                SeedEvent("same", "Football", new DateTime(2026, 10, 1), "Edited on site",
                    WashingtonScheduleTime.AssumePacific(new DateTime(2026, 9, 28, 8, 0, 0)))
            }
        };

        var merged = SportsSchedules.MergeDeployedSeed(seed, live);
        merged.Events.Should().ContainSingle(e => e.Name == "Edited on site");
    }

    [Fact]
    public void Merge_uses_seed_when_live_has_no_last_modified()
    {
        var seed = new SportsSchedulesData
        {
            Events = new List<Event>
            {
                SeedEvent("same", "Soccer", new DateTime(2026, 10, 1), "Seed name",
                    WashingtonScheduleTime.AssumePacific(new DateTime(2026, 9, 27, 21, 0, 0)),
                    "Luke")
            }
        };
        var live = new SportsSchedulesData
        {
            Events = new List<Event>
            {
                SeedEvent("same", "Soccer", new DateTime(2026, 10, 1), "Old blob name")
            }
        };

        var merged = SportsSchedules.MergeDeployedSeed(seed, live);
        var evt = merged.Events.Should().ContainSingle().Subject;
        evt.Name.Should().Be("Seed name");
        evt.LastModifiedBy.Should().Be("Luke");
    }

    private static Event SeedEvent(
        string id,
        string sport,
        DateTime date,
        string name = "Game",
        DateTimeOffset? lastModifiedAt = null,
        string lastModifiedBy = "")
    {
        return new Event
        {
            Id = id,
            Sport = sport,
            Date = date,
            Name = name,
            Location = "Field",
            Kids = new List<string> { "June" },
            LastModifiedAt = lastModifiedAt,
            LastModifiedBy = lastModifiedBy
        };
    }
}
