using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;
using static UnitTest.PhotoShoot.PhotoShootTestData;

namespace UnitTest.PhotoShoot;

public class PhotoShootSeedImportTests
{
    [Fact]
    public void CreateFromCatalog_BuildsSetupsAndShotsInCatalogOrder()
    {
        var doc = PhotoShootSeeder.CreateFromCatalog(Catalog(), "seeded", Now);

        doc.Name.Should().Be("Catalog Shoot");
        doc.SeedCatalogId.Should().Be("test-catalog");
        doc.Setups.Select(s => s.Name).Should().Equal("White headshots", "Kids");
        OrderTitles(doc).Should().Equal("Warm Smile", "Suspicious Side-Eye", "Child Amazed");
        doc.Shots.Should().OnlyContain(s => s.Status == ShotStatus.NotStarted && s.SeedKey.StartsWith("shot:"));
        doc.Setups.Should().OnlyContain(s => s.SeedKey.StartsWith("setup:"));
        doc.Setups.Single(s => s.Name == "Kids").RequiresChildren.Should().BeTrue();
    }

    [Fact]
    public void Merge_RunTwice_DoesNotCreateDuplicates()
    {
        var doc = PhotoShootSeeder.CreateFromCatalog(Catalog(), "seeded", Now);

        var second = PhotoShootSeeder.Merge(doc, Catalog(), Now);
        var third = PhotoShootSeeder.Merge(doc, Catalog(), Now);

        second.AddedShots.Should().Be(0);
        second.AddedSetups.Should().Be(0);
        second.SkippedExisting.Should().Be(5);
        third.AddedShots.Should().Be(0);
        doc.Shots.Should().HaveCount(3);
        doc.Setups.Should().HaveCount(2);
    }

    [Fact]
    public void Merge_DoesNotOverwriteUserEdits()
    {
        var doc = PhotoShootSeeder.CreateFromCatalog(Catalog(), "seeded", Now);
        var smile = Shot(doc, "Warm Smile");
        smile.Title = "My Edited Smile";
        smile.PhotographerInstructions = "Edited by Luke";
        PhotoShootEditor.SetStatus(doc, smile.Id, ShotStatus.Completed, Now);
        doc.Setups[0].Lighting = "Custom lighting";

        PhotoShootSeeder.Merge(doc, Catalog(), Now);

        var reloaded = doc.Shots.Single(s => s.Id == smile.Id);
        reloaded.Title.Should().Be("My Edited Smile");
        reloaded.PhotographerInstructions.Should().Be("Edited by Luke");
        reloaded.Status.Should().Be(ShotStatus.Completed);
        doc.Setups[0].Lighting.Should().Be("Custom lighting");
    }

    [Fact]
    public void Merge_DoesNotResurrectDeletedSeedItems()
    {
        var doc = PhotoShootSeeder.CreateFromCatalog(Catalog(), "seeded", Now);
        PhotoShootEditor.DeleteShot(doc, Shot(doc, "Suspicious Side-Eye").Id);
        PhotoShootEditor.DeleteSetup(doc, doc.Setups.Single(s => s.Name == "Kids").Id, null);

        var result = PhotoShootSeeder.Merge(doc, Catalog(), Now);

        result.AddedShots.Should().Be(0);
        result.AddedSetups.Should().Be(0);
        result.SkippedDeleted.Should().Be(2);
        doc.Shots.Should().NotContain(s => s.Title == "Suspicious Side-Eye");
        doc.Setups.Should().NotContain(s => s.Name == "Kids");
    }

    [Fact]
    public void Merge_AddsNewCatalogItemsToExistingSetups()
    {
        var doc = PhotoShootSeeder.CreateFromCatalog(Catalog(), "seeded", Now);
        var catalog = Catalog();
        catalog.CatalogVersion = 2;
        catalog.Shots.Add(new PhotoShootCatalogShot { Key = "new-one", SetupKey = "01-white", Title = "Brand New", EstimatedMinutes = 2 });

        var result = PhotoShootSeeder.Merge(doc, catalog, Now);

        result.AddedShots.Should().Be(1);
        doc.SeedCatalogVersion.Should().Be(2);
        OrderTitles(doc).Should().Equal("Warm Smile", "Suspicious Side-Eye", "Brand New", "Child Amazed");
    }

    [Fact]
    public void ValidateCatalog_FlagsDuplicateAndUnknownKeys()
    {
        var catalog = Catalog();
        catalog.Shots.Add(new PhotoShootCatalogShot { Key = "warm-smile", SetupKey = "01-white", Title = "Dup" });
        catalog.Shots.Add(new PhotoShootCatalogShot { Key = "orphan", SetupKey = "99-missing", Title = "Orphan" });

        var errors = PhotoShootValidator.ValidateCatalog(catalog);

        errors.Should().Contain(e => e.Contains("Duplicate shot key 'warm-smile'"));
        errors.Should().Contain(e => e.Contains("unknown setup '99-missing'"));
        PhotoShootValidator.ValidateCatalog(Catalog()).Should().BeEmpty();
    }

