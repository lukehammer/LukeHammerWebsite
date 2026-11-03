using BlazorApp.Shared.PhotoReview;
using FluentAssertions;

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
}
