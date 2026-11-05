using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorApp.Shared.PhotoReview
{
    /// <summary>
    /// The public photo library: each accepted (Primary) photo with its cropped versions, across every shoot. A pose is one
    /// shot title and category. The newest shoot's accepted photos for a pose are current; older accepted photos stay listed
    /// as earlier. Built from public review data only, so hidden photos, superseded versions and viewer details never appear.
    /// Used for the HTML page and the JSON manifest.
    /// </summary>
    public static class PhotoLibrary
    {
        /// <summary>The versions offered for download, in the order they are listed.</summary>
        public static readonly ArtifactKind[] Kinds = { ArtifactKind.CroppedWhiteBackground, ArtifactKind.Cropped, ArtifactKind.CroppedTransparent };

        /// <summary>One shoot's review data, read without any viewer identity so hidden photos stay hidden.</summary>
        public class Source
        {
            public string ShootId { get; set; } = "";
            /// <summary>Planned shoot date (yyyy-MM-dd), or empty.</summary>
            public string ShootDate { get; set; } = "";
            public DateTime CreatedUtc { get; set; }
            public PhotoReviewProjection Projection { get; set; } = null!;
        }

        public static List<PhotoLibraryItem> Build(IEnumerable<Source> shoots, string apiBase)
        {
            var items = new List<PhotoLibraryItem>();
            foreach (var shoot in shoots)
            {
                var summary = shoot.Projection.Summary();
                foreach (var shot in summary.Shots)
                {
                    var detail = shoot.Projection.ShotDetail(shot.ShotId);
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
                            ShootId = shoot.ShootId,
                            ShootDate = shoot.ShootDate,
                            ShootCreatedUtc = shoot.CreatedUtc,
                            PoseKey = PoseKey(shot),
                            PoseSummary = shot.Summary,
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
            }

            return Arrange(items);
        }

        /// <summary>
        /// Marks the newest shoot's photos for each pose as current, then orders the list: poses by category, current photos
        /// first, then earlier photos newest first.
        /// </summary>
        public static List<PhotoLibraryItem> Arrange(IEnumerable<PhotoLibraryItem> items)
        {
            var list = items.ToList();
            var newestShootByPose = list
                .GroupBy(item => item.PoseKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.ShootCreatedUtc).First().ShootId, StringComparer.Ordinal);
            foreach (var item in list)
            {
                item.IsCurrent = item.ShootId == newestShootByPose[item.PoseKey];
            }

            return list
                .OrderBy(item => item.CategorySlug, StringComparer.Ordinal)
                .ThenBy(item => item.PoseKey, StringComparer.Ordinal)
                .ThenBy(item => item.IsCurrent ? 0 : 1)
                .ThenByDescending(item => item.ShootCreatedUtc)
                .ThenBy(item => item.PhotoKey, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>A pose stays the same across shoots: its category and title slug. Untitled shots fall back to their id.</summary>
        public static string PoseKey(ShotSummary shot)
        {
            var slug = Regex.Replace(shot.Title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return PhotoReviewLabels.CategorySlug(shot.Category) + "/" + (slug.Length > 0 ? slug : shot.ShotId);
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
            html.Append("article{background:#fff;border-radius:8px;padding:12px;box-shadow:0 1px 3px rgba(0,0,0,.08)}article.earlier{opacity:.85}");
            html.Append("article img{width:100%;height:auto;border-radius:4px;display:block}h2{font-size:1.05rem;margin:10px 0 4px}");
            html.Append(".meta{font-size:.85rem;color:#555;margin:0 0 8px}ul{list-style:none;padding:0;margin:0}li{margin:4px 0}a{color:#1a56db}</style>\n");
            html.Append("</head>\n<body>\n<main>\n<h1>Lucky Luke Magic Photo Library</h1>\n");
            html.Append("<p>Photos from our family magic shows in Vancouver, WA and Portland, OR. Download any version below. <a href=\"https://lukehammer.com/photos\">Filter and sort the library</a>.</p>\n");
            html.Append("<section class=\"grid\" aria-label=\"Photos\">\n");
            foreach (var item in items)
            {
                html.Append(item.IsCurrent ? "<article>\n" : "<article class=\"earlier\">\n");
                if (item.ThumbUrl.Length > 0)
                {
                    html.Append("<img src=\"").Append(Enc(item.ThumbUrl)).Append("\" alt=\"").Append(Enc(AltText(item))).Append("\" loading=\"lazy\">\n");
                }

                html.Append("<h2>").Append(Enc(item.Title)).Append("</h2>\n");
                var rating = item.Rating == null ? "Not yet rated" : $"Rated {item.Rating:0.0} from {item.RatingCount} vote{(item.RatingCount == 1 ? "" : "s")}";
                html.Append("<p class=\"meta\">").Append(Enc(item.Category)).Append(" · ").Append(Enc(rating)).Append("</p>\n");
                if (!item.IsCurrent)
                {
                    var earlier = item.ShootDate.Length > 0 ? $"Earlier accepted photo from {item.ShootDate}" : "Earlier accepted photo";
                    html.Append("<p class=\"meta\">").Append(Enc(earlier)).Append("</p>\n");
                }

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
        /// <summary>The shoot this photo was accepted in. Internal ids only; the public page shows the date instead of names.</summary>
        public string ShootId { get; set; } = "";
        public string ShootDate { get; set; } = "";
        public DateTime ShootCreatedUtc { get; set; }
        /// <summary>Same key for the same pose across shoots, so the newest accepted photo can stand in for older ones.</summary>
        public string PoseKey { get; set; } = "";
        /// <summary>The pose's summary from the shot plan. Empty when none was written.</summary>
        public string PoseSummary { get; set; } = "";
        /// <summary>True for the newest accepted photo of its pose. False means an earlier accepted photo, still listed.</summary>
        public bool IsCurrent { get; set; } = true;
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
