using System.Text;
using System.Text.RegularExpressions;
using BlazorApp.Shared.PhotoReview;
using BlazorApp.Shared.PhotoShoot;

namespace PhotoReviewImport;

/// <summary>
/// Builds a manifest for the selection pass from selection-index.csv, shot-coverage.csv, selection-review.md and
/// the per-photo sidecars. Website shot numbers map to planner ids by the planner's order, and every title is
/// cross-checked, so a reordered planner fails loudly instead of mis-filing photos.
/// </summary>
public static class SelectionManifestBuilder
{
    public const string Slate827Warning =
        "Source 827 (Magic Print Shirt slate) is unreliable: the photos after it show a white speckled shirt and plaid, so this match rests on the visible pose, not the slate.";

    public static ReviewManifest Build(string collection, PhotoShootDocument planner, out List<string> notes)
    {
        notes = new List<string>();
        var planned = PhotoReviewProjection.PlannedRefs(planner);
        var byNumber = planned.ToDictionary(shot => shot.ShotNumber);
        var coverage = Csv.ReadFile(Path.Combine(collection, "shot-coverage.csv"));
        var index = Csv.ReadFile(Path.Combine(collection, "selection-index.csv"));
        var missingReasons = MissingReasons(Path.Combine(collection, "selection-review.md"));

        var manifest = new ReviewManifest
        {
            Format = ReviewManifest.FormatName,
            Version = 1,
            ShootId = planner.Id,
            GeneratedUtc = DateTime.UtcNow,
            Generator = "claude-code build-selection (from Codex selection files)"
        };

        foreach (var row in coverage)
        {
            var number = int.Parse(row["shot"]);
            if (!byNumber.TryGetValue(number, out var shot))
            {
                throw new ToolException($"shot-coverage.csv shot {number} is not in the planner (it has {planned.Count} shots).");
            }

            var noMatch = row["status"].Trim().Equals("No dedicated match", StringComparison.OrdinalIgnoreCase);
            manifest.Shots.Add(new ManifestShot
            {
                ShotId = shot.ShotId,
                ShotNumber = number,
                Title = row["website_title"],
                Category = row["category"],
                Coverage = noMatch ? nameof(ShotCoverage.NoDedicatedMatch) : nameof(ShotCoverage.Selected),
                CoverageNotes = noMatch && missingReasons.TryGetValue(number, out var reason) ? reason : row.GetValueOrDefault("notes", ""),
                SuggestedStatus = noMatch ? nameof(ShotReviewStatus.NotCaptured) : null,
                SuggestedBy = noMatch ? "Codex" : null
            });
        }

        ApplyAssessments(Path.Combine(collection, "capture-assessments.json"), manifest, byNumber, notes);

        foreach (var row in index)
        {
            var number = int.Parse(row["shot"]);
            if (!byNumber.TryGetValue(number, out var shot))
            {
                throw new ToolException($"selection-index.csv shot {number} is not in the planner.");
            }

            var relative = row["relative_path"].Replace('\\', '/');
            var fileName = row["filename"];
            var key = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            var folder = relative.Contains('/') ? relative.Substring(0, relative.IndexOf('/')) : "";
            var notesRelative = (folder.Length > 0 ? folder + "/" : "") + key + "-selection-notes.md";
            var sidecar = File.Exists(Path.Combine(collection, notesRelative)) ? File.ReadAllText(Path.Combine(collection, notesRelative)) : "";
            if (sidecar.Length == 0) notes.Add($"warning: no sidecar for {fileName}");
            var evidenceText = row["matching_evidence"];
            var reasonText = row["reason"];
            var bytes = new FileInfo(Path.Combine(collection, relative)).Length;

            manifest.Photos.Add(new ManifestPhoto
            {
                PhotoKey = key,
                ShotId = shot.ShotId,
                ShotNumber = number,
                Category = folder,
                Selection = Selection(row["status"]),
                Evidence = Evidence(evidenceText, reasonText),
                EvidenceText = evidenceText,
                Reason = reasonText,
                Warnings = Warnings(evidenceText, reasonText),
                NotesFile = notesRelative,
                Source = new PhotoSource
                {
                    PhotoNumber = int.TryParse(row.GetValueOrDefault("source_photo_number", ""), out var source) ? source : 0,
                    Url = row.GetValueOrDefault("flickr_url", ""),
                    OriginalFileName = SidecarValue(sidecar, "Original source filename"),
                    Archive = Path.GetFileName(SidecarValue(sidecar, "Archive").Replace('\\', '/'))
                },
                Versions = new List<ManifestVersion>
                {
                    new ManifestVersion
                    {
                        Kind = nameof(ArtifactKind.Original),
                        File = relative,
                        Sha256 = row["sha256"].Trim().ToLowerInvariant(),
                        Bytes = bytes,
                        Width = int.Parse(row["width"]),
                        Height = int.Parse(row["height"]),
                        ContentType = "image/jpeg",
                        Provenance = nameof(ArtifactProvenance.Camera),
                        ProcessingNotes = "Untouched camera original, byte-identical to the downloaded album file."
                    }
                }
            });
        }

        var withPhotos = manifest.Photos.Select(photo => photo.ShotId).Distinct().Count();
        notes.Add($"{manifest.Shots.Count} shots ({manifest.Shots.Count(s => s.Coverage == nameof(ShotCoverage.NoDedicatedMatch))} without a dedicated match), {manifest.Photos.Count} photos on {withPhotos} shots.");
        return manifest;
    }

