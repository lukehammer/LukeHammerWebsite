using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared
{
    public class Event
    {
        public string Id { get; set; } = string.Empty;

        public DateTime Date { get; set; }

        public TimeSpan? StartTime { get; set; }

        public string Name { get; set; }

        public string Location { get; set; }

        public string Sport { get; set; } = SportNames.Football;

        public List<string> Kids { get; set; } = new();

        public string LastModifiedBy { get; set; } = string.Empty;

        public DateTimeOffset? LastModifiedAt { get; set; }

        /// <summary>Comma-separated kid names for display (alphabetical).</summary>
        [JsonIgnore]
        public string KidsDisplay
        {
            get
            {
                var kids = Kids ?? new List<string>();
                return kids.Count == 0
                    ? string.Empty
                    : string.Join(", ", kids.OrderBy(k => k, StringComparer.Ordinal));
            }
        }

        /// <summary>Primary kid for row theming when multiple kids are selected (first alphabetically among allowed names).</summary>
        [JsonIgnore]
        public string RowThemeKid =>
            (Kids ?? new List<string>())
                .Where(ScheduleKids.IsAllowed)
                .OrderBy(k => k, StringComparer.Ordinal)
                .FirstOrDefault()
            ?? string.Empty;

        public void ApplyLastModified(string submittedBy)
        {
            LastModifiedBy = submittedBy;
            LastModifiedAt = WashingtonScheduleTime.Now;
        }

        [JsonIgnore]
        public string LastUpdatedByDisplay =>
            string.IsNullOrWhiteSpace(LastModifiedBy) ? "—" : LastModifiedBy;

        [JsonIgnore]
        public string LastModifiedWhenDisplay =>
            WashingtonScheduleTime.FormatLastModified(LastModifiedAt);

        /// <summary>Who and when (for signed-in schedule admin view).</summary>
        [JsonIgnore]
        public string LastModifiedSummary
        {
            get
            {
                var who = LastUpdatedByDisplay;
                var when = LastModifiedWhenDisplay;
                if (who == "—" && when == "—")
                {
                    return "—";
                }

                if (who == "—")
                {
                    return when;
                }

                if (when == "—")
                {
                    return who;
                }

                return $"{who} · {when}";
            }
        }

        [JsonIgnore]
        public string SportLabel =>
            string.IsNullOrWhiteSpace(Sport) ? string.Empty : Sport.Trim();

        [JsonIgnore]
        public string LocationDisplay =>
            string.IsNullOrWhiteSpace(Location) ? "—" : Location.Trim();

        [JsonIgnore]
        public string MatchupTitle
        {
            get
            {
                if (string.Equals(Sport, SportNames.Football, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Woodland JV {Name}";
                }

                if (string.Equals(Sport, SportNames.Soccer, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Bain {Name}";
                }

                return Name ?? string.Empty;
            }
        }

        [JsonIgnore]
        public string MatchupDisplay => string.IsNullOrWhiteSpace(LocationDisplay) || LocationDisplay == "—"
            ? MatchupTitle
            : $"{MatchupTitle} at {LocationDisplay}";

        [JsonIgnore]
        public string ScheduleMetaLine
        {
            get
            {
                var day = Date.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture);
                var datePart = Date.ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                return $"{day} · {datePart} · {TimeDisplay}";
            }
        }

        [JsonIgnore]
        public string ScheduleMetaLineWithSport
        {
            get
            {
                var sport = SportLabel;
                return string.IsNullOrWhiteSpace(sport)
                    ? ScheduleMetaLine
                    : $"{ScheduleMetaLine} · {sport}";
            }
        }

        [JsonIgnore]
        public IEnumerable<string> ScheduleMetaParts =>
            SplitMetaLine(ScheduleMetaLine);

        [JsonIgnore]
        public IEnumerable<string> ScheduleMetaPartsWithSport =>
            SplitMetaLine(ScheduleMetaLineWithSport);

        private static IEnumerable<string> SplitMetaLine(string line) =>
            (line ?? string.Empty).Split(new[] { " · " }, StringSplitOptions.None);

        [JsonIgnore]
        public string TimeDisplay =>
            StartTime.HasValue
                ? DateTime.Today.Add(StartTime.Value).ToString("h:mm tt")
                : "unscheduled";

        public static Event CreateBaseBallGame(string dateTime, string name)
        {
            var parsed = DateTime.Parse(dateTime);

            return new Event
            {
                Date = parsed.Date,
                StartTime = parsed.TimeOfDay,
                Name = name,
                Location = "H.B. Fuller Company Park"
            };
        }

        public static Event CreateContest(string date, string name, string location, TimeSpan? startTime = null)
        {
            return new Event
            {
                Date = DateTime.Parse(date).Date,
                StartTime = startTime,
                Name = name,
                Location = location
            };
        }
    }
}
