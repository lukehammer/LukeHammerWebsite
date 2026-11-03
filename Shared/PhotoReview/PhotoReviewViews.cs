using System;
using System.Collections.Generic;

namespace BlazorApp.Shared.PhotoReview
{
    // Response shapes for /api/photoreview. Built server-side from the planner, catalog and participation
    // documents, so removed content, secrets and visitor ids never leave the server for non-admins.

    public class ReviewViewer
    {
        public bool HasIdentity { get; set; }
        public string DisplayName { get; set; } = "";
        public string Tag { get; set; } = "";
        public bool Suspended { get; set; }
        public bool IsAdmin { get; set; }
        public string AdminName { get; set; } = "";
        /// <summary>Signed in with Microsoft as the admin email, but not yet the bound account.</summary>
        public string AdminProblem { get; set; } = "";
        public bool SignedInWithMicrosoft { get; set; }
    }

    public class ReviewCounts
    {
        public int PlannedShots { get; set; }
        public int ShotsWithPhotos { get; set; }
        public int ShotsWithoutMatch { get; set; }
        public int Photos { get; set; }
        /// <summary>Luke's confirmed verdicts only (unreviewed shots count as Needs review).</summary>
        public Dictionary<ShotReviewStatus, int> ByStatus { get; set; } = new Dictionary<ShotReviewStatus, int>();
        /// <summary>Luke's verdict where set, otherwise the provisional Codex suggestion.</summary>
        public Dictionary<ShotReviewStatus, int> ByProvisionalStatus { get; set; } = new Dictionary<ShotReviewStatus, int>();
        public int OwnerConfirmed { get; set; }
        public Dictionary<ArtifactKind, int> VersionsByKind { get; set; } = new Dictionary<ArtifactKind, int>();
        public Dictionary<MatchEvidence, int> PhotosByEvidence { get; set; } = new Dictionary<MatchEvidence, int>();
        public int Ratings { get; set; }
        public int Comments { get; set; }
        public int OpenSuggestions { get; set; }
        public int People { get; set; }
        public int SecondShootItems { get; set; }
    }

    public class CategoryView
    {
        public ReviewCategory Category { get; set; }
        public string Label { get; set; } = "";
        public string Slug { get; set; } = "";
        public int ShotCount { get; set; }
        public int PhotoCount { get; set; }
    }

    public class ReviewSummaryResponse
    {
        public string ShootId { get; set; } = "";
        public string ShootName { get; set; } = "";
        public ReviewViewer Viewer { get; set; } = new ReviewViewer();
        public ReviewCounts Counts { get; set; } = new ReviewCounts();
        public List<CategoryView> Categories { get; set; } = new List<CategoryView>();
        public List<ShotSummary> Shots { get; set; } = new List<ShotSummary>();
        public List<PhotoSummary> Photos { get; set; } = new List<PhotoSummary>();
        public DateTime? LastImportUtc { get; set; }
    }

    public class ShotSummary
    {
        public string ShotId { get; set; } = "";
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public ReviewCategory Category { get; set; }
        public string SetupName { get; set; } = "";
        public string Priority { get; set; } = "";
        public bool MustHave { get; set; }
        public ShotCoverage Coverage { get; set; }
        public string CoverageNotes { get; set; } = "";
        public ShotReviewStatus? SuggestedStatus { get; set; }
        public string SuggestionReason { get; set; } = "";
        public string SuggestedBy { get; set; } = "";
        /// <summary>Luke's verdict, or Needs review when he hasn't set one. Authoritative.</summary>
        public ShotReviewStatus Status { get; set; }
        public bool StatusConfirmed { get; set; }
        /// <summary>Luke's verdict when set (always wins), else the suggestion, else Needs review. For filters and the plan.</summary>
        public ShotReviewStatus ProvisionalStatus { get; set; }
        /// <summary>"owner", "suggestion" or "" (nothing yet).</summary>
        public string ProvisionalSource { get; set; } = "";
        public string OwnerNote { get; set; } = "";
        public List<string> PhotoIds { get; set; } = new List<string>();
        public string CoverPhotoId { get; set; } = "";
        public int CommentCount { get; set; }
        public int OpenSuggestions { get; set; }
        public bool HasWarnings { get; set; }
        public MatchEvidence? BestEvidence { get; set; }
    }

    public class ThumbView
    {
        public string VersionId { get; set; } = "";
        public List<int> Widths { get; set; } = new List<int>();
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class PhotoSummary
    {
        public string Id { get; set; } = "";
        public string PhotoKey { get; set; } = "";
        public string ShotId { get; set; } = "";
        public List<string> AlsoShotIds { get; set; } = new List<string>();
        public ReviewCategory Category { get; set; }
        public SelectionRole Selection { get; set; }
        public MatchEvidence Evidence { get; set; }
        public bool HasWarnings { get; set; }
        public ThumbView Thumb { get; set; } = new ThumbView();
        public List<ArtifactKind> Kinds { get; set; } = new List<ArtifactKind>();
        public double Average { get; set; }
        public int RatingCount { get; set; }
        public int? MyRating { get; set; }
        public int CommentCount { get; set; }
        public bool Hidden { get; set; }
    }

