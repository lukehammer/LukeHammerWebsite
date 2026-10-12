using System.Text.Json;
using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;

namespace UnitTest.PhotoShoot;

public class PhotoShootCatalogTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 16, 0, 0, DateTimeKind.Utc);

    private static PhotoShootCatalog LoadCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "photoshoot-catalog.json");
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        return JsonSerializer.Deserialize<PhotoShootCatalog>(json, options)!;
    }

    [Fact]
    public void Catalog_PassesValidation()
    {
        var catalog = LoadCatalog();

        PhotoShootValidator.ValidateCatalog(catalog).Should().BeEmpty();
    }

    [Fact]
    public void Catalog_MeetsShotListTargets()
    {
        var catalog = LoadCatalog();

        catalog.Shots.Should().HaveCountGreaterThanOrEqualTo(95).And.HaveCountLessThanOrEqualTo(110);
        catalog.Setups.Should().HaveCountGreaterThanOrEqualTo(20).And.HaveCountLessThanOrEqualTo(30);
        catalog.Shots.Count(s => s.Priority == ShotPriority.MustHave).Should().BeGreaterThanOrEqualTo(30);
        catalog.Shots.Select(s => s.Category).Distinct().Should().BeEquivalentTo(Enum.GetValues<ShotCategory>());
        catalog.Setups.Should().Contain(s => s.RequiresChildren);
    }

    [Fact]
    public void Catalog_KeysAreUniqueAndShotsReferenceKnownSetups()
    {
        var catalog = LoadCatalog();
        var setupKeys = catalog.Setups.Select(s => s.Key).ToList();

        setupKeys.Should().OnlyHaveUniqueItems();
        catalog.Shots.Select(s => s.Key).Should().OnlyHaveUniqueItems();
        catalog.Shots.Should().OnlyContain(s => setupKeys.Contains(s.SetupKey));
        catalog.Setups.Should().OnlyContain(s => catalog.Shots.Any(shot => shot.SetupKey == s.Key));
    }

    [Fact]
    public void Catalog_EveryShotHasDirectionForBothPhones()
    {
        var catalog = LoadCatalog();

        catalog.Shots.Should().OnlyContain(s =>
            !string.IsNullOrWhiteSpace(s.Title) &&
            !string.IsNullOrWhiteSpace(s.PerformerInstructions) &&
            !string.IsNullOrWhiteSpace(s.FacialExpression) &&
            !string.IsNullOrWhiteSpace(s.PhotographerInstructions) &&
            s.EstimatedMinutes > 0);
    }

    [Fact]
    public void Catalog_SeedsValidDocumentAndSecondSeedIsNoOp()
    {
        var catalog = LoadCatalog();
        var doc = PhotoShootSeeder.CreateFromCatalog(catalog, "studio", Now);

        PhotoShootValidator.ValidateDocument(doc).Errors.Should().BeEmpty();
        doc.Shots.Should().HaveCount(catalog.Shots.Count);
        doc.Setups.Should().HaveCount(catalog.Setups.Count);

        var again = PhotoShootSeeder.Merge(doc, catalog, Now);

        again.AddedShots.Should().Be(0);
        again.AddedSetups.Should().Be(0);
        doc.Shots.Should().HaveCount(catalog.Shots.Count);
    }

    [Fact]
    public void Catalog_SuggestedOrderKeepsEveryShotAndMustHavesLead()
    {
        var doc = PhotoShootSeeder.CreateFromCatalog(LoadCatalog(), "studio", Now);
        var before = doc.Shots.Select(s => s.Id).ToHashSet();

        PhotoShootPlanner.ApplySuggestedOrder(doc);

        doc.Shots.Select(s => s.Id).Should().BeEquivalentTo(before);
        foreach (var group in PhotoShootPlanner.Groups(doc))
        {
            var priorities = group.Shots.Select(s => s.Priority == ShotPriority.MustHave ? 0 : 1).ToList();
            priorities.Should().BeInAscendingOrder();
        }
        PhotoShootPlanner.EstimateTotal(doc).TotalMinutes.Should().BeGreaterThan(0);
    }
}
