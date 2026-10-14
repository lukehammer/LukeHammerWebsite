using System.Text;
using BlazorApp.Shared.PhotoShoot;

namespace BlazorApp.Client.PhotoShoot
{
    /// <summary>
    /// Plain-text brief of every photograph still on the shoot. Written for another AI to read
    /// without guessing from pictures, celebrity names, or session controls.
    /// </summary>
    public static class PhotographBriefWriter
    {
        public static string Write(PhotoShootDocument doc)
        {
            var groups = PhotoShootPlanner.Groups(doc, includeEmptySetups: false);
            var text = new StringBuilder();

            text.AppendLine("PHOTOGRAPH BRIEF");
            text.AppendLine();
            text.AppendLine("This document is the complete list of photographs Luke Hammer, performing as Lucky Luke Magic, intends to capture.");
            text.AppendLine("Every photograph listed here is a picture still to be taken, unless its session mark says otherwise.");
            text.AppendLine("Do not add a photograph that is not listed.");
            text.AppendLine("Do not drop a photograph because a reference picture was a poor match.");
            text.AppendLine("A name under Expression inspiration is a hint for a face or a pose. That person is not in the photograph. Use a picture of them only when the face matches the expression written for that photograph.");
            text.AppendLine("This document does not include reference photos, celebrity pictures, or planning mock-ups.");
            text.AppendLine();
            text.AppendLine("Shoot name: " + Value(doc.Name));
            text.AppendLine("Shoot date: " + (PsUi.Has(doc.ShootDate) ? doc.ShootDate.Trim() : "not set"));
            text.AppendLine("Description: " + Value(doc.Description));
            text.AppendLine("Photographs: " + doc.Shots.Count);
            text.AppendLine("Setups: " + groups.Count);
            text.AppendLine();
            text.AppendLine("A setup is a group of photographs that share one background, lighting setup, camera position, and outfit.");
            text.AppendLine("Photographs are numbered in shooting order.");
            text.AppendLine("A field written as \"none\" was left blank on purpose.");

            foreach (var group in groups)
            {
                var setup = group.Setup;
                text.AppendLine();
                text.AppendLine("------------------------------------------------------------");
                text.AppendLine(setup == null
                    ? "SETUP (unassigned)"
                    : $"SETUP {setup.Sequence}. {setup.Name}");
                text.AppendLine("Photographs in this setup: " + group.Shots.Count);
                text.AppendLine("Description: " + Value(setup?.Description));
                text.AppendLine("Background: " + Value(setup?.Background));
                text.AppendLine("Lighting: " + Value(setup?.Lighting));
                text.AppendLine("Camera position: " + Value(setup?.CameraPosition));
                text.AppendLine("Outfit: " + Value(setup?.Outfit));
                text.AppendLine("Props shared by this setup: " + List(setup?.Props));
                text.AppendLine("Changeover into this setup: " + Minutes(setup?.ChangeoverMinutes ?? 0));
                text.AppendLine(setup?.RequiresChildren == true
                    ? "Children on set: yes. A guardian stays with each child, and a signed model release is required before any photograph of a child."
                    : "Children on set: no");
                text.AppendLine("Setup notes: " + Value(setup?.Notes));

                foreach (var shot in group.Shots)
                {
                    var number = PhotoShootPlanner.OrderNumber(doc, shot.Id);
                    text.AppendLine();
                    text.AppendLine($"PHOTO {number}");
                    text.AppendLine("Title: " + Value(shot.Title));
                    text.AppendLine("Setup: " + (setup == null ? "unassigned" : setup.Name));
                    text.AppendLine("Priority: " + PhotoShootLabels.Priority(shot.Priority));
                    text.AppendLine("Category: " + PhotoShootLabels.Category(shot.Category));
                    text.AppendLine("The photograph: " + Value(shot.Summary));
                    text.AppendLine("Objective: " + Value(shot.Objective));
                    text.AppendLine("Where this photograph will be used: " + Value(shot.MarketingPurpose));
                    text.AppendLine("Face: " + Value(shot.FacialExpression));
                    text.AppendLine("Body: " + Value(shot.BodyPosition));
                    text.AppendLine("Hands: " + Value(shot.HandPosition));
                    text.AppendLine("Eyes: " + Value(shot.EyeDirection));
                    text.AppendLine("What Luke does: " + Value(shot.PerformerInstructions));
                    text.AppendLine("Framing: " + Value(shot.Framing));
                    text.AppendLine("Orientation: " + PhotoShootLabels.Orientation(shot.Orientation));
                    text.AppendLine("Crop: " + Value(shot.RecommendedCrop));
                    text.AppendLine("Empty space left for words later: " + PhotoShootLabels.NegativeSpace(shot.NegativeSpace));
                    text.AppendLine("Camera direction: " + Value(shot.PhotographerInstructions));
                    text.AppendLine("Props for this photograph: " + List(shot.Props));
                    text.AppendLine("Lighting note: " + Value(shot.LightingNotes));
                    text.AppendLine("Background: " + (PsUi.Has(shot.Background) ? Value(shot.Background) : "use the setup background"));
                    text.AppendLine("Expression inspiration: " + Value(shot.Inspiration) + ". This is not a person to photograph.");
                    text.AppendLine("Notes: " + Value(shot.Notes));
                    text.AppendLine("Planned minutes: " + Minutes(shot.EstimatedMinutes));
                    text.AppendLine("Session mark: " + PhotoShootLabels.Status(shot.Status));
                }
            }

            text.AppendLine();
            text.AppendLine("END OF PHOTOGRAPH BRIEF");
            return text.ToString();
        }

        private static string Value(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "none";
            }

            return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string List(IEnumerable<string>? items)
        {
            var values = (items ?? Array.Empty<string>()).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToList();
            return values.Count == 0 ? "none" : string.Join(" | ", values);
        }

        private static string Minutes(int minutes) => minutes <= 0 ? "not set" : minutes + (minutes == 1 ? " minute" : " minutes");
    }
}
