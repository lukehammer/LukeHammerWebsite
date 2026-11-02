using BlazorApp.Shared.PhotoReview;
using FluentAssertions;
using static UnitTest.PhotoReview.PhotoReviewTestData;

namespace UnitTest.PhotoReview;

public class PhotoReviewIngestTests
{
    private static IngestReport Apply(ReviewCatalog catalog, ReviewManifest manifest, DateTime? now = null) =>
        PhotoReviewIngest.Apply(catalog, manifest, PhotoReviewProjection.PlannedRefs(Planner()), new Dictionary<string, ThumbInfo>(), _ => null, now ?? Now);

    private static ManifestVersion Derivative(string kind, string parent, int seed, string provenance = "NonGenerative") => new ManifestVersion
    {
        Kind = kind,
        ParentKind = parent,
        File = $"edits/{kind}-{seed}.png",
        Sha256 = Sha(seed, 'b'),
        Bytes = 500 + seed,
        Width = 2000,
        Height = 3000,
        ContentType = "image/png",
        Provenance = provenance,
        ProcessingNotes = "Background removed with a mask."
    };

    [Fact]
    public void Import_IsIdempotent_AndPhotoIdsAreStable()
    {
        var catalog = new ReviewCatalog();
        var manifest = Manifest(Photo(1, "shot-001"), Photo(2, "shot-002"));
        var first = Apply(catalog, manifest);
        first.Ok.Should().BeTrue();
        first.PhotosAdded.Should().Be(2);
        var ids = catalog.Photos.Select(p => p.Id).ToList();
        ids.Should().Equal(PhotoReviewIngest.PhotoIdFor("lucky-luke-magic-pose-01"), PhotoReviewIngest.PhotoIdFor("lucky-luke-magic-pose-02"));

        var again = Apply(catalog, manifest, Now.AddHours(1));
        again.Ok.Should().BeTrue();
        again.Changed.Should().BeFalse();
        again.PhotosUnchanged.Should().Be(2);
        catalog.Photos.Should().HaveCount(2);
        catalog.Photos.Select(p => p.Id).Should().Equal(ids);
        catalog.Imports.Should().ContainSingle("a no-op import is not recorded");
    }

    [Fact]
    public void Import_CreatesAReviewRowForEveryPlannedShot_IncludingAbsentOnes()
    {
        var catalog = new ReviewCatalog();
        Apply(catalog, Manifest(Photo(1, "shot-001"))).Ok.Should().BeTrue();
        catalog.Shots.Should().HaveCount(6);
        catalog.Shots.Select(s => s.ShotNumber).Should().Equal(1, 2, 3, 4, 5, 6);
        catalog.Shots.Single(s => s.ShotId == "shot-006").Category.Should().Be(ReviewCategory.KidsAndVolunteers);
        catalog.Shots.Should().OnlyContain(s => s.OwnerStatus == null, "imports never decide success");
    }

    [Fact]
    public void Import_RejectsUnknownShots_AndNumberOrTitleMismatches()
    {
        var bad = Manifest(Photo(1, "shot-999"));
        Apply(new ReviewCatalog(), bad).Errors.Should().ContainSingle(e => e.Contains("not in the live planner"));

        var wrongNumber = Manifest(Photo(1, "shot-002"));
        wrongNumber.Photos[0].ShotNumber = 3;
        Apply(new ReviewCatalog(), wrongNumber).Errors.Should().ContainSingle(e => e.Contains("does not match planner number 2"));

        var wrongTitle = Manifest();
        wrongTitle.Shots.Add(new ManifestShot { ShotId = "shot-001", ShotNumber = 1, Title = "Something else" });
        var catalog = new ReviewCatalog();
        Apply(catalog, wrongTitle).Errors.Should().ContainSingle(e => e.Contains("does not match planner title"));
        catalog.Shots.Should().BeEmpty("nothing is written when validation fails");
    }

