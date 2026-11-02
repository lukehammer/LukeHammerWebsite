using BlazorApp.Shared.PhotoReview;
using BlazorApp.Shared.PhotoShoot;

namespace UnitTest.PhotoReview;

/// <summary>Synthetic fixtures only. No real photos, no production storage.</summary>
internal static class PhotoReviewTestData
{
    public static readonly DateTime Now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A planner with <paramref name="shots"/> shots across two setups, ids "shot-001"….</summary>
    public static PhotoShootDocument Planner(int shots = 6)
    {
        var doc = PhotoShootEditor.CreateShoot("shoot1", "Test shoot", "", "", Now);
        doc.Setups.Add(new PhotoSetup { Id = "setup-a", Sequence = 1, Name = "White seamless" });
        doc.Setups.Add(new PhotoSetup { Id = "setup-b", Sequence = 2, Name = "Gray" });
        for (var i = 1; i <= shots; i++)
        {
            doc.Shots.Add(new PhotoShot
            {
                Id = $"shot-{i:000}",
                SetupId = i <= shots / 2 ? "setup-a" : "setup-b",
                Sequence = i,
                Title = $"Pose {i}",
                Summary = $"Summary {i}",
                Category = i == shots ? ShotCategory.ChildVolunteer : ShotCategory.EssentialPortrait,
                Priority = i == 1 ? ShotPriority.MustHave : ShotPriority.Standard,
                FacialExpression = "Big smile"
            });
        }

        return PhotoShootEditor.Normalize(doc);
    }

    public static string Sha(int seed, char fill = 'a') => seed.ToString("x4") + new string(fill, 60);

    public static ManifestVersion Original(int seed) => new ManifestVersion
    {
        Kind = "Original",
        File = $"Headshots/photo-{seed}.jpg",
        Sha256 = Sha(seed),
        Bytes = 1000 + seed,
        Width = 3648,
        Height = 5472,
        ContentType = "image/jpeg",
        Provenance = "Camera"
    };

    public static ManifestPhoto Photo(int seed, string shotId, params ManifestVersion[] extra)
    {
        var versions = new List<ManifestVersion> { Original(seed) };
        versions.AddRange(extra);
        return new ManifestPhoto
        {
            PhotoKey = $"lucky-luke-magic-pose-{seed:00}",
            ShotId = shotId,
            Category = "Headshots",
            Selection = "Primary",
            Evidence = "SlateSupported",
            EvidenceText = "Photographed shot slate.",
            Reason = "Good smile.",
            Warnings = new List<string>(),
            NotesMarkdown = "# Notes\nArchive: C:\\Users\\Someone\\Downloads\\album.zip",
            Versions = versions
        };
    }

    public static ReviewManifest Manifest(params ManifestPhoto[] photos) => new ReviewManifest
    {
        Format = ReviewManifest.FormatName,
        Version = 1,
        ShootId = "shoot1",
        Generator = "test",
        Photos = photos.ToList()
    };

    public static ReviewCatalog ImportedCatalog(PhotoShootDocument planner, params ManifestPhoto[] photos)
    {
        var catalog = new ReviewCatalog { ShootId = "shoot1" };
        var report = PhotoReviewIngest.Apply(catalog, Manifest(photos), PhotoReviewProjection.PlannedRefs(planner), new Dictionary<string, ThumbInfo>(), _ => null, Now);
        if (!report.Ok) throw new InvalidOperationException(string.Join("\n", report.Errors));
        return catalog;
    }

    public static (ParticipationDocument Doc, ReviewActor Actor, string Token) WithVisitor(ParticipationDocument? doc = null, string name = "Bard")
    {
        doc ??= new ParticipationDocument { ShootId = "shoot1" };
        var id = PhotoReviewParticipation.NewId(12);
        var secret = PhotoReviewParticipation.NewId(32);
        var result = PhotoReviewParticipation.Register(doc, name, id, secret, Now);
        if (!result.Ok) throw new InvalidOperationException(result.Error);
        return (doc, new ReviewActor { VisitorId = id }, id + "." + secret);
    }

    public static ReviewActor Admin => new ReviewActor { IsAdmin = true, AdminName = "Luke" };
}

/// <summary>Tests that swap the API's static store override must not run in parallel with each other.</summary>
[CollectionDefinition("PhotoReviewStore", DisableParallelization = true)]
public sealed class PhotoReviewStoreCollection
{
}
