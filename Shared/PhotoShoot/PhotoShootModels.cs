using System;
using System.Collections.Generic;

namespace BlazorApp.Shared.PhotoShoot
{
    /// <summary>
    /// One photo shoot (setups, shots, references, and live session state). Persisted as a single JSON
    /// document so every save is atomic under one ETag.
    /// </summary>
    public class PhotoShootDocument
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        /// <summary>Planned shoot date (yyyy-MM-dd), optional.</summary>
        public string ShootDate { get; set; } = "";
        /// <summary>Optional break every N minutes of shooting (0 = no planned breaks).</summary>
        public int BreakEveryMinutes { get; set; } = 60;
        public int BreakDurationMinutes { get; set; } = 10;
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        /// <summary>Incremented on every saved change; lets clients detect stale views cheaply.</summary>
        public long Revision { get; set; }

        /// <summary>Catalog this shoot was seeded from (empty for hand-built or imported shoots).</summary>
        public string SeedCatalogId { get; set; } = "";
        public int SeedCatalogVersion { get; set; }
        /// <summary>Seed keys the user deleted, so re-seeding never resurrects them.</summary>
        public List<string> DeletedSeedKeys { get; set; } = new List<string>();

        public List<PhotoSetup> Setups { get; set; } = new List<PhotoSetup>();
        public List<PhotoShot> Shots { get; set; } = new List<PhotoShot>();
        public List<PhotoReference> References { get; set; } = new List<PhotoReference>();
        public PhotoShootSession Session { get; set; } = new PhotoShootSession();
    }

    public class PhotoSetup
    {
        public string Id { get; set; } = "";
        public int Sequence { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Background { get; set; } = "";
        public string Lighting { get; set; } = "";
        public string CameraPosition { get; set; } = "";
        public string Outfit { get; set; } = "";
        public List<string> Props { get; set; } = new List<string>();
        /// <summary>Estimated minutes to switch into this setup from the previous one.</summary>
        public int ChangeoverMinutes { get; set; }
        public bool RequiresChildren { get; set; }
        public string Notes { get; set; } = "";
        public string SeedKey { get; set; } = "";
    }

    public class PhotoShot
    {
        public string Id { get; set; } = "";
        public string ShootId { get; set; } = "";
        /// <summary>Owning setup; empty means "Unassigned".</summary>
        public string SetupId { get; set; } = "";
        /// <summary>Order within the setup (setups themselves are ordered by <see cref="PhotoSetup.Sequence"/>).</summary>
        public int Sequence { get; set; }

        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Objective { get; set; } = "";
        public string MarketingPurpose { get; set; } = "";
        public ShotCategory Category { get; set; }
        public ShotPriority Priority { get; set; } = ShotPriority.Standard;
        public ShotStatus Status { get; set; } = ShotStatus.NotStarted;

        public string FacialExpression { get; set; } = "";
        public string BodyPosition { get; set; } = "";
        public string HandPosition { get; set; } = "";
        public string EyeDirection { get; set; } = "";
        public string PhotographerInstructions { get; set; } = "";
        public string PerformerInstructions { get; set; } = "";
        public string Framing { get; set; } = "";
        public CameraOrientation Orientation { get; set; }
        public string RecommendedCrop { get; set; } = "";
        public NegativeSpaceDirection NegativeSpace { get; set; }
        /// <summary>Overrides the setup background when not empty.</summary>
        public string Background { get; set; } = "";
        public List<string> Props { get; set; } = new List<string>();
        public string LightingNotes { get; set; } = "";
        public int EstimatedMinutes { get; set; } = 3;
        public string Inspiration { get; set; } = "";
        public List<string> ReferenceSearchPhrases { get; set; } = new List<string>();
        public List<string> Tags { get; set; } = new List<string>();
        public string Notes { get; set; } = "";

        public DateTime? StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
        /// <summary>Seconds between start and completion when both were tracked.</summary>
        public int? ActualSeconds { get; set; }
        public bool IsFavorite { get; set; }

        public string SeedKey { get; set; } = "";
        public DateTime UpdatedUtc { get; set; }
    }

    public class PhotoReference
    {
        public string Id { get; set; } = "";
        public List<string> ShotIds { get; set; } = new List<string>();
        public string Title { get; set; } = "";
        public string SourceUrl { get; set; } = "";
        public string Credit { get; set; } = "";
        public string PerformerOrCharacter { get; set; } = "";
        public string WhatToImitate { get; set; } = "";
        public string WhatToChange { get; set; } = "";
        public string UsageRights { get; set; } = "";
        /// <summary>Private storage name for an uploaded image (served only through the authorized API).</summary>
        public string ImageId { get; set; } = "";
        public string ImageContentType { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }

        public bool HasImage => !string.IsNullOrWhiteSpace(ImageId);
    }

    /// <summary>Server-persisted live session state shared by the director and photographer phones.</summary>
    public class PhotoShootSession
    {
        public bool IsActive { get; set; }
        public string CurrentShotId { get; set; } = "";
        public DateTime? StartedUtc { get; set; }
        public DateTime? EndedUtc { get; set; }
        public DateTime? UpdatedUtc { get; set; }
        public string UpdatedBy { get; set; } = "";
        /// <summary>Set when the photographer taps "Ready for next"; cleared when the director moves on.</summary>
        public DateTime? NextRequestedUtc { get; set; }
        public string NextRequestedBy { get; set; } = "";
        /// <summary>When true the photographer may advance to the next shot without the director.</summary>
        public bool PhotographerCanAdvance { get; set; }
        public bool MustHaveOnly { get; set; }
        public List<SessionNote> Notes { get; set; } = new List<SessionNote>();
    }

    public class SessionNote
    {
        public string Id { get; set; } = "";
        public string ShotId { get; set; } = "";
        public string Text { get; set; } = "";
        public string Author { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
    }

    public class PhotoShootSummary
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ShootDate { get; set; } = "";
        public int ShotCount { get; set; }
        public int CompletedCount { get; set; }
        public int SetupCount { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public bool IsDefault { get; set; }
    }

    public class PhotoShootIndex
    {
        public string DefaultShootId { get; set; } = "";
        public List<PhotoShootSummary> Shoots { get; set; } = new List<PhotoShootSummary>();
    }

    public class PhotoShootMe
    {
        public PhotoShootAccess Access { get; set; }
        public string UserName { get; set; } = "";
        public string IdentityProvider { get; set; } = "";
        public bool DevBypass { get; set; }
    }
}