    [Fact]
    public void Import_RefusesAChangedOriginal()
    {
        var catalog = new ReviewCatalog();
        Apply(catalog, Manifest(Photo(1, "shot-001"))).Ok.Should().BeTrue();
        var tampered = Manifest(Photo(1, "shot-001"));
        tampered.Photos[0].Versions[0].Sha256 = Sha(77);
        var report = Apply(catalog, tampered);
        report.Errors.Should().ContainSingle(e => e.Contains("Originals are immutable"));
        catalog.Photos.Single().Versions.Single().Sha256.Should().Be(Sha(1));
    }

    [Fact]
    public void Derivatives_LinkToTheirParent_AndRedeliverySupersedes()
    {
        var catalog = new ReviewCatalog();
        Apply(catalog, Manifest(Photo(1, "shot-001"))).Ok.Should().BeTrue();
        var original = catalog.Photos.Single().Versions.Single();

        // Derivative-only manifest: photo metadata omitted, so it must not be erased.
        var derivatives = Manifest(new ManifestPhoto
        {
            PhotoKey = "lucky-luke-magic-pose-01",
            Versions = new List<ManifestVersion>
            {
                Derivative("CroppedTransparent", "Transparent", 3),
                Derivative("Transparent", "Original", 2),
                Derivative("Cropped", "Original", 4)
            }
        });
        var report = Apply(catalog, derivatives);
        report.Ok.Should().BeTrue(string.Join("; ", report.Errors));
        report.VersionsAdded.Should().Be(3);
        var photo = catalog.Photos.Single();
        photo.Reason.Should().Be("Good smile.");
        photo.Evidence.Should().Be(MatchEvidence.SlateSupported);
        var transparent = photo.Versions.Single(v => v.Kind == ArtifactKind.Transparent);
        transparent.ParentVersionId.Should().Be(original.Id);
        photo.Versions.Single(v => v.Kind == ArtifactKind.CroppedTransparent).ParentVersionId.Should().Be(transparent.Id);
        transparent.FileName.Should().Be("lucky-luke-magic-pose-01-transparent.png");
        original.FileName.Should().Be("lucky-luke-magic-pose-01.jpg");

        Apply(catalog, derivatives).Changed.Should().BeFalse();

        var redelivered = Manifest(new ManifestPhoto { PhotoKey = "lucky-luke-magic-pose-01", Versions = new List<ManifestVersion> { Derivative("Transparent", "Original", 9) } });
        var second = Apply(catalog, redelivered);
        second.VersionsSuperseded.Should().Be(1);
        photo.Versions.Count(v => v.Kind == ArtifactKind.Transparent).Should().Be(2);
        photo.Versions.Count(v => v.Kind == ArtifactKind.Transparent && v.IsCurrent).Should().Be(1);
        photo.Versions.Single(v => v.Kind == ArtifactKind.Transparent && v.IsCurrent).Sha256.Should().Be(Sha(9, 'b'));
    }

    [Fact]
    public void Derivatives_NeedAParent_AndHonestProvenance()
    {
        var catalog = new ReviewCatalog();
        Apply(catalog, Manifest(Photo(1, "shot-001"))).Ok.Should().BeTrue();
        Apply(catalog, Manifest(new ManifestPhoto { PhotoKey = "lucky-luke-magic-pose-01", Versions = { Derivative("CroppedTransparent", "Transparent", 3) } }))
            .Errors.Should().Contain(e => e.Contains("parent Transparent does not exist"));
        Apply(catalog, Manifest(new ManifestPhoto { PhotoKey = "lucky-luke-magic-pose-01", Versions = { Derivative("Cropped", "Original", 3, "Camera") } }))
            .Errors.Should().Contain(e => e.Contains("NonGenerative or Generative"));
        Apply(catalog, Manifest(new ManifestPhoto { PhotoKey = "lucky-luke-magic-new", Versions = { Derivative("Cropped", "Original", 3) } }))
            .Errors.Should().Contain(e => e.Contains("need an Original"));
    }

