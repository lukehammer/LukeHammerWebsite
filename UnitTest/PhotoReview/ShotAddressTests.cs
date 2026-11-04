using BlazorApp.Shared.PhotoReview;
using FluentAssertions;

namespace UnitTest.PhotoReview;

public class ShotAddressTests
{
    private static ShotSummary Shot(string id, int number, string title) => new ShotSummary { ShotId = id, Number = number, Title = title };

    private static readonly ShotSummary[] Shots =
    {
        Shot("e75ee391f0f7", 1, "Warm Smile Headshot"),
        Shot("aa11bb22cc33", 2, "Warm Smile Headshot!"),
        Shot("0f1e2d3c4b5a", 3, "Jumping Jack #2 (wide)")
    };

    [Fact]
    public void Slug_joins_number_and_lowercase_title_words()
    {
        ShotAddress.Slug(1, "Warm Smile Headshot").Should().Be("001-warm-smile-headshot");
        ShotAddress.Slug(12, "Jumping Jack #2 (wide)").Should().Be("012-jumping-jack-2-wide");
    }

    [Fact]
    public void Slug_is_only_the_number_when_the_title_has_no_letters_or_digits()
    {
        ShotAddress.Slug(7, "★ ★").Should().Be("007");
    }

    [Fact]
    public void Resolve_accepts_the_number_and_title_slug()
    {
        ShotAddress.ResolveId("003-jumping-jack-2-wide", Shots).Should().Be("0f1e2d3c4b5a");
    }

    [Fact]
    public void Resolve_still_accepts_an_older_shot_id()
    {
        ShotAddress.ResolveId("e75ee391f0f7", Shots).Should().Be("e75ee391f0f7");
    }

    [Fact]
    public void Resolve_uses_the_number_to_choose_between_shots_with_the_same_title()
    {
        ShotAddress.ResolveId("002-warm-smile-headshot", Shots).Should().Be("aa11bb22cc33");
        ShotAddress.ResolveId("001-warm-smile-headshot", Shots).Should().Be("e75ee391f0f7");
    }

    [Fact]
    public void Resolve_falls_back_to_the_first_title_match_when_the_number_is_stale()
    {
        ShotAddress.ResolveId("009-warm-smile-headshot", Shots).Should().Be("e75ee391f0f7");
    }

    [Fact]
    public void Resolve_returns_null_for_an_unknown_title_or_id()
    {
        ShotAddress.ResolveId("001-no-such-shot", Shots).Should().BeNull();
        ShotAddress.ResolveId("ffffffffffff", Shots).Should().BeNull();
    }
}
