using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class ScheduleGoingSelectionTests
{
    [Fact]
    public void Normalize_withoutStoredPrefs_selectsStevieAndGrandpa()
    {
        var names = ScheduleSpectators.MergeNames(new ScheduleSpectatorsData
        {
            CustomNames = new List<string> { "Grandpa" }
        });

        var prefs = ScheduleGoingSelectionDefaults.Normalize(null, names);

        prefs.IncludeMe.Should().BeTrue();
        prefs.SelectedNames.Should().Equal("Grandpa", "Stevie");
    }

    [Fact]
    public void Normalize_restoresStoredPrefs()
    {
        var stored = new ScheduleGoingSelectionPrefs
        {
            IncludeMe = false,
            SelectedNames = new List<string> { "Laura", "Casey" }
        };

        var prefs = ScheduleGoingSelectionDefaults.Normalize(
            stored,
            ScheduleSpectators.MergeNames(new ScheduleSpectatorsData()));

        prefs.IncludeMe.Should().BeFalse();
        prefs.SelectedNames.Should().Equal("Casey", "Laura");
    }
}
