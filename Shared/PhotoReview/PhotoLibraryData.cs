using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlazorApp.Shared.PhotoReview
{
    /// <summary>
    /// The library as data for AI tools and scripts: one flat list of photos with how each was categorized and rated,
    /// its versions, and a description of every field. Capture details (camera, lens, settings) are never included.
    /// </summary>
    public static class PhotoLibraryData
    {
        public const int SchemaVersion = 1;

        public static readonly string[] SortOptions = { "category", "rating", "newest", "title" };

        public const string Description =
            "Lucky Luke Magic photo library: accepted photos from family magic shows in Vancouver, WA and Portland, OR. " +
            "Each photo lists how it was categorized and rated, and every version available to download. " +
            "Capture details such as camera and settings are not included.";

        public static readonly IReadOnlyDictionary<string, string> FieldDescriptions = new Dictionary<string, string>
        {
            ["schemaVersion"] = "Version of this response format. Increases only when a field changes meaning or is removed.",
            ["generatedUtc"] = "When this response was built (UTC).",
            ["filters"] = "The filters and sort that were applied. A null value means that filter was not applied.",
            ["totalPhotos"] = "How many photos are in the library before filters.",
            ["count"] = "How many photos are in this response after filters.",
            ["photos"] = "The matching photos, in the requested order.",
            ["photos[].id"] = "Stable photo id. Use it to refer to a photo.",
            ["photos[].photoKey"] = "Readable photo name. Stable across re-imports.",
            ["photos[].title"] = "Shot title. A number in brackets is added when one shoot has several accepted photos of the same shot.",
            ["photos[].shotNumber"] = "The shot's number in the plan of the shoot it was accepted in.",
            ["photos[].category"] = "Shot category, as shown on the library page.",
            ["photos[].categorySlug"] = "Machine-friendly category name, for filters.",
            ["photos[].poseKey"] = "Same value for the same pose across shoots: category slug and title slug. Use it to group photos of one pose.",
            ["photos[].poseSummary"] = "Short description of the pose from the shot plan. Null when none was written.",
            ["photos[].rating"] = "Average of visitor votes on a 1 to 10 scale, rounded to one decimal. Null when nobody has voted.",
            ["photos[].ratingCount"] = "How many votes the rating is based on.",
            ["photos[].isCurrent"] = "True for the newest accepted photo of its pose. False for an earlier accepted photo, which is still listed.",
            ["photos[].shootDate"] = "Planned date of the shoot the photo was accepted in (yyyy-MM-dd). Null when no date was set.",
            ["photos[].orientation"] = "landscape, portrait or square, from the size of the cropped version. Null when unknown.",
            ["photos[].hasTransparentVersion"] = "True when a cropped transparent version is available.",
            ["photos[].thumbnailUrl"] = "Small preview image. Null when none is available.",
            ["photos[].versions"] = "Versions that can be downloaded for this photo: cropped versions first, then the uncropped original and its full-frame edits.",
            ["photos[].versions[].kind"] = "Internal name of the version. Cropped: CroppedWhiteBackground, Cropped, CroppedTransparent. Uncropped: Original (exactly as taken), WhiteBackground, Transparent.",
            ["photos[].versions[].isCropped"] = "True when the version is cropped to the subject. False for the full frame.",
            ["photos[].versions[].thumbnailUrl"] = "Small preview of this version. Null when none is available.",
            ["photos[].versions[].previewUrl"] = "Large preview of this version, sized for viewing on screen. Null when none is available.",
            ["photos[].versions[].label"] = "Readable name of the version, for display.",
            ["photos[].versions[].fileName"] = "File name of the version.",
            ["photos[].versions[].widthPx"] = "Width of the version in pixels.",
            ["photos[].versions[].heightPx"] = "Height of the version in pixels.",
            ["photos[].versions[].bytes"] = "File size in bytes.",
            ["photos[].versions[].url"] = "Address that shows the file.",
            ["photos[].versions[].downloadUrl"] = "Same file, served as a download."
        };

        /// <summary>Reads the filters and sort from a query string such as "?category=headshots&amp;minRating=8&amp;sort=rating". Unknown or invalid values are ignored.</summary>
        public static PhotoLibraryQuery Parse(string? queryString)
        {
            var query = new PhotoLibraryQuery();
            foreach (var pair in (queryString ?? "").TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(new[] { '=' }, 2);
                var key = Uri.UnescapeDataString(parts[0]).ToLowerInvariant();
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')).Trim() : "";
                switch (key)
                {
                    case "category":
                        if (value.Length > 0) query.Category = value.ToLowerInvariant();
                        break;
                    case "minrating":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum) && minimum >= 1 && minimum <= 10)
                        {
                            query.MinRating = minimum;
                        }

                        break;
                    case "current":
                        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) query.Current = true;
                        else if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) query.Current = false;
                        break;
                    case "sort":
                        var sort = value.ToLowerInvariant();
                        if (SortOptions.Contains(sort)) query.Sort = sort;
                        break;
                }
            }

            return query;
        }

        /// <summary>Filters and sorts the library. <paramref name="items"/> must already be arranged so that current flags are set.</summary>
        public static LibraryDocument Document(IReadOnlyList<PhotoLibraryItem> items, PhotoLibraryQuery query, DateTime generatedUtc)
        {
            IEnumerable<PhotoLibraryItem> selected = items;
            if (query.Category != null) selected = selected.Where(item => item.CategorySlug == query.Category);
            if (query.MinRating is double minimum) selected = selected.Where(item => item.Rating != null && item.Rating >= minimum);
            if (query.Current is bool current) selected = selected.Where(item => item.IsCurrent == current);
            var photos = Sort(selected, query.Sort).Select(Photo).ToList();

            return new LibraryDocument
            {
                SchemaVersion = SchemaVersion,
                GeneratedUtc = generatedUtc,
                Description = Description,
                Filters = query,
                TotalPhotos = items.Count,
                Count = photos.Count,
                FieldDescriptions = FieldDescriptions,
                Photos = photos
            };
        }

        private static IEnumerable<PhotoLibraryItem> Sort(IEnumerable<PhotoLibraryItem> items, string sort) => sort switch
        {
            "rating" => items.OrderByDescending(item => item.Rating ?? -1)
                .ThenByDescending(item => item.RatingCount)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PhotoKey, StringComparer.Ordinal),
            "newest" => items.OrderByDescending(item => item.ShootCreatedUtc)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PhotoKey, StringComparer.Ordinal),
            "title" => items.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PhotoKey, StringComparer.Ordinal),
            // "category" keeps the order from Arrange: category, pose, current first.
            _ => items
        };

        private static LibraryPhoto Photo(PhotoLibraryItem item)
        {
            var reference = item.Files.FirstOrDefault(file => file.Kind == ArtifactKind.CroppedWhiteBackground.ToString()) ?? item.Files.FirstOrDefault();
            return new LibraryPhoto
            {
                Id = item.PhotoId,
                PhotoKey = item.PhotoKey,
                Title = item.Title,
                ShotNumber = item.ShotNumber,
                Category = item.Category,
                CategorySlug = item.CategorySlug,
                PoseKey = item.PoseKey,
                PoseSummary = item.PoseSummary.Length > 0 ? item.PoseSummary : null,
                Rating = item.Rating,
                RatingCount = item.RatingCount,
                IsCurrent = item.IsCurrent,
                ShootDate = item.ShootDate.Length > 0 ? item.ShootDate : null,
                Orientation = Orientation(reference),
                HasTransparentVersion = item.Files.Any(file => file.Kind == ArtifactKind.CroppedTransparent.ToString()),
                ThumbnailUrl = item.ThumbUrl.Length > 0 ? item.ThumbUrl : null,
                Versions = item.Files.Select(file => new LibraryVersion
                {
                    Kind = file.Kind,
                    Label = file.Label,
                    FileName = file.FileName,
                    WidthPx = file.Width,
                    HeightPx = file.Height,
                    Bytes = file.Bytes,
                    Url = file.Url,
                    DownloadUrl = file.DownloadUrl,
                    IsCropped = file.IsCropped,
                    ThumbnailUrl = file.ThumbUrl.Length > 0 ? file.ThumbUrl : null,
                    PreviewUrl = file.PreviewUrl.Length > 0 ? file.PreviewUrl : null
                }).ToList()
            };
        }

        private static string? Orientation(PhotoLibraryFile? file)
        {
            if (file == null || file.Width == 0 || file.Height == 0) return null;
            if (file.Width > file.Height) return "landscape";
            return file.Width < file.Height ? "portrait" : "square";
        }
    }

    /// <summary>The filters and sort a request asked for. Null means the filter was not applied.</summary>
    public class PhotoLibraryQuery
    {
        public string? Category { get; set; }
        public double? MinRating { get; set; }
        public bool? Current { get; set; }
        public string Sort { get; set; } = "category";
    }

    public class LibraryDocument
    {
        public int SchemaVersion { get; set; }
        public DateTime GeneratedUtc { get; set; }
        public string Description { get; set; } = "";
        public PhotoLibraryQuery Filters { get; set; } = new PhotoLibraryQuery();
        public int TotalPhotos { get; set; }
        public int Count { get; set; }
        public IReadOnlyDictionary<string, string> FieldDescriptions { get; set; } = new Dictionary<string, string>();
        public List<LibraryPhoto> Photos { get; set; } = new List<LibraryPhoto>();
    }

    public class LibraryPhoto
    {
        public string Id { get; set; } = "";
        public string PhotoKey { get; set; } = "";
        public string Title { get; set; } = "";
        public int ShotNumber { get; set; }
        public string Category { get; set; } = "";
        public string CategorySlug { get; set; } = "";
        public string PoseKey { get; set; } = "";
        public string? PoseSummary { get; set; }
        public double? Rating { get; set; }
        public int RatingCount { get; set; }
        public bool IsCurrent { get; set; }
        public string? ShootDate { get; set; }
        public string? Orientation { get; set; }
        public bool HasTransparentVersion { get; set; }
        public string? ThumbnailUrl { get; set; }
        public List<LibraryVersion> Versions { get; set; } = new List<LibraryVersion>();
    }

    public class LibraryVersion
    {
        public string Kind { get; set; } = "";
        public string Label { get; set; } = "";
        public string FileName { get; set; } = "";
        public int WidthPx { get; set; }
        public int HeightPx { get; set; }
        public long Bytes { get; set; }
        public string Url { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public bool IsCropped { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? PreviewUrl { get; set; }
    }
}
