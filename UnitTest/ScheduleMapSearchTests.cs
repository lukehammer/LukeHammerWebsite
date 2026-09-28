using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class ScheduleMapSearchTests
{
    [Fact]
    public void BuildSearchQuery_appends_vancouver_wa_for_local_venue()
    {
        ScheduleMapSearch.BuildSearchQuery("Woodland Middle School")
            .Should().Be("Woodland Middle School, Vancouver, WA");
    }

    [Fact]
    public void BuildSearchQuery_skips_anchor_when_location_already_has_wa()
    {
        ScheduleMapSearch.BuildSearchQuery("Kelso HS, Kelso, WA")
            .Should().Be("Kelso HS, Kelso, WA");
    }

    [Fact]
    public void BuildGoogleMapsSearchUrl_includes_anchored_query()
    {
        var url = ScheduleMapSearch.BuildGoogleMapsSearchUrl("RORC — Field 5A");
        url.Should().Contain("google.com/maps/search/");
        url.Should().Contain(Uri.EscapeDataString("RORC — Field 5A, Vancouver, WA"));
    }
}
