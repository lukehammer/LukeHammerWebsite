using System.Text;
using System.Text.Json;
using BlazorApp.Shared.PhotoShoot;

namespace BlazorApp.Client.PhotoShoot
{
    /// <summary>
    /// What each photograph is for, plus links to its reference photos.
    /// Shoot-day logistics (lighting, timing, wardrobe changes, session progress) are left out.
    /// </summary>
    public static class PhotographBriefWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        public static PhotographBrief Build(PhotoShootDocument doc, Func<PhotoReference, string> imageUrl)
        {
            var photographs = new List<PhotographBriefItem>();
            foreach (var group in PhotoShootPlanner.Groups(doc, includeEmptySetups: false))
            {
                foreach (var shot in group.Shots)
                {
                    photographs.Add(new PhotographBriefItem
                    {
                        Number = PhotoShootPlanner.OrderNumber(doc, shot.Id),
                        Title = Clean(shot.Title),
                        Setup = group.Setup?.Name ?? "Unassigned",
                        ThePhotograph = Clean(shot.Summary),
                        Objective = Clean(shot.Objective),
                        WhereItWillBeUsed = Clean(shot.MarketingPurpose),
                        ReferencePhotos = Links(doc, shot, imageUrl)
                    });
                }
            }

            return new PhotographBrief { Photographs = photographs };
        }

        public static string WriteText(PhotographBrief brief)
        {
            var text = new StringBuilder();
            text.AppendLine("PHOTOGRAPH BRIEF");
            text.AppendLine();
            text.AppendLine("This document says what each photograph is for, and gives links to reference photos.");
            text.AppendLine("It leaves out lighting, timing, wardrobe changes, and other details of running the shoot.");
            text.AppendLine("Do not add a photograph that is not listed.");
            text.AppendLine("A reference link is an example to look at. It is not the finished photograph.");
            text.AppendLine("Photographs: " + brief.Photographs.Count);

            foreach (var photo in brief.Photographs)
            {
                text.AppendLine();
                text.AppendLine("PHOTO " + photo.Number);
                text.AppendLine("Title: " + Shown(photo.Title));
                text.AppendLine("Setup: " + Shown(photo.Setup));
                text.AppendLine("The photograph: " + Shown(photo.ThePhotograph));
                text.AppendLine("What we are trying to accomplish: " + Shown(photo.Objective));
                text.AppendLine("Where it will be used: " + Shown(photo.WhereItWillBeUsed));
                if (photo.ReferencePhotos.Count == 0)
                {
                    text.AppendLine("Reference photos: none");
                }
                else
                {
                    text.AppendLine("Reference photos:");
                    foreach (var reference in photo.ReferencePhotos)
                    {
                        text.AppendLine("- " + Shown(reference.Title) + ": " + reference.Link);
                    }
                }
            }

            text.AppendLine();
            text.AppendLine("END OF PHOTOGRAPH BRIEF");
            return text.ToString();
        }

        public static string WriteJson(PhotographBrief brief) => JsonSerializer.Serialize(brief, JsonOptions);

        private static List<PhotographBriefLink> Links(PhotoShootDocument doc, PhotoShot shot, Func<PhotoReference, string> imageUrl)
        {
            var links = new List<PhotographBriefLink>();
            foreach (var reference in PhotoShootEditor.ReferencesForShot(doc, shot.Id))
            {
                if (IsWebLink(reference.SourceUrl))
                {
                    links.Add(new PhotographBriefLink { Title = Clean(reference.Title), Link = reference.SourceUrl.Trim() });
                }

                if (reference.HasImage)
                {
                    var uploadedTitle = Clean(reference.Title);
                    if (IsWebLink(reference.SourceUrl))
                    {
                        uploadedTitle = string.IsNullOrEmpty(uploadedTitle) ? "Uploaded image" : uploadedTitle + " (uploaded image)";
                    }

                    links.Add(new PhotographBriefLink { Title = uploadedTitle, Link = imageUrl(reference) });
                }
            }

            return links;
        }

        private static bool IsWebLink(string? url) =>
            url != null && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

        private static string Clean(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Shown(string? text) => string.IsNullOrWhiteSpace(text) ? "none" : text;
    }

    public class PhotographBrief
    {
        public List<PhotographBriefItem> Photographs { get; set; } = new();
    }

    public class PhotographBriefItem
    {
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string Setup { get; set; } = "";
        public string ThePhotograph { get; set; } = "";
        public string Objective { get; set; } = "";
        public string WhereItWillBeUsed { get; set; } = "";
        public List<PhotographBriefLink> ReferencePhotos { get; set; } = new();
    }

    public class PhotographBriefLink
    {
        public string Title { get; set; } = "";
        public string Link { get; set; } = "";
    }
}