    /// <summary>The share link for one version: its path under the review API, or empty when no link is active.</summary>
    public class ShareLinkResponse
    {
        public bool Shared { get; set; }
        public string Path { get; set; } = "";
    }

    public class VersionView
    {
        public string Id { get; set; } = "";
        public ArtifactKind Kind { get; set; }
        public string ParentVersionId { get; set; } = "";
        public ArtifactKind? ParentKind { get; set; }
        public string FileName { get; set; } = "";
        public string DeliveredFileName { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public long Bytes { get; set; }
        public string ContentType { get; set; } = "";
        public ArtifactProvenance Provenance { get; set; }
        public string ProcessingNotes { get; set; } = "";
        /// <summary>True when a share link is active for this version. The link itself comes from the share endpoint.</summary>
        public bool Shared { get; set; }
        public string Tool { get; set; } = "";
        public DateTime? CreatedUtc { get; set; }
        public DateTime ImportedUtc { get; set; }
        public bool Superseded { get; set; }
        public List<int> ThumbWidths { get; set; } = new List<int>();
        public string Sha256 { get; set; } = "";
    }

    public class PhotoDetail
    {
        public PhotoSummary Summary { get; set; } = new PhotoSummary();
        public string EvidenceText { get; set; } = "";
        public string Reason { get; set; } = "";
        public List<string> Warnings { get; set; } = new List<string>();
        public string NotesMarkdown { get; set; } = "";
        public PhotoSource Source { get; set; } = new PhotoSource();
        public List<VersionView> Versions { get; set; } = new List<VersionView>();
        public List<ArtifactKind> MissingKinds { get; set; } = new List<ArtifactKind>();
    }

    public class ReferenceView
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Credit { get; set; } = "";
        public string WhatToImitate { get; set; } = "";
        public string WhatToChange { get; set; } = "";
        public string Slot { get; set; } = "";
        public string Provenance { get; set; } = "";
        public bool HasImage { get; set; }
        public string SourceUrl { get; set; } = "";
        public long Version { get; set; }
    }

    /// <summary>The intended photograph, copied from the planner. Shoot-day logistics are left out.</summary>
    public class ShotBrief
    {
        public string Summary { get; set; } = "";
        public string Objective { get; set; } = "";
        public string MarketingPurpose { get; set; } = "";
        public string FacialExpression { get; set; } = "";
        public string BodyPosition { get; set; } = "";
        public string HandPosition { get; set; } = "";
        public string EyeDirection { get; set; } = "";
        public string PerformerInstructions { get; set; } = "";
        public string PhotographerInstructions { get; set; } = "";
        public string Framing { get; set; } = "";
        public string Orientation { get; set; } = "";
        public string RecommendedCrop { get; set; } = "";
        public string NegativeSpace { get; set; } = "";
        public string Background { get; set; } = "";
        public List<string> Props { get; set; } = new List<string>();
        public string Inspiration { get; set; } = "";
        public string SetupName { get; set; } = "";
        public string BlockName { get; set; } = "";
        public string Outfit { get; set; } = "";
    }

    public class CommentView
    {
        public string Id { get; set; } = "";
        public CommentTarget Target { get; set; }
        public string TargetId { get; set; } = "";
        public string ParentId { get; set; } = "";
        public string AuthorName { get; set; } = "";
        public string AuthorTag { get; set; } = "";
        /// <summary>Only filled in for the admin.</summary>
        public string AuthorId { get; set; } = "";
        public bool ByOwner { get; set; }
        public string Text { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public DateTime? EditedUtc { get; set; }
        /// <summary>The comment is gone but replies remain: show "deleted" in its place.</summary>
        public bool Placeholder { get; set; }
        public string PlaceholderText { get; set; } = "";
        public bool Mine { get; set; }
    }

