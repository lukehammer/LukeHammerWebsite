using BlazorApp.Shared.PhotoReview;
using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;
using static UnitTest.PhotoReview.PhotoReviewTestData;

namespace UnitTest.PhotoReview;

public class PhotoLibraryTests
{
    private static PhotoLibraryItem Item(string title) => new PhotoLibraryItem
    {
        PhotoId = "f947d72caff9",
        Title = title,
        ShotNumber = 1,
        Category = "Headshots",
        CategorySlug = "headshots",
        Rating = 7.0,
        RatingCount = 1,
        ThumbUrl = "https://lukehammer.com/api/photoreview/photos/f947d72caff9/versions/7638ff91550f0afb/thumb/480",
        Files =
        {
            new PhotoLibraryFile
            {
                Kind = "CroppedWhiteBackground",
                Label = "cropped on white",
                FileName = "warm-smile-01.jpg",
                Bytes = 2048 * 1024,
                Url = "https://lukehammer.com/api/photoreview/photos/f947d72caff9/versions/abc/file",
                DownloadUrl = "https://lukehammer.com/api/photoreview/photos/f947d72caff9/versions/abc/file?download=1"
            }
        }
    };

    [Fact]
    public void Html_is_a_complete_page_with_images_and_download_links()
    {
        var html = PhotoLibrary.Html(new[] { Item("Warm Smile Headshot") }, "https://lukehammer.com/api/photoreview/library");

        html.Should().StartWith("<!doctype html>");
        html.Should().Contain("<img src=\"https://lukehammer.com/api/photoreview/photos/f947d72caff9/versions/7638ff91550f0afb/thumb/480\"");
        html.Should().Contain("alt=\"Lucky Luke Magic: Warm Smile Headshot, headshots\"");
        html.Should().Contain("?download=1\">Download cropped on white (2.0 MB)</a>");
        html.Should().Contain("<link rel=\"canonical\" href=\"https://lukehammer.com/api/photoreview/library\">");
    }

    [Fact]
    public void Html_encodes_titles_so_they_cannot_inject_markup()
    {
        var html = PhotoLibrary.Html(new[] { Item("<script>alert(1)</script> & \"quotes\"") }, "https://lukehammer.com/x");

        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;quotes&quot;");
    }

    [Fact]
    public void Html_says_not_yet_rated_when_there_are_no_votes()
    {
        var item = Item("New Shot");
        item.Rating = null;
        item.RatingCount = 0;

        PhotoLibrary.Html(new[] { item }, "https://lukehammer.com/x").Should().Contain("Not yet rated");
    }

    [Fact]
    public void Kind_labels_match_the_versions_offered_for_download()
    {
        PhotoLibrary.Kinds.Should().Equal(ArtifactKind.CroppedWhiteBackground, ArtifactKind.Cropped, ArtifactKind.CroppedTransparent);
        PhotoLibrary.KindLabel(ArtifactKind.CroppedTransparent).Should().Be("cropped transparent");
    }

    [Fact]
    public void Newest_accepted_photo_is_current_and_older_accepted_photos_stay_listed()
    {
        var older = Planner();
        older.ShootDate = "2026-06-14";
        var newer = Planner();
        newer.CreatedUtc = Now.AddDays(30);

        var items = PhotoLibrary.Build(new[]
        {
            Shoot("older", older, ImportedCatalog(older, Photo(1, "shot-001", Cropped(1)))),
            Shoot("newer", newer, ImportedCatalog(newer, Photo(2, "shot-001", Cropped(2))))
        }, ApiBase);

        items.Should().HaveCount(2);
        items[0].ShootId.Should().Be("newer", "the current photo for a pose comes before its earlier photos");
        items[0].IsCurrent.Should().BeTrue();
        items[1].IsCurrent.Should().BeFalse();

        var html = PhotoLibrary.Html(items, ApiBase + "/library");
        html.Should().Contain("<article class=\"earlier\">");
        html.Should().Contain("Earlier accepted photo from 2026-06-14");
    }

    [Fact]
    public void A_pose_with_only_an_older_photo_is_still_current()
    {
        var older = Planner();
        var newer = Planner();
        newer.CreatedUtc = Now.AddDays(30);

        var items = PhotoLibrary.Build(new[]
        {
            Shoot("older", older, ImportedCatalog(older, Photo(1, "shot-001", Cropped(1)))),
            Shoot("newer", newer, ImportedCatalog(newer, Photo(2, "shot-002", Cropped(2))))
        }, ApiBase);

        items.Should().HaveCount(2);
        items.Should().OnlyContain(item => item.IsCurrent);
    }

    [Fact]
    public void Hidden_photos_from_older_shoots_stay_out_of_the_library()
    {
        var older = Planner();
        var newer = Planner();
        newer.CreatedUtc = Now.AddDays(30);
        var olderCatalog = ImportedCatalog(older, Photo(1, "shot-001", Cropped(1)));
        PhotoReviewAdminOps.SetPhotoHidden(olderCatalog, Admin, olderCatalog.Photos[0].Id, true, Now);

        var items = PhotoLibrary.Build(new[]
        {
            Shoot("older", older, olderCatalog),
            Shoot("newer", newer, ImportedCatalog(newer, Photo(2, "shot-001", Cropped(2))))
        }, ApiBase);

        items.Should().ContainSingle().Which.ShootId.Should().Be("newer");
    }

    private const string ApiBase = "https://lukehammer.com/api/photoreview";

    private static PhotoLibrary.Source Shoot(string id, PhotoShootDocument planner, ReviewCatalog catalog) => new PhotoLibrary.Source
    {
        ShootId = id,
        ShootDate = planner.ShootDate,
        CreatedUtc = planner.CreatedUtc,
        Projection = new PhotoReviewProjection(planner, catalog, new ParticipationDocument(), new ReviewActor(), new ReviewViewer())
    };

    private static ManifestVersion Cropped(int seed) => new ManifestVersion
    {
        Kind = "CroppedWhiteBackground",
        File = $"Cropped/photo-{seed}.jpg",
        Sha256 = Sha(seed, 'c'),
        Bytes = 500 + seed,
        Width = 2000,
        Height = 3000,
        ContentType = "image/jpeg",
        Provenance = "NonGenerative",
        ParentKind = "Original"
    };
}
