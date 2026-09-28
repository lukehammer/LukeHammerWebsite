using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class EventDisplayTests
{
    [Fact]
    public void MatchupTitle_is_sport_colon_name()
    {
        var evt = new Event
        {
            Sport = SportNames.Soccer,
            Name = "vs PSCN G20 Starburst (R)"
        };

        evt.MatchupTitle.Should().Be("Soccer: vs PSCN G20 Starburst (R)");
    }

    [Fact]
    public void GoogleMapsSearchUrl_encodes_location_query()
    {
        var evt = new Event { Location = "RORC — Field 5A" };

        evt.GoogleMapsSearchUrl.Should().Contain("google.com/maps/search/");
        evt.GoogleMapsSearchUrl.Should().Contain(Uri.EscapeDataString("RORC — Field 5A, Vancouver, WA"));
    }
}
