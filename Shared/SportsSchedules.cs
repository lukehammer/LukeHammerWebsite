using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace BlazorApp.Shared
{
    public static class SportsSchedules
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static SportsSchedulesData ParseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SportsSchedulesData();
            }

            SportsSchedulesData data;
            try
            {
                data = JsonSerializer.Deserialize<SportsSchedulesData>(json, JsonOptions)
                    ?? new SportsSchedulesData();
            }
            catch (JsonException)
            {
                throw;
            }

            MigrateLegacyKidsFromJson(json, data);
            NormalizeEvents(data);
            return data;
        }

        /// <summary>Try to parse schedule JSON; returns false when the payload is invalid.</summary>
        public static bool TryParseJson(string json, out SportsSchedulesData data, out string? errorMessage)
        {
            data = new SportsSchedulesData();
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            try
            {
                data = ParseJson(json);
                return true;
            }
            catch (JsonException)
            {
                errorMessage = "Schedule data is invalid or corrupted. Try clearing the local sports schedule file and reloading.";
                return false;
            }
        }

        /// <summary>Maps legacy per-event <c>kid</c> string to <c>kids</c> when loading old JSON.</summary>
        private static void MigrateLegacyKidsFromJson(string json, SportsSchedulesData data)
        {
            data.Events ??= new List<Event>();
            if (data.Events.Count == 0)
            {
                return;
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return;
            }

            if (root == null || TryGetEventsArray(root, out var eventsArray) == false)
            {
                return;
            }

            var count = Math.Min(eventsArray.Count, data.Events.Count);
            for (var i = 0; i < count; i++)
            {
                var evt = data.Events[i];
                evt.Kids ??= new List<string>();

                if (evt.Kids.Count > 0)
                {
                    evt.Kids = ScheduleKids.NormalizeKidsList(evt.Kids);
                    continue;
                }

                if (eventsArray[i] is JsonObject obj
                    && obj.TryGetPropertyValue("kid", out var kidNode)
                    && kidNode is JsonValue kidValue
                    && kidValue.TryGetValue<string>(out var legacyKid)
                    && !string.IsNullOrWhiteSpace(legacyKid))
                {
                    evt.Kids = ScheduleKids.NormalizeKidsList(new[] { legacyKid });
                }
            }
        }

        private static void NormalizeEvents(SportsSchedulesData data)
        {
            data.Events ??= new List<Event>();
            foreach (var evt in data.Events)
            {
                if (evt == null)
                {
                    continue;
                }

                evt.Kids ??= new List<string>();
                if (evt.Kids.Count > 0)
                {
                    evt.Kids = ScheduleKids.NormalizeKidsList(evt.Kids);
                }

                if (evt.LastModifiedAt.HasValue)
                {
                    evt.LastModifiedAt = WashingtonScheduleTime.NormalizeStoredTime(evt.LastModifiedAt.Value);
                }

                evt.LastModifiedBy = ScheduleKids.NormalizeStoredPersonName(evt.LastModifiedBy);

                evt.Sport = MigrateLegacySportValue(evt.Sport);
                evt.Sport = SportNames.NormalizeOrDefault(evt.Sport);
            }
        }

        private static string MigrateLegacySportValue(string? sport)
        {
            if (string.IsNullOrWhiteSpace(sport))
            {
                return SportNames.Football;
            }

            if (int.TryParse(sport!.Trim(), out var index)
                && index >= 0
                && index < SportNames.Defaults.Length)
            {
                return SportNames.Defaults[index];
            }

            return sport;
        }

        private static bool TryGetEventsArray(JsonNode root, out JsonArray eventsArray)
        {
            eventsArray = null!;
            foreach (var property in root.AsObject())
            {
                if (!property.Key.Equals("events", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value is JsonArray array)
                {
                    eventsArray = array;
                    return true;
                }

                return false;
            }

            return false;
        }

        public static string ToJson(SportsSchedulesData data) =>
            JsonSerializer.Serialize(data, JsonOptions);

        /// <summary>Assigns a new GUID to any event missing an id. Returns true if any id was added.</summary>
        public static bool EnsureEventIds(SportsSchedulesData data)
        {
            data.Events ??= new List<Event>();
            var changed = false;
            foreach (var evt in data.Events)
            {
                if (string.IsNullOrWhiteSpace(evt.Id))
                {
                    evt.Id = Guid.NewGuid().ToString("D");
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// Combines deployed repo seed with live storage on deploy/sync.
        /// Events only on live are kept; only on seed are added; same id uses live when it has a newer lastModifiedAt.
        /// </summary>
        public static SportsSchedulesData MergeDeployedSeed(SportsSchedulesData seed, SportsSchedulesData live)
        {
            seed ??= new SportsSchedulesData();
            live ??= new SportsSchedulesData();
            NormalizeEvents(seed);
            NormalizeEvents(live);

            var merged = new Dictionary<string, Event>(StringComparer.OrdinalIgnoreCase);
            foreach (var evt in live.Events ?? new List<Event>())
            {
                if (evt == null || string.IsNullOrWhiteSpace(evt.Id))
                {
                    continue;
                }

                merged[evt.Id] = evt;
            }

            foreach (var seedEvt in seed.Events ?? new List<Event>())
            {
                if (seedEvt == null || string.IsNullOrWhiteSpace(seedEvt.Id))
                {
                    continue;
                }

                if (merged.TryGetValue(seedEvt.Id, out var liveEvt))
                {
                    merged[seedEvt.Id] = ShouldKeepLiveEvent(liveEvt, seedEvt) ? liveEvt : seedEvt;
                }
                else
                {
                    merged[seedEvt.Id] = seedEvt;
                }
            }

            var data = new SportsSchedulesData
            {
                Events = merged.Values
                    .OrderBy(e => e.Date)
                    .ThenBy(e => e.StartTime ?? TimeSpan.MaxValue)
                    .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            NormalizeEvents(data);
            EnsureEventIds(data);
            return data;
        }

        private static bool ShouldKeepLiveEvent(Event live, Event seed)
        {
            if (!live.LastModifiedAt.HasValue)
            {
                return false;
            }

            if (!seed.LastModifiedAt.HasValue)
            {
                return true;
            }

            var liveAt = WashingtonScheduleTime.NormalizeStoredTime(live.LastModifiedAt.Value);
            var seedAt = WashingtonScheduleTime.NormalizeStoredTime(seed.LastModifiedAt.Value);
            return liveAt > seedAt;
        }

        public static IReadOnlyList<Event> GetUpcoming(IEnumerable<Event> events, string? sport = null, string? kid = null)
        {
            if (events == null)
            {
                return Array.Empty<Event>();
            }

            IEnumerable<Event> query = events.Where(x => x != null && x.Date >= DateTime.Today);

            if (sport is { } sportValue && !string.IsNullOrWhiteSpace(sportValue))
            {
                var sportFilter = sportValue.Trim();
                query = query.Where(x =>
                    string.Equals(x!.Sport, sportFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (kid is { } kidValue && !string.IsNullOrWhiteSpace(kidValue))
            {
                var kidFilter = kidValue.Trim();
                query = query.Where(x =>
                    (x!.Kids ?? Enumerable.Empty<string>()).Any(k =>
                        string.Equals(k, kidFilter, StringComparison.OrdinalIgnoreCase)));
            }

            return query
                .OrderBy(x => x.Date)
                .ThenBy(x => x.StartTime ?? TimeSpan.MaxValue)
                .ToList();
        }
    }
}
