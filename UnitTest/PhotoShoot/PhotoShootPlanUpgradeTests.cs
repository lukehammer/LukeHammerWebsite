using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;

namespace UnitTest.PhotoShoot;

public class PhotoShootPlanUpgradeTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Apply_KeepsExistingImages_AndFillsShotsThatHaveNone()
    {
        var catalog = LoadCatalog();
        var doc = PhotoShootSeeder.CreateFromCatalog(catalog, "studio", Now);
        doc.References.Add(new PhotoReference
        {
            Id = "decoy-both-hands",
            Title = "Man in suit - both hands adjusting bow tie, elbows relaxed",
            ImageId = "blob-bowtie",
            ImageContentType = "image/jpeg"
        });
        PhotoShootPlanUpgrade.Apply(doc, Now);
        var shot = doc.Shots.First(s => s.SeedKey == "shot:warm-smile-headshot");
        var upload = new PhotoReference
        {
            Id = "upload-1",
            ShotIds = new List<string> { shot.Id },
            Title = "Existing Luke mockup",
            ImageId = "blob-keep-me",
            ImageContentType = "image/png",
            SourceUrl = "https://example.com/existing-mockup.png"
        };
        doc.References.Add(upload);

        var beforeReferenceIds = doc.References.Select(r => r.Id).ToList();
        var beforeImageIds = doc.References.Select(r => r.ImageId).Where(id => !string.IsNullOrEmpty(id)).ToList();

        PhotoShootPlanUpgrade.Apply(doc, Now).Should().BeFalse();

        doc.References.Select(r => r.Id).Should().Contain(beforeReferenceIds);
        doc.References.Select(r => r.ImageId).Should().Contain(beforeImageIds);
        doc.References.Should().Contain(r => r.Id == "upload-1" && r.ImageId == "blob-keep-me" && r.ShotIds.Contains(shot.Id));
        var doublePoint = doc.Shots.Single(s => s.SeedKey == "shot:promotional-double-point");
        doc.References.Single(r => r.Id == "decoy-both-hands").ShotIds.Should().NotContain(doublePoint.Id);
        doc.References.Should().Contain(r => r.Title == "Brad Ross double point" && r.Provenance == ReferenceProvenance.MissingOriginal);
        PhotoShootPlanUpgrade.HasPhoto(doc, shot.Id).Should().BeTrue();

        var greeting = doc.Shots.Single(s => s.SeedKey == "shot:goofy-close-up-greeting");
        doc.References.Should().Contain(r =>
            r.SourceUrl == PhotoShootPlanUpgrade.MacKingGreetingUrl
            && r.Provenance == ReferenceProvenance.ExactUserProvided
            && r.ShotIds.Contains(greeting.Id));

        doc.Shots.Should().OnlyContain(s => PhotoShootPlanUpgrade.HasPhoto(doc, s.Id));
        doc.Setups.Single(s => s.SeedKey == "setup:01-white-tight-headshot").ShootingBlock.Should().Be(1);
        doc.Setups.Single(s => s.SeedKey == "setup:26-floor-play").ShootingBlock.Should().Be(8);
        doc.Shots.Single(s => s.SeedKey == "shot:burgundy-wedding-elegance").SetupId
            .Should().Be(doc.Setups.Single(s => s.SeedKey == "setup:14-white-magic-cards").Id);

        PhotoShootPlanUpgrade.Apply(doc, Now).Should().BeFalse();
        PhotoShootPlanUpgrade.NeedsWork(doc).Should().BeFalse();
    }

    private static PhotoShootCatalog LoadCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "photoshoot-catalog.json");
        var json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<PhotoShootCatalog>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        })!;
    }
}
