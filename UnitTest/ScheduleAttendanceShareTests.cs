using System;
using System.Collections.Generic;
using BlazorApp.Shared;

namespace UnitTest;

public class ScheduleAttendanceShareTests
{
    [Fact]
    public void BuildMessage_UsesOxfordCommaAndI_Last()
    {
        var evt = new Event
        {
            Date = new DateTime(2026, 10, 10),
            StartTime = new TimeSpan(13, 0, 0),
            Sport = SportNames.Football,
            Name = "Game",
            Location = "Castle Rock",
            Kids = new List<string> { "Cohen" }
        };

        var message = ScheduleAttendanceShare.BuildMessage(
            evt,
            new[] { "I", "Grandpa", "Stevie" });

        Assert.Equal(
            "Grandpa, Stevie, and I are going to Cohen's football game at Castle Rock Saturday October 10th at 1:00 PM.",
            message);
    }

    [Fact]
    public void FormatAttendeeLeadPhrase_LukeSteveAndI()
    {
        var phrase = ScheduleAttendanceShare.FormatAttendeeLeadPhrase(
            new[] { "Luke", "Steve", "I" });

        Assert.Equal("Luke, Steve, and I are", phrase);
    }

    [Fact]
    public void FormatAttendeeLeadPhrase_OnlyI_UsesAm()
    {
        Assert.Equal("I am", ScheduleAttendanceShare.FormatAttendeeLeadPhrase(new[] { "I" }));
    }

    [Fact]
    public void FormatAttendeeLeadPhrase_OneOther_UsesIs()
    {
        Assert.Equal("Luke is", ScheduleAttendanceShare.FormatAttendeeLeadPhrase(new[] { "Luke" }));
    }

    [Fact]
    public void MergeNames_IncludesBuiltInAndCustom()
    {
        var data = new ScheduleSpectatorsData
        {
            CustomNames = new List<string> { "Grandpa", "Zelda" }
        };

        var merged = ScheduleSpectators.MergeNames(data);

        Assert.Contains("Cohen", merged);
        Assert.Contains("Stevie", merged);
        Assert.Contains("Grandpa", merged);
        Assert.Contains("Zelda", merged);
    }
}
