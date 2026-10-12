using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.PhotoShoot
{
    public class PhotoShootProgress
    {
        public int Total { get; set; }
        public int Completed { get; set; }
        public int Skipped { get; set; }
        public int InProgress { get; set; }
        public int NotStarted { get; set; }
        public int Remaining => Total - Completed;
        public int MustHaveTotal { get; set; }
        public int MustHaveCompleted { get; set; }
        public int MustHaveRemaining => MustHaveTotal - MustHaveCompleted;
        public int Favorites { get; set; }

        public int Percent => Total == 0 ? 0 : (int)Math.Round(Completed * 100.0 / Total);
        public int MustHavePercent => MustHaveTotal == 0 ? 0 : (int)Math.Round(MustHaveCompleted * 100.0 / MustHaveTotal);
    }

    /// <summary>Estimated minutes. Always presented to users as an estimate.</summary>
    public class PhotoShootEstimate
    {
        public int ShootingMinutes { get; set; }
        public int ChangeoverMinutes { get; set; }
        public int BreakMinutes { get; set; }
        public int SetupCount { get; set; }
        public int ShotCount { get; set; }
        public int TotalMinutes => ShootingMinutes + ChangeoverMinutes + BreakMinutes;

        public static string Format(int minutes)
        {
            if (minutes <= 0)
            {
                return "0 min";
            }

            var hours = minutes / 60;
            var rest = minutes % 60;
            if (hours == 0)
            {
                return $"{rest} min";
            }

            return rest == 0 ? $"{hours} hr" : $"{hours} hr {rest} min";
        }
    }

    public class SetupGroup
    {
        /// <summary>Null for the "Unassigned" bucket.</summary>
        public PhotoSetup? Setup { get; set; }
        public List<PhotoShot> Shots { get; set; } = new List<PhotoShot>();
        public string Key => Setup?.Id ?? "";
        public string Name => Setup?.Name ?? "Unassigned";
        public int CompletedCount => Shots.Count(s => s.Status == ShotStatus.Completed);
        public int MustHaveCount => Shots.Count(s => s.Priority == ShotPriority.MustHave);
        public int EstimatedMinutes => Shots.Sum(s => Math.Max(0, s.EstimatedMinutes));
        public bool IsDone => Shots.Count > 0 && Shots.All(s => s.Status == ShotStatus.Completed);
    }

    /// <summary>Pure ordering, grouping, progress, and estimate logic for a photo shoot.</summary>
    public static class PhotoShootPlanner
    {
        public static IReadOnlyList<PhotoSetup> OrderedSetups(PhotoShootDocument doc) =>
            (doc.Setups ?? new List<PhotoSetup>())
                .OrderBy(s => s.Sequence)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public static IReadOnlyList<SetupGroup> Groups(PhotoShootDocument doc, bool includeEmptySetups = true)
        {
            var shots = doc.Shots ?? new List<PhotoShot>();
            var setups = OrderedSetups(doc);
            var setupIds = new HashSet<string>(setups.Select(s => s.Id), StringComparer.Ordinal);
            var groups = new List<SetupGroup>();

            foreach (var setup in setups)
            {
                var group = new SetupGroup
                {
                    Setup = setup,
                    Shots = shots.Where(s => s.SetupId == setup.Id).OrderBy(s => s.Sequence).ToList()
                };
                if (includeEmptySetups || group.Shots.Count > 0)
                {
                    groups.Add(group);
                }
            }

            var unassigned = shots
                .Where(s => string.IsNullOrEmpty(s.SetupId) || !setupIds.Contains(s.SetupId))
                .OrderBy(s => s.Sequence)
                .ToList();
            if (unassigned.Count > 0)
            {
                groups.Add(new SetupGroup { Setup = null, Shots = unassigned });
            }

            return groups;
        }

        public static IReadOnlyList<PhotoShot> OrderedShots(PhotoShootDocument doc) =>
            Groups(doc, includeEmptySetups: false).SelectMany(g => g.Shots).ToList();

        public static PhotoShot? FindShot(PhotoShootDocument doc, string? shotId) =>
            string.IsNullOrEmpty(shotId) ? null : doc.Shots?.FirstOrDefault(s => s.Id == shotId);

        public static PhotoSetup? FindSetup(PhotoShootDocument doc, string? setupId) =>
            string.IsNullOrEmpty(setupId) ? null : doc.Setups?.FirstOrDefault(s => s.Id == setupId);

        /// <summary>1-based position of the shot in the overall shooting order (0 if not found).</summary>
        public static int OrderNumber(PhotoShootDocument doc, string shotId)
        {
            var ordered = OrderedShots(doc);
            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Id == shotId)
                {
                    return i + 1;
                }
            }

            return 0;
        }

        /// <summary>Renumbers setup and per-setup shot sequences to 1..n, preserving current order.</summary>
        public static void Resequence(PhotoShootDocument doc)
        {
            var setups = OrderedSetups(doc);
            for (var i = 0; i < setups.Count; i++)
            {
                setups[i].Sequence = i + 1;
            }

            foreach (var group in Groups(doc))
            {
                for (var i = 0; i < group.Shots.Count; i++)
                {
                    group.Shots[i].Sequence = i + 1;
                }
            }
        }

        /// <summary>
        /// Moves a shot one position up (delta &lt; 0) or down (delta &gt; 0). At a setup edge the shot
        /// crosses into the neighbouring setup, so the whole order can be changed with two buttons.
        /// </summary>
        public static bool MoveShot(PhotoShootDocument doc, string shotId, int delta)
        {
            var shot = FindShot(doc, shotId);
            if (shot == null || delta == 0)
            {
                return false;
            }

            Resequence(doc);
            var groups = Groups(doc).ToList();
            var groupIndex = groups.FindIndex(g => g.Shots.Any(s => s.Id == shotId));
            if (groupIndex < 0)
            {
                return false;
            }

            var group = groups[groupIndex];
            var indexInGroup = group.Shots.FindIndex(s => s.Id == shotId);

            if (delta < 0)
            {
                if (indexInGroup > 0)
                {
                    Swap(group.Shots[indexInGroup], group.Shots[indexInGroup - 1]);
                    return true;
                }

                if (groupIndex == 0 || groups[groupIndex - 1].Setup == null)
                {
                    return false;
                }

                var target = groups[groupIndex - 1];
                shot.SetupId = target.Key;
                shot.Sequence = target.Shots.Count == 0 ? 1 : target.Shots.Max(s => s.Sequence) + 1;
                Resequence(doc);
                return true;
            }

            if (indexInGroup < group.Shots.Count - 1)
            {
                Swap(group.Shots[indexInGroup], group.Shots[indexInGroup + 1]);
                return true;
            }

            if (groupIndex >= groups.Count - 1 || groups[groupIndex + 1].Setup == null)
            {
                return false;
            }

            var next = groups[groupIndex + 1];
            shot.SetupId = next.Key;
            shot.Sequence = 0;
            Resequence(doc);
            return true;
        }

        /// <summary>Moves the shot to the end of another setup (empty id = unassigned).</summary>
        public static bool MoveShotToSetup(PhotoShootDocument doc, string shotId, string? setupId)
        {
            var shot = FindShot(doc, shotId);
            var targetId = setupId ?? "";
            if (shot == null)
            {
                return false;
            }

            if (targetId.Length > 0 && FindSetup(doc, targetId) == null)
            {
                return false;
            }

            if (shot.SetupId == targetId)
            {
                return false;
            }

            var siblings = doc.Shots.Where(s => s.SetupId == targetId && s.Id != shotId).ToList();
            shot.SetupId = targetId;
            shot.Sequence = siblings.Count == 0 ? 1 : siblings.Max(s => s.Sequence) + 1;
            Resequence(doc);
            return true;
        }

        /// <summary>Places <paramref name="shotId"/> immediately after <paramref name="afterShotId"/> in the order.</summary>
        public static bool PlaceAfter(PhotoShootDocument doc, string shotId, string afterShotId)
        {
            var shot = FindShot(doc, shotId);
            var anchor = FindShot(doc, afterShotId);
            if (shot == null || anchor == null || shot.Id == anchor.Id)
            {
                return false;
            }

            Resequence(doc);
            var group = Groups(doc).First(g => g.Shots.Any(s => s.Id == anchor.Id));
            var list = group.Shots.Where(s => s.Id != shot.Id).ToList();
            var anchorIndex = list.FindIndex(s => s.Id == anchor.Id);
            list.Insert(anchorIndex + 1, shot);
            shot.SetupId = anchor.SetupId;
            for (var i = 0; i < list.Count; i++)
            {
                list[i].Sequence = i + 1;
            }

            Resequence(doc);
            return true;
        }

        public static bool MoveSetup(PhotoShootDocument doc, string setupId, int delta)
        {
            Resequence(doc);
            var setups = OrderedSetups(doc).ToList();
            var index = setups.FindIndex(s => s.Id == setupId);
            var target = index + Math.Sign(delta);
            if (index < 0 || delta == 0 || target < 0 || target >= setups.Count)
            {
                return false;
            }

            var a = setups[index];
            var b = setups[target];
            var seq = a.Sequence;
            a.Sequence = b.Sequence;
            b.Sequence = seq;
            return true;
        }

        public static PhotoShootProgress Progress(PhotoShootDocument doc)
        {
            var shots = doc.Shots ?? new List<PhotoShot>();
            return new PhotoShootProgress
            {
                Total = shots.Count,
                Completed = shots.Count(s => s.Status == ShotStatus.Completed),
                Skipped = shots.Count(s => s.Status == ShotStatus.Skipped),
                InProgress = shots.Count(s => s.Status == ShotStatus.InProgress),
                NotStarted = shots.Count(s => s.Status == ShotStatus.NotStarted),
                MustHaveTotal = shots.Count(s => s.Priority == ShotPriority.MustHave),
                MustHaveCompleted = shots.Count(s => s.Priority == ShotPriority.MustHave && s.Status == ShotStatus.Completed),
                Favorites = shots.Count(s => s.IsFavorite)
            };
        }

        /// <summary>Estimate for the whole plan, regardless of status.</summary>
        public static PhotoShootEstimate EstimateTotal(PhotoShootDocument doc) =>
            Estimate(doc, _ => true, currentSetupId: null);

        /// <summary>
        /// Estimate for what is left: every shot not completed (skipped shots count, since they are meant to be
        /// revisited), changeovers for setups that still have work (except the one currently set up), plus breaks.
        /// </summary>
        public static PhotoShootEstimate EstimateRemaining(PhotoShootDocument doc, bool mustHaveOnly = false)
        {
            var current = FindShot(doc, doc.Session?.CurrentShotId);
            var currentSetupId = doc.Session?.IsActive == true ? current?.SetupId : null;
            return Estimate(
                doc,
                s => s.Status != ShotStatus.Completed && (!mustHaveOnly || s.Priority == ShotPriority.MustHave),
                currentSetupId);
        }

        private static PhotoShootEstimate Estimate(PhotoShootDocument doc, Func<PhotoShot, bool> include, string? currentSetupId)
        {
            var estimate = new PhotoShootEstimate();
            foreach (var group in Groups(doc, includeEmptySetups: false))
            {
                var shots = group.Shots.Where(include).ToList();
                if (shots.Count == 0)
                {
                    continue;
                }

                estimate.ShotCount += shots.Count;
                estimate.ShootingMinutes += shots.Sum(s => Math.Max(0, s.EstimatedMinutes));
                if (group.Setup != null)
                {
                    estimate.SetupCount++;
                    if (group.Setup.Id != currentSetupId)
                    {
                        estimate.ChangeoverMinutes += Math.Max(0, group.Setup.ChangeoverMinutes);
                    }
                }
            }

            if (doc.BreakEveryMinutes > 0 && doc.BreakDurationMinutes > 0)
            {
                var working = estimate.ShootingMinutes + estimate.ChangeoverMinutes;
                estimate.BreakMinutes = (working / doc.BreakEveryMinutes) * doc.BreakDurationMinutes;
            }

            return estimate;
        }

        private static bool IsActionable(PhotoShot shot) =>
            shot.Status == ShotStatus.NotStarted || shot.Status == ShotStatus.InProgress;

        /// <summary>
        /// Next shot to shoot after <paramref name="fromShotId"/>: the next not-started/in-progress shot in order,
        /// wrapping to the start; when none remain, the first skipped shot. Null when everything is complete.
        /// </summary>
        public static PhotoShot? NextShot(PhotoShootDocument doc, string? fromShotId, bool mustHaveOnly = false)
        {
            var ordered = OrderedShots(doc)
                .Where(s => !mustHaveOnly || s.Priority == ShotPriority.MustHave || s.Id == fromShotId)
                .ToList();
            var start = string.IsNullOrEmpty(fromShotId) ? -1 : ordered.FindIndex(s => s.Id == fromShotId);

            for (var i = start + 1; i < ordered.Count; i++)
            {
                if (IsActionable(ordered[i]) && ordered[i].Id != fromShotId)
                {
                    return ordered[i];
                }
            }

            for (var i = 0; i <= start && i < ordered.Count; i++)
            {
                if (IsActionable(ordered[i]) && ordered[i].Id != fromShotId)
                {
                    return ordered[i];
                }
            }

            return ordered.FirstOrDefault(s => s.Status == ShotStatus.Skipped && s.Id != fromShotId);
        }

        public static PhotoShot? PreviousShot(PhotoShootDocument doc, string? fromShotId)
        {
            var ordered = OrderedShots(doc);
            var index = ordered.ToList().FindIndex(s => s.Id == fromShotId);
            return index > 0 ? ordered[index - 1] : null;
        }

        /// <summary>Neighbour in the plain order (used by the details page prev/next buttons).</summary>
        public static PhotoShot? AdjacentShot(PhotoShootDocument doc, string shotId, int delta)
        {
            var ordered = OrderedShots(doc).ToList();
            var index = ordered.FindIndex(s => s.Id == shotId);
            var target = index + Math.Sign(delta);
            return index < 0 || target < 0 || target >= ordered.Count ? null : ordered[target];
        }

        /// <summary>First remaining shot in the first later setup that still has remaining shots.</summary>
        public static PhotoShot? FirstShotOfNextSetup(PhotoShootDocument doc, string? fromShotId, bool mustHaveOnly = false)
        {
            var groups = Groups(doc, includeEmptySetups: false).ToList();
            var current = FindShot(doc, fromShotId);
            var startGroup = current == null ? -1 : groups.FindIndex(g => g.Shots.Any(s => s.Id == current.Id));

            for (var i = startGroup + 1; i < groups.Count; i++)
            {
                var candidate = groups[i].Shots.FirstOrDefault(s =>
                    IsActionable(s) && (!mustHaveOnly || s.Priority == ShotPriority.MustHave));
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Upcoming actionable shots after the current one, in order.</summary>
        public static IReadOnlyList<PhotoShot> Upcoming(PhotoShootDocument doc, string? fromShotId, int count, bool mustHaveOnly = false)
        {
            var ordered = OrderedShots(doc).ToList();
            var start = string.IsNullOrEmpty(fromShotId) ? -1 : ordered.FindIndex(s => s.Id == fromShotId);
            return ordered
                .Skip(start + 1)
                .Where(s => IsActionable(s) && (!mustHaveOnly || s.Priority == ShotPriority.MustHave))
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Suggested order: setups sharing a background family stay together; families and setups with more
        /// must-have shots go first; setups that need the children keep their slot (kids arrive at a fixed time);
        /// inside each setup, must-have shots come first, then standard, then bonus.
        /// </summary>
        public static void ApplySuggestedOrder(PhotoShootDocument doc)
        {
            Resequence(doc);
            var groups = Groups(doc, includeEmptySetups: true).Where(g => g.Setup != null).ToList();

            foreach (var group in Groups(doc))
            {
                var sorted = group.Shots
                    .Select((s, i) => new { Shot = s, Index = i })
                    .OrderBy(x => (int)x.Shot.Priority)
                    .ThenBy(x => x.Index)
                    .Select(x => x.Shot)
                    .ToList();
                for (var i = 0; i < sorted.Count; i++)
                {
                    sorted[i].Sequence = i + 1;
                }
            }

            var pinned = groups
                .Select((g, i) => new { Group = g, Index = i })
                .Where(x => x.Group.Setup!.RequiresChildren)
                .ToList();
            var movable = groups.Where(g => !g.Setup!.RequiresChildren).ToList();

            var families = movable
                .Select((g, i) => new { Group = g, Index = i, Family = BackgroundFamily(g.Setup!.Background) })
                .GroupBy(x => x.Family)
                .Select(f => new
                {
                    MustHave = f.Sum(x => x.Group.MustHaveCount),
                    FirstIndex = f.Min(x => x.Index),
                    Members = f.OrderByDescending(x => x.Group.MustHaveCount).ThenBy(x => x.Index).Select(x => x.Group).ToList()
                })
                .OrderByDescending(f => f.MustHave)
                .ThenBy(f => f.FirstIndex)
                .SelectMany(f => f.Members)
                .ToList();

            foreach (var pin in pinned.OrderBy(p => p.Index))
            {
                families.Insert(Math.Min(pin.Index, families.Count), pin.Group);
            }

            for (var i = 0; i < families.Count; i++)
            {
                families[i].Setup!.Sequence = i + 1;
            }

            Resequence(doc);
        }

        private static readonly string[] BackgroundColorWords =
        {
            "white", "grey", "gray", "black", "charcoal", "teal", "burgundy", "gold", "pink", "blue", "green", "red", "yellow", "purple"
        };

        /// <summary>Coarse background grouping key ("white seamless paper" and "white sweep" are the same family).</summary>
        public static string BackgroundFamily(string? background)
        {
            var text = (background ?? "").Trim().ToLowerInvariant();
            if (text.Length == 0)
            {
                return "";
            }

            var words = text.Split(new[] { ' ', ',', '-', '/', '(', ')', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (word == "grey")
                {
                    return "gray";
                }

                if (BackgroundColorWords.Contains(word))
                {
                    return word;
                }
            }

            return text;
        }

        private static void Swap(PhotoShot a, PhotoShot b)
        {
            var seq = a.Sequence;
            a.Sequence = b.Sequence;
            b.Sequence = seq;
        }
    }
}
