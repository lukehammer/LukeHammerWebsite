using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace BlazorApp.Shared.PhotoReview
{
    /// <summary>
    /// The public photo library: each accepted (Primary) photo with its cropped versions. Built from public review data
    /// only, so hidden photos, superseded versions and viewer details never appear. Used for the HTML page and the JSON manifest.
    /// </summary>
    public static class PhotoLibrary
    {
        /// <summary>The versions offered for download, in the order they are listed.</summary>
        public static readonly ArtifactKind[] Kinds = { ArtifactKind.CroppedWhiteBackground, ArtifactKind.Cropped, ArtifactKind.CroppedTransparent };

        public static List<PhotoLibraryItem> Build(PhotoReviewProjection projection, ReviewSummaryResponse summary, string apiBase)
        {
            var items = new List<PhotoLibraryItem>();
            foreach (var shot in summary.Shots)
            {
                var detail = projection.ShotDetail(shot.ShotId);
                if (detail == null) continue;
                var photos = detail.Photos
                    .Where(photo => photo.Summary.Selection == SelectionRole.Primary && !photo.Summary.Hidden)
                    .OrderBy(photo => photo.Summary.PhotoKey, StringComparer.Ordinal)
                    .ToList();
                for (var index = 0; index < photos.Count; index++)
                {
                    var photo = photos[index];
                    var files = new List<PhotoLibraryFile>();
                    foreach (var kind in Kinds)
                    {
                        var version = photo.Versions.FirstOrDefault(item => item.Kind == kind && !item.Superseded);
                        if (version == null) continue;
                        var url = $"{apiBase}/photos/{photo.Summary.Id}/versions/{version.Id}/file";
                        files.Add(new PhotoLibraryFile
                        {
                            Kind = kind.ToString(),
                            Label = KindLabel(kind),
                            FileName = version.FileName,
                            Bytes = version.Bytes,
                            Width = version.Width,
                            Height = version.Height,
                            Url = url,
                            DownloadUrl = url + "?download=1"
                        });
                    }

                    if (files.Count == 0) continue;
                    var thumb = photo.Summary.Thumb;
                    var thumbUrl = thumb.VersionId.Length > 0 && thumb.Widths.Count > 0
                        ? $"{apiBase}/photos/{photo.Summary.Id}/versions/{thumb.VersionId}/thumb/{thumb.Widths.Min()}"
                        : "";
                    items.Add(new PhotoLibraryItem
                    {
                        PhotoId = photo.Summary.Id,
                        PhotoKey = photo.Summary.PhotoKey,
                        ShotNumber = shot.Number,
                        Title = photos.Count > 1 ? $"{shot.Title} ({index + 1})" : shot.Title,
                        Category = PhotoReviewLabels.Category(shot.Category),
                        CategorySlug = PhotoReviewLabels.CategorySlug(shot.Category),
                        Rating = photo.Summary.RatingCount > 0 ? Math.Round(photo.Summary.Average, 1) : null,
                        RatingCount = photo.Summary.RatingCount,
                        ThumbUrl = thumbUrl,
                        Files = files
                    });
                }
            }

            return items;
        }

        /// <summary>A complete HTML page with one card per photo. Everything is server-rendered and HTML-encoded, so it reads without JavaScript.</summary>
        public static string Html(IReadOnlyList<PhotoLibraryItem> items, string pageUrl)
        {
            var html = new StringBuilder();
            html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            html.Append("<title>Lucky Luke Magic Photo Library</title>\n");
            html.Append("<meta name=\"description\" content=\"Free family magic show photos from Lucky Luke Magic in Vancouver, WA and Portland, OR.\">\n");
            html.Append("<link rel=\"canonical\" href=\"").Append(Enc(pageUrl)).Append("\">\n");
            html.Append("<style>body{font-family:system-ui,sans-serif;margin:0;padding:16px;background:#f7f7f9;color:#111}");
            html.Append("h1{margin:0 0 8px}main>p{margin:0 0 24px}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:16px}");
            html.Append("article{background:#fff;border-radius:8px;padding:12px;box-shadow:0 1px 3px rgba(0,0,0,.08)}");
            html.Append("article img{width:100%;height:auto;border-radius:4px;display:block}h2{font-size:1.05rem;margin:10px 0 4px}");
            html.Append(".meta{font-size:.85rem;color:#555;margin:0 0 8px}ul{list-style:none;padding:0;margin:0}li{margin:4px 0}a{color:#1a56db}</style>\n");
            html.Append("</head>\n<body>\n<main>\n<h1>Lucky Luke Magic Photo Library</h1>\n");
            html.Append("<p>Photos from our family magic shows in Vancouver, WA and Portland, OR. Download any version below.</p>\n");
            html.Append("<section class=\"grid\" aria-label=\"Photos\">\n");
            foreach (var item in items)
            {
                html.Append("<article>\n");
                if (item.ThumbUrl.Length > 0)
                {
                    html.Append("<img src=\"").Append(Enc(item.ThumbUrl)).Append("\" alt=\"").Append(Enc(AltText(item))).Append("\" loading=\"lazy\">\n");
                }

                html.Append("<h2>").Append(Enc(item.Title)).Append("</h2>\n");
                var rating = item.Rating == null ? "Not yet rated" : $"Rated {item.Rating:0.0} from {item.RatingCount} vote{(item.RatingCount == 1 ? "" : "s")}";
                html.Append("<p class=\"meta\">").Append(Enc(item.Category)).Append(" · ").Append(Enc(rating)).Append("</p>\n");
                html.Append("<ul>\n");
                foreach (var file in item.Files)
                {
                    html.Append("<li><a href=\"").Append(Enc(file.DownloadUrl)).Append("\">Download ").Append(Enc(file.Label))
                        .Append(" (").Append(Enc(FileSize(file.Bytes))).Append(")</a></li>\n");
                }

                html.Append("</ul>\n</article>\n");
            }

            html.Append("</section>\n</main>\n</body>\n</html>\n");
            return html.ToString();
        }

        public static string KindLabel(ArtifactKind kind) => kind switch
        {
            ArtifactKind.CroppedWhiteBackground => "cropped on white",
            ArtifactKind.Cropped => "cropped",
            ArtifactKind.CroppedTransparent => "cropped transparent",
            _ => kind.ToString()
        };

        private static string AltText(PhotoLibraryItem item) => $"Lucky Luke Magic: {item.Title}, {item.Category.ToLowerInvariant()}";

        private static string FileSize(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:F1} MB" : $"{Math.Max(1, bytes / 1024)} KB";

        private static string Enc(string text) => WebUtility.HtmlEncode(text ?? "");
    }

    public class PhotoLibraryItem
    {
        public string PhotoId { get; set; } = "";
        public string PhotoKey { get; set; } = "";
        public int ShotNumber { get; set; }
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string CategorySlug { get; set; } = "";
        public double? Rating { get; set; }
        public int RatingCount { get; set; }
        public string ThumbUrl { get; set; } = "";
        public List<PhotoLibraryFile> Files { get; set; } = new List<PhotoLibraryFile>();
    }

    public class PhotoLibraryFile
    {
        public string Kind { get; set; } = "";
        public string Label { get; set; } = "";
        public string FileName { get; set; } = "";
        public long Bytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Url { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
    }
}
