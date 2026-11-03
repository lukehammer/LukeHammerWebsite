using BlazorApp.Shared.PhotoReview;

namespace BlazorApp.Client.PhotoReview
{
    /// <summary>Small display helpers shared by the review pages.</summary>
    public static class PrUi
    {
        public const string Business = "Lucky Luke Magic";
        public const string ServiceArea = "family magician serving Vancouver, WA and Portland, OR";

        public static string StatusClass(ShotReviewStatus status) => status switch
        {
            ShotReviewStatus.Successful => "pr-status-success",
            ShotReviewStatus.PartiallySuccessful => "pr-status-partial",
            ShotReviewStatus.Unsuccessful => "pr-status-fail",
            ShotReviewStatus.NotCaptured => "pr-status-missing",
            _ => "pr-status-review"
        };

        public static string StatusIcon(ShotReviewStatus status) => status switch
        {
            ShotReviewStatus.Successful => "bi-check-circle-fill",
            ShotReviewStatus.PartiallySuccessful => "bi-circle-half",
            ShotReviewStatus.Unsuccessful => "bi-x-circle",
            ShotReviewStatus.NotCaptured => "bi-camera-video-off",
            _ => "bi-hourglass-split"
        };

        public static string EvidenceClass(MatchEvidence evidence) => evidence switch
        {
            MatchEvidence.SlateSupported => "pr-ev-slate",
            MatchEvidence.Inferred => "pr-ev-inferred",
            MatchEvidence.Approximate => "pr-ev-approx",
            _ => "pr-ev-none"
        };

        public static string Alt(string shotTitle, int index) =>
            $"{Business}: {shotTitle}{(index > 0 ? $" (photo {index})" : "")}";

        public static string Bytes(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:F1} MB" : $"{Math.Max(1, bytes / 1024)} KB";

        public static string Ago(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
            if (span.TotalDays < 14) return $"{(int)span.TotalDays} d ago";
            return utc.ToLocalTime().ToString("MMM d, yyyy");
        }

        public static string When(DateTime utc) => utc.ToLocalTime().ToString("MMM d, yyyy h:mm tt");

        public static bool Has(string? text) => !string.IsNullOrWhiteSpace(text);

        /// <summary>"(provisional)", "(all set by Luke)" or "(58 provisional · 4 by Luke)" for a status tile.</summary>
        public static string Split(ReviewCounts? counts, ShotReviewStatus status)
        {
            if (counts == null) return "(provisional)";
            var total = counts.ByProvisionalStatus.GetValueOrDefault(status);
            var byLuke = counts.ByStatus.GetValueOrDefault(status);
            var provisional = total - byLuke;
            if (byLuke == 0) return "(provisional)";
            if (provisional == 0) return "(all set by Luke)";
            return $"({provisional} provisional · {byLuke} by Luke)";
        }

        public static string NewRequestId() => Guid.NewGuid().ToString("N");

        public const string ReviewGridPath = "photoshoot/review";

        /// <summary>Shot link. Passing the grid's own URL (from <see cref="ReviewReturnPath"/>) lets the shot page's Back link return to the same filtered grid.</summary>
        public static string ShotLink(string shotId, string? back = null) =>
            "photoshoot/review/shot/" + shotId + (ReviewReturnPath(back) is { } path ? "?back=" + Uri.EscapeDataString(path) : "");

        public static string ReviewCategoryPath(ReviewCategory category) => ReviewGridPath + "?c=" + PhotoReviewLabels.CategorySlug(category);

        /// <summary>Accepts only a relative path into the review grid (for example "photoshoot/review?c=full-body"), so a Back link can never leave the site.</summary>
        public static string? ReviewReturnPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 500 || !path.StartsWith(ReviewGridPath, StringComparison.Ordinal)) return null;
            var rest = path[ReviewGridPath.Length..];
            return rest.Length == 0 || rest[0] == '?' ? path : null;
        }

        public static string PhotoLink(string photoId) => "photoshoot/review/photo/" + photoId;

        /// <summary>Preview box shape from the displayed version's real pixel size, so the whole frame fits. Portrait fallback when unknown.</summary>
        public static string Ratio(ThumbView? thumb) => thumb is { Width: > 0, Height: > 0 } ? $"{thumb.Width} / {thumb.Height}" : "4 / 5";

        /// <summary>Landscape previews take two grid columns instead of being squeezed into a portrait card.</summary>
        public static bool Wide(ThumbView? thumb) => thumb is { Width: > 0, Height: > 0 } && thumb.Width > thumb.Height;

        public static string Number(int number) => "#" + number.ToString("000");

        /// <summary>A safe, tiny subset of Markdown: "#"/"##" headings, "- " bullets, paragraphs. No HTML.</summary>
        public static List<(string Kind, string Text)> MarkdownBlocks(string markdown)
        {
            var blocks = new List<(string, string)>();
            var paragraph = new List<string>();
            void Flush()
            {
                if (paragraph.Count > 0)
                {
                    blocks.Add(("p", string.Join("\n", paragraph)));
                    paragraph.Clear();
                }
            }

            foreach (var raw in (markdown ?? "").Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0)
                {
                    Flush();
                }
                else if (line.StartsWith("# "))
                {
                    Flush();
                    blocks.Add(("h1", line.Substring(2).Trim()));
                }
                else if (line.StartsWith("## ") || line.StartsWith("### "))
                {
                    Flush();
                    blocks.Add(("h2", line.TrimStart('#').Trim()));
                }
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    Flush();
                    blocks.Add(("li", line.Substring(2).Trim()));
                }
                else
                {
                    paragraph.Add(line);
                }
            }

            Flush();
            return blocks;
        }
    }
}
