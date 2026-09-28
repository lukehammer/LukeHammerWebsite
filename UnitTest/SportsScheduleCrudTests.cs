using System.Text.Json;
using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class SportsScheduleCrudTests
{
    [Theory]
    [InlineData("Luke")]
    [InlineData("luke")]
    public void Luke_name_matcher_recognizes_luke(string name)
    {
        LukeNameMatcher.IsLuke(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("Luke Hammer")]
    [InlineData("LUKE HAMMER")]
    [InlineData("June")]
    [InlineData("Choen")]
    [InlineData("")]
    [InlineData("   ")]
    public void Luke_name_matcher_rejects_non_luke(string name)
    {
        LukeNameMatcher.IsLuke(name).Should().BeFalse();
    }

    [Theory]
    [InlineData("JUNE", "June")]
    [InlineData("Alaina", "Alaina")]
    [InlineData("1123", "Luke")]
    public void Schedule_submitters_normalizes_allowed_first_names(string input, string expected)
    {
        ScheduleSubmitters.TryValidateFirstName(input, out var normalized, out var error)
            .Should().BeTrue(error);
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("Luke")]
    [InlineData("luke")]
    public void Login_rejects_luke_name_with_fake_message(string input)
    {
        ScheduleSubmitters.TryValidateLoginFirstName(input, out _, out var error).Should().BeFalse();
        error.Should().Be(ScheduleSubmitters.FakeLukeMessage);
    }

    [Fact]
    public void Luke_session_token_1123_resolves_for_api_writes()
    {
        ScheduleSubmitters.TryResolveSubmitterForWrite("1123", out var normalized, out var error)
            .Should().BeTrue(error);
        normalized.Should().Be("Luke");
    }

    [Theory]
    [InlineData("1123", "Luke")]
    [InlineData("Luke", "Luke")]
    [InlineData("luke", "Luke")]
    [InlineData("June", "June")]
    public void Write_resolver_accepts_signed_in_submitters_without_fake_message(string input, string expected)
    {
        ScheduleSubmitters.TryResolveSubmitterForWrite(input, out var normalized, out var error)
            .Should().BeTrue(error);
        normalized.Should().Be(expected);
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Luke")]
    [InlineData("1123")]
    public void Write_resolver_never_returns_fake_luke_message(string input)
    {
        ScheduleSubmitters.TryResolveSubmitterForWrite(input, out _, out var error);
        error.Should().NotBe(ScheduleSubmitters.FakeLukeMessage);
    }

    [Theory]
    [InlineData("sean", "Sean is not allowed.")]
    [InlineData("Bob", "Bob is not allowed.")]
    [InlineData("Luke Hammer", "Luke hammer is not allowed.")]
    public void Schedule_submitters_rejects_invalid_first_names(string input, string expectedError)
    {
        ScheduleSubmitters.IsAllowedFirstName(input).Should().BeFalse();
        ScheduleSubmitters.TryValidateFirstName(input, out _, out var error).Should().BeFalse();
        error.Should().Be(expectedError);
    }

    [Fact]
    public void Schedule_submitters_allowlist_includes_adults_and_kids()
    {
        ScheduleSubmitters.AllowedFirstNames.Should().NotContain("Luke");
        ScheduleSubmitters.AllowedFirstNames.Should().Contain("Casey");
        foreach (var kid in ScheduleKids.AllowedKids)
        {
            ScheduleSubmitters.AllowedFirstNames.Should().Contain(kid);
        }
    }

    [Fact]
    public void Notification_policy_skips_luke_only()
    {
        ScheduleChangeNotificationPolicy.ShouldNotify("Luke").Should().BeFalse();
        ScheduleChangeNotificationPolicy.ShouldNotify("June").Should().BeTrue();
    }

    [Fact]
    public void Parse_json_assigns_missing_event_ids()
    {
        const string json = """
            {
              "events": [
                {
                  "sport": "Soccer",
                  "kids": ["June"],
                  "date": "2026-10-01",
                  "name": "vs Test",
                  "location": "Field"
                }
              ]
            }
            """;

        var data = SportsSchedules.ParseJson(json);
        SportsSchedules.EnsureEventIds(data).Should().BeTrue();
        data.Events.Should().ContainSingle();
        data.Events[0].Id.Should().NotBeNullOrWhiteSpace();
        Guid.Parse(data.Events[0].Id).Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Ensure_event_ids_is_idempotent_when_ids_present()
    {
        var data = new SportsSchedulesData
        {
            Events =
            {
                new Event
                {
                    Id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                    Sport = SportNames.Baseball,
                    Kids = new List<string> { "Levi" },
                    Date = DateTime.Today,
                    Name = "Game",
                    Location = "Park"
                }
            }
        };

        SportsSchedules.EnsureEventIds(data).Should().BeFalse();
        data.Events[0].Id.Should().Be("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    }

    [Fact]
    public void Apply_last_modified_sets_submitted_by_and_pacific_timestamp()
    {
        var evt = new Event { Name = "Game", Location = "Field" };
        var before = WashingtonScheduleTime.Now;

        evt.ApplyLastModified("June");

        evt.LastModifiedBy.Should().Be("June");
        evt.LastModifiedAt.Should().NotBeNull();
        evt.LastModifiedAt!.Value.Should().BeOnOrAfter(before);
        evt.LastModifiedAt!.Value.Offset.Should().Be(WashingtonScheduleTime.Now.Offset);
    }

    [Fact]
    public void Parse_json_maps_legacy_kid_to_kids()
    {
        const string json = """
            {
              "events": [
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
        data.Events.Should().ContainSingle();
        data.Events[0].Kids.Should().Equal("June");
    }

    [Fact]
    public void Write_request_deserializes_string_sport()
    {
        const string json = """
            {
              "submittedBy": "Luke",
              "event": {
                "sport": "Football",
                "kids": ["Choen"],
                "date": "2026-10-03T00:00:00",
                "name": "Test Game",
                "location": "Field",
                "startTime": "15:00:00"
              }
            }
            """;

        var webOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var fromWeb = JsonSerializer.Deserialize<SportsEventWriteRequest>(json, webOptions);
        fromWeb!.Event.Sport.Should().Be(SportNames.Football);

        var request = JsonSerializer.Deserialize<SportsEventWriteRequest>(json, SportsSchedules.JsonOptions);
        request!.Event.Sport.Should().Be(SportNames.Football);
    }

    [Fact]
    public void SportNames_accepts_custom_sport_labels()
    {
        SportNames.TryNormalize("basketball", out var known).Should().BeTrue();
        known.Should().Be(SportNames.Basketball);
        SportNames.TryNormalize("Volleyball", out var custom).Should().BeTrue();
        custom.Should().Be("Volleyball");
        SportNames.TryNormalize("football", out var football).Should().BeTrue();
        football.Should().Be(SportNames.Football);
    }

    [Fact]
    public void Parse_json_round_trips_last_modified_fields()
    {
        const string json = """
            {
              "events": [
                {
                  "id": "11111111-2222-3333-4444-555555555555",
                  "sport": "Soccer",
                  "kids": ["June"],
                  "date": "2026-10-01",
                  "name": "vs Test",
                  "location": "Field",
                  "lastModifiedBy": "Luke",
                  "lastModifiedAt": "2026-09-27T19:00:00-07:00"
                }
              ]
            }
            """;

        var data = SportsSchedules.ParseJson(json);
        data.Events.Should().ContainSingle();
        data.Events[0].LastModifiedBy.Should().Be("Luke");
        data.Events[0].LastModifiedAt.Should().Be(DateTimeOffset.Parse("2026-09-27T19:00:00-07:00"));

        var roundTrip = SportsSchedules.ParseJson(SportsSchedules.ToJson(data));
        roundTrip.Events[0].LastModifiedBy.Should().Be("Luke");
        roundTrip.Events[0].LastModifiedAt.Should().Be(DateTimeOffset.Parse("2026-09-27T19:00:00-07:00"));
    }
}
