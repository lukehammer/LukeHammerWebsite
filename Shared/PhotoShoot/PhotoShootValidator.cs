using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.PhotoShoot
{
    public static class PhotoShootValidator
    {
        public const int MaxTitleLength = 120;
        public const int MaxTextLength = 4000;
        public const int MaxListItems = 50;
        public const int MaxEstimatedMinutes = 240;
        public const int MaxChangeoverMinutes = 240;
        public const int MaxShotsPerShoot = 1000;
        public const int MaxSetupsPerShoot = 200;
        public const int MaxReferencesPerShoot = 2000;

        public static List<string> ValidateShoot(string? name, int breakEvery, int breakDuration)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("Shoot name is required.");
            }
            else if (name!.Trim().Length > MaxTitleLength)
            {
                errors.Add($"Shoot name must be {MaxTitleLength} characters or fewer.");
            }

            if (breakEvery < 0 || breakEvery > 600)
            {
                errors.Add("Break interval must be between 0 and 600 minutes.");
            }

            if (breakDuration < 0 || breakDuration > 120)
            {
                errors.Add("Break length must be between 0 and 120 minutes.");
            }

            return errors;
        }

        public static List<string> ValidateShot(PhotoShot shot, PhotoShootDocument? doc = null)
        {
            var errors = new List<string>();
            if (shot == null)
            {
                errors.Add("Shot is required.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(shot.Title))
            {
                errors.Add("Title is required.");
            }
            else if (shot.Title.Trim().Length > MaxTitleLength)
            {
                errors.Add($"Title must be {MaxTitleLength} characters or fewer.");
            }

            if (shot.EstimatedMinutes < 0 || shot.EstimatedMinutes > MaxEstimatedMinutes)
            {
                errors.Add($"Estimated time must be between 0 and {MaxEstimatedMinutes} minutes.");
            }

            if (!Enum.IsDefined(typeof(ShotCategory), shot.Category)) errors.Add("Unknown category.");
            if (!Enum.IsDefined(typeof(ShotPriority), shot.Priority)) errors.Add("Unknown priority.");
            if (!Enum.IsDefined(typeof(ShotStatus), shot.Status)) errors.Add("Unknown status.");
            if (!Enum.IsDefined(typeof(CameraOrientation), shot.Orientation)) errors.Add("Unknown orientation.");
            if (!Enum.IsDefined(typeof(NegativeSpaceDirection), shot.NegativeSpace)) errors.Add("Unknown negative space direction.");

            foreach (var text in new[]
            {
                shot.Summary, shot.Objective, shot.MarketingPurpose, shot.FacialExpression, shot.BodyPosition, shot.HandPosition,
                shot.EyeDirection, shot.PhotographerInstructions, shot.PerformerInstructions, shot.Framing, shot.RecommendedCrop,
                shot.Background, shot.LightingNotes, shot.Inspiration, shot.Notes
            })
            {
                if ((text ?? "").Length > MaxTextLength)
                {
                    errors.Add($"Text fields must be {MaxTextLength} characters or fewer.");
                    break;
                }
            }

            if ((shot.Props?.Count ?? 0) > MaxListItems || (shot.Tags?.Count ?? 0) > MaxListItems || (shot.ReferenceSearchPhrases?.Count ?? 0) > MaxListItems)
            {
                errors.Add($"Lists are limited to {MaxListItems} items.");
            }

            if (doc != null && !string.IsNullOrEmpty(shot.SetupId) && PhotoShootPlanner.FindSetup(doc, shot.SetupId) == null)
            {
                errors.Add("Setup not found.");
            }

            return errors;
        }

        public static List<string> ValidateSetup(PhotoSetup setup)
        {
            var errors = new List<string>();
            if (setup == null)
            {
                errors.Add("Setup is required.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(setup.Name))
            {
                errors.Add("Setup name is required.");
            }
            else if (setup.Name.Trim().Length > MaxTitleLength)
            {
                errors.Add($"Setup name must be {MaxTitleLength} characters or fewer.");
            }

            if (setup.ChangeoverMinutes < 0 || setup.ChangeoverMinutes > MaxChangeoverMinutes)
            {
                errors.Add($"Changeover time must be between 0 and {MaxChangeoverMinutes} minutes.");
            }

            foreach (var text in new[] { setup.Description, setup.Background, setup.Lighting, setup.CameraPosition, setup.Outfit, setup.Notes })
            {
                if ((text ?? "").Length > MaxTextLength)
                {
                    errors.Add($"Text fields must be {MaxTextLength} characters or fewer.");
                    break;
                }
            }

            if ((setup.Props?.Count ?? 0) > MaxListItems)
            {
                errors.Add($"Props are limited to {MaxListItems} items.");
            }

            return errors;
        }

        public static List<string> ValidateReference(PhotoReference reference)
        {
            var errors = new List<string>();
            if (reference == null)
            {
                errors.Add("Reference is required.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(reference.Title))
            {
                errors.Add("Reference title is required.");
            }
            else if (reference.Title.Trim().Length > MaxTitleLength)
            {
                errors.Add($"Reference title must be {MaxTitleLength} characters or fewer.");
            }

            if (!IsSafeUrl(reference.SourceUrl))
            {
                errors.Add("Source URL must be a full http:// or https:// address.");
            }

            foreach (var text in new[] { reference.Credit, reference.PerformerOrCharacter, reference.WhatToImitate, reference.WhatToChange, reference.UsageRights })
            {
                if ((text ?? "").Length > MaxTextLength)
                {
                    errors.Add($"Text fields must be {MaxTextLength} characters or fewer.");
                    break;
                }
            }

            return errors;
        }

        /// <summary>Empty is allowed; otherwise only absolute http(s) URLs (blocks javascript: and data: links).</summary>
        public static bool IsSafeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            return Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>Validates an imported/exported document. Errors block import; warnings are informational.</summary>
        public static (List<string> Errors, List<string> Warnings) ValidateDocument(PhotoShootDocument? doc)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            if (doc == null)
            {
                errors.Add("The file does not contain a photo shoot.");
                return (errors, warnings);
            }

            if (string.IsNullOrWhiteSpace(doc.Name))
            {
                warnings.Add("Shoot has no name; one will be assigned.");
            }

            var setups = doc.Setups ?? new List<PhotoSetup>();
            var shots = doc.Shots ?? new List<PhotoShot>();
            var references = doc.References ?? new List<PhotoReference>();

            if (shots.Count > MaxShotsPerShoot) errors.Add($"Too many shots (max {MaxShotsPerShoot}).");
            if (setups.Count > MaxSetupsPerShoot) errors.Add($"Too many setups (max {MaxSetupsPerShoot}).");
            if (references.Count > MaxReferencesPerShoot) errors.Add($"Too many references (max {MaxReferencesPerShoot}).");

            for (var i = 0; i < setups.Count; i++)
            {
                foreach (var e in ValidateSetup(setups[i]))
                {
                    errors.Add($"Setup #{i + 1} ({setups[i]?.Name}): {e}");
                }
            }

            for (var i = 0; i < shots.Count; i++)
            {
                foreach (var e in ValidateShot(shots[i]))
                {
                    errors.Add($"Shot #{i + 1} ({shots[i]?.Title}): {e}");
                }
            }

            for (var i = 0; i < references.Count; i++)
            {
                foreach (var e in ValidateReference(references[i]))
                {
                    errors.Add($"Reference #{i + 1} ({references[i]?.Title}): {e}");
                }
            }

            var duplicateShotIds = shots.Where(s => s != null && !string.IsNullOrEmpty(s.Id)).GroupBy(s => s.Id).Count(g => g.Count() > 1);
            if (duplicateShotIds > 0)
            {
                warnings.Add($"{duplicateShotIds} duplicate shot id(s) found; every imported item receives a new id.");
            }

            var setupIds = new HashSet<string>(setups.Where(s => s != null).Select(s => s.Id ?? ""));
            var orphaned = shots.Count(s => s != null && !string.IsNullOrEmpty(s.SetupId) && !setupIds.Contains(s.SetupId));
            if (orphaned > 0)
            {
                warnings.Add($"{orphaned} shot(s) reference a missing setup and will be placed in Unassigned.");
            }

            return (errors.Take(50).ToList(), warnings);
        }

        public static List<string> ValidateCatalog(PhotoShootCatalog catalog)
        {
            var errors = new List<string>();
            if (catalog == null)
            {
                errors.Add("Catalog is empty.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(catalog.CatalogId))
            {
                errors.Add("catalogId is required.");
            }

            var setupKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setup in catalog.Setups ?? new List<PhotoShootCatalogSetup>())
            {
                if (string.IsNullOrWhiteSpace(setup.Key)) errors.Add($"Setup '{setup.Name}' has no key.");
                else if (!setupKeys.Add(setup.Key)) errors.Add($"Duplicate setup key '{setup.Key}'.");
                if (string.IsNullOrWhiteSpace(setup.Name)) errors.Add($"Setup '{setup.Key}' has no name.");
            }

            var shotKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shot in catalog.Shots ?? new List<PhotoShootCatalogShot>())
            {
                if (string.IsNullOrWhiteSpace(shot.Key)) errors.Add($"Shot '{shot.Title}' has no key.");
                else if (!shotKeys.Add(shot.Key)) errors.Add($"Duplicate shot key '{shot.Key}'.");
                if (string.IsNullOrWhiteSpace(shot.Title)) errors.Add($"Shot '{shot.Key}' has no title.");
                if (!string.IsNullOrEmpty(shot.SetupKey) && !setupKeys.Contains(shot.SetupKey)) errors.Add($"Shot '{shot.Key}' references unknown setup '{shot.SetupKey}'.");
                if (shot.EstimatedMinutes < 0 || shot.EstimatedMinutes > MaxEstimatedMinutes) errors.Add($"Shot '{shot.Key}' has an invalid estimate.");
            }

            return errors;
        }
    }
}