    public sealed class CaptureAssessment
    {
        public string ShotId { get; set; } = "";
        public int ShotNumber { get; set; }
        public string Title { get; set; } = "";
        public string SuggestedStatus { get; set; } = "";
        public string AssessmentBy { get; set; } = "";
        public string Reason { get; set; } = "";
        public bool OwnerApproved { get; set; }
    }

    /// <summary>
    /// Codex's per-shot capture assessments become labeled suggestions. They are never Luke's verdict, even if a
    /// row claims ownerApproved: owner approval only comes from Luke signed in on the site.
    /// </summary>
    public static void ApplyAssessments(string path, ReviewManifest manifest, IReadOnlyDictionary<int, PlannedShotRef> byNumber, List<string> notes)
    {
        if (!File.Exists(path))
        {
            notes.Add("No capture-assessments.json; shots without a dedicated match default to a Not captured suggestion.");
            return;
        }

        var assessments = System.Text.Json.JsonSerializer.Deserialize<List<CaptureAssessment>>(File.ReadAllText(path), Program.Json)
            ?? throw new ToolException("capture-assessments.json is empty.");
        var seen = new HashSet<string>();
        foreach (var item in assessments)
        {
            if (!byNumber.TryGetValue(item.ShotNumber, out var planned) || planned.ShotId != item.ShotId)
            {
                throw new ToolException($"capture-assessments.json: shot {item.ShotNumber} id {item.ShotId} does not match the planner ({(planned == null ? "no such number" : planned.ShotId)}).");
            }

            if (!string.Equals(Squash(item.Title), Squash(planned.Title), StringComparison.Ordinal))
            {
                throw new ToolException($"capture-assessments.json: shot {item.ShotNumber} title \"{item.Title}\" does not match planner \"{planned.Title}\".");
            }

            if (!PhotoReviewIngest.TryEnum<ShotReviewStatus>(item.SuggestedStatus, out var status))
            {
                throw new ToolException($"capture-assessments.json: shot {item.ShotNumber} has unknown status \"{item.SuggestedStatus}\".");
            }

            if (!seen.Add(item.ShotId)) throw new ToolException($"capture-assessments.json: shot {item.ShotNumber} is listed twice.");
            if (item.OwnerApproved) notes.Add($"warning: shot {item.ShotNumber} says ownerApproved; imported as a suggestion only. Luke approves on the site.");

            var shot = manifest.Shots.FirstOrDefault(s => s.ShotId == item.ShotId);
            if (shot == null)
            {
                shot = new ManifestShot { ShotId = item.ShotId, ShotNumber = item.ShotNumber, Title = item.Title };
                manifest.Shots.Add(shot);
            }

            if (shot.Coverage == nameof(ShotCoverage.NoDedicatedMatch) && status != ShotReviewStatus.NotCaptured)
            {
                notes.Add($"warning: shot {item.ShotNumber} has no dedicated match but the assessment says {status}.");
            }

            shot.SuggestedStatus = status.ToString();
            shot.SuggestionReason = item.Reason;
            shot.SuggestedBy = string.IsNullOrWhiteSpace(item.AssessmentBy) ? "Codex" : item.AssessmentBy.Trim();
        }

        var counts = assessments.GroupBy(a => a.SuggestedStatus).Select(g => $"{g.Count()} {g.Key}");
        notes.Add($"Capture assessments: {assessments.Count} shots ({string.Join(", ", counts)}), imported as suggestions.");
        if (assessments.Count != byNumber.Count) notes.Add($"warning: assessments cover {assessments.Count} of {byNumber.Count} planned shots.");
    }

