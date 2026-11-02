extern alias importer;
using ApiIsolated;
using BlazorApp.Shared.PhotoReview;
using FluentAssertions;
using LuckyLuke.PhotoReviewStorage;
using importer::PhotoReviewImport;
using static UnitTest.PhotoReview.PhotoReviewTestData;

namespace UnitTest.PhotoReview;

/// <summary>Runs against an isolated temp folder. Never touches production storage.</summary>
[Collection("PhotoReviewStore")]
public sealed class PhotoReviewStorageAndToolTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "photoreview-test-" + Guid.NewGuid().ToString("N"));

    public PhotoReviewStorageAndToolTests()
    {
        Directory.CreateDirectory(_dir);
        PhotoReviewData.UseStore(new LocalReviewStore(_dir));
    }

    public void Dispose()
    {
        PhotoReviewData.UseStore(null);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public async Task ConcurrentRatings_AreAllCounted_WithOneVoteEach()
    {
        var doc = new ParticipationDocument { ShootId = "shoot1" };
        var actors = Enumerable.Range(0, 40).Select(i => WithVisitor(doc, "Person " + i).Actor).ToList();
        (await PhotoReviewData.UpdateParticipationAsync("shoot1", stored =>
        {
            stored.Visitors.AddRange(doc.Visitors);
            return ReviewResult.Done();
        })).Result.Ok.Should().BeTrue();

        // Everyone rates the same photo at once, then half of them change their minds at once.
        var results = await Task.WhenAll(actors.Select((actor, i) => Task.Run(() =>
            PhotoReviewData.UpdateParticipationAsync("shoot1", stored => PhotoReviewParticipation.Rate(stored, actor, "p1", 1 + i % 10, _ => true, Now)))));
        var changes = await Task.WhenAll(actors.Take(20).Select(actor => Task.Run(() =>
            PhotoReviewData.UpdateParticipationAsync("shoot1", stored => PhotoReviewParticipation.Rate(stored, actor, "p1", 10, _ => true, Now)))));

        var failed = results.Concat(changes).Count(r => !r.Result.Ok);
        var (final, _) = await PhotoReviewData.LoadParticipationAsync("shoot1");
        final.Votes.Should().HaveCount(40);
        final.Totals["p1"].Count.Should().Be(final.Votes.Count);
        final.Totals["p1"].Sum.Should().Be(final.Votes.Sum(v => v.Value));
        failed.Should().Be(0, "retries absorb the contention");
        final.Votes.Where(v => actors.Take(20).Any(a => a.VisitorId == v.VisitorId)).Should().OnlyContain(v => v.Value == 10);
    }

    [Fact]
    public async Task AdminBinding_IsCreateOnly()
    {
        var first = new BlazorApp.Shared.PhotoShoot.SwaClientPrincipal { IdentityProvider = "aad", UserId = "real-luke", UserDetails = "luke@lukehammermagic.com" };
        var second = new BlazorApp.Shared.PhotoShoot.SwaClientPrincipal { IdentityProvider = "aad", UserId = "someone-else", UserDetails = "luke@lukehammermagic.com" };
        (await PhotoReviewData.TryBindAsync(first)).Should().BeTrue();
        (await PhotoReviewData.TryBindAsync(second)).Should().BeFalse("an existing binding is never replaced");
        (await PhotoReviewData.LoadBindingAsync())!.UserId.Should().Be("real-luke");
    }

    [Fact]
    public async Task LocalStore_ETagConditionsHold()
    {
        var store = new LocalReviewStore(_dir);
        var etag = await store.TryWriteAsync("review/x.json", "1"u8.ToArray(), "application/json", null, createOnly: true);
        etag.Should().NotBeNull();
        (await store.TryWriteAsync("review/x.json", "2"u8.ToArray(), "application/json", null, createOnly: true)).Should().BeNull();
        (await store.TryWriteAsync("review/x.json", "2"u8.ToArray(), "application/json", "\"stale\"", createOnly: false)).Should().BeNull();
        (await store.TryWriteAsync("review/x.json", "2"u8.ToArray(), "application/json", etag, createOnly: false)).Should().NotBeNull();
        Func<Task> act = () => store.ReadAsync("../outside.json");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Csv_HandlesQuotesCommasNewlinesAndBom()
    {
        var rows = Csv.Parse("﻿shot,reason,sha\r\n001,\"Smile, with \"\"teeth\"\"\nand eyes\",abc\r\n\r\n002,plain,def\r\n");
        rows.Should().HaveCount(2);
        rows[0]["shot"].Should().Be("001");
        rows[0]["reason"].Should().Be("Smile, with \"teeth\"\nand eyes");
        rows[1]["sha"].Should().Be("def");
    }

    [Theory]
    [InlineData("Photographed shot-14 slate at source 82; pose checked.", "", "SlateSupported")]
    [InlineData("Slate-supported interval: follows shot 2 slate.", "", "SlateSupported")]
    [InlineData("Inferred match, medium confidence: no dedicated slate found.", "", "Inferred")]
    [InlineData("Visual match to a later wardrobe variation; no dedicated slate.", "", "Approximate")]
    [InlineData("Photographed shot-9 slate at source 50.", "so this is an approximate execution of the brief.", "Approximate")]
    public void Evidence_IsClassifiedConservatively(string evidence, string reason, string expected) =>
        SelectionManifestBuilder.Evidence(evidence, reason).Should().Be(expected);

    [Fact]
    public void Warnings_FlagTheSource827SlateAndMirroredTitles()
    {
        SelectionManifestBuilder.Warnings("Inferred match within source interval [827, 882].", "").Should().ContainSingle().Which.Should().Contain("827");
        SelectionManifestBuilder.Warnings("Inferred; source 1827 frame", "").Should().BeEmpty();
        SelectionManifestBuilder.Warnings("Slate", "Captured subject is on image-left, opposite the Subject Right title; preserve orientation.")
            .Should().ContainSingle().Which.Should().Contain("Do not mirror");
    }

    [Theory]
    [InlineData(SkiaSharp.SKEncodedOrigin.TopLeft, 0, 0, 0, 0)]
    [InlineData(SkiaSharp.SKEncodedOrigin.RightTop, 0, 0, 30, 0)]      // 90° clockwise: stored top-left lands top-right
    [InlineData(SkiaSharp.SKEncodedOrigin.BottomRight, 0, 0, 40, 30)]  // 180°
    [InlineData(SkiaSharp.SKEncodedOrigin.LeftBottom, 0, 0, 0, 40)]    // 270° clockwise
    [InlineData(SkiaSharp.SKEncodedOrigin.TopRight, 0, 0, 40, 0)]      // mirrored
    public void OrientationMatrix_PutsStoredCornersWhereExifSays(SkiaSharp.SKEncodedOrigin origin, float x, float y, float expectedX, float expectedY)
    {
        var point = importer::PhotoReviewImport.Program.OrientationMatrix(origin, 40, 30).MapPoint(x, y);
        point.X.Should().BeApproximately(expectedX, 0.01f);
        point.Y.Should().BeApproximately(expectedY, 0.01f);
    }

    [Fact]
    public void Importer_LocalRun_UploadsThumbnailsAndIsIdempotent()
    {
        // Synthetic generated image only.
        var root = Path.Combine(_dir, "collection");
        Directory.CreateDirectory(Path.Combine(root, "Headshots"));
        var file = Path.Combine(root, "Headshots", "lucky-luke-magic-test-01.jpg");
        using (var bitmap = new SkiaSharp.SKBitmap(600, 900))
        {
            bitmap.Erase(new SkiaSharp.SKColor(200, 30, 60));
            using var data = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 85);
            File.WriteAllBytes(file, data.ToArray());
        }

        var bytes = File.ReadAllBytes(file);
        var planner = Planner();
        var store = Path.Combine(_dir, "store");
        Directory.CreateDirectory(Path.Combine(store, "shoots"));
        File.WriteAllText(Path.Combine(store, "shoots", "shoot1.json"), System.Text.Json.JsonSerializer.Serialize(planner, importer::PhotoReviewImport.Program.Json));
        var manifest = Manifest(new ManifestPhoto
        {
            PhotoKey = "lucky-luke-magic-test-01",
            ShotId = "shot-001",
            Category = "Headshots",
            Versions = new List<ManifestVersion>
            {
                new ManifestVersion { Kind = "Original", File = "Headshots/lucky-luke-magic-test-01.jpg", Sha256 = importer::PhotoReviewImport.Program.Sha256(bytes), Bytes = bytes.Length, Width = 600, Height = 900, Provenance = "Camera" }
            }
        });
        var manifestPath = Path.Combine(_dir, "manifest.json");
        File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest, importer::PhotoReviewImport.Program.Json));

        importer::PhotoReviewImport.Program.Main(new[] { "import", "--manifest", manifestPath, "--root", root, "--local-dir", store }).GetAwaiter().GetResult().Should().Be(0);
        var sha = importer::PhotoReviewImport.Program.Sha256(bytes);
        File.ReadAllBytes(Path.Combine(store, "review", "assets", sha)).Should().Equal(bytes, "originals are stored byte-identically");
        File.Exists(Path.Combine(store, "review", "thumbs", sha + "-480.webp")).Should().BeTrue();
        File.Exists(Path.Combine(store, "review", "thumbs", sha + "-600.webp")).Should().BeTrue("thumbnails are never upscaled");
        var catalogPath = Path.Combine(store, "review", "shoot1", "catalog.json");
        var before = File.ReadAllText(catalogPath);
        before.Should().Contain("thumbWidths");

        importer::PhotoReviewImport.Program.Main(new[] { "import", "--manifest", manifestPath, "--root", root, "--local-dir", store }).GetAwaiter().GetResult().Should().Be(0);
        File.ReadAllText(catalogPath).Should().Be(before, "a second identical import writes nothing");

        // A file that doesn't match its manifest hash stops the import before anything is written.
        File.WriteAllBytes(file, bytes.Concat(new byte[] { 0 }).ToArray());
        importer::PhotoReviewImport.Program.Main(new[] { "import", "--manifest", manifestPath, "--root", root, "--local-dir", store }).GetAwaiter().GetResult().Should().Be(3);
        File.ReadAllText(catalogPath).Should().Be(before);
    }
}
