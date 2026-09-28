using System.Text.Json;
using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class ScheduleKidsSpellingTests
{
    /// <summary>Canonical kid first names — update here when the family list changes.</summary>
    public static readonly string[] ExpectedCanonicalKidNames =
    {
        "Cael",
        "Cohen",
        "Juniper",
        "Levi",
        "Lily",
        "Silas"
    };

    private static readonly string[] KnownMisspellings = { "Choen", "June" };

    [Fact]
    public void AllowedKids_matches_expected_canonical_spellings()
    {
        ScheduleKids.AllowedKids.Should().Equal(ExpectedCanonicalKidNames);
    }

    [Theory]
    [MemberData(nameof(CanonicalKidNameCases))]
    public void CanonicalizeKidName_maps_legacy_and_preserves_correct_spelling(
        string input,
        string expected)
    {
        ScheduleKids.CanonicalizeKidName(input).Should().Be(expected);
    }

    public static IEnumerable<object[]> CanonicalKidNameCases()
    {
        foreach (var name in ExpectedCanonicalKidNames)
        {
            yield return new object[] { name, name };
            yield return new object[] { $"  {name}  ", name };
        }

        yield return new object[] { "Choen", "Cohen" };
        yield return new object[] { " Choen ", "Cohen" };
        yield return new object[] { "June", "Juniper" };
        yield return new object[] { " JUNE ", "Juniper" };
    }

    [Fact]
    public void Seed_sports_schedules_json_does_not_contain_known_misspellings()
    {
        var json = ReadSeedFile("sports-schedules.json");
        foreach (var misspelling in KnownMisspellings)
        {
            json.Should().NotContain($"\"{misspelling}\"", $"seed JSON must not store '{misspelling}'");
        }
    }

    [Fact]
    public void Seed_sports_schedules_events_use_only_allowed_kid_spellings()
    {
        var json = ReadSeedFile("sports-schedules.json");
        var data = SportsSchedules.ParseJson(json);
        var kidsOnEvents = data.Events
            .SelectMany(e => e.Kids ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        kidsOnEvents.Should().NotBeEmpty();
        kidsOnEvents.Should().OnlyContain(k => ExpectedCanonicalKidNames.Contains(k));
        kidsOnEvents.Should().NotContain(KnownMisspellings);
    }

    [Fact]
    public void Kid_theme_colors_json_keys_match_allowed_kids()
    {
        var json = ReadSeedFile("kid-theme-colors.json");
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement
            .GetProperty("backgroundByKid")
            .EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        keys.Should().Equal(ExpectedCanonicalKidNames.OrderBy(k => k, StringComparer.Ordinal));
        keys.Should().NotContain(KnownMisspellings);
    }

    private static string ReadSeedFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", fileName);
        File.Exists(path).Should().BeTrue($"test output should include Api/data/{fileName}");
        return File.ReadAllText(path);
    }
}
