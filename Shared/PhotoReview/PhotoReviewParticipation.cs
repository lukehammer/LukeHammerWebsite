using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BlazorApp.Shared.PhotoReview
{
    public class ReviewResult
    {
        public bool Ok => Status == 0;
        /// <summary>0 on success, otherwise an HTTP status code.</summary>
        public int Status { get; set; }
        public string Error { get; set; } = "";
        public bool Changed { get; set; }
        public string CreatedId { get; set; } = "";

        public static ReviewResult Done(bool changed = true, string createdId = "") => new ReviewResult { Changed = changed, CreatedId = createdId };
        public static ReviewResult Fail(int status, string error) => new ReviewResult { Status = status, Error = error };
        public static ReviewResult BadRequest(string error) => Fail(400, error);
        public static ReviewResult Forbidden(string error) => Fail(403, error);
        public static ReviewResult NotFound(string what) => Fail(404, what + " not found.");
    }

    /// <summary>Who is acting. Visitor id comes only from a verified cookie; admin only from the bound Microsoft account.</summary>
    public class ReviewActor
    {
        public string VisitorId { get; set; } = "";
        public bool IsAdmin { get; set; }
        public string AdminName { get; set; } = "";
    }

    /// <summary>Visitor identity, ratings, comments, suggestions and moderation. Pure, no I/O.</summary>
    public static class PhotoReviewParticipation
    {
        public const int MinRating = 1;
        public const int MaxRating = 10;
        public const int MaxNameLength = 40;
        public const int MaxCommentLength = 2000;
        public const int MaxTitleLength = 120;
        public const int MaxDescriptionLength = 3000;
        public const int MaxClientRequestIdLength = 64;
        public const int MaxVisitors = 5000;
        public const int MaxCommentsPerVisitor = 400;
        public const int MaxSuggestionsPerVisitor = 200;
        public const int MaxThreadDepth = 6;
        public const string SuspendedMessage = "Your contributions are paused on this review. Contact Luke if you think this is a mistake.";

        // ---------- ids and secrets ----------

        public static string NewId(int bytes = 12)
        {
            var buffer = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(buffer);
            }

            return Hex(buffer);
        }

        public static string HashSecret(string secret)
        {
            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(secret ?? "")));
            }
        }

        public static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }

        /// <summary>Constant-time comparison of two hex strings.</summary>
        public static bool SameHash(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            var diff = 0;
            for (var i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }

            return diff == 0;
        }

        public static bool IsHexId(string? value, int length) =>
            !string.IsNullOrEmpty(value) && value!.Length == length && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

        /// <summary>Cookie value is "{visitorId}.{secret}". Returns false for anything malformed.</summary>
        public static bool TryParseToken(string? token, out string visitorId, out string secret)
        {
            visitorId = "";
            secret = "";
            var text = (token ?? "").Trim();
            var dot = text.IndexOf('.');
            if (dot <= 0)
            {
                return false;
            }

            var id = text.Substring(0, dot);
            var key = text.Substring(dot + 1);
            if (!IsHexId(id, 24) || !IsHexId(key, 64))
            {
                return false;
            }

            visitorId = id;
            secret = key;
            return true;
        }

        /// <summary>Returns the visitor only when the secret matches its stored hash.</summary>
        public static Visitor? Authenticate(ParticipationDocument doc, string? token)
        {
            if (!TryParseToken(token, out var id, out var secret))
            {
                return null;
            }

            var visitor = doc.Visitors.FirstOrDefault(item => item.Id == id);
            return visitor != null && SameHash(visitor.SecretHash, HashSecret(secret)) ? visitor : null;
        }

        public static string CleanName(string? name)
        {
            var text = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            while (text.Contains("  "))
            {
                text = text.Replace("  ", " ");
            }

            return text;
        }

        public static string CleanText(string? text) =>
            new string((text ?? "").Where(c => c == '\n' || c == '\t' || !char.IsControl(c)).ToArray()).Replace("\r", "").Trim();

        // ---------- visitors ----------

        /// <summary>Creates a visitor and returns the secret for the cookie. Only the hash is stored.</summary>
        public static ReviewResult Register(ParticipationDocument doc, string? displayName, string visitorId, string secret, DateTime nowUtc)
        {
            var name = CleanName(displayName);
            if (name.Length == 0)
            {
                return ReviewResult.BadRequest("Enter a display name.");
            }

            if (name.Length > MaxNameLength)
            {
                return ReviewResult.BadRequest($"Display names can be up to {MaxNameLength} characters.");
            }

            if (doc.Visitors.Count >= MaxVisitors)
            {
                return ReviewResult.Fail(503, "The review has reached its participant limit.");
            }

            if (!IsHexId(visitorId, 24) || !IsHexId(secret, 64) || doc.Visitors.Any(item => item.Id == visitorId))
            {
                return ReviewResult.BadRequest("Could not create a visitor id.");
            }

            doc.Visitors.Add(new Visitor
            {
                Id = visitorId,
                DisplayName = name,
                SecretHash = HashSecret(secret),
                CreatedUtc = nowUtc,
                LastActiveUtc = nowUtc
            });
            return ReviewResult.Done(createdId: visitorId);
        }

        public static ReviewResult Rename(ParticipationDocument doc, ReviewActor actor, string? displayName, DateTime nowUtc)
        {
            var visitor = ActiveVisitor(doc, actor, out var error);
            if (visitor == null)
            {
                return error!;
            }

            var name = CleanName(displayName);
            if (name.Length == 0 || name.Length > MaxNameLength)
            {
                return ReviewResult.BadRequest($"Display names need 1 to {MaxNameLength} characters.");
            }

            visitor.DisplayName = name;
            visitor.LastActiveUtc = nowUtc;
            return ReviewResult.Done();
        }

        /// <summary>Visitor who may contribute. Removed visitors get 403; unknown callers get 401.</summary>
        public static Visitor? ActiveVisitor(ParticipationDocument doc, ReviewActor actor, out ReviewResult? error)
        {
            error = null;
            var visitor = string.IsNullOrEmpty(actor?.VisitorId) ? null : doc.Visitors.FirstOrDefault(item => item.Id == actor!.VisitorId);
            if (visitor == null)
            {
                error = ReviewResult.Fail(401, "Enter a display name to take part.");
                return null;
            }

            if (visitor.RemovedUtc != null)
            {
                error = ReviewResult.Forbidden(SuspendedMessage);
                return null;
            }

            return visitor;
        }

        // ---------- ratings ----------

        /// <summary>Sets the caller's single current rating for a photo. Repeating the same value is a no-op.</summary>
        public static ReviewResult Rate(ParticipationDocument doc, ReviewActor actor, string photoId, int value, Func<string, bool> photoExists, DateTime nowUtc)
        {
            var visitor = ActiveVisitor(doc, actor, out var error);
            if (visitor == null)
            {
                return error!;
            }

            if (value < MinRating || value > MaxRating)
            {
                return ReviewResult.BadRequest($"Ratings go from {MinRating} to {MaxRating}.");
            }

            if (!photoExists(photoId))
            {
                return ReviewResult.NotFound("Photo");
            }

            visitor.LastActiveUtc = nowUtc;
            var vote = doc.Votes.FirstOrDefault(item => item.VisitorId == visitor.Id && item.PhotoId == photoId && !item.IsRemoved);
            if (vote == null)
            {
                doc.Votes.Add(new PhotoVote { VisitorId = visitor.Id, PhotoId = photoId, Value = value, CreatedUtc = nowUtc });
            }
            else if (vote.Value == value)
            {
                return ReviewResult.Done(changed: false);
            }
            else
            {
                vote.Value = value;
                vote.UpdatedUtc = nowUtc;
            }

            RecalculateTotal(doc, photoId);
            return ReviewResult.Done();
        }

        public static ReviewResult ClearRating(ParticipationDocument doc, ReviewActor actor, string photoId, DateTime nowUtc)
        {
            var visitor = ActiveVisitor(doc, actor, out var error);
            if (visitor == null)
            {
                return error!;
            }

            var removed = doc.Votes.RemoveAll(item => item.VisitorId == visitor.Id && item.PhotoId == photoId && !item.IsRemoved);
            visitor.LastActiveUtc = nowUtc;
            if (removed == 0)
            {
                return ReviewResult.Done(changed: false);
            }

            RecalculateTotal(doc, photoId);
            return ReviewResult.Done();
        }

        public static void RecalculateTotal(ParticipationDocument doc, string photoId)
        {
            var active = doc.Votes.Where(item => item.PhotoId == photoId && !item.IsRemoved).ToList();
            if (active.Count == 0)
            {
                doc.Totals.Remove(photoId);
                return;
            }

            doc.Totals[photoId] = new RatingTotal { Sum = active.Sum(item => (long)item.Value), Count = active.Count };
        }

        public static void RecalculateAllTotals(ParticipationDocument doc)
        {
            doc.Totals = doc.Votes
                .Where(item => !item.IsRemoved)
                .GroupBy(item => item.PhotoId)
                .ToDictionary(group => group.Key, group => new RatingTotal { Sum = group.Sum(item => (long)item.Value), Count = group.Count() });
        }

        // ---------- comments ----------

        public static ReviewResult AddComment(
            ParticipationDocument doc,
            ReviewActor actor,
            CommentTarget target,
            string targetId,
            string? parentId,
            string? text,
            string? clientRequestId,
            Func<CommentTarget, string, bool> targetExists,
            DateTime nowUtc)
        {
            var visitor = ActiveVisitor(doc, actor, out var error);
            if (visitor == null)
            {
                return error!;
            }

            var requestId = (clientRequestId ?? "").Trim();
            if (requestId.Length > MaxClientRequestIdLength)
            {
                return ReviewResult.BadRequest("Request id is too long.");
            }

            if (requestId.Length > 0)
            {
                var existing = doc.Comments.FirstOrDefault(item => item.VisitorId == visitor.Id && item.ClientRequestId == requestId);
                if (existing != null)
                {
                    return ReviewResult.Done(changed: false, createdId: existing.Id);
                }
            }

            var body = CleanText(text);
            if (body.Length == 0)
            {
                return ReviewResult.BadRequest("Write a comment first.");
            }

            if (body.Length > MaxCommentLength)
            {
                return ReviewResult.BadRequest($"Comments can be up to {MaxCommentLength} characters.");
            }

            var parent = "";
            if (!string.IsNullOrEmpty(parentId))
            {
                var parentComment = doc.Comments.FirstOrDefault(item => item.Id == parentId);
                if (parentComment == null || parentComment.Target != target || parentComment.TargetId != targetId)
                {
                    return ReviewResult.NotFound("Comment being replied to");
                }

                if (Depth(doc, parentComment) >= MaxThreadDepth)
                {
                    return ReviewResult.BadRequest("This thread is as deep as it can go. Reply further up instead.");
                }

                parent = parentComment.Id;
            }
            else if (!targetExists(target, targetId))
            {
                return ReviewResult.NotFound(target == CommentTarget.Suggestion ? "Suggestion" : target == CommentTarget.Shot ? "Shot" : "Photo");
            }

            if (doc.Comments.Count(item => item.VisitorId == visitor.Id) >= MaxCommentsPerVisitor)
            {
                return ReviewResult.BadRequest("You've reached the comment limit for this review.");
            }

            var comment = new ReviewComment
            {
                Id = NewId(),
                VisitorId = visitor.Id,
                Target = target,
                TargetId = targetId,
                ParentId = parent,
                Text = body,
                ClientRequestId = requestId,
                CreatedUtc = nowUtc,
                ByOwner = actor!.IsAdmin
            };
            doc.Comments.Add(comment);
            visitor.LastActiveUtc = nowUtc;
            return ReviewResult.Done(createdId: comment.Id);
        }

        public static ReviewResult EditComment(ParticipationDocument doc, ReviewActor actor, string commentId, string? text, DateTime nowUtc)
        {
            var visitor = ActiveVisitor(doc, actor, out var error);
            if (visitor == null)
            {
                return error!;
            }

            var comment = doc.Comments.FirstOrDefault(item => item.Id == commentId && !item.IsRemoved && item.DeletedUtc == null);
            if (comment == null)
            {
                return ReviewResult.NotFound("Comment");
            }

            if (comment.VisitorId != visitor.Id)
            {
                return ReviewResult.Forbidden("You can only edit your own comments.");
            }

            var body = CleanText(text);
            if (body.Length == 0 || body.Length > MaxCommentLength)
            {
                return ReviewResult.BadRequest($"Comments need 1 to {MaxCommentLength} characters.");
            }

            if (body == comment.Text)
            {
                return ReviewResult.Done(changed: false);
            }

            comment.Text = body;
            comment.UpdatedUtc = nowUtc;
            visitor.LastActiveUtc = nowUtc;
            return ReviewResult.Done();
        }

        /// <summary>Author withdraws their own comment. Replies from others stay under a placeholder.</summary>
        public static ReviewResult DeleteComment(ParticipationDocument doc, ReviewActor actor, string commentId, DateTime nowUtc)
        {
            var visitor = ActiveVisitor(doc, actor, out var error);
            if (visitor == null)
            {
                return error!;
            }

            var comment = doc.Comments.FirstOrDefault(item => item.Id == commentId && !item.IsRemoved);
            if (comment == null)
            {
                return ReviewResult.NotFound("Comment");
            }

            if (comment.VisitorId != visitor.Id)
            {
                return ReviewResult.Forbidden("You can only remove your own comments.");
            }

            if (comment.DeletedUtc != null)
            {
                return ReviewResult.Done(changed: false);
            }

            comment.DeletedUtc = nowUtc;
            visitor.LastActiveUtc = nowUtc;
            return ReviewResult.Done();
        }

        private static int Depth(ParticipationDocument doc, ReviewComment comment)
        {
            var depth = 1;
            var current = comment;
            var seen = new HashSet<string>();
            while (!string.IsNullOrEmpty(current.ParentId) && seen.Add(current.Id))
            {
                var parent = doc.Comments.FirstOrDefault(item => item.Id == current.ParentId);
                if (parent == null)
                {
                    break;
                }

                depth++;
                current = parent;
            }

            return depth;
        }

        // ---------- suggestions ----------

        public static ReviewResult AddSuggestion(
            ParticipationDocument doc,
            ReviewActor actor,
            SuggestionKind kind,
            string? shotId,
            ShotReviewStatus? suggestedStatus,
            string? title,
            string? description,
            string? clientRequestId,
            Func<string, bool> shotExists,
            DateTime nowUtc)
        {
            var visitor = ActiveVisitor(doc, actor, out var error);
            if (visitor == null)
            {
                return error!;
            }

            var requestId = (clientRequestId ?? "").Trim();
            if (requestId.Length > MaxClientRequestIdLength)
            {
                return ReviewResult.BadRequest("Request id is too long.");
            }

            if (requestId.Length > 0)
            {
                var existing = doc.Suggestions.FirstOrDefault(item => item.VisitorId == visitor.Id && item.ClientRequestId == requestId);
                if (existing != null)
                {
                    return ReviewResult.Done(changed: false, createdId: existing.Id);
                }
            }

            var validation = ValidateSuggestion(kind, shotId, suggestedStatus, title, description, shotExists, out var cleanTitle, out var cleanDescription);
            if (validation != null)
            {
                return validation;
            }

            if (doc.Suggestions.Count(item => item.VisitorId == visitor.Id) >= MaxSuggestionsPerVisitor)
            {
                return ReviewResult.BadRequest("You've reached the suggestion limit for this review.");
            }

            var suggestion = new PoseSuggestion
            {
                Id = NewId(),
                VisitorId = visitor.Id,
                Kind = kind,
                ShotId = kind == SuggestionKind.NewPose ? (shotId ?? "").Trim() : shotId!.Trim(),
                SuggestedStatus = kind == SuggestionKind.StatusChange ? suggestedStatus : null,
                Title = cleanTitle,
                Description = cleanDescription,
                ClientRequestId = requestId,
                CreatedUtc = nowUtc,
                ByOwner = actor!.IsAdmin
            };
            doc.Suggestions.Add(suggestion);
            visitor.LastActiveUtc = nowUtc;
            return ReviewResult.Done(createdId: suggestion.Id);
        }

        public static ReviewResult EditSuggestion(
            ParticipationDocument doc,
            ReviewActor actor,
            string suggestionId,
            ShotReviewStatus? suggestedStatus,
            string? title,
            string? description,
            Func<string, bool> shotExists,
            DateTime nowUtc)
        {
            var visitor = ActiveVisitor(doc, actor, out var error);
            if (visitor == null)
            {
                return error!;
            }

            var suggestion = doc.Suggestions.FirstOrDefault(item => item.Id == suggestionId && !item.IsRemoved && item.DeletedUtc == null);
            if (suggestion == null)
            {
                return ReviewResult.NotFound("Suggestion");
            }

            if (suggestion.VisitorId != visitor.Id)
            {
                return ReviewResult.Forbidden("You can only edit your own suggestions.");
            }

            if (suggestion.State != SuggestionState.Open)
            {
                return ReviewResult.Fail(409, "Luke has already reviewed this suggestion, so it can't be edited. Add a reply instead.");
            }

            var validation = ValidateSuggestion(suggestion.Kind, suggestion.ShotId, suggestedStatus, title, description, shotExists, out var cleanTitle, out var cleanDescription);
            if (validation != null)
            {
                return validation;
            }

            suggestion.Title = cleanTitle;
            suggestion.Description = cleanDescription;
            suggestion.SuggestedStatus = suggestion.Kind == SuggestionKind.StatusChange ? suggestedStatus : null;
            suggestion.UpdatedUtc = nowUtc;
            visitor.LastActiveUtc = nowUtc;
            return ReviewResult.Done();
        }

        public static ReviewResult DeleteSuggestion(ParticipationDocument doc, ReviewActor actor, string suggestionId, DateTime nowUtc)
        {
            var visitor = ActiveVisitor(doc, actor, out var error);
            if (visitor == null)
            {
                return error!;
            }

            var suggestion = doc.Suggestions.FirstOrDefault(item => item.Id == suggestionId && !item.IsRemoved);
            if (suggestion == null)
            {
                return ReviewResult.NotFound("Suggestion");
            }

            if (suggestion.VisitorId != visitor.Id)
            {
                return ReviewResult.Forbidden("You can only withdraw your own suggestions.");
            }

            if (suggestion.DeletedUtc != null)
            {
                return ReviewResult.Done(changed: false);
            }

            suggestion.DeletedUtc = nowUtc;
            visitor.LastActiveUtc = nowUtc;
            return ReviewResult.Done();
        }

        /// <summary>Admin decision. Approval puts the idea in the second-shoot queue; it never edits the planner.</summary>
        public static ReviewResult DecideSuggestion(ParticipationDocument doc, ReviewActor actor, string suggestionId, SuggestionState state, string? note, DateTime nowUtc)
        {
            if (actor == null || !actor.IsAdmin)
            {
                return ReviewResult.Forbidden("Only Luke can approve or decline suggestions.");
            }

            var suggestion = doc.Suggestions.FirstOrDefault(item => item.Id == suggestionId && !item.IsRemoved && item.DeletedUtc == null);
            if (suggestion == null)
            {
                return ReviewResult.NotFound("Suggestion");
            }

            suggestion.State = state;
            suggestion.StateNote = CleanText(note);
            if (suggestion.StateNote.Length > MaxCommentLength)
            {
                suggestion.StateNote = suggestion.StateNote.Substring(0, MaxCommentLength);
            }

            suggestion.StateUtc = nowUtc;
            return ReviewResult.Done();
        }

        private static ReviewResult? ValidateSuggestion(
            SuggestionKind kind,
            string? shotId,
            ShotReviewStatus? suggestedStatus,
            string? title,
            string? description,
            Func<string, bool> shotExists,
            out string cleanTitle,
            out string cleanDescription)
        {
            cleanTitle = CleanName(title);
            cleanDescription = CleanText(description);
            if (!Enum.IsDefined(typeof(SuggestionKind), kind))
            {
                return ReviewResult.BadRequest("Unknown suggestion type.");
            }

            var shot = (shotId ?? "").Trim();
            if (kind != SuggestionKind.NewPose && (shot.Length == 0 || !shotExists(shot)))
            {
                return ReviewResult.NotFound("Planned shot");
            }

            if (kind == SuggestionKind.NewPose && shot.Length > 0 && !shotExists(shot))
            {
                return ReviewResult.NotFound("Related planned shot");
            }

            if (kind == SuggestionKind.StatusChange && (suggestedStatus == null || !Enum.IsDefined(typeof(ShotReviewStatus), suggestedStatus.Value)))
            {
                return ReviewResult.BadRequest("Pick the status you suggest.");
            }

            if (kind == SuggestionKind.NewPose && cleanTitle.Length == 0)
            {
                return ReviewResult.BadRequest("Give the new pose a short name.");
            }

            if (cleanTitle.Length > MaxTitleLength)
            {
                return ReviewResult.BadRequest($"Titles can be up to {MaxTitleLength} characters.");
            }

            if (kind != SuggestionKind.StatusChange && cleanDescription.Length == 0)
            {
                return ReviewResult.BadRequest("Describe the pose.");
            }

            if (cleanDescription.Length > MaxDescriptionLength)
            {
                return ReviewResult.BadRequest($"Descriptions can be up to {MaxDescriptionLength} characters.");
            }

            return null;
        }

        // ---------- moderation ----------

        /// <summary>
        /// Soft-removes everything a visitor contributed and blocks further submissions from that identity.
        /// Ratings totals are recalculated; other people's replies stay under a placeholder.
        /// </summary>
        public static ReviewResult RemovePerson(ParticipationDocument doc, ReviewActor actor, string visitorId, string? note, DateTime nowUtc)
        {
            if (actor == null || !actor.IsAdmin)
            {
                return ReviewResult.Forbidden("Only Luke can remove people.");
            }

            var visitor = doc.Visitors.FirstOrDefault(item => item.Id == visitorId);
            if (visitor == null)
            {
                return ReviewResult.NotFound("Person");
            }

            if (visitor.RemovedUtc != null)
            {
                return ReviewResult.Done(changed: false);
            }

            var removalId = NewId(8);
            var votes = Mark(doc.Votes, visitorId, removalId, nowUtc);
            var comments = Mark(doc.Comments, visitorId, removalId, nowUtc);
            var suggestions = Mark(doc.Suggestions, visitorId, removalId, nowUtc);
            visitor.RemovedUtc = nowUtc;
            visitor.RemovalId = removalId;
            RecalculateAllTotals(doc);
            doc.Audit.Add(new AuditEntry
            {
                Id = NewId(8),
                Utc = nowUtc,
                Actor = actor.AdminName,
                Action = "RemovePerson",
                VisitorId = visitorId,
                RemovalId = removalId,
                Votes = votes,
                Comments = comments,
                Suggestions = suggestions,
                Note = CleanText(note)
            });
            return ReviewResult.Done(createdId: removalId);
        }

        /// <summary>Undoes exactly what the matching removal did. Items the author deleted themselves stay deleted.</summary>
        public static ReviewResult RestorePerson(ParticipationDocument doc, ReviewActor actor, string visitorId, string? note, DateTime nowUtc)
        {
            if (actor == null || !actor.IsAdmin)
            {
                return ReviewResult.Forbidden("Only Luke can restore people.");
            }

            var visitor = doc.Visitors.FirstOrDefault(item => item.Id == visitorId);
            if (visitor == null)
            {
                return ReviewResult.NotFound("Person");
            }

            if (visitor.RemovedUtc == null)
            {
                return ReviewResult.Done(changed: false);
            }

            var removalId = visitor.RemovalId;
            var votes = 0;
            foreach (var vote in doc.Votes.Where(item => item.VisitorId == visitorId && item.RemovalId == removalId).ToList())
            {
                // A visitor has one current vote per photo; if a newer one exists keep it and drop the old one.
                if (doc.Votes.Any(item => item != vote && item.VisitorId == visitorId && item.PhotoId == vote.PhotoId && !item.IsRemoved))
                {
                    doc.Votes.Remove(vote);
                    continue;
                }

                vote.RemovedUtc = null;
                vote.RemovalId = "";
                votes++;
            }

            var comments = Unmark(doc.Comments, visitorId, removalId);
            var suggestions = Unmark(doc.Suggestions, visitorId, removalId);
            visitor.RemovedUtc = null;
            visitor.RemovalId = "";
            RecalculateAllTotals(doc);
            doc.Audit.Add(new AuditEntry
            {
                Id = NewId(8),
                Utc = nowUtc,
                Actor = actor.AdminName,
                Action = "RestorePerson",
                VisitorId = visitorId,
                RemovalId = removalId,
                Votes = votes,
                Comments = comments,
                Suggestions = suggestions,
                Note = CleanText(note)
            });
            return ReviewResult.Done();
        }

        private static int Mark<T>(List<T> items, string visitorId, string removalId, DateTime nowUtc) where T : Contribution
        {
            var count = 0;
            foreach (var item in items.Where(entry => entry.VisitorId == visitorId && !entry.IsRemoved))
            {
                item.RemovedUtc = nowUtc;
                item.RemovalId = removalId;
                count++;
            }

            return count;
        }

        private static int Unmark<T>(List<T> items, string visitorId, string removalId) where T : Contribution
        {
            var count = 0;
            foreach (var item in items.Where(entry => entry.VisitorId == visitorId && entry.RemovalId == removalId))
            {
                item.RemovedUtc = null;
                item.RemovalId = "";
                count++;
            }

            return count;
        }

        /// <summary>
        /// Hard-deletes clearly labeled test visitors (display name starts with <paramref name="namePrefix"/>) and
        /// everything they contributed, then recalculates totals. Used only by the local cleanup tool after
        /// production smoke tests; never exposed through the API. Returns the number of visitors purged.
        /// </summary>
        public static int PurgeTestVisitors(ParticipationDocument doc, string namePrefix)
        {
            if (string.IsNullOrWhiteSpace(namePrefix) || namePrefix.Trim().Length < 8)
            {
                throw new ArgumentException("Use a long, explicit test prefix.", nameof(namePrefix));
            }

            var ids = new HashSet<string>(doc.Visitors.Where(v => v.DisplayName.StartsWith(namePrefix, StringComparison.Ordinal)).Select(v => v.Id));
            if (ids.Count == 0)
            {
                return 0;
            }

            var suggestionIds = new HashSet<string>(doc.Suggestions.Where(s => ids.Contains(s.VisitorId)).Select(s => s.Id));
            doc.Visitors.RemoveAll(v => ids.Contains(v.Id));
            doc.Votes.RemoveAll(v => ids.Contains(v.VisitorId));
            doc.Suggestions.RemoveAll(s => ids.Contains(s.VisitorId));
            doc.Comments.RemoveAll(c => ids.Contains(c.VisitorId) || (c.Target == CommentTarget.Suggestion && suggestionIds.Contains(c.TargetId)));
            doc.Audit.RemoveAll(a => ids.Contains(a.VisitorId));
            RecalculateAllTotals(doc);
            return ids.Count;
        }

        /// <summary>Repairs nulls after deserialization.</summary>
        public static ParticipationDocument Normalize(ParticipationDocument doc)
        {
            doc.Visitors ??= new List<Visitor>();
            doc.Votes ??= new List<PhotoVote>();
            doc.Comments ??= new List<ReviewComment>();
            doc.Suggestions ??= new List<PoseSuggestion>();
            doc.Audit ??= new List<AuditEntry>();
            doc.Totals ??= new Dictionary<string, RatingTotal>();
            return doc;
        }
    }
}
