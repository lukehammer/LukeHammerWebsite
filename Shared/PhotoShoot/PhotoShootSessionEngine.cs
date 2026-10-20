using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.PhotoShoot
{
    public static class ShotStatusRules
    {
        /// <summary>Directors need flexibility (reopen, revisit skipped), so any change of status is allowed.</summary>
        public static bool IsAllowed(ShotStatus from, ShotStatus to) => from != to;

        /// <summary>Applies a status and keeps the timing fields consistent. Returns false when nothing changed.</summary>
        public static bool Apply(PhotoShot shot, ShotStatus to, DateTime nowUtc)
        {
            if (!IsAllowed(shot.Status, to))
            {
                return false;
            }

            switch (to)
            {
                case ShotStatus.InProgress:
                    shot.StartedUtc = nowUtc;
                    shot.CompletedUtc = null;
                    shot.ActualSeconds = null;
                    break;
                case ShotStatus.Completed:
                    shot.CompletedUtc = nowUtc;
                    shot.ActualSeconds = shot.StartedUtc.HasValue && shot.Status == ShotStatus.InProgress
                        ? Math.Max(0, (int)(nowUtc - shot.StartedUtc.Value).TotalSeconds)
                        : (int?)null;
                    if (shot.DeliverableStatus == DeliverableStatus.Planned)
                    {
                        shot.DeliverableStatus = DeliverableStatus.Captured;
                    }
                    break;
                case ShotStatus.NotStarted:
                case ShotStatus.Skipped:
                    shot.StartedUtc = null;
                    shot.CompletedUtc = null;
                    shot.ActualSeconds = null;
                    break;
            }

            shot.Status = to;
            shot.UpdatedUtc = nowUtc;
            return true;
        }
    }

    public class SessionResult
    {
        public bool Ok { get; set; }
        /// <summary>HTTP-style status for failures (400, 403, 404).</summary>
        public int ErrorStatus { get; set; }
        public string Message { get; set; } = "";

        public static SessionResult Success(string message = "") => new SessionResult { Ok = true, Message = message };
        public static SessionResult Fail(int status, string message) => new SessionResult { Ok = false, ErrorStatus = status, Message = message };
    }

    /// <summary>Applies director/photographer session commands to the persisted session state.</summary>
    public static class PhotoShootSessionEngine
    {
        public const int MaxNoteLength = 500;
        public const int MaxNotes = 300;

        private static readonly HashSet<SessionAction> PhotographerActions = new HashSet<SessionAction>
        {
            SessionAction.RequestNext,
            SessionAction.CompleteCurrent,
            SessionAction.AddNote
        };

        private static readonly HashSet<SessionAction> PhotographerAdvanceActions = new HashSet<SessionAction>
        {
            SessionAction.Next,
            SessionAction.Previous
        };

        public static bool IsAllowed(PhotoShootAccess access, SessionAction action, PhotoShootSession session)
        {
            switch (access)
            {
                case PhotoShootAccess.Owner:
                case PhotoShootAccess.Admin:
                    return true;
                case PhotoShootAccess.Photographer:
                    return PhotographerActions.Contains(action)
                        || (session.PhotographerCanAdvance && PhotographerAdvanceActions.Contains(action));
                default:
                    return false;
            }
        }

        public static SessionResult Apply(PhotoShootDocument doc, SessionCommand command, PhotoShootAccess access, string actor, DateTime nowUtc)
        {
            doc.Session ??= new PhotoShootSession();
            var session = doc.Session;

            if (!IsAllowed(access, command.Action, session))
            {
                return SessionResult.Fail(403, "You don't have permission for that action.");
            }

            var result = ApplyCore(doc, session, command, actor, nowUtc);
            if (result.Ok)
            {
                session.UpdatedUtc = nowUtc;
                session.UpdatedBy = actor ?? "";
            }

            return result;
        }

        private static SessionResult ApplyCore(PhotoShootDocument doc, PhotoShootSession session, SessionCommand command, string actor, DateTime now)
        {
            var current = PhotoShootPlanner.FindShot(doc, session.CurrentShotId);

            switch (command.Action)
            {
                case SessionAction.Start:
                    session.IsActive = true;
                    session.StartedUtc ??= now;
                    session.EndedUtc = null;
                    if (current == null)
                    {
                        var first = PhotoShootPlanner.NextShot(doc, null, session.MustHaveOnly);
                        if (first != null)
                        {
                            SetCurrent(doc, session, first, now);
                        }
                    }
                    else if (current.Status == ShotStatus.NotStarted)
                    {
                        ShotStatusRules.Apply(current, ShotStatus.InProgress, now);
                    }

                    return SessionResult.Success("Session started.");

                case SessionAction.End:
                    session.IsActive = false;
                    session.EndedUtc = now;
                    return SessionResult.Success("Session ended. Progress is saved.");

                case SessionAction.SetCurrent:
                {
                    var target = PhotoShootPlanner.FindShot(doc, command.ShotId);
                    if (target == null)
                    {
                        return SessionResult.Fail(404, "Shot not found.");
                    }

                    SetCurrent(doc, session, target, now);
                    return SessionResult.Success();
                }

                case SessionAction.Next:
                    return Advance(doc, session, current, now);

                case SessionAction.Previous:
                {
                    var previous = PhotoShootPlanner.PreviousShot(doc, current?.Id);
                    if (previous == null)
                    {
                        return SessionResult.Fail(400, "Already at the first shot.");
                    }

                    SetCurrent(doc, session, previous, now);
                    return SessionResult.Success();
                }

                case SessionAction.CompleteCurrent:
                    if (current == null)
                    {
                        return SessionResult.Fail(400, "There is no current shot.");
                    }

                    if (current.Status != ShotStatus.Completed)
                    {
                        ShotStatusRules.Apply(current, ShotStatus.Completed, now);
                    }

                    return Advance(doc, session, current, now, allowNone: true);

                case SessionAction.SkipCurrent:
                    if (current == null)
                    {
                        return SessionResult.Fail(400, "There is no current shot.");
                    }

                    ShotStatusRules.Apply(current, ShotStatus.Skipped, now);
                    return Advance(doc, session, current, now, allowNone: true);

                case SessionAction.CompleteShot:
                case SessionAction.SkipShot:
                case SessionAction.ReopenShot:
                {
                    var target = PhotoShootPlanner.FindShot(doc, command.ShotId);
                    if (target == null)
                    {
                        return SessionResult.Fail(404, "Shot not found.");
                    }

                    var status = command.Action == SessionAction.CompleteShot ? ShotStatus.Completed
                        : command.Action == SessionAction.SkipShot ? ShotStatus.Skipped
                        : (target.Id == session.CurrentShotId && session.IsActive ? ShotStatus.InProgress : ShotStatus.NotStarted);
                    ShotStatusRules.Apply(target, status, now);
                    return SessionResult.Success();
                }

                case SessionAction.NextSetup:
                {
                    var target = PhotoShootPlanner.FirstShotOfNextSetup(doc, current?.Id, session.MustHaveOnly);
                    if (target == null)
                    {
                        return SessionResult.Fail(400, "No later setup has remaining shots.");
                    }

                    SetCurrent(doc, session, target, now);
                    return SessionResult.Success();
                }

                case SessionAction.ShootNext:
                {
                    var target = PhotoShootPlanner.FindShot(doc, command.ShotId);
                    if (target == null)
                    {
                        return SessionResult.Fail(404, "Shot not found.");
                    }

                    if (current == null || current.Id == target.Id)
                    {
                        SetCurrent(doc, session, target, now);
                        return SessionResult.Success();
                    }

                    PhotoShootPlanner.PlaceAfter(doc, target.Id, current.Id);
                    return SessionResult.Success($"\"{target.Title}\" is up next.");
                }

                case SessionAction.RequestNext:
                    session.NextRequestedUtc = now;
                    session.NextRequestedBy = actor ?? "";
                    return SessionResult.Success("Director notified.");

                case SessionAction.ClearNextRequest:
                    session.NextRequestedUtc = null;
                    session.NextRequestedBy = "";
                    return SessionResult.Success();

                case SessionAction.AddNote:
                {
                    var text = (command.Text ?? "").Trim();
                    if (text.Length == 0)
                    {
                        return SessionResult.Fail(400, "Note is empty.");
                    }

                    if (text.Length > MaxNoteLength)
                    {
                        text = text.Substring(0, MaxNoteLength);
                    }

                    var shotId = string.IsNullOrEmpty(command.ShotId) ? session.CurrentShotId : command.ShotId;
                    if (!string.IsNullOrEmpty(shotId) && PhotoShootPlanner.FindShot(doc, shotId) == null)
                    {
                        return SessionResult.Fail(404, "Shot not found.");
                    }

                    session.Notes ??= new List<SessionNote>();
                    session.Notes.Add(new SessionNote
                    {
                        Id = PhotoShootIds.New(),
                        ShotId = shotId ?? "",
                        Text = text,
                        Author = actor ?? "",
                        CreatedUtc = now
                    });
                    if (session.Notes.Count > MaxNotes)
                    {
                        session.Notes.RemoveRange(0, session.Notes.Count - MaxNotes);
                    }

                    return SessionResult.Success("Note added.");
                }

                case SessionAction.SetPhotographerCanAdvance:
                    session.PhotographerCanAdvance = command.Flag;
                    return SessionResult.Success();

                case SessionAction.SetMustHaveOnly:
                    session.MustHaveOnly = command.Flag;
                    return SessionResult.Success();

                default:
                    return SessionResult.Fail(400, "Unknown action.");
            }
        }

        private static SessionResult Advance(PhotoShootDocument doc, PhotoShootSession session, PhotoShot? current, DateTime now, bool allowNone = false)
        {
            var next = PhotoShootPlanner.NextShot(doc, current?.Id, session.MustHaveOnly);
            if (next == null)
            {
                session.NextRequestedUtc = null;
                session.NextRequestedBy = "";
                return allowNone
                    ? SessionResult.Success("That was the last remaining shot!")
                    : SessionResult.Fail(400, "No remaining shots.");
            }

            SetCurrent(doc, session, next, now);
            return SessionResult.Success();
        }

        private static void SetCurrent(PhotoShootDocument doc, PhotoShootSession session, PhotoShot target, DateTime now)
        {
            var previous = PhotoShootPlanner.FindShot(doc, session.CurrentShotId);
            if (previous != null && previous.Id != target.Id && previous.Status == ShotStatus.InProgress)
            {
                // Leaving a shot without finishing it puts it back in the queue.
                ShotStatusRules.Apply(previous, ShotStatus.NotStarted, now);
            }

            session.CurrentShotId = target.Id;
            session.NextRequestedUtc = null;
            session.NextRequestedBy = "";
            if (session.IsActive && (target.Status == ShotStatus.NotStarted || target.Status == ShotStatus.Skipped))
            {
                ShotStatusRules.Apply(target, ShotStatus.InProgress, now);
            }
        }
    }

    public static class PhotoShootIds
    {
        public static string New() => Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>Ids are used in blob names and local file paths, so only allow a safe character set.</summary>
        public static bool IsValid(string? id)
        {
            if (string.IsNullOrEmpty(id) || id!.Length > 64)
            {
                return false;
            }

            foreach (var c in id)
            {
                var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
