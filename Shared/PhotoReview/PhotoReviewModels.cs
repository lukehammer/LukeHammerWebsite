using System;
using System.Collections.Generic;

namespace BlazorApp.Shared.PhotoReview
{
    /// <summary>
    /// Admin- and importer-owned review data for one shoot: selected photos with their artifact versions,
    /// per-shot review state and the second-shoot queue. Stored apart from the planner document, which
    /// it references by stable shot id and never rewrites.
    /// </summary>
    public class ReviewCatalog
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string ShootId { get; set; } = "";
        public long Revision { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public List<ShotReview> Shots { get; set; } = new List<ShotReview>();
        public List<ReviewPhoto> Photos { get; set; } = new List<ReviewPhoto>();
        public List<QueueEntry> Queue { get; set; } = new List<QueueEntry>();
        public List<ImportRecord> Imports { get; set; } = new List<ImportRecord>();
    }

    public class ShotReview
    {
        public string ShotId { get; set; } = "";
        public int ShotNumber { get; set; }
        public ReviewCategory Category { get; set; }
        public bool CategoryAssigned { get; set; }
        public ShotCoverage Coverage { get; set; }
        public string CoverageNotes { get; set; } = "";
        /// <summary>What the selection pass suggests. Shown as provisional, never as Luke's verdict.</summary>
        public ShotReviewStatus? SuggestedStatus { get; set; }
        public string SuggestionReason { get; set; } = "";
        /// <summary>Who made the suggestion, e.g. "Codex".</summary>
        public string SuggestedBy { get; set; } = "";
        /// <summary>Luke's authoritative verdict. Null means Needs review.</summary>
        public ShotReviewStatus? OwnerStatus { get; set; }
        public string OwnerNote { get; set; } = "";
        public DateTime? OwnerStatusUtc { get; set; }
    }

    public class ReviewPhoto
    {
        /// <summary>Server id, derived from <see cref="PhotoKey"/> so re-imports land on the same record.</summary>
        public string Id { get; set; } = "";
        public string PhotoKey { get; set; } = "";
        public string ShotId { get; set; } = "";
        public List<string> AlsoShotIds { get; set; } = new List<string>();
        public ReviewCategory Category { get; set; }
        public SelectionRole Selection { get; set; }
        public MatchEvidence Evidence { get; set; }
        public string EvidenceText { get; set; } = "";
        public string Reason { get; set; } = "";
        public List<string> Warnings { get; set; } = new List<string>();
        public string NotesMarkdown { get; set; } = "";
        public PhotoSource Source { get; set; } = new PhotoSource();
        public List<ArtifactVersion> Versions { get; set; } = new List<ArtifactVersion>();
        public DateTime ImportedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        /// <summary>Hidden photos are only visible to the admin. Nothing is ever hard-deleted from the review.</summary>
        public DateTime? HiddenUtc { get; set; }
    }

    public class PhotoSource
    {
        public int PhotoNumber { get; set; }
        public string Url { get; set; } = "";
        public string OriginalFileName { get; set; } = "";
        public string Archive { get; set; } = "";
    }

    public class ArtifactVersion
    {
        /// <summary>First 16 hex characters of the SHA-256. Unique within a photo.</summary>
        public string Id { get; set; } = "";
        public ArtifactKind Kind { get; set; }
        /// <summary>Version this one was produced from (empty for the original).</summary>
        public string ParentVersionId { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Bytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string ContentType { get; set; } = "";
        /// <summary>Download name, e.g. lucky-luke-magic-warm-smile-headshot-01-cropped.jpg.</summary>
        public string FileName { get; set; } = "";
        /// <summary>Name of the file as delivered.</summary>
        public string DeliveredFileName { get; set; } = "";
        public ArtifactProvenance Provenance { get; set; }
        public string ProcessingNotes { get; set; } = "";
        public string Tool { get; set; } = "";
        public DateTime? CreatedUtc { get; set; }
        public DateTime ImportedUtc { get; set; }
        /// <summary>Set when a newer version of the same kind replaced this one. Kept as history.</summary>
        public DateTime? SupersededUtc { get; set; }
        /// <summary>Widths of the browsing thumbnails that exist for this version.</summary>
        public List<int> ThumbWidths { get; set; } = new List<int>();
        public string ThumbContentType { get; set; } = "";

        public bool IsCurrent => SupersededUtc == null;
    }

