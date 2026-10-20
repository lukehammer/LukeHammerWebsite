using System;
using System.Collections.Generic;

namespace BlazorApp.Shared.PhotoShoot
{
    /// <summary>Picks show folders from the pose text that is already on the shot.</summary>
    public static class PhotoShootMarkets
    {
        public static List<ShowMarket> Infer(PhotoShot shot)
        {
            var hay = ((shot.MarketingPurpose ?? "") + " " + (shot.Title ?? "") + " " + (shot.Summary ?? "")).ToLowerInvariant();
            var markets = new List<ShowMarket>();
            if (shot.Category == ShotCategory.ChildVolunteer || Has(hay, "kid") || Has(hay, "child") || Has(hay, "birthday") || Has(hay, "school") || Has(hay, "library"))
            {
                markets.Add(ShowMarket.Kids);
            }

            if (Has(hay, "family") || Has(hay, "wedding") || Has(hay, "parent"))
            {
                markets.Add(ShowMarket.Family);
            }

            if (Has(hay, "bachelorette") || Has(hay, "bridal") || Has(hay, "bride"))
            {
                markets.Add(ShowMarket.Bachelorette);
            }

            if (Has(hay, "corporate") || Has(hay, "linkedin") || Has(hay, "proposal") || Has(hay, "press kit"))
            {
                markets.Add(ShowMarket.Corporate);
            }

            if (markets.Count == 0)
            {
                markets.Add(ShowMarket.AnyShow);
            }

            return markets;
        }

        private static bool Has(string hay, string needle) =>
            hay.IndexOf(needle, StringComparison.Ordinal) >= 0;
    }
}