    private static string Squash(string? value) => new string((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static string Selection(string status)
    {
        var text = status.Trim().ToLowerInvariant();
        if (text.StartsWith("proposed")) return nameof(SelectionRole.ProposedPrimary);
        if (text.StartsWith("primary")) return nameof(SelectionRole.Primary);
        return nameof(SelectionRole.Alternate);
    }

    /// <summary>Slate-supported when a photographed slate is cited; Approximate for wardrobe-variation or "approximate execution" matches; otherwise Inferred.</summary>
    public static string Evidence(string evidence, string reason)
    {
        var e = evidence.Trim();
        if (e.StartsWith("Visual match to a later wardrobe variation", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("approximate execution", StringComparison.OrdinalIgnoreCase)
            || e.Contains("approximate", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(MatchEvidence.Approximate);
        }

        if (e.StartsWith("Photographed shot-", StringComparison.OrdinalIgnoreCase) || e.StartsWith("Slate-supported", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(MatchEvidence.SlateSupported);
        }

        return nameof(MatchEvidence.Inferred);
    }

    public static List<string> Warnings(string evidence, string reason)
    {
        var warnings = new List<string>();
        if (Regex.IsMatch(evidence, @"\b827\b"))
        {
            warnings.Add(Slate827Warning);
        }

        foreach (var sentence in Regex.Split(reason, @"(?<=[.;])\s+"))
        {
            if (sentence.Contains("opposite the", StringComparison.OrdinalIgnoreCase) && sentence.Contains("title", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Image direction differs from the title: " + sentence.Trim().TrimEnd(';', '.') + ". Do not mirror the original.");
            }
        }

        return warnings;
    }

    /// <summary>"- 052 — Title: reason" lines under "Entries without a clear dedicated match".</summary>
    public static Dictionary<int, string> MissingReasons(string reviewPath)
    {
        var result = new Dictionary<int, string>();
        if (!File.Exists(reviewPath)) return result;
        foreach (var line in File.ReadAllLines(reviewPath))
        {
            var match = Regex.Match(line, @"^- (\d{3}) — [^:]+: (.+)$");
            if (match.Success) result[int.Parse(match.Groups[1].Value)] = match.Groups[2].Value.Trim();
        }

        return result;
    }

    private static string SidecarValue(string sidecar, string label)
    {
        var match = Regex.Match(sidecar, "^" + Regex.Escape(label) + @":\s*(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }
}

/// <summary>RFC 4180 CSV reader (quoted fields, embedded commas, quotes and newlines; optional BOM).</summary>
public static class Csv
{
    public static List<Dictionary<string, string>> ReadFile(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    public static List<Dictionary<string, string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        text = text.TrimStart('﻿');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n': row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new List<string>(); break;
                default: field.Append(c); break;
            }
        }

        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        if (rows.Count == 0) return new List<Dictionary<string, string>>();
        var header = rows[0].Select(name => name.Trim()).ToList();
        return rows.Skip(1)
            .Where(values => values.Any(value => value.Length > 0))
            .Select(values => header.Select((name, i) => (name, value: i < values.Count ? values[i] : "")).ToDictionary(pair => pair.name, pair => pair.value))
            .ToList();
    }
}