    /// <summary>Admin metadata for one second-shoot queue item. Candidates are computed; this only stores overrides.</summary>
    public class QueueEntry
    {
        /// <summary>"shot:{shotId}" or "suggestion:{suggestionId}".</summary>
        public string Key { get; set; } = "";
        public QueuePriority? Priority { get; set; }
        public string Notes { get; set; } = "";
        public bool Excluded { get; set; }
        /// <summary>Approved suggestions only appear when this is true.</summary>
        public bool Approved { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public class ImportRecord
    {
        public string Id { get; set; } = "";
        public DateTime Utc { get; set; }
        public string Generator { get; set; } = "";
        public string ManifestSha256 { get; set; } = "";
        public int PhotosAdded { get; set; }
        public int PhotosUpdated { get; set; }
        public int VersionsAdded { get; set; }
        public int VersionsSuperseded { get; set; }
        public int ShotsUpdated { get; set; }
    }

    /// <summary>
    /// Visitor-owned data: identities, ratings, comments and suggestions, plus moderation audit.
    /// Rating totals are stored and recomputed in the same atomic save as every vote change.
    /// </summary>
    public class ParticipationDocument
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string ShootId { get; set; } = "";
        public long Revision { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public List<Visitor> Visitors { get; set; } = new List<Visitor>();
        public List<PhotoVote> Votes { get; set; } = new List<PhotoVote>();
        public Dictionary<string, RatingTotal> Totals { get; set; } = new Dictionary<string, RatingTotal>();
        public List<ReviewComment> Comments { get; set; } = new List<ReviewComment>();
        public List<PoseSuggestion> Suggestions { get; set; } = new List<PoseSuggestion>();
        public List<AuditEntry> Audit { get; set; } = new List<AuditEntry>();
    }

    public class Visitor
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        /// <summary>SHA-256 (hex) of the browser secret. The secret itself is never stored.</summary>
        public string SecretHash { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public DateTime LastActiveUtc { get; set; }
        public DateTime? RemovedUtc { get; set; }
        public string RemovalId { get; set; } = "";
    }

    /// <summary>Shared by every contribution type so removal and restore work the same way.</summary>
    public abstract class Contribution
    {
        public string VisitorId { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public DateTime? UpdatedUtc { get; set; }
        /// <summary>Set when the admin removed the author. Restored together by removal id.</summary>
        public DateTime? RemovedUtc { get; set; }
        public string RemovalId { get; set; } = "";

        public bool IsRemoved => RemovedUtc != null;
    }

    public class PhotoVote : Contribution
    {
        public string PhotoId { get; set; } = "";
        /// <summary>1 to 10. Zero is never a valid rating.</summary>
        public int Value { get; set; }
    }

    public class RatingTotal
    {
        public long Sum { get; set; }
        public int Count { get; set; }

        public double Average => Count == 0 ? 0 : Math.Round((double)Sum / Count, 2);
    }

    public class ReviewComment : Contribution
    {
        public string Id { get; set; } = "";
        public CommentTarget Target { get; set; }
        public string TargetId { get; set; } = "";
        /// <summary>Empty for a top-level comment.</summary>
        public string ParentId { get; set; } = "";
        public string Text { get; set; } = "";
        public string ClientRequestId { get; set; } = "";
        /// <summary>Withdrawn by its author.</summary>
        public DateTime? DeletedUtc { get; set; }
        /// <summary>Written while signed in as the admin; shown with an owner badge.</summary>
        public bool ByOwner { get; set; }
    }

    public class PoseSuggestion : Contribution
    {
        public string Id { get; set; } = "";
        public SuggestionKind Kind { get; set; }
        /// <summary>Planned shot this refers to. Empty for a brand-new pose.</summary>
        public string ShotId { get; set; } = "";
        public ShotReviewStatus? SuggestedStatus { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string ClientRequestId { get; set; } = "";
        public DateTime? DeletedUtc { get; set; }
        public bool ByOwner { get; set; }
        public SuggestionState State { get; set; }
        public string StateNote { get; set; } = "";
        public DateTime? StateUtc { get; set; }
    }

    public class AuditEntry
    {
        public string Id { get; set; } = "";
        public DateTime Utc { get; set; }
        public string Actor { get; set; } = "";
        public string Action { get; set; } = "";
        public string VisitorId { get; set; } = "";
        public string RemovalId { get; set; } = "";
        public int Votes { get; set; }
        public int Comments { get; set; }
        public int Suggestions { get; set; }
        public string Note { get; set; } = "";
    }
}