    [Fact]
    public void Reimport_PreservesOwnerDecisionsAndHiddenState()
    {
        var planner = Planner();
        var planned = PhotoReviewProjection.PlannedRefs(planner);
        var catalog = new ReviewCatalog();
        var manifest = Manifest(Photo(1, "shot-001"));
        manifest.Shots.Add(new ManifestShot { ShotId = "shot-002", Coverage = "NoDedicatedMatch", SuggestedStatus = "NotCaptured", Category = "Magic and Props" });
        Apply(catalog, manifest).Ok.Should().BeTrue();
        PhotoReviewAdminOps.SetShotStatus(catalog, planned, Admin, "shot-001", ShotReviewStatus.Successful, "Great", Now).Ok.Should().BeTrue();
        PhotoReviewAdminOps.SetPhotoHidden(catalog, Admin, catalog.Photos[0].Id, true, Now).Ok.Should().BeTrue();

        manifest.Photos[0].Reason = "Updated reasoning.";
        Apply(catalog, manifest, Now.AddDays(1)).PhotosUpdated.Should().Be(1);

        var row = catalog.Shots.Single(s => s.ShotId == "shot-001");
        row.OwnerStatus.Should().Be(ShotReviewStatus.Successful);
        row.OwnerNote.Should().Be("Great");
        catalog.Photos[0].HiddenUtc.Should().NotBeNull();
        catalog.Photos[0].Reason.Should().Be("Updated reasoning.");
        var absent = catalog.Shots.Single(s => s.ShotId == "shot-002");
        absent.SuggestedStatus.Should().Be(ShotReviewStatus.NotCaptured);
        absent.OwnerStatus.Should().BeNull("a suggestion is not a verdict");
        absent.Category.Should().Be(ReviewCategory.MagicAndProps);
    }

    [Fact]
    public void LocalPaths_AreStrippedFromStoredText()
    {
        var catalog = new ReviewCatalog();
        var photo = Photo(1, "shot-001");
        photo.Source = new PhotoSource { Archive = @"C:\Users\LukeR\Downloads\album-1.zip", Url = "javascript:alert(1)" };
        Apply(catalog, Manifest(photo)).Ok.Should().BeTrue();
        catalog.Photos[0].NotesMarkdown.Should().Contain("Archive: album.zip").And.NotContain("Downloads");
        catalog.Photos[0].Source.Archive.Should().Be("album-1.zip");
        catalog.Photos[0].Source.Url.Should().BeEmpty("only http(s) links are kept");
    }

    [Fact]
    public void Validation_RejectsBadKeysTypesAndDuplicates()
    {
        var manifest = Manifest(Photo(1, "shot-001"), Photo(1, "shot-002"));
        manifest.Photos.Add(new ManifestPhoto { PhotoKey = "Bad Key!", ShotId = "shot-001" });
        var gif = Photo(3, "shot-001");
        gif.Versions[0].ContentType = "image/gif";
        manifest.Photos.Add(gif);
        var report = Apply(new ReviewCatalog(), manifest);
        report.Errors.Should().Contain(e => e.Contains("listed twice"));
        report.Errors.Should().Contain(e => e.Contains("photoKey must match"));
        report.Errors.Should().Contain(e => e.Contains("JPEG, PNG or WebP"));
    }

    [Fact]
    public void WrongFormatOrShoot_IsRejected()
    {
        var manifest = Manifest(Photo(1, "shot-001"));
        manifest.Format = "something";
        Apply(new ReviewCatalog(), manifest).Ok.Should().BeFalse();
        var other = Manifest(Photo(1, "shot-001"));
        other.ShootId = "other";
        Apply(new ReviewCatalog { ShootId = "shoot1" }, other).Errors.Should().Contain(e => e.Contains("catalog is for"));
    }
}
