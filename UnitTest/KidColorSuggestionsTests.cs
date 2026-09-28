using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class KidColorSuggestionsTests
{
    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#000000")]
    [InlineData("#808080")]
    [InlineData("#F3F4F6")]
    [InlineData("#111111")]
    public void Monochrome_forbidden_rejects_neutral_tones(string hex)
    {
        KidColorSuggestions.IsMonochromeForbidden(hex).Should().BeTrue();
    }

    [Theory]
    [InlineData("#DBEAFE")]
    [InlineData("#FCE7F3")]
    [InlineData("#FF5733")]
    public void Monochrome_forbidden_allows_vivid_colors(string hex)
    {
        KidColorSuggestions.IsMonochromeForbidden(hex).Should().BeFalse();
    }

    [Fact]
    public void Similarity_detects_identical_hex_as_too_similar()
    {
        KidColorSuggestions.IsTooSimilar("#DBEAFE", "#DBEAFE", KidColorSuggestions.DefaultSimilarityThreshold)
            .Should().BeTrue();
    }

    [Fact]
    public void Similarity_distinguishes_default_kid_palette_pairs()
    {
        var defaults = KidThemeColors.CreateDefaultData().BackgroundByKid.Values.ToList();
        for (var i = 0; i < defaults.Count; i++)
        {
            for (var j = i + 1; j < defaults.Count; j++)
            {
                KidColorSuggestions.IsTooSimilar(
                        defaults[i],
                        defaults[j],
                        KidColorSuggestions.DefaultSimilarityThreshold)
                    .Should().BeFalse($"Expected {defaults[i]} and {defaults[j]} to be distinct enough.");
            }
        }
    }

    [Fact]
    public void Wcag_contrast_rejects_background_too_dark_for_black_text()
    {
        KidColorSuggestions.MeetsWcag21AaNormalTextContrast("#1E40AF").Should().BeFalse();
    }

    [Theory]
    [InlineData("#DBEAFE")]
    [InlineData("#FEF3C7")]
    [InlineData("#FCE7F3")]
    public void Wcag_contrast_allows_light_schedule_backgrounds(string hex)
    {
        KidColorSuggestions.MeetsWcag21AaNormalTextContrast(hex).Should().BeTrue();
        KidColorSuggestions.ContrastRatio(KidColorSuggestions.ScheduleRowTextHex, hex)
            .Should().BeGreaterOrEqualTo(KidColorSuggestions.Wcag21AaNormalTextMinContrastRatio);
    }

    [Fact]
    public void Default_palette_passes_color_rules()
    {
        var data = KidThemeColors.CreateDefaultData();
        KidThemeColors.TryValidateColorRules(data, out var issues).Should().BeTrue();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_hex_is_rejected()
    {
        var data = KidThemeColors.CreateDefaultData();
        var firstKid = ScheduleKids.AllowedKids[0];
        var secondKid = ScheduleKids.AllowedKids[1];
        data.BackgroundByKid[secondKid] = data.BackgroundByKid[firstKid];

        KidThemeColors.TryValidateColorRules(data, out var issues).Should().BeFalse();
        issues.Should().ContainKey(secondKid);
        issues[secondKid].SuggestedHex.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Suggest_next_color_maximizes_minimum_distance()
    {
        var used = new[] { "#DBEAFE", "#D8F3DC", "#FCE7F3" };
        var suggested = KidColorSuggestions.SuggestNextColor(used);
        suggested.Should().NotBeNull();
        KidColorSuggestions.IsMonochromeForbidden(suggested!).Should().BeFalse();

        foreach (var hex in used)
        {
            KidColorSuggestions.IsTooSimilar(suggested!, hex, KidColorSuggestions.DefaultSimilarityThreshold)
                .Should().BeFalse();
        }

        var minDist = used.Min(u => KidColorSuggestions.DeltaE(suggested!, u));
        var alternate = KidColorSuggestions.SuggestNextColor(used.Concat(new[] { suggested! }));
        if (alternate != null)
        {
            var alternateMin = used.Min(u => KidColorSuggestions.DeltaE(alternate, u));
            minDist.Should().BeGreaterThanOrEqualTo(alternateMin - 0.01);
        }
    }

    [Fact]
    public void Gray_submission_gets_vivid_suggestion()
    {
        var others = KidThemeColors.CreateDefaultData().BackgroundByKid.Values
            .Take(3)
            .ToList();
        var issue = KidColorSuggestions.ValidateSingleColor("#CCCCCC", others);
        issue.Should().NotBeNull();
        issue!.SuggestedHex.Should().NotBeNull();
        KidColorSuggestions.IsMonochromeForbidden(issue.SuggestedHex!).Should().BeFalse();
    }
}
