using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlazorApp.Shared.PhotoReview
{
    /// <summary>Readable shot page addresses such as "001-warm-smile-headshot", and the lookup that turns one back into a shot.</summary>
    public static class ShotAddress
    {
        /// <summary>The number and title slug: "001-warm-smile-headshot". A title with no letters or digits leaves just the number.</summary>
        public static string Slug(int number, string title) =>
            number.ToString("000") + (TitleSlug(title) is { Length: > 0 } slug ? "-" + slug : "");

        /// <summary>Lowercase words joined by hyphens: "Warm Smile Headshot!" becomes "warm-smile-headshot".</summary>
        public static string TitleSlug(string title) => Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

        /// <summary>
        /// The shot an address refers to: its id, or its number and title slug. The number only picks between shots
        /// that share a title. Returns null when nothing matches.
        /// </summary>
        public static string? ResolveId(string reference, IEnumerable<ShotSummary> shots)
        {
            var list = shots.ToList();
            var byId = list.FirstOrDefault(shot => shot.ShotId == reference);
            if (byId != null) return byId.ShotId;

            var parts = reference.Split(new[] { '-' }, 2);
            int? number = parts[0].Length > 0 && parts[0].All(c => char.IsDigit(c)) && int.TryParse(parts[0], out var parsed) ? parsed : null;
            var titlePart = parts.Length > 1 ? parts[1] : number == null ? reference : "";

            var matches = list.Where(shot => TitleSlug(shot.Title) == titlePart).ToList();
            if (matches.Count == 0) return null;
            return (matches.FirstOrDefault(shot => shot.Number == number) ?? matches[0]).ShotId;
        }
    }
}
