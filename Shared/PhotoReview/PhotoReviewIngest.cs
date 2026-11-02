using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorApp.Shared.PhotoReview
{
    // ---------- manifest (see docs/photoshoot/ARTIFACT_MANIFEST.md) ----------

    public class ReviewManifest
    {
        public const string FormatName = "lucky-luke-photo-review-manifest";

        public string Format { get; set; } = "";
        public int Version { get; set; }
        public string ShootId { get; set; } = "";
        public DateTime? GeneratedUtc { get; set; }
        public string Generator { get; set; } = "";
        public List<ManifestShot> Shots { get; set; } = new List<ManifestShot>();
        public List<ManifestPhoto> Photos { get; set; } = new List<ManifestPhoto>();
    }

    public class ManifestShot
    {
        public string ShotId { get; set; } = "";
        public int? ShotNumber { get; set; }
        public string? Title { get; set; }
        public string? Category { get; set; }
        public string? Coverage { get; set; }
        public string? CoverageNotes { get; set; }
        public string? SuggestedStatus { get; set; }
        public string? SuggestionReason { get; set; }
        public string? SuggestedBy { get; set; }
    }

    /// <summary>Nullable fields mean "leave as is", so derivative-only manifests don't erase photo metadata.</summary>
    public class ManifestPhoto
    {
        public string PhotoKey { get; set; } = "";
        public string? ShotId { get; set; }
        public int? ShotNumber { get; set; }
        public List<string>? AlsoShotIds { get; set; }
        public string? Category { get; set; }
        public string? Selection { get; set; }
        public string? Evidence { get; set; }
        public string? EvidenceText { get; set; }
        public string? Reason { get; set; }
        public List<string>? Warnings { get; set; }
        public string? NotesFile { get; set; }
        public string? NotesMarkdown { get; set; }
        public PhotoSource? Source { get; set; }
        public List<ManifestVersion> Versions { get; set; } = new List<ManifestVersion>();
    }

    public class ManifestVersion
    {
        public string Kind { get; set; } = "";
        public string File { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Bytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string? ContentType { get; set; }
        public string? ParentKind { get; set; }
        public string? Provenance { get; set; }
        public string? ProcessingNotes { get; set; }
        public string? Tool { get; set; }
        public DateTime? CreatedUtc { get; set; }
    }

    /// <summary>A planner shot as the importer sees it: stable id plus its global number and title for cross-checks.</summary>
    public class PlannedShotRef
    {
        public string ShotId { get; set; } = "";
        public int ShotNumber { get; set; }
        public string Title { get; set; } = "";
        public string PlannerCategory { get; set; } = "";
    }

    public class ThumbInfo
    {
        public List<int> Widths { get; set; } = new List<int>();
        public string ContentType { get; set; } = "";
    }

    public class IngestReport
    {
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public int PhotosAdded { get; set; }
        public int PhotosUpdated { get; set; }
        public int PhotosUnchanged { get; set; }
        public int VersionsAdded { get; set; }
        public int VersionsSuperseded { get; set; }
        public int ShotsUpdated { get; set; }

        public bool Ok => Errors.Count == 0;
        public bool Changed => PhotosAdded + PhotosUpdated + VersionsAdded + VersionsSuperseded + ShotsUpdated > 0;

        public override string ToString() =>
            $"photos +{PhotosAdded} ~{PhotosUpdated} ={PhotosUnchanged}; versions +{VersionsAdded} superseded {VersionsSuperseded}; shots ~{ShotsUpdated}; {Errors.Count} errors, {Warnings.Count} warnings";
    }

    /// <summary>Validates a manifest against the live planner and upserts it into the catalog. Pure, no I/O.</summary>
    public static class PhotoReviewIngest
    {
        public const int MaxNotesLength = 20000;
        public static readonly int[] ThumbWidths = { 480, 1280 };
        private static readonly Regex PhotoKeyPattern = new Regex("^[a-z0-9][a-z0-9-]{0,99}$", RegexOptions.CultureInvariant);
        private static readonly Regex ShaPattern = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex WindowsPath = new Regex(@"[A-Za-z]:\\(?:[^\\\r\n]+\\)*([^\\\r\n]+)", RegexOptions.CultureInvariant);

        /// <summary>Server photo id derived from the stable key, so the same photo always maps to the same record.</summary>
        public static string PhotoIdFor(string photoKey)
        {
            using (var sha = SHA256.Create())
            {
                return PhotoReviewParticipation.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes("photo:" + photoKey))).Substring(0, 12);
            }
        }

        public static string VersionIdFor(string sha256) => sha256.Substring(0, 16);

        /// <summary>Replaces local absolute paths (for example a Downloads folder) with just the file name.</summary>
        public static string StripLocalPaths(string? text) =>
            WindowsPath.Replace(text ?? "", match => match.Groups[1].Value);

        public static string Extension(string contentType) => contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

        public static string ContentTypeForFile(string file)
        {
            var lower = (file ?? "").ToLowerInvariant();
            if (lower.EndsWith(".png")) return "image/png";
            if (lower.EndsWith(".webp")) return "image/webp";
            if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return "image/jpeg";
            return "";
        }

        public static string DownloadName(string photoKey, ArtifactKind kind, string contentType) =>
            photoKey + (kind == ArtifactKind.Original ? "" : "-" + PhotoReviewLabels.KindSlug(kind)) + Extension(contentType);

        /// <summary>Default category for shots the manifest has not filed yet.</summary>
        public static ReviewCategory DefaultCategory(PlannedShotRef shot)
        {
            var title = (shot.Title ?? "").ToLowerInvariant();
            switch (shot.PlannerCategory)
            {
                case "ChildVolunteer":
                    return ReviewCategory.KidsAndVolunteers;
                case "MagicianSpecific":
                    return ReviewCategory.MagicAndProps;
                case "PhysicalComedy":
                case "AbsurdComedy":
                    return ReviewCategory.ComedyPoses;
                case "AdvertisingNegativeSpace":
                    return ReviewCategory.AdvertisingAndSigns;
            }

            if (title.Contains("seated") || title.Contains("stool")) return ReviewCategory.SeatedPortraits;
            if (title.Contains("point") || title.Contains("present")) return ReviewCategory.PointingAndPresenting;
            if (title.Contains("headshot") || shot.PlannerCategory == "FacialStorytelling") return ReviewCategory.Headshots;
            return ReviewCategory.FullBody;
        }

        /// <summary>Makes sure every planned shot has a review row, in planner order. Never touches owner verdicts.</summary>
        public static int EnsureShots(ReviewCatalog catalog, IReadOnlyList<PlannedShotRef> planned)
        {
            catalog.Shots ??= new List<ShotReview>();
            var changed = 0;
            foreach (var shot in planned)
            {
                var row = catalog.Shots.FirstOrDefault(item => item.ShotId == shot.ShotId);
                if (row == null)
                {
                    catalog.Shots.Add(new ShotReview { ShotId = shot.ShotId, ShotNumber = shot.ShotNumber, Category = DefaultCategory(shot) });
                    changed++;
                }
                else if (row.ShotNumber != shot.ShotNumber)
                {
                    row.ShotNumber = shot.ShotNumber;
                    changed++;
                }
            }

            return changed;
        }

        public static IngestReport Validate(ReviewManifest manifest, IReadOnlyList<PlannedShotRef> planned, ReviewCatalog catalog)
        {
            var report = new IngestReport();
            if (manifest == null)
            {
                report.Errors.Add("Manifest is empty.");
                return report;
            }

            if (manifest.Format != ReviewManifest.FormatName || manifest.Version != 1)
            {
                report.Errors.Add($"Manifest format must be \"{ReviewManifest.FormatName}\" version 1.");
            }

            if (!string.IsNullOrEmpty(catalog.ShootId) && manifest.ShootId != catalog.ShootId)
            {
                report.Errors.Add($"Manifest is for shoot \"{manifest.ShootId}\" but the catalog is for \"{catalog.ShootId}\".");
            }

            var byId = planned.ToDictionary(item => item.ShotId);
            void CheckShot(string where, string? shotId, int? number, string? title)
            {
                if (string.IsNullOrEmpty(shotId) || !byId.TryGetValue(shotId!, out var shot))
                {
                    report.Errors.Add($"{where}: shot id \"{shotId}\" is not in the live planner.");
                    return;
                }

                if (number != null && number.Value != shot.ShotNumber)
                {
                    report.Errors.Add($"{where}: shot number {number} does not match planner number {shot.ShotNumber} for {shotId} (\"{shot.Title}\").");
                }

                if (title != null && Squash(title) != Squash(shot.Title))
                {
                    report.Errors.Add($"{where}: title \"{title}\" does not match planner title \"{shot.Title}\" for {shotId}.");
                }
            }

            foreach (var shot in manifest.Shots ?? new List<ManifestShot>())
            {
                var where = $"shot {shot.ShotNumber?.ToString() ?? shot.ShotId}";
                CheckShot(where, shot.ShotId, shot.ShotNumber, shot.Title);
                if (shot.Category != null && !PhotoReviewLabels.TryParseCategory(shot.Category, out _)) report.Errors.Add($"{where}: unknown category \"{shot.Category}\".");
                if (shot.Coverage != null && !TryEnum<ShotCoverage>(shot.Coverage, out _)) report.Errors.Add($"{where}: unknown coverage \"{shot.Coverage}\".");
                if (shot.SuggestedStatus != null && !TryEnum<ShotReviewStatus>(shot.SuggestedStatus, out _)) report.Errors.Add($"{where}: unknown status \"{shot.SuggestedStatus}\".");
            }

            var keys = new HashSet<string>();
            foreach (var photo in manifest.Photos ?? new List<ManifestPhoto>())
            {
                var where = $"photo \"{photo.PhotoKey}\"";
                if (!PhotoKeyPattern.IsMatch(photo.PhotoKey ?? ""))
                {
                    report.Errors.Add($"{where}: photoKey must match [a-z0-9-]{{1,100}}.");
                    continue;
                }

                if (!keys.Add(photo.PhotoKey!))
                {
                    report.Errors.Add($"{where}: listed twice.");
                }

                var existing = catalog.Photos.FirstOrDefault(item => item.PhotoKey == photo.PhotoKey);
                if (photo.ShotId != null || existing == null)
                {
                    CheckShot(where, photo.ShotId, photo.ShotNumber, null);
                }

                foreach (var also in photo.AlsoShotIds ?? new List<string>())
                {
                    CheckShot(where + " (also)", also, null, null);
                }

                if (photo.Category != null && !PhotoReviewLabels.TryParseCategory(photo.Category, out _)) report.Errors.Add($"{where}: unknown category \"{photo.Category}\".");
                if (photo.Selection != null && !TryEnum<SelectionRole>(photo.Selection, out _)) report.Errors.Add($"{where}: unknown selection \"{photo.Selection}\".");
                if (photo.Evidence != null && !TryEnum<MatchEvidence>(photo.Evidence, out _)) report.Errors.Add($"{where}: unknown evidence \"{photo.Evidence}\".");
                if ((photo.NotesMarkdown ?? "").Length > MaxNotesLength) report.Errors.Add($"{where}: notes are longer than {MaxNotesLength} characters.");

                var versions = photo.Versions ?? new List<ManifestVersion>();
                var kinds = new HashSet<ArtifactKind>();
                var hasOriginal = existing?.Versions.Any(item => item.Kind == ArtifactKind.Original) == true;
                foreach (var version in versions)
                {
                    var vWhere = $"{where} {version.Kind}";
                    if (!PhotoReviewLabels.TryParseKind(version.Kind, out var kind))
                    {
                        report.Errors.Add($"{vWhere}: unknown kind.");
                        continue;
                    }

                    if (!kinds.Add(kind)) report.Errors.Add($"{vWhere}: more than one {kind} for the same photo.");
                    if (!ShaPattern.IsMatch(version.Sha256 ?? "")) report.Errors.Add($"{vWhere}: sha256 must be 64 lowercase hex characters.");
                    if (version.Bytes <= 0) report.Errors.Add($"{vWhere}: bytes must be positive.");
                    if (version.Width <= 0 || version.Height <= 0) report.Errors.Add($"{vWhere}: width and height are required.");
                    var contentType = string.IsNullOrEmpty(version.ContentType) ? ContentTypeForFile(version.File) : version.ContentType!;
                    if (contentType != "image/jpeg" && contentType != "image/png" && contentType != "image/webp") report.Errors.Add($"{vWhere}: only JPEG, PNG or WebP files are accepted.");

                    var provenance = ArtifactProvenance.Camera;
                    if (version.Provenance != null && !TryEnum(version.Provenance, out provenance)) report.Errors.Add($"{vWhere}: unknown provenance \"{version.Provenance}\".");
                    if (kind == ArtifactKind.Original)
                    {
                        hasOriginal = true;
                        if (provenance != ArtifactProvenance.Camera) report.Errors.Add($"{vWhere}: an Original must have provenance Camera.");
                        if (!string.IsNullOrEmpty(version.ParentKind)) report.Errors.Add($"{vWhere}: an Original has no parent.");
                        var stored = existing?.Versions.FirstOrDefault(item => item.Kind == ArtifactKind.Original);
                        if (stored != null && stored.Sha256 != version.Sha256)
                        {
                            report.Errors.Add($"{vWhere}: differs from the stored original ({stored.Sha256.Substring(0, 12)}…). Originals are immutable.");
                        }
                    }
                    else
                    {
                        if (version.Provenance == null || provenance == ArtifactProvenance.Camera) report.Errors.Add($"{vWhere}: derivatives need provenance NonGenerative or Generative.");
                        if (!PhotoReviewLabels.TryParseKind(version.ParentKind, out var parentKind) || parentKind == kind)
                        {
                            report.Errors.Add($"{vWhere}: parentKind is required and must be a different kind.");
                        }
                        else if (!versions.Any(item => PhotoReviewLabels.TryParseKind(item.Kind, out var k) && k == parentKind)
                                 && existing?.Versions.Any(item => item.Kind == parentKind && item.IsCurrent) != true)
                        {
                            report.Errors.Add($"{vWhere}: parent {parentKind} does not exist for this photo.");
                        }
                    }
                }

                if (!hasOriginal)
                {
                    report.Errors.Add($"{where}: new photos need an Original version.");
                }
            }

            if (report.Ok)
            {
                CheckParentCycles(manifest, report);
            }

            return report;
        }

        private static void CheckParentCycles(ReviewManifest manifest, IngestReport report)
        {
            foreach (var photo in manifest.Photos)
            {
                foreach (var version in photo.Versions)
                {
                    var seen = new HashSet<string>();
                    var current = version;
                    while (current != null && !string.IsNullOrEmpty(current.ParentKind))
                    {
                        if (!seen.Add(Squash(current.Kind)))
                        {
                            report.Errors.Add($"photo \"{photo.PhotoKey}\": parent chain loops at {current.Kind}.");
                            break;
                        }

                        var parent = current.ParentKind;
                        current = photo.Versions.FirstOrDefault(item => Squash(item.Kind) == Squash(parent));
                    }
                }
            }
        }

        /// <summary>
        /// Validates, then upserts. Returns the report; the catalog is only changed when the report is Ok.
        /// <paramref name="thumbs"/> maps sha256 to the thumbnails the importer produced.
        /// </summary>
        public static IngestReport Apply(
            ReviewCatalog catalog,
            ReviewManifest manifest,
            IReadOnlyList<PlannedShotRef> planned,
            IDictionary<string, ThumbInfo> thumbs,
            Func<string, string?> readNotesFile,
            DateTime nowUtc)
        {
            Normalize(catalog);
            if (string.IsNullOrEmpty(catalog.ShootId))
            {
                catalog.ShootId = manifest?.ShootId ?? "";
            }

            var report = Validate(manifest!, planned, catalog);
            if (!report.Ok)
            {
                return report;
            }

            report.ShotsUpdated += EnsureShots(catalog, planned);
            foreach (var item in manifest!.Shots ?? new List<ManifestShot>())
            {
                var row = catalog.Shots.First(shot => shot.ShotId == item.ShotId);
                var before = Fingerprint(row);
                if (item.Category != null && PhotoReviewLabels.TryParseCategory(item.Category, out var category))
                {
                    row.Category = category;
                    row.CategoryAssigned = true;
                }

                if (item.Coverage != null && TryEnum<ShotCoverage>(item.Coverage, out var coverage)) row.Coverage = coverage;
                if (item.CoverageNotes != null) row.CoverageNotes = StripLocalPaths(item.CoverageNotes).Trim();
                // Suggestions are evidence only. OwnerStatus is never touched by an import.
                if (item.SuggestedStatus != null && TryEnum<ShotReviewStatus>(item.SuggestedStatus, out var suggested)) row.SuggestedStatus = suggested;
                if (item.SuggestionReason != null) row.SuggestionReason = StripLocalPaths(item.SuggestionReason).Trim();
                if (item.SuggestedBy != null) row.SuggestedBy = item.SuggestedBy.Trim();
                if (Fingerprint(row) != before) report.ShotsUpdated++;
            }

            foreach (var item in manifest.Photos ?? new List<ManifestPhoto>())
            {
                var photo = catalog.Photos.FirstOrDefault(entry => entry.PhotoKey == item.PhotoKey);
                var isNew = photo == null;
                if (photo == null)
                {
                    photo = new ReviewPhoto { Id = PhotoIdFor(item.PhotoKey), PhotoKey = item.PhotoKey, ImportedUtc = nowUtc };
                    catalog.Photos.Add(photo);
                }

                var before = Fingerprint(photo);
                if (item.ShotId != null) photo.ShotId = item.ShotId;
                if (item.AlsoShotIds != null) photo.AlsoShotIds = item.AlsoShotIds.Distinct().Where(id => id != photo.ShotId).ToList();
                if (item.Category != null && PhotoReviewLabels.TryParseCategory(item.Category, out var category)) photo.Category = category;
                else if (isNew) photo.Category = catalog.Shots.FirstOrDefault(shot => shot.ShotId == photo.ShotId)?.Category ?? ReviewCategory.Headshots;
                if (item.Selection != null && TryEnum<SelectionRole>(item.Selection, out var selection)) photo.Selection = selection;
                if (item.Evidence != null && TryEnum<MatchEvidence>(item.Evidence, out var evidence)) photo.Evidence = evidence;
                if (item.EvidenceText != null) photo.EvidenceText = StripLocalPaths(item.EvidenceText).Trim();
                if (item.Reason != null) photo.Reason = StripLocalPaths(item.Reason).Trim();
                if (item.Warnings != null) photo.Warnings = item.Warnings.Select(text => StripLocalPaths(text).Trim()).Where(text => text.Length > 0).Distinct().ToList();
                var notes = item.NotesMarkdown ?? (string.IsNullOrEmpty(item.NotesFile) ? null : readNotesFile(item.NotesFile!));
                if (notes != null)
                {
                    notes = StripLocalPaths(notes).Replace("\r", "").Trim();
                    photo.NotesMarkdown = notes.Length > MaxNotesLength ? notes.Substring(0, MaxNotesLength) : notes;
                }

                if (item.Source != null)
                {
                    photo.Source = new PhotoSource
                    {
                        PhotoNumber = item.Source.PhotoNumber,
                        Url = SafeUrl(item.Source.Url),
                        OriginalFileName = StripLocalPaths(item.Source.OriginalFileName),
                        Archive = StripLocalPaths(item.Source.Archive)
                    };
                }

                var versionsChanged = ApplyVersions(photo, item, thumbs, nowUtc, report);
                if (isNew)
                {
                    photo.UpdatedUtc = nowUtc;
                    report.PhotosAdded++;
                }
                else if (versionsChanged || Fingerprint(photo) != before)
                {
                    photo.UpdatedUtc = nowUtc;
                    report.PhotosUpdated++;
                }
                else
                {
                    report.PhotosUnchanged++;
                }
            }

            if (report.Changed)
            {
                catalog.Imports.Add(new ImportRecord
                {
                    Id = PhotoReviewParticipation.NewId(8),
                    Utc = nowUtc,
                    Generator = (manifest.Generator ?? "").Trim(),
                    PhotosAdded = report.PhotosAdded,
                    PhotosUpdated = report.PhotosUpdated,
                    VersionsAdded = report.VersionsAdded,
                    VersionsSuperseded = report.VersionsSuperseded,
                    ShotsUpdated = report.ShotsUpdated
                });
            }

            return report;
        }

        private static readonly ArtifactKind[] ApplyOrder =
        {
            ArtifactKind.Original, ArtifactKind.Cropped, ArtifactKind.Transparent, ArtifactKind.WhiteBackground,
            ArtifactKind.CroppedTransparent, ArtifactKind.CroppedWhiteBackground
        };

        private static bool ApplyVersions(ReviewPhoto photo, ManifestPhoto item, IDictionary<string, ThumbInfo> thumbs, DateTime nowUtc, IngestReport report)
        {
            var changed = false;
            var incoming = (item.Versions ?? new List<ManifestVersion>())
                .Select(version => { PhotoReviewLabels.TryParseKind(version.Kind, out var kind); return (kind, version); })
                .OrderBy(pair => Array.IndexOf(ApplyOrder, pair.kind))
                .ToList();
            foreach (var (kind, version) in incoming)
            {
                var contentType = string.IsNullOrEmpty(version.ContentType) ? ContentTypeForFile(version.File) : version.ContentType!;
                TryEnum<ArtifactProvenance>(version.Provenance ?? "Camera", out var provenance);
                var parentId = "";
                if (kind != ArtifactKind.Original && PhotoReviewLabels.TryParseKind(version.ParentKind, out var parentKind))
                {
                    parentId = photo.Versions.First(entry => entry.Kind == parentKind && entry.IsCurrent).Id;
                }

                var current = photo.Versions.FirstOrDefault(entry => entry.Kind == kind && entry.IsCurrent);
                thumbs.TryGetValue(version.Sha256, out var thumb);
                if (current != null && current.Sha256 == version.Sha256)
                {
                    // Same file again: refresh descriptive fields only.
                    var before = current.ProcessingNotes + "|" + current.Tool + "|" + current.ParentVersionId + "|" + string.Join(",", current.ThumbWidths);
                    if (version.ProcessingNotes != null) current.ProcessingNotes = StripLocalPaths(version.ProcessingNotes).Trim();
                    if (version.Tool != null) current.Tool = version.Tool.Trim();
                    if (kind != ArtifactKind.Original) current.ParentVersionId = parentId;
                    if (thumb != null && thumb.Widths.Count > 0)
                    {
                        current.ThumbWidths = thumb.Widths.OrderBy(w => w).ToList();
                        current.ThumbContentType = thumb.ContentType;
                    }

                    changed |= before != current.ProcessingNotes + "|" + current.Tool + "|" + current.ParentVersionId + "|" + string.Join(",", current.ThumbWidths);
                    continue;
                }

                var versionId = VersionIdFor(version.Sha256);
                if (photo.Versions.Any(entry => entry.Id == versionId && entry.Kind != kind))
                {
                    report.Warnings.Add($"photo \"{photo.PhotoKey}\": {kind} has the same bytes as its {photo.Versions.First(entry => entry.Id == versionId).Kind} version.");
                    versionId = versionId.Substring(0, 12) + PhotoReviewLabels.KindSlug(kind).Substring(0, Math.Min(4, PhotoReviewLabels.KindSlug(kind).Length));
                }

                var previous = photo.Versions.FirstOrDefault(entry => entry.Kind == kind && entry.Sha256 == version.Sha256 && !entry.IsCurrent);
                if (current != null)
                {
                    current.SupersededUtc = nowUtc;
                    report.VersionsSuperseded++;
                }

                if (previous != null)
                {
                    // Re-delivered an older file: bring it back as current.
                    previous.SupersededUtc = null;
                    previous.ParentVersionId = parentId;
                    changed = true;
                    continue;
                }

                photo.Versions.Add(new ArtifactVersion
                {
                    Id = versionId,
                    Kind = kind,
                    ParentVersionId = parentId,
                    Sha256 = version.Sha256,
                    Bytes = version.Bytes,
                    Width = version.Width,
                    Height = version.Height,
                    ContentType = contentType,
                    FileName = DownloadName(photo.PhotoKey, kind, contentType),
                    DeliveredFileName = FileNameOnly(version.File),
                    Provenance = provenance,
                    ProcessingNotes = StripLocalPaths(version.ProcessingNotes).Trim(),
                    Tool = (version.Tool ?? "").Trim(),
                    CreatedUtc = version.CreatedUtc,
                    ImportedUtc = nowUtc,
                    ThumbWidths = thumb?.Widths.OrderBy(w => w).ToList() ?? new List<int>(),
                    ThumbContentType = thumb?.ContentType ?? ""
                });
                report.VersionsAdded++;
                changed = true;
            }

            return changed;
        }

        public static ReviewCatalog Normalize(ReviewCatalog catalog)
        {
            catalog.Shots ??= new List<ShotReview>();
            catalog.Photos ??= new List<ReviewPhoto>();
            catalog.Queue ??= new List<QueueEntry>();
            catalog.Imports ??= new List<ImportRecord>();
            foreach (var photo in catalog.Photos)
            {
                photo.Versions ??= new List<ArtifactVersion>();
                photo.Warnings ??= new List<string>();
                photo.AlsoShotIds ??= new List<string>();
                photo.Source ??= new PhotoSource();
            }

            return catalog;
        }

        public static bool TryEnum<T>(string? value, out T result) where T : struct
        {
            var key = Squash(value);
            foreach (T candidate in Enum.GetValues(typeof(T)))
            {
                if (Squash(candidate.ToString()) == key)
                {
                    result = candidate;
                    return true;
                }
            }

            result = default;
            return false;
        }

        private static string FileNameOnly(string? path)
        {
            var text = (path ?? "").Replace('\\', '/');
            var slash = text.LastIndexOf('/');
            return slash >= 0 ? text.Substring(slash + 1) : text;
        }

        private static string SafeUrl(string? url)
        {
            var text = (url ?? "").Trim();
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http") ? text : "";
        }

        private static string Fingerprint(ShotReview row) =>
            $"{row.Category}|{row.CategoryAssigned}|{row.Coverage}|{row.CoverageNotes}|{row.SuggestedStatus}|{row.SuggestionReason}|{row.SuggestedBy}";

        private static string Fingerprint(ReviewPhoto photo) =>
            string.Join("|", photo.ShotId, string.Join(",", photo.AlsoShotIds), photo.Category, photo.Selection, photo.Evidence,
                photo.EvidenceText, photo.Reason, string.Join("\u0001", photo.Warnings), photo.NotesMarkdown,
                photo.Source.PhotoNumber, photo.Source.Url, photo.Source.OriginalFileName, photo.Source.Archive);

        private static string Squash(string? value) =>
            new string((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
