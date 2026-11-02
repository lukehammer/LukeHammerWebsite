using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared.PhotoReview
{
    /// <summary>The nine broad filing categories for selected photos. Every planned shot belongs to one.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ReviewCategory
    {
        Headshots = 0,
        FullBody = 1,
        PointingAndPresenting = 2,
        ChildishPoses = 3,
        ComedyPoses = 4,
        MagicAndProps = 5,
        SeatedPortraits = 6,
        AdvertisingAndSigns = 7,
        KidsAndVolunteers = 8
    }

    /// <summary>Owner's verdict on whether the planned shot was achieved. Only the admin sets it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ShotReviewStatus
    {
        NeedsReview = 0,
        Successful = 1,
        PartiallySuccessful = 2,
        Unsuccessful = 3,
        NotCaptured = 4
    }

    /// <summary>What the selection pass found for a planned shot. Evidence only, not an approval.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ShotCoverage
    {
        Unknown = 0,
        Selected = 1,
        NoDedicatedMatch = 2
    }

    /// <summary>How confidently a photo was matched to its planned shot.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MatchEvidence
    {
        Unmatched = 0,
        SlateSupported = 1,
        Inferred = 2,
        Approximate = 3
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SelectionRole
    {
        Alternate = 0,
        Primary = 1,
        ProposedPrimary = 2
    }

    /// <summary>One version of a photograph. Original is the untouched camera file; the rest derive from it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ArtifactKind
    {
        Original = 0,
        Cropped = 1,
        Transparent = 2,
        CroppedTransparent = 3,
        WhiteBackground = 4,
        CroppedWhiteBackground = 5
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ArtifactProvenance
    {
        Camera = 0,
        NonGenerative = 1,
        Generative = 2
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SuggestionKind
    {
        /// <summary>A visitor thinks a planned shot deserves a different status.</summary>
        StatusChange = 0,
        /// <summary>A change to how an existing planned pose should be shot.</summary>
        PoseChange = 1,
        /// <summary>A brand-new pose for the next shoot.</summary>
        NewPose = 2
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SuggestionState
    {
        Open = 0,
        Approved = 1,
        Declined = 2
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CommentTarget
    {
        Photo = 0,
        Shot = 1,
        Suggestion = 2
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum QueuePriority
    {
        High = 0,
        Medium = 1,
        Low = 2
    }

    public static class PhotoReviewLabels
    {
        public static readonly IReadOnlyList<ReviewCategory> Categories = (ReviewCategory[])Enum.GetValues(typeof(ReviewCategory));

        public static readonly IReadOnlyList<ArtifactKind> Kinds = (ArtifactKind[])Enum.GetValues(typeof(ArtifactKind));

        public static readonly IReadOnlyList<ShotReviewStatus> Statuses = (ShotReviewStatus[])Enum.GetValues(typeof(ShotReviewStatus));

        public static string Category(ReviewCategory category) => category switch
        {
            ReviewCategory.Headshots => "Headshots",
            ReviewCategory.FullBody => "Full Body",
            ReviewCategory.PointingAndPresenting => "Pointing and Presenting",
            ReviewCategory.ChildishPoses => "Childish Poses",
            ReviewCategory.ComedyPoses => "Comedy Poses",
            ReviewCategory.MagicAndProps => "Magic and Props",
            ReviewCategory.SeatedPortraits => "Seated Portraits",
            ReviewCategory.AdvertisingAndSigns => "Advertising and Signs",
            ReviewCategory.KidsAndVolunteers => "Kids and Volunteers",
            _ => category.ToString()
        };

        public static string CategorySlug(ReviewCategory category) => category switch
        {
            ReviewCategory.FullBody => "full-body",
            ReviewCategory.PointingAndPresenting => "pointing-and-presenting",
            ReviewCategory.ChildishPoses => "childish-poses",
            ReviewCategory.ComedyPoses => "comedy-poses",
            ReviewCategory.MagicAndProps => "magic-and-props",
            ReviewCategory.SeatedPortraits => "seated-portraits",
            ReviewCategory.AdvertisingAndSigns => "advertising-and-signs",
            ReviewCategory.KidsAndVolunteers => "kids-and-volunteers",
            _ => "headshots"
        };

        /// <summary>Accepts enum names, display names ("Full Body") and slugs ("full-body").</summary>
        public static bool TryParseCategory(string? value, out ReviewCategory category)
        {
            var key = Squash(value);
            foreach (var candidate in Categories)
            {
                if (Squash(candidate.ToString()) == key || Squash(Category(candidate)) == key)
                {
                    category = candidate;
                    return true;
                }
            }

            category = ReviewCategory.Headshots;
            return false;
        }

        public static string Status(ShotReviewStatus status) => status switch
        {
            ShotReviewStatus.Successful => "Successful",
            ShotReviewStatus.PartiallySuccessful => "Partially successful",
            ShotReviewStatus.Unsuccessful => "Unsuccessful",
            ShotReviewStatus.NotCaptured => "Not captured",
            _ => "Needs review"
        };

        public static string Evidence(MatchEvidence evidence) => evidence switch
        {
            MatchEvidence.SlateSupported => "Slate-supported",
            MatchEvidence.Inferred => "Inferred",
            MatchEvidence.Approximate => "Approximate",
            _ => "Unmatched"
        };

        public static string EvidenceHelp(MatchEvidence evidence) => evidence switch
        {
            MatchEvidence.SlateSupported => "A photographed on-set slate identifies this shot.",
            MatchEvidence.Inferred => "No slate for this frame; matched from shot order and the visible pose.",
            MatchEvidence.Approximate => "Close but not exact: a related pose, wardrobe variation or partial execution.",
            _ => "Not matched to a planned shot."
        };

        public static string Selection(SelectionRole role) => role switch
        {
            SelectionRole.Primary => "Primary",
            SelectionRole.ProposedPrimary => "Proposed primary",
            _ => "Alternate"
        };

        public static string Coverage(ShotCoverage coverage) => coverage switch
        {
            ShotCoverage.Selected => "Photos selected",
            ShotCoverage.NoDedicatedMatch => "No dedicated match found",
            _ => "Not reviewed yet"
        };

        public static string Kind(ArtifactKind kind) => kind switch
        {
            ArtifactKind.Original => "Original",
            ArtifactKind.Cropped => "Cropped",
            ArtifactKind.Transparent => "Transparent",
            ArtifactKind.CroppedTransparent => "Cropped transparent",
            ArtifactKind.WhiteBackground => "White background (cleaned)",
            ArtifactKind.CroppedWhiteBackground => "Cropped white background",
            _ => kind.ToString()
        };

        public static string KindSlug(ArtifactKind kind) => kind switch
        {
            ArtifactKind.Cropped => "cropped",
            ArtifactKind.Transparent => "transparent",
            ArtifactKind.CroppedTransparent => "cropped-transparent",
            ArtifactKind.WhiteBackground => "white-background",
            ArtifactKind.CroppedWhiteBackground => "cropped-white-background",
            _ => "original"
        };

        public static bool TryParseKind(string? value, out ArtifactKind kind)
        {
            var key = Squash(value);
            foreach (var candidate in Kinds)
            {
                if (Squash(candidate.ToString()) == key || Squash(KindSlug(candidate)) == key)
                {
                    kind = candidate;
                    return true;
                }
            }

            kind = ArtifactKind.Original;
            return false;
        }

        public static string Provenance(ArtifactProvenance provenance) => provenance switch
        {
            ArtifactProvenance.Camera => "Camera original",
            ArtifactProvenance.NonGenerative => "Non-generative edit",
            ArtifactProvenance.Generative => "Generative (AI) edit",
            _ => provenance.ToString()
        };

        public static string SuggestionKindLabel(SuggestionKind kind) => kind switch
        {
            SuggestionKind.StatusChange => "Status suggestion",
            SuggestionKind.PoseChange => "Pose change",
            _ => "New pose idea"
        };

        public static string Priority(QueuePriority priority) => priority switch
        {
            QueuePriority.High => "High",
            QueuePriority.Low => "Low",
            _ => "Medium"
        };

        private static string Squash(string? value)
        {
            var chars = new List<char>();
            foreach (var c in (value ?? "").ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    chars.Add(c);
                }
            }

            return new string(chars.ToArray());
        }
    }
}
