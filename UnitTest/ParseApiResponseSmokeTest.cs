using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class ParseApiResponseSmokeTest
{
    [Fact]
    public void ParseJson_accepts_functions_default_write_as_json_shape()
    {
        const string json = """
            {
              "Events": [
                {
                  "Id": "b4588fcb-6f84-4fa9-bc06-b0487a9bde7e",
                  "Date": "2026-09-12T00:00:00",
                  "StartTime": "14:30:00",
                  "Name": "vs La Center Blue",
                  "Location": "Woodland HS",
                  "Sport": "Football",
                  "Kids": null
                }
              ]
            }
            """;

        var data = SportsSchedules.ParseJson(json);
        data.Events.Should().ContainSingle();
        data.Events[0].Kids.Should().NotBeNull();
        var act = () => _ = data.Events[0].RowThemeKid;
        act.Should().NotThrow();
        ScheduleKidThemes.RowThemeClass(data.Events[0]).Should().BeEmpty();
    }

    [Fact]
    public void ParseJson_maps_legacy_kid_when_events_array_is_pascal_case()
    {
        const string json = """
            {
              "Events": [
                {
                  "sport": "Soccer",
                  "kid": "June",
                  "date": "2026-10-01",
                  "name": "vs Test",
                  "location": "Field"
                }
              ]
            }
            """;

        var data = SportsSchedules.ParseJson(json);
        data.Events[0].Kids.Should().Equal("June");
    }

    [Fact]
    public void GetUpcoming_with_null_kids_does_not_throw()
    {
        const string json = """
            {
              "Events": [
                {
                  "Date": "2099-01-01T00:00:00",
                  "Sport": "Baseball",
                  "Kids": null
                }
              ]
            }
            """;

        var data = SportsSchedules.ParseJson(json);
        var act = () => SportsSchedules.GetUpcoming(data.Events, SportNames.Baseball, "Levi");
        act.Should().NotThrow();
    }
}
