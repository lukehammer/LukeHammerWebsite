using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared
{
    public class FamilyEvent
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime Date { get; set; }
        /// <summary>Last day of a multi-day event; null for single-day events.</summary>
        public DateTime? EndDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public string Location { get; set; } = "";
        public string Description { get; set; } = "";
        public bool ThemeVotingEnabled { get; set; } = true;
        /// <summary>Label for the "what are you bringing" field; blank uses the default.</summary>
        public string BringPrompt { get; set; } = "";
        public bool RsvpEnabled { get; set; }
        /// <summary>Number of families that need to RSVP "going".</summary>
        public int? RsvpGoal { get; set; }
        public DateTime? RsvpDeadline { get; set; }
        public List<FamilyEventRsvp> Rsvps { get; set; } = new List<FamilyEventRsvp>();
        public List<FamilyEventSignupList> SignupLists { get; set; } = new List<FamilyEventSignupList>();
        public PotluckSurveyState Potluck { get; set; } = new PotluckSurveyState();
        public List<FamilyEventQuestion> Questions { get; set; } = new List<FamilyEventQuestion>();
        public DateTimeOffset? LastModifiedAt { get; set; }

        [JsonIgnore]
        public DateTime LastDay => (EndDate ?? Date).Date;

        [JsonIgnore]
        public bool IsMultiDay => LastDay > Date.Date;
    }

    public static class FamilyRsvpStatus
    {
        public const string Going = "going";
        public const string Maybe = "maybe";
        public const string No = "no";

        public static bool IsValid(string status) =>
            status == Going || status == Maybe || status == No;
    }

    public class FamilyEventRsvp
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = FamilyRsvpStatus.Going;
        /// <summary>People coming with this RSVP; null when not given.</summary>
        public int? Headcount { get; set; }
        /// <summary>Who is coming; at most <see cref="Headcount"/> entries.</summary>
        public List<FamilyEventAttendee> Attendees { get; set; } = new List<FamilyEventAttendee>();
        public string Note { get; set; } = "";
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public class FamilyEventAttendee
    {
        public string Name { get; set; } = "";
        /// <summary>See <see cref="FamilyAttendeeAge"/>: blank, "adult", or a kid's age.</summary>
        public string Age { get; set; } = "";
    }

    public static class FamilyAttendeeAge
    {
        public const string Adult = "adult";
        public const int MaxKidAge = 17;

        public static bool IsValid(string age) =>
            age.Length == 0 || age == Adult || (int.TryParse(age, out var years) && years >= 0 && years <= MaxKidAge && age == years.ToString());

        public static bool IsKid(string age) => age.Length > 0 && age != Adult && IsValid(age);

        public static string Label(string age) =>
            age == Adult ? "adult"
            : age == "0" ? "under 1"
            : IsKid(age) ? $"age {age}"
            : "";
    }

    public class FamilyEventRsvpRequest
    {
        public string Name { get; set; } = "";
        /// <summary>One of <see cref="FamilyRsvpStatus"/>, or blank to remove the RSVP.</summary>
        public string Status { get; set; } = "";
        public int? Headcount { get; set; }
        public List<FamilyEventAttendee> Attendees { get; set; } = new List<FamilyEventAttendee>();
        public string Note { get; set; } = "";
    }

    public class FamilyEventSignupList
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Prompt { get; set; } = "";
        public List<FamilyEventSignupEntry> Entries { get; set; } = new List<FamilyEventSignupEntry>();
    }

    public class FamilyEventSignupEntry
    {
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public class FamilyEventQuestion
    {
        public string Id { get; set; } = "";
        public string AskedBy { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTimeOffset AskedAt { get; set; }
        public List<FamilyEventAnswer> Answers { get; set; } = new List<FamilyEventAnswer>();
    }

    public class FamilyEventAnswer
    {
        public string Id { get; set; } = "";
        public string AnsweredBy { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTimeOffset AnsweredAt { get; set; }
    }

    /// <summary>Question, answer, or sign-up entry text; blank text on a sign-up removes the entry.</summary>
    public class FamilyEventPostRequest
    {
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// <summary>Deletes a question or answer; allowed for its author or Luke's organizer code.</summary>
    public class FamilyEventDeletePostRequest
    {
        public string Name { get; set; } = "";
    }

    public class FamilyEventsData
    {
        public List<FamilyEvent> Events { get; set; } = new List<FamilyEvent>();
        /// <summary>Built-in events already added once, so deleting one does not bring it back.</summary>
        public List<string> AppliedSeedIds { get; set; } = new List<string>();
    }

    /// <summary>API response: all events plus the server's Pacific date so the client applies the same lock rule.</summary>
    public class FamilyEventsResponse
    {
        public List<FamilyEvent> Events { get; set; } = new List<FamilyEvent>();
        public DateTime Today { get; set; }
    }

    public class FamilyEventDetailsRequest
    {
        public string SubmittedBy { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime Date { get; set; }
        public DateTime? EndDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public string Location { get; set; } = "";
        public string Description { get; set; } = "";
        public bool ThemeVotingEnabled { get; set; } = true;
        public string BringPrompt { get; set; } = "";
        public bool RsvpEnabled { get; set; }
        public int? RsvpGoal { get; set; }
        public DateTime? RsvpDeadline { get; set; }
        /// <summary>Sign-up list titles and prompts; existing lists are matched by title so their entries are kept.</summary>
        public List<FamilyEventSignupList> SignupLists { get; set; } = new List<FamilyEventSignupList>();
        /// <summary>Starting theme options; only used when creating an event.</summary>
        public List<string> ThemeOptions { get; set; } = new List<string>();
    }

    public sealed class FamilyRsvpSummary
    {
        public int GoingFamilies { get; set; }
        public int GoingPeople { get; set; }
        public int GoingAdults { get; set; }
        public int GoingKids { get; set; }
        public int MaybeFamilies { get; set; }
        public int NotGoingFamilies { get; set; }
    }

    public static class FamilyEventRules
    {
        /// <summary>Events stay editable through this many days after the event's last day (Pacific).</summary>
        public const int EditableDaysAfterEvent = 3;

        public const int MaxEventDays = 30;

        public const string LockedMessage =
            "This event has passed and can no longer be changed.";

        public const string LockedDateMessage =
            "That date is more than 3 days ago, so the event would already be locked. Choose a later date.";

        public static bool IsLocked(DateTime lastDay, DateTime today) =>
            today.Date > lastDay.Date.AddDays(EditableDaysAfterEvent);

        public static bool IsLocked(FamilyEvent familyEvent, DateTime today) => IsLocked(familyEvent.LastDay, today);

        public static bool CanDeletePost(string author, string requestedBy) =>
            !string.IsNullOrWhiteSpace(requestedBy)
            && (string.Equals(requestedBy.Trim(), ScheduleSubmitters.LukeAuthCode, StringComparison.Ordinal)
                || string.Equals(requestedBy.Trim(), author?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static bool IsPast(DateTime lastDay, DateTime today) =>
            lastDay.Date < today.Date;

        public static bool IsPast(FamilyEvent familyEvent, DateTime today) => IsPast(familyEvent.LastDay, today);

        public static List<FamilyEvent> Upcoming(IEnumerable<FamilyEvent> events, DateTime today) =>
            events
                .Where(familyEvent => !IsPast(familyEvent, today))
                .OrderBy(familyEvent => familyEvent.Date)
                .ThenBy(familyEvent => familyEvent.StartTime ?? TimeSpan.MaxValue)
                .ToList();

        public static List<FamilyEvent> Past(IEnumerable<FamilyEvent> events, DateTime today) =>
            events
                .Where(familyEvent => IsPast(familyEvent, today))
                .OrderByDescending(familyEvent => familyEvent.LastDay)
                .ThenByDescending(familyEvent => familyEvent.StartTime ?? TimeSpan.MinValue)
                .ToList();

        /// <summary>The next (or in-progress) event; falls back to the most recent past event.</summary>
        public static FamilyEvent? PickDefault(IEnumerable<FamilyEvent> events, DateTime today)
        {
            var list = events.ToList();
            return Upcoming(list, today).FirstOrDefault() ?? Past(list, today).FirstOrDefault();
        }

        public static FamilyRsvpSummary SummarizeRsvps(IEnumerable<FamilyEventRsvp> rsvps)
        {
            var list = rsvps.ToList();
            var going = list.Where(rsvp => rsvp.Status == FamilyRsvpStatus.Going).ToList();
            return new FamilyRsvpSummary
            {
                GoingFamilies = going.Count,
                GoingPeople = going.Sum(rsvp => rsvp.Headcount ?? 0),
                GoingAdults = going.Sum(rsvp => rsvp.Attendees?.Count(attendee => attendee.Age == FamilyAttendeeAge.Adult) ?? 0),
                GoingKids = going.Sum(rsvp => rsvp.Attendees?.Count(attendee => FamilyAttendeeAge.IsKid(attendee.Age)) ?? 0),
                MaybeFamilies = list.Count(rsvp => rsvp.Status == FamilyRsvpStatus.Maybe),
                NotGoingFamilies = list.Count(rsvp => rsvp.Status == FamilyRsvpStatus.No)
            };
        }
    }
}
