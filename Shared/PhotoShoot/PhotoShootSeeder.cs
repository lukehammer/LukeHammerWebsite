using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.PhotoShoot
{
    /// <summary>
    /// Idempotent catalog seeding: only adds setups/shots whose seed key is not already present and was not
    /// deleted by the user. Existing items are never modified, so re-seeding cannot overwrite edits.
    /// </summary>
    public static class PhotoShootSeeder
    {
        public const string SetupKeyPrefix = "setup:";
        public const string ShotKeyPrefix = "shot:";

        public static PhotoShootDocument CreateFromCatalog(PhotoShootCatalog catalog, string shootId, DateTime nowUtc)
        {
            var doc = PhotoShootEditor.CreateShoot(shootId, catalog.ShootName, catalog.ShootDescription, "", nowUtc);
            Merge(doc, catalog, nowUtc);
            return doc;
        }

        public static SeedResult Merge(PhotoShootDocument doc, PhotoShootCatalog catalog, DateTime nowUtc)
        {
            PhotoShootEditor.Normalize(doc);
            var result = new SeedResult { ShootId = doc.Id };
            var deleted = new HashSet<string>(doc.DeletedSeedKeys, StringComparer.Ordinal);

            var setupBySeed = doc.Setups
                .Where(s => !string.IsNullOrEmpty(s.SeedKey))
                .GroupBy(s => s.SeedKey)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var nextSetupSequence = doc.Setups.Count == 0 ? 1 : doc.Setups.Max(s => s.Sequence) + 1;

            foreach (var source in catalog.Setups ?? new List<PhotoShootCatalogSetup>())
            {
                if (string.IsNullOrWhiteSpace(source.Key))
                {
                    continue;
                }

                var seedKey = SetupKeyPrefix + source.Key.Trim();
                if (setupBySeed.ContainsKey(seedKey))
                {
                    result.SkippedExisting++;
                    continue;
                }

                if (deleted.Contains(seedKey))
                {
                    result.SkippedDeleted++;
                    continue;
                }

                var setup = new PhotoSetup
                {
                    Id = PhotoShootIds.New(),
                    SeedKey = seedKey,
                    Sequence = nextSetupSequence++,
                    Name = (source.Name ?? "").Trim(),
                    Description = source.Description ?? "",
                    Background = source.Background ?? "",
                    Lighting = source.Lighting ?? "",
                    CameraPosition = source.CameraPosition ?? "",
                    Outfit = source.Outfit ?? "",
                    Props = PhotoShootEditor.CleanList(source.Props),
                    ChangeoverMinutes = Math.Max(0, source.ChangeoverMinutes),
                    RequiresChildren = source.RequiresChildren,
                    Notes = source.Notes ?? ""
                };
                doc.Setups.Add(setup);
                setupBySeed[seedKey] = setup;
                result.AddedSetups++;
            }

            var existingShotKeys = new HashSet<string>(doc.Shots.Where(s => !string.IsNullOrEmpty(s.SeedKey)).Select(s => s.SeedKey), StringComparer.Ordinal);
            foreach (var source in catalog.Shots ?? new List<PhotoShootCatalogShot>())
            {
                if (string.IsNullOrWhiteSpace(source.Key))
                {
                    continue;
                }

                var seedKey = ShotKeyPrefix + source.Key.Trim();
                if (existingShotKeys.Contains(seedKey))
                {
                    result.SkippedExisting++;
                    continue;
                }

                if (deleted.Contains(seedKey))
                {
                    result.SkippedDeleted++;
                    continue;
                }

                setupBySeed.TryGetValue(SetupKeyPrefix + (source.SetupKey ?? "").Trim(), out var setup);
                var setupId = setup?.Id ?? "";
                var siblings = doc.Shots.Where(s => s.SetupId == setupId).ToList();
                doc.Shots.Add(new PhotoShot
                {
                    Id = PhotoShootIds.New(),
                    ShootId = doc.Id,
                    SetupId = setupId,
                    Sequence = siblings.Count == 0 ? 1 : siblings.Max(s => s.Sequence) + 1,
                    SeedKey = seedKey,
                    Title = (source.Title ?? "").Trim(),
                    Summary = source.Summary ?? "",
                    Objective = source.Objective ?? "",
                    MarketingPurpose = source.MarketingPurpose ?? "",
                    Category = source.Category,
                    Priority = source.Priority,
                    Status = ShotStatus.NotStarted,
                    FacialExpression = source.FacialExpression ?? "",
                    BodyPosition = source.BodyPosition ?? "",
                    HandPosition = source.HandPosition ?? "",
                    EyeDirection = source.EyeDirection ?? "",
                    PhotographerInstructions = source.PhotographerInstructions ?? "",
                    PerformerInstructions = source.PerformerInstructions ?? "",
                    Framing = source.Framing ?? "",
                    Orientation = source.Orientation,
                    RecommendedCrop = source.RecommendedCrop ?? "",
                    NegativeSpace = source.NegativeSpace,
                    Background = source.Background ?? "",
                    Props = PhotoShootEditor.CleanList(source.Props),
                    LightingNotes = source.LightingNotes ?? "",
                    EstimatedMinutes = Math.Max(0, Math.Min(PhotoShootValidator.MaxEstimatedMinutes, source.EstimatedMinutes)),
                    Inspiration = source.Inspiration ?? "",
                    ReferenceSearchPhrases = PhotoShootEditor.CleanList(source.ReferenceSearchPhrases),
                    Tags = PhotoShootEditor.CleanList(source.Tags),
                    Notes = source.Notes ?? "",
                    UpdatedUtc = nowUtc
                });
                existingShotKeys.Add(seedKey);
                result.AddedShots++;
            }

            doc.SeedCatalogId = catalog.CatalogId ?? "";
            doc.SeedCatalogVersion = Math.Max(doc.SeedCatalogVersion, catalog.CatalogVersion);
            if (string.IsNullOrWhiteSpace(doc.Name))
            {
                doc.Name = catalog.ShootName ?? "Photo Shoot";
            }

            if (string.IsNullOrWhiteSpace(doc.Description))
            {
                doc.Description = catalog.ShootDescription ?? "";
            }

            PhotoShootEditor.Normalize(doc);
            return result;
        }
    }

    public class CloneResult
    {
        public PhotoShootDocument Document { get; set; } = new PhotoShootDocument();
        /// <summary>Uploaded images that storage must copy (source image id, new image id).</summary>
        public List<KeyValuePair<string, string>> ImageCopies { get; set; } = new List<KeyValuePair<string, string>>();
    }

    /// <summary>Deep copies a shoot with fresh ids (used for duplicate and import).</summary>
    public static class PhotoShootCloner
    {
        public static CloneResult Clone(PhotoShootDocument source, string newShootId, string newName, bool resetProgress, DateTime nowUtc)
        {
            var result = new CloneResult();
            var setupMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var shotMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var seenShotSeeds = new HashSet<string>(StringComparer.Ordinal);
            var seenSetupSeeds = new HashSet<string>(StringComparer.Ordinal);

            var doc = new PhotoShootDocument
            {
                Id = newShootId,
                Name = string.IsNullOrWhiteSpace(newName) ? (source.Name ?? "Photo Shoot") : newName.Trim(),
                Description = source.Description ?? "",
                ShootDate = source.ShootDate ?? "",
                BreakEveryMinutes = source.BreakEveryMinutes,
                BreakDurationMinutes = source.BreakDurationMinutes,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc,
                SeedCatalogId = source.SeedCatalogId ?? "",
                SeedCatalogVersion = source.SeedCatalogVersion,
                DeletedSeedKeys = PhotoShootEditor.CleanList(source.DeletedSeedKeys)
            };

            foreach (var setup in (source.Setups ?? new List<PhotoSetup>()).Where(s => s != null))
            {
                var id = PhotoShootIds.New();
                if (!string.IsNullOrEmpty(setup.Id) && !setupMap.ContainsKey(setup.Id))
                {
                    setupMap[setup.Id] = id;
                }

                var seedKey = setup.SeedKey ?? "";
                if (seedKey.Length > 0 && !seenSetupSeeds.Add(seedKey))
                {
                    seedKey = "";
                }

                doc.Setups.Add(new PhotoSetup
                {
                    Id = id,
                    Sequence = setup.Sequence,
                    Name = setup.Name ?? "",
                    Description = setup.Description ?? "",
                    Background = setup.Background ?? "",
                    Lighting = setup.Lighting ?? "",
                    CameraPosition = setup.CameraPosition ?? "",
                    Outfit = setup.Outfit ?? "",
                    Props = PhotoShootEditor.CleanList(setup.Props),
                    ChangeoverMinutes = setup.ChangeoverMinutes,
                    RequiresChildren = setup.RequiresChildren,
                    Notes = setup.Notes ?? "",
                    SeedKey = seedKey
                });
            }

            foreach (var shot in (source.Shots ?? new List<PhotoShot>()).Where(s => s != null))
            {
                var id = PhotoShootIds.New();
                if (!string.IsNullOrEmpty(shot.Id) && !shotMap.ContainsKey(shot.Id))
                {
                    shotMap[shot.Id] = id;
                }

                var seedKey = shot.SeedKey ?? "";
                if (seedKey.Length > 0 && !seenShotSeeds.Add(seedKey))
                {
                    seedKey = "";
                }

                var copy = new PhotoShot
                {
                    Id = id,
                    ShootId = newShootId,
                    SetupId = !string.IsNullOrEmpty(shot.SetupId) && setupMap.TryGetValue(shot.SetupId, out var setupId) ? setupId : "",
                    Sequence = shot.Sequence,
                    Title = shot.Title ?? "",
                    Summary = shot.Summary ?? "",
                    Objective = shot.Objective ?? "",
                    MarketingPurpose = shot.MarketingPurpose ?? "",
                    Category = shot.Category,
                    Priority = shot.Priority,
                    Status = resetProgress ? ShotStatus.NotStarted : shot.Status,
                    FacialExpression = shot.FacialExpression ?? "",
                    BodyPosition = shot.BodyPosition ?? "",
                    HandPosition = shot.HandPosition ?? "",
                    EyeDirection = shot.EyeDirection ?? "",
                    PhotographerInstructions = shot.PhotographerInstructions ?? "",
                    PerformerInstructions = shot.PerformerInstructions ?? "",
                    Framing = shot.Framing ?? "",
                    Orientation = shot.Orientation,
                    RecommendedCrop = shot.RecommendedCrop ?? "",
                    NegativeSpace = shot.NegativeSpace,
                    Background = shot.Background ?? "",
                    Props = PhotoShootEditor.CleanList(shot.Props),
                    LightingNotes = shot.LightingNotes ?? "",
                    EstimatedMinutes = shot.EstimatedMinutes,
                    Inspiration = shot.Inspiration ?? "",
                    ReferenceSearchPhrases = PhotoShootEditor.CleanList(shot.ReferenceSearchPhrases),
                    Tags = PhotoShootEditor.CleanList(shot.Tags),
                    Notes = shot.Notes ?? "",
                    IsFavorite = !resetProgress && shot.IsFavorite,
                    StartedUtc = resetProgress ? null : shot.StartedUtc,
                    CompletedUtc = resetProgress ? null : shot.CompletedUtc,
                    ActualSeconds = resetProgress ? null : shot.ActualSeconds,
                    SeedKey = seedKey,
                    UpdatedUtc = nowUtc
                };
                if (copy.Status == ShotStatus.InProgress)
                {
                    copy.Status = ShotStatus.NotStarted;
                    copy.StartedUtc = null;
                }

                doc.Shots.Add(copy);
            }

            foreach (var reference in (source.References ?? new List<PhotoReference>()).Where(r => r != null))
            {
                var copy = new PhotoReference
                {
                    Id = PhotoShootIds.New(),
                    ShotIds = (reference.ShotIds ?? new List<string>()).Where(shotMap.ContainsKey).Select(id => shotMap[id]).Distinct().ToList(),
                    Title = reference.Title ?? "",
                    SourceUrl = reference.SourceUrl ?? "",
                    Credit = reference.Credit ?? "",
                    PerformerOrCharacter = reference.PerformerOrCharacter ?? "",
                    WhatToImitate = reference.WhatToImitate ?? "",
                    WhatToChange = reference.WhatToChange ?? "",
                    UsageRights = reference.UsageRights ?? "",
                    CreatedUtc = reference.CreatedUtc == default ? nowUtc : reference.CreatedUtc,
                    UpdatedUtc = nowUtc
                };
                if (PhotoShootIds.IsValid(reference.ImageId))
                {
                    copy.ImageId = PhotoShootIds.New();
                    copy.ImageContentType = reference.ImageContentType ?? "";
                    result.ImageCopies.Add(new KeyValuePair<string, string>(reference.ImageId, copy.ImageId));
                }

                doc.References.Add(copy);
            }

            if (!resetProgress && source.Session != null)
            {
                doc.Session.Notes = (source.Session.Notes ?? new List<SessionNote>())
                    .Where(n => n != null)
                    .Select(n => new SessionNote
                    {
                        Id = PhotoShootIds.New(),
                        ShotId = !string.IsNullOrEmpty(n.ShotId) && shotMap.TryGetValue(n.ShotId, out var mapped) ? mapped : "",
                        Text = n.Text ?? "",
                        Author = n.Author ?? "",
                        CreatedUtc = n.CreatedUtc
                    })
                    .ToList();
            }

            result.Document = PhotoShootEditor.Normalize(doc);
            return result;
        }
    }
}
