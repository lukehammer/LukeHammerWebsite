using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.PhotoShoot
{
    public class EditResult<T> where T : class
    {
        public T? Value { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public bool NotFound { get; set; }
        public bool Ok => Errors.Count == 0 && !NotFound;

        public static EditResult<T> Success(T value) => new EditResult<T> { Value = value };
        public static EditResult<T> Invalid(List<string> errors) => new EditResult<T> { Errors = errors };
        public static EditResult<T> Missing(string what) => new EditResult<T> { NotFound = true, Errors = new List<string> { $"{what} not found." } };
    }

    /// <summary>Validated create/update/delete operations on a shoot document (pure, no I/O).</summary>
    public static class PhotoShootEditor
    {
        public static PhotoShootDocument CreateShoot(string id, string name, string description, string shootDate, DateTime nowUtc) =>
            Normalize(new PhotoShootDocument
            {
                Id = id,
                Name = (name ?? "").Trim(),
                Description = (description ?? "").Trim(),
                ShootDate = (shootDate ?? "").Trim(),
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            });

        /// <summary>Repairs nulls and dangling references so the rest of the app can trust the document.</summary>
        public static PhotoShootDocument Normalize(PhotoShootDocument doc)
        {
            doc.Setups ??= new List<PhotoSetup>();
            doc.Shots ??= new List<PhotoShot>();
            doc.References ??= new List<PhotoReference>();
            doc.DeletedSeedKeys ??= new List<string>();
            doc.Session ??= new PhotoShootSession();
            doc.Session.Notes ??= new List<SessionNote>();
            doc.Name ??= "";
            doc.Description ??= "";
            doc.ShootDate ??= "";
            doc.SeedCatalogId ??= "";

            doc.Setups.RemoveAll(s => s == null);
            doc.Shots.RemoveAll(s => s == null);
            doc.References.RemoveAll(r => r == null);

            foreach (var setup in doc.Setups)
            {
                if (string.IsNullOrEmpty(setup.Id)) setup.Id = PhotoShootIds.New();
                setup.Props = CleanList(setup.Props);
                setup.Name ??= "";
                setup.Description ??= "";
                setup.Background ??= "";
                setup.Lighting ??= "";
                setup.CameraPosition ??= "";
                setup.Outfit ??= "";
                setup.Notes ??= "";
                setup.SeedKey ??= "";
            }

            var setupIds = new HashSet<string>(doc.Setups.Select(s => s.Id));
            foreach (var shot in doc.Shots)
            {
                if (string.IsNullOrEmpty(shot.Id)) shot.Id = PhotoShootIds.New();
                shot.ShootId = doc.Id;
                shot.SetupId ??= "";
                if (shot.SetupId.Length > 0 && !setupIds.Contains(shot.SetupId)) shot.SetupId = "";
                NormalizeShotText(shot);
            }

            var shotIds = new HashSet<string>(doc.Shots.Select(s => s.Id));
            foreach (var reference in doc.References)
            {
                if (string.IsNullOrEmpty(reference.Id)) reference.Id = PhotoShootIds.New();
                reference.ShotIds = CleanList(reference.ShotIds).Where(shotIds.Contains).ToList();
                reference.Title ??= "";
                reference.SourceUrl ??= "";
                reference.Credit ??= "";
                reference.PerformerOrCharacter ??= "";
                reference.WhatToImitate ??= "";
                reference.WhatToChange ??= "";
                reference.UsageRights ??= "";
                reference.ImageId ??= "";
                reference.ImageContentType ??= "";
            }

            if (!string.IsNullOrEmpty(doc.Session.CurrentShotId) && !shotIds.Contains(doc.Session.CurrentShotId))
            {
                doc.Session.CurrentShotId = "";
            }

            doc.Session.CurrentShotId ??= "";
            PhotoShootPlanner.Resequence(doc);
            return doc;
        }

        private static void NormalizeShotText(PhotoShot shot)
        {
            shot.Title = (shot.Title ?? "").Trim();
            shot.Summary ??= "";
            shot.Objective ??= "";
            shot.MarketingPurpose ??= "";
            shot.FacialExpression ??= "";
            shot.BodyPosition ??= "";
            shot.HandPosition ??= "";
            shot.EyeDirection ??= "";
            shot.PhotographerInstructions ??= "";
            shot.PerformerInstructions ??= "";
            shot.Framing ??= "";
            shot.RecommendedCrop ??= "";
            shot.Background ??= "";
            shot.LightingNotes ??= "";
            shot.Inspiration ??= "";
            shot.Notes ??= "";
            shot.SeedKey ??= "";
            shot.Props = CleanList(shot.Props);
            shot.Tags = CleanList(shot.Tags);
            shot.ReferenceSearchPhrases = CleanList(shot.ReferenceSearchPhrases);
        }

        public static List<string> CleanList(IEnumerable<string>? items) =>
            (items ?? Enumerable.Empty<string>())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(PhotoShootValidator.MaxListItems)
                .ToList();

        /// <summary>Splits a comma/newline separated text box into a clean list.</summary>
        public static List<string> ParseList(string? text) =>
            CleanList((text ?? "").Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));

        public static EditResult<PhotoShot> CreateShot(PhotoShootDocument doc, PhotoShot input, DateTime nowUtc)
        {
            var errors = PhotoShootValidator.ValidateShot(input, doc);
            if (doc.Shots.Count >= PhotoShootValidator.MaxShotsPerShoot)
            {
                errors.Add($"A shoot can have at most {PhotoShootValidator.MaxShotsPerShoot} shots.");
            }

            if (errors.Count > 0)
            {
                return EditResult<PhotoShot>.Invalid(errors);
            }

            var shot = new PhotoShot { Id = PhotoShootIds.New(), ShootId = doc.Id, Status = ShotStatus.NotStarted };
            CopyEditableFields(input, shot);
            shot.SetupId = input.SetupId ?? "";
            var siblings = doc.Shots.Where(s => s.SetupId == shot.SetupId).ToList();
            shot.Sequence = siblings.Count == 0 ? 1 : siblings.Max(s => s.Sequence) + 1;
            shot.UpdatedUtc = nowUtc;
            NormalizeShotText(shot);
            doc.Shots.Add(shot);
            PhotoShootPlanner.Resequence(doc);
            return EditResult<PhotoShot>.Success(shot);
        }

        public static EditResult<PhotoShot> UpdateShot(PhotoShootDocument doc, string shotId, PhotoShot input, DateTime nowUtc)
        {
            var shot = PhotoShootPlanner.FindShot(doc, shotId);
            if (shot == null)
            {
                return EditResult<PhotoShot>.Missing("Shot");
            }

            var errors = PhotoShootValidator.ValidateShot(input, doc);
            if (errors.Count > 0)
            {
                return EditResult<PhotoShot>.Invalid(errors);
            }

            CopyEditableFields(input, shot);
            NormalizeShotText(shot);
            if ((input.SetupId ?? "") != shot.SetupId)
            {
                PhotoShootPlanner.MoveShotToSetup(doc, shot.Id, input.SetupId ?? "");
            }

            if (input.Status != shot.Status)
            {
                ShotStatusRules.Apply(shot, input.Status, nowUtc);
            }

            shot.UpdatedUtc = nowUtc;
            return EditResult<PhotoShot>.Success(shot);
        }

        private static void CopyEditableFields(PhotoShot from, PhotoShot to)
        {
            to.Title = from.Title;
            to.Summary = from.Summary;
            to.Objective = from.Objective;
            to.MarketingPurpose = from.MarketingPurpose;
            to.Category = from.Category;
            to.Priority = from.Priority;
            to.DeliverableStatus = from.DeliverableStatus;
            to.FacialExpression = from.FacialExpression;
            to.BodyPosition = from.BodyPosition;
            to.HandPosition = from.HandPosition;
            to.EyeDirection = from.EyeDirection;
            to.PhotographerInstructions = from.PhotographerInstructions;
            to.PerformerInstructions = from.PerformerInstructions;
            to.Framing = from.Framing;
            to.Orientation = from.Orientation;
            to.RecommendedCrop = from.RecommendedCrop;
            to.NegativeSpace = from.NegativeSpace;
            to.Background = from.Background;
            to.Props = CleanList(from.Props);
            to.LightingNotes = from.LightingNotes;
            to.EstimatedMinutes = from.EstimatedMinutes;
            to.Inspiration = from.Inspiration;
            to.ReferenceSearchPhrases = CleanList(from.ReferenceSearchPhrases);
            to.Tags = CleanList(from.Tags);
            to.Notes = from.Notes;
            to.IsFavorite = from.IsFavorite;
        }

        public static bool DeleteShot(PhotoShootDocument doc, string shotId)
        {
            var shot = PhotoShootPlanner.FindShot(doc, shotId);
            if (shot == null)
            {
                return false;
            }

            if (doc.Session.CurrentShotId == shotId)
            {
                var next = PhotoShootPlanner.NextShot(doc, shotId, doc.Session.MustHaveOnly);
                doc.Session.CurrentShotId = next?.Id ?? "";
            }

            RememberDeletedSeed(doc, shot.SeedKey);
            doc.Shots.Remove(shot);
            foreach (var reference in doc.References)
            {
                reference.ShotIds.RemoveAll(id => id == shotId);
            }

            PhotoShootPlanner.Resequence(doc);
            return true;
        }

        public static bool SetStatus(PhotoShootDocument doc, string shotId, ShotStatus status, DateTime nowUtc)
        {
            var shot = PhotoShootPlanner.FindShot(doc, shotId);
            return shot != null && ShotStatusRules.Apply(shot, status, nowUtc);
        }

        public static EditResult<PhotoSetup> CreateSetup(PhotoShootDocument doc, PhotoSetup input)
        {
            var errors = PhotoShootValidator.ValidateSetup(input);
            if (doc.Setups.Count >= PhotoShootValidator.MaxSetupsPerShoot)
            {
                errors.Add($"A shoot can have at most {PhotoShootValidator.MaxSetupsPerShoot} setups.");
            }

            if (errors.Count > 0)
            {
                return EditResult<PhotoSetup>.Invalid(errors);
            }

            var setup = new PhotoSetup { Id = PhotoShootIds.New() };
            CopySetupFields(input, setup);
            setup.Sequence = doc.Setups.Count == 0 ? 1 : doc.Setups.Max(s => s.Sequence) + 1;
            doc.Setups.Add(setup);
            PhotoShootPlanner.Resequence(doc);
            return EditResult<PhotoSetup>.Success(setup);
        }

        public static EditResult<PhotoSetup> UpdateSetup(PhotoShootDocument doc, string setupId, PhotoSetup input)
        {
            var setup = PhotoShootPlanner.FindSetup(doc, setupId);
            if (setup == null)
            {
                return EditResult<PhotoSetup>.Missing("Setup");
            }

            var errors = PhotoShootValidator.ValidateSetup(input);
            if (errors.Count > 0)
            {
                return EditResult<PhotoSetup>.Invalid(errors);
            }

            CopySetupFields(input, setup);
            return EditResult<PhotoSetup>.Success(setup);
        }

        private static void CopySetupFields(PhotoSetup from, PhotoSetup to)
        {
            to.Name = (from.Name ?? "").Trim();
            to.Description = from.Description ?? "";
            to.Background = from.Background ?? "";
            to.Lighting = from.Lighting ?? "";
            to.CameraPosition = from.CameraPosition ?? "";
            to.Outfit = from.Outfit ?? "";
            to.Props = CleanList(from.Props);
            to.ChangeoverMinutes = from.ChangeoverMinutes;
            to.RequiresChildren = from.RequiresChildren;
            to.Notes = from.Notes ?? "";
        }

        /// <summary>Deletes a setup. Its shots move to <paramref name="moveShotsToSetupId"/> or become unassigned.</summary>
        public static bool DeleteSetup(PhotoShootDocument doc, string setupId, string? moveShotsToSetupId)
        {
            var setup = PhotoShootPlanner.FindSetup(doc, setupId);
            if (setup == null)
            {
                return false;
            }

            var target = moveShotsToSetupId != setupId ? PhotoShootPlanner.FindSetup(doc, moveShotsToSetupId) : null;
            foreach (var shot in doc.Shots.Where(s => s.SetupId == setupId).OrderBy(s => s.Sequence).ToList())
            {
                PhotoShootPlanner.MoveShotToSetup(doc, shot.Id, target?.Id ?? "");
            }

            RememberDeletedSeed(doc, setup.SeedKey);
            doc.Setups.Remove(setup);
            PhotoShootPlanner.Resequence(doc);
            return true;
        }

        public static EditResult<PhotoReference> CreateReference(PhotoShootDocument doc, PhotoReference input, DateTime nowUtc)
        {
            var errors = PhotoShootValidator.ValidateReference(input);
            if (doc.References.Count >= PhotoShootValidator.MaxReferencesPerShoot)
            {
                errors.Add("Too many references.");
            }

            if (errors.Count > 0)
            {
                return EditResult<PhotoReference>.Invalid(errors);
            }

            var reference = new PhotoReference { Id = PhotoShootIds.New(), CreatedUtc = nowUtc };
            CopyReferenceFields(doc, input, reference);
            reference.UpdatedUtc = nowUtc;
            doc.References.Add(reference);
            return EditResult<PhotoReference>.Success(reference);
        }

        public static EditResult<PhotoReference> UpdateReference(PhotoShootDocument doc, string referenceId, PhotoReference input, DateTime nowUtc)
        {
            var reference = doc.References.FirstOrDefault(r => r.Id == referenceId);
            if (reference == null)
            {
                return EditResult<PhotoReference>.Missing("Reference");
            }

            var errors = PhotoShootValidator.ValidateReference(input);
            if (errors.Count > 0)
            {
                return EditResult<PhotoReference>.Invalid(errors);
            }

            CopyReferenceFields(doc, input, reference);
            reference.UpdatedUtc = nowUtc;
            return EditResult<PhotoReference>.Success(reference);
        }

        private static void CopyReferenceFields(PhotoShootDocument doc, PhotoReference from, PhotoReference to)
        {
            var shotIds = new HashSet<string>(doc.Shots.Select(s => s.Id));
            to.Title = (from.Title ?? "").Trim();
            to.SourceUrl = (from.SourceUrl ?? "").Trim();
            to.Credit = from.Credit ?? "";
            to.PerformerOrCharacter = from.PerformerOrCharacter ?? "";
            to.WhatToImitate = from.WhatToImitate ?? "";
            to.WhatToChange = from.WhatToChange ?? "";
            to.UsageRights = from.UsageRights ?? "";
            to.ShotIds = CleanList(from.ShotIds).Where(shotIds.Contains).ToList();
        }

        /// <summary>Removes the reference and returns its image id (if any) so storage can delete the file.</summary>
        public static string? DeleteReference(PhotoShootDocument doc, string referenceId)
        {
            var reference = doc.References.FirstOrDefault(r => r.Id == referenceId);
            if (reference == null)
            {
                return null;
            }

            doc.References.Remove(reference);
            return reference.ImageId ?? "";
        }

        public static IReadOnlyList<PhotoReference> ReferencesForShot(PhotoShootDocument doc, string shotId) =>
            (doc.References ?? new List<PhotoReference>()).Where(r => r.ShotIds != null && r.ShotIds.Contains(shotId)).ToList();

        private static void RememberDeletedSeed(PhotoShootDocument doc, string? seedKey)
        {
            if (!string.IsNullOrWhiteSpace(seedKey) && !doc.DeletedSeedKeys.Contains(seedKey!))
            {
                doc.DeletedSeedKeys.Add(seedKey!);
            }
        }
    }
}