    public class SuggestionView
    {
        public string Id { get; set; } = "";
        public SuggestionKind Kind { get; set; }
        public string ShotId { get; set; } = "";
        public int ShotNumber { get; set; }
        public string ShotTitle { get; set; } = "";
        public ShotReviewStatus? SuggestedStatus { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string AuthorName { get; set; } = "";
        public string AuthorTag { get; set; } = "";
        public string AuthorId { get; set; } = "";
        public bool ByOwner { get; set; }
        public bool Mine { get; set; }
        public SuggestionState State { get; set; }
        public string StateNote { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public DateTime? EditedUtc { get; set; }
        public bool Placeholder { get; set; }
        public List<CommentView> Comments { get; set; } = new List<CommentView>();
    }

    public class ShotDetailResponse
    {
        public ReviewViewer Viewer { get; set; } = new ReviewViewer();
        public ShotSummary Shot { get; set; } = new ShotSummary();
        public ShotBrief Brief { get; set; } = new ShotBrief();
        public List<ReferenceView> References { get; set; } = new List<ReferenceView>();
        public List<PhotoDetail> Photos { get; set; } = new List<PhotoDetail>();
        /// <summary>Photos filed under another shot that also apply to this one.</summary>
        public List<PhotoDetail> RelatedPhotos { get; set; } = new List<PhotoDetail>();
        public List<CommentView> Comments { get; set; } = new List<CommentView>();
        public List<SuggestionView> Suggestions { get; set; } = new List<SuggestionView>();
        public string PreviousShotId { get; set; } = "";
        public string NextShotId { get; set; } = "";
    }

    public class SuggestionsResponse
    {
        public ReviewViewer Viewer { get; set; } = new ReviewViewer();
        public List<SuggestionView> Suggestions { get; set; } = new List<SuggestionView>();
    }

    public class PersonView
    {
        public string VisitorId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Tag { get; set; } = "";
        public DateTime FirstActiveUtc { get; set; }
        public DateTime LastActiveUtc { get; set; }
        public bool Removed { get; set; }
        public DateTime? RemovedUtc { get; set; }
        public int Ratings { get; set; }
        public int Comments { get; set; }
        public int Suggestions { get; set; }
        public int RemovedItems { get; set; }
    }

    public class PersonVoteView
    {
        public string PhotoId { get; set; } = "";
        public string PhotoKey { get; set; } = "";
        public int Value { get; set; }
        public bool Removed { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public class PersonDetailResponse
    {
        public PersonView Person { get; set; } = new PersonView();
        public List<PersonVoteView> Votes { get; set; } = new List<PersonVoteView>();
        public List<CommentView> Comments { get; set; } = new List<CommentView>();
        public List<SuggestionView> Suggestions { get; set; } = new List<SuggestionView>();
        /// <summary>Per-comment state for the admin: "", "removed", "deleted by author".</summary>
        public Dictionary<string, string> CommentStates { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> SuggestionStates { get; set; } = new Dictionary<string, string>();
    }

    public class PeopleResponse
    {
        public List<PersonView> People { get; set; } = new List<PersonView>();
        public List<AuditEntry> Audit { get; set; } = new List<AuditEntry>();
    }

    public class QueueItemView
    {
        public string Key { get; set; } = "";
        public bool FromSuggestion { get; set; }
        public string ShotId { get; set; } = "";
        public int ShotNumber { get; set; }
        public string Title { get; set; } = "";
        public ReviewCategory? Category { get; set; }
        /// <summary>Why it is in the queue, e.g. "Not captured" or "Approved suggestion".</summary>
        public string Reason { get; set; } = "";
        /// <summary>True when Luke set the status or approved the suggestion; false = provisional.</summary>
        public bool ReasonConfirmed { get; set; }
        /// <summary>The suggestion's explanation (e.g. Codex's per-shot reason) when provisional.</summary>
        public string ReasonDetail { get; set; } = "";
        public ShotReviewStatus? Status { get; set; }
        public QueuePriority Priority { get; set; }
        public string Notes { get; set; } = "";
        public bool Excluded { get; set; }
        public ShotBrief Brief { get; set; } = new ShotBrief();
        public List<ReferenceView> References { get; set; } = new List<ReferenceView>();
        /// <summary>Earlier attempts worth showing on set (partial matches, alternates).</summary>
        public List<PhotoSummary> Examples { get; set; } = new List<PhotoSummary>();
        public SuggestionView? Suggestion { get; set; }
    }

    public class SecondShootResponse
    {
        public ReviewViewer Viewer { get; set; } = new ReviewViewer();
        public List<QueueItemView> Items { get; set; } = new List<QueueItemView>();
        /// <summary>Open suggestions waiting for Luke's decision (shown separately, not in the plan).</summary>
        public List<SuggestionView> PendingSuggestions { get; set; } = new List<SuggestionView>();
        public DateTime GeneratedUtc { get; set; }
    }

    // ---------- requests ----------

    public class VisitorRequest
    {
        public string DisplayName { get; set; } = "";
    }

    public class RateRequest
    {
        public int Value { get; set; }
    }

    public class CommentRequest
    {
        public CommentTarget Target { get; set; }
        public string TargetId { get; set; } = "";
        public string ParentId { get; set; } = "";
        public string Text { get; set; } = "";
        public string ClientRequestId { get; set; } = "";
    }

    public class SuggestionRequest
    {
        public SuggestionKind Kind { get; set; }
        public string ShotId { get; set; } = "";
        public ShotReviewStatus? SuggestedStatus { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string ClientRequestId { get; set; } = "";
    }

    public class ShotStatusRequest
    {
        /// <summary>Null clears Luke's verdict back to Needs review.</summary>
        public ShotReviewStatus? Status { get; set; }
        public string Note { get; set; } = "";
    }

    public class DecisionRequest
    {
        public SuggestionState State { get; set; }
        public string Note { get; set; } = "";
    }

    public class QueueUpdateRequest
    {
        public QueuePriority? Priority { get; set; }
        public string Notes { get; set; } = "";
        public bool Excluded { get; set; }
    }

    public class ModerationRequest
    {
        public string Note { get; set; } = "";
        /// <summary>Must repeat the person's display name, as a deliberate confirmation.</summary>
        public string ConfirmName { get; set; } = "";
    }

    public class PhotoHideRequest
    {
        public bool Hidden { get; set; }
    }
}