    [Fact]
    public void Clone_AssignsFreshIdsRemapsLinksAndResetsProgress()
    {
        var source = TwoSetups();
        var a1 = Shot(source, "a1");
        PhotoShootEditor.SetStatus(source, a1.Id, ShotStatus.Completed, Now);
        a1.IsFavorite = true;
        var reference = PhotoShootEditor.CreateReference(source, new PhotoReference { Title = "R", ShotIds = new List<string> { a1.Id } }, Now).Value!;
        reference.ImageId = "img123";
        source.Session.CurrentShotId = a1.Id;

        var clone = PhotoShootCloner.Clone(source, "copy1", "Next Year", resetProgress: true, Now);
        var doc = clone.Document;

        doc.Id.Should().Be("copy1");
        doc.Name.Should().Be("Next Year");
        doc.Shots.Select(s => s.Id).Should().NotIntersectWith(source.Shots.Select(s => s.Id));
        doc.Setups.Select(s => s.Id).Should().NotIntersectWith(source.Setups.Select(s => s.Id));
        OrderTitles(doc).Should().Equal(OrderTitles(source));
        doc.Shots.Should().OnlyContain(s => s.Status == ShotStatus.NotStarted && !s.IsFavorite && s.ShootId == "copy1");
        doc.Session.CurrentShotId.Should().BeEmpty();

        var copiedA1 = Shot(doc, "a1");
        doc.References.Single().ShotIds.Should().Equal(copiedA1.Id);
        clone.ImageCopies.Should().ContainSingle(c => c.Key == "img123" && c.Value == doc.References.Single().ImageId);
        source.Shots.Should().Contain(s => s.Status == ShotStatus.Completed, "the source is untouched");
    }

    [Fact]
    public void Clone_KeepingProgress_PreservesStatusesButNotInProgress()
    {
        var source = TwoSetups();
        PhotoShootEditor.SetStatus(source, Shot(source, "a1").Id, ShotStatus.Completed, Now);
        PhotoShootEditor.SetStatus(source, Shot(source, "a2").Id, ShotStatus.InProgress, Now);
        PhotoShootEditor.SetStatus(source, Shot(source, "a3").Id, ShotStatus.Skipped, Now);

        var doc = PhotoShootCloner.Clone(source, "copy2", "", resetProgress: false, Now).Document;

        Shot(doc, "a1").Status.Should().Be(ShotStatus.Completed);
        Shot(doc, "a2").Status.Should().Be(ShotStatus.NotStarted);
        Shot(doc, "a3").Status.Should().Be(ShotStatus.Skipped);
        doc.Name.Should().Be("Test Shoot");
    }

    [Fact]
    public void Clone_HandlesDuplicateIdsAndSeedKeysPredictably()
    {
        var source = TwoSetups();
        var a1 = Shot(source, "a1");
        var a2 = Shot(source, "a2");
        a2.Id = a1.Id;
        a1.SeedKey = "shot:x";
        a2.SeedKey = "shot:x";

        var doc = PhotoShootCloner.Clone(source, "copy3", "x", resetProgress: false, Now).Document;

        doc.Shots.Should().HaveCount(5);
        doc.Shots.Select(s => s.Id).Should().OnlyHaveUniqueItems();
        doc.Shots.Count(s => s.SeedKey == "shot:x").Should().Be(1);
    }

    [Fact]
    public void ImportThenReseed_IsStillIdempotent()
    {
        var original = PhotoShootSeeder.CreateFromCatalog(Catalog(), "seeded", Now);
        var imported = PhotoShootCloner.Clone(original, "imported", "Imported", resetProgress: false, Now).Document;

        var result = PhotoShootSeeder.Merge(imported, Catalog(), Now);

        result.AddedShots.Should().Be(0);
        imported.Shots.Should().HaveCount(3);
    }

    [Fact]
    public void ValidateDocument_RejectsBadDataAndWarnsOnOrphans()
    {
        var doc = TwoSetups();
        doc.Shots[0].Title = "";
        doc.Shots[1].SetupId = "ghost";
        doc.References.Add(new PhotoReference { Title = "Bad link", SourceUrl = "javascript:alert(1)" });

        var (errors, warnings) = PhotoShootValidator.ValidateDocument(doc);

        errors.Should().Contain(e => e.Contains("Title is required"));
        errors.Should().Contain(e => e.Contains("Source URL"));
        warnings.Should().Contain(w => w.Contains("missing setup"));
        PhotoShootValidator.ValidateDocument(null).Errors.Should().NotBeEmpty();
        PhotoShootValidator.ValidateDocument(TwoSetups()).Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("https://example.com/a.jpg", true)]
    [InlineData("http://example.com", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    [InlineData("example.com/no-scheme", false)]
    [InlineData("ftp://example.com/file", false)]
    public void IsSafeUrl_OnlyAllowsHttpAndHttps(string url, bool expected)
    {
        PhotoShootValidator.IsSafeUrl(url).Should().Be(expected);
    }

    [Fact]
    public void ReferenceCrud_ValidatesAndFiltersUnknownShots()
    {
        var doc = TwoSetups();
        var a1 = Shot(doc, "a1");

        var created = PhotoShootEditor.CreateReference(doc, new PhotoReference
        {
            Title = "Mr. Bean side-eye",
            SourceUrl = "https://example.com/bean",
            ShotIds = new List<string> { a1.Id, "ghost" },
            UsageRights = "Inspiration only - do not publish"
        }, Now);
        created.Ok.Should().BeTrue();
        created.Value!.ShotIds.Should().Equal(a1.Id);

        PhotoShootEditor.CreateReference(doc, new PhotoReference { Title = "" }, Now).Ok.Should().BeFalse();

        var updated = PhotoShootEditor.UpdateReference(doc, created.Value.Id, new PhotoReference { Title = "Renamed", ShotIds = new List<string>() }, Now);
        updated.Ok.Should().BeTrue();
        updated.Value!.Title.Should().Be("Renamed");

        created.Value.ImageId = "abc123";
        PhotoShootEditor.DeleteReference(doc, created.Value.Id).Should().Be("abc123");
        PhotoShootEditor.DeleteReference(doc, created.Value.Id).Should().BeNull();
    }

    [Fact]
    public void ParseList_SplitsCommasAndNewlines()
    {
        PhotoShootEditor.ParseList("Wand, cards\nSilk,  ,wand").Should().Equal("Wand", "cards", "Silk");
    }
}
