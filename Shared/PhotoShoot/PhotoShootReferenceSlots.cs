using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.PhotoShoot
{
    public class ShotReferenceSlots
    {
        public PhotoReference? AiA { get; set; }
        public PhotoReference? AiB { get; set; }
        public PhotoReference? Celebrity { get; set; }
        public PhotoReference? RealWorld { get; set; }
        public List<PhotoReference> Additional { get; set; } = new List<PhotoReference>();

        public int Filled => (AiA == null ? 0 : 1) + (AiB == null ? 0 : 1) + (Celebrity == null ? 0 : 1) + (RealWorld == null ? 0 : 1);

        public string Gap(ReferenceSlot slot) => slot switch
        {
            ReferenceSlot.AiA when AiA == null => "No AI image has been generated for this pose. No image-generation provider is configured.",
            ReferenceSlot.AiB when AiB == null => "No second AI image has been generated. No image-generation provider is configured.",
            ReferenceSlot.Celebrity when Celebrity == null => "No real celebrity photograph is attached to this shot.",
            ReferenceSlot.RealWorld when RealWorld == null => "No second real photograph is attached to this shot.",
            _ => ""
        };
    }

    public class ReferenceCoverage
    {
        public int Shots { get; set; }
        public int AiA { get; set; }
        public int AiB { get; set; }
        public int Celebrity { get; set; }
        public int RealWorld { get; set; }
        public int CompleteShots { get; set; }
        public int Required => Shots * 4;
        public int Filled => AiA + AiB + Celebrity + RealWorld;
    }

    /// <summary>Sorts existing references into the four required slots without removing any photo.</summary>
    public static class PhotoShootReferenceSlots
    {
        private static readonly string[] KnownPeople =
        {
            "jim carrey", "steve martin", "martin short", "jack black", "weird al", "neil patrick harris",
            "penn jillette", "penn ", "teller", "matt donnelly", "mind noodler", "mac king", "mat king",
            "silly billy", "david kaye", "dick van dyke", "gene wilder", "groucho", "rowan atkinson",
            "mr. bean", "christopher lloyd", "brian blessed", "lucille ball", "david harbour", "david harbor",
            "robin williams", "will ferrell", "carol burnett", "buster keaton", "charlie chaplin", "bill murray",
            "johnny carson", "harpo"
        };

        private static readonly string[] GenericPeople =
        {
            "man", "woman", "person", "people", "model", "actor", "magician", "comedian", "subject", "unknown",
            "bearded man", "man in suit"
        };

        public static bool AssignExisting(PhotoShootDocument doc)
        {
            var changed = false;
            foreach (var shot in doc.Shots)
            {
                var linked = PhotoShootEditor.ReferencesForShot(doc, shot.Id).ToList();
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (var reference in linked)
                {
                    if (reference.Roles.Any(role => role.ShotId == shot.Id && IsRequired(role.Slot)))
                    {
                        used.Add(reference.Id);
                    }
                }

                changed |= Give(shot.Id, linked, used, ReferenceSlot.AiA, IsAi);
                changed |= Give(shot.Id, linked, used, ReferenceSlot.AiB, IsAi);
                changed |= Give(shot.Id, linked, used, ReferenceSlot.Celebrity, IsCelebrity);
                changed |= Give(shot.Id, linked, used, ReferenceSlot.RealWorld, IsReal);
            }

            return changed;
        }

        public static ShotReferenceSlots ForShot(PhotoShootDocument doc, string shotId)
        {
            var slots = new ShotReferenceSlots();
            foreach (var reference in PhotoShootEditor.ReferencesForShot(doc, shotId))
            {
                var role = reference.Roles.FirstOrDefault(r => r.ShotId == shotId && IsRequired(r.Slot));
                if (role == null)
                {
                    slots.Additional.Add(reference);
                    continue;
                }

                switch (role.Slot)
                {
                    case ReferenceSlot.AiA when slots.AiA == null: slots.AiA = reference; break;
                    case ReferenceSlot.AiB when slots.AiB == null: slots.AiB = reference; break;
                    case ReferenceSlot.Celebrity when slots.Celebrity == null: slots.Celebrity = reference; break;
                    case ReferenceSlot.RealWorld when slots.RealWorld == null: slots.RealWorld = reference; break;
                    default: slots.Additional.Add(reference); break;
                }
            }

            return slots;
        }

        public static ReferenceCoverage Coverage(PhotoShootDocument doc)
        {
            var coverage = new ReferenceCoverage { Shots = doc.Shots.Count };
            foreach (var shot in doc.Shots)
            {
                var slots = ForShot(doc, shot.Id);
                if (slots.AiA != null) coverage.AiA++;
                if (slots.AiB != null) coverage.AiB++;
                if (slots.Celebrity != null) coverage.Celebrity++;
                if (slots.RealWorld != null) coverage.RealWorld++;
                if (slots.Filled == 4) coverage.CompleteShots++;
            }

            return coverage;
        }

        private static bool Give(string shotId, List<PhotoReference> linked, HashSet<string> used, ReferenceSlot slot, Func<PhotoReference, bool> match)
        {
            if (linked.Any(r => r.Roles.Any(role => role.ShotId == shotId && role.Slot == slot)))
            {
                return false;
            }

            var found = linked.FirstOrDefault(r => !used.Contains(r.Id) && match(r));
            if (found == null)
            {
                return false;
            }

            found.Roles.Add(new ReferenceRole { ShotId = shotId, Slot = slot });
            used.Add(found.Id);
            return true;
        }

        private static bool IsRequired(ReferenceSlot slot) =>
            slot == ReferenceSlot.AiA || slot == ReferenceSlot.AiB || slot == ReferenceSlot.Celebrity || slot == ReferenceSlot.RealWorld;

        public static bool IsAi(PhotoReference reference)
        {
            if (reference.Provenance == ReferenceProvenance.AiGenerated)
            {
                return true;
            }

            var title = reference.Title ?? "";
            return title.Contains("AI mock-up", StringComparison.OrdinalIgnoreCase)
                || title.Contains("AI mockup", StringComparison.OrdinalIgnoreCase)
                || title.Contains("AI-generated", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSubject(PhotoReference reference) =>
            reference.Provenance == ReferenceProvenance.SubjectPortrait
            || (reference.Title ?? "").Contains("Luke cartoon portrait", StringComparison.OrdinalIgnoreCase);

        public static bool IsMissing(PhotoReference reference) =>
            reference.Provenance == ReferenceProvenance.MissingOriginal;

        public static bool Usable(PhotoReference reference) =>
            reference.HasImage || PhotoShootPlanUpgrade.LooksLikeImageUrl(reference.SourceUrl);

        public static bool IsCelebrity(PhotoReference reference)
        {
            if (!Usable(reference) || IsAi(reference) || IsSubject(reference) || IsMissing(reference))
            {
                return false;
            }

            var hay = ((reference.Title ?? "") + " " + (reference.PerformerOrCharacter ?? "") + " " + (reference.Credit ?? "")).ToLowerInvariant();
            if (KnownPeople.Any(name => hay.Contains(name)))
            {
                return true;
            }

            var who = (reference.PerformerOrCharacter ?? "").Trim();
            if (who.Length < 3)
            {
                return false;
            }

            var lower = who.ToLowerInvariant();
            if (lower.Contains("luke") || GenericPeople.Any(name => lower == name))
            {
                return false;
            }

            return who.Any(char.IsLetter);
        }

        public static bool IsReal(PhotoReference reference) =>
            Usable(reference) && !IsAi(reference) && !IsSubject(reference) && !IsMissing(reference);
    }
}
