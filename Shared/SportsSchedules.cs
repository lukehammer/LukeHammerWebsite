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

            JsonNode root;
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
            }
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

        public static IReadOnlyList<Event> GetUpcoming(IEnumerable<Event> events, Sport? sport = null, string? kid = null)
        {
            if (events == null)
            {
                return Array.Empty<Event>();
            }

            IEnumerable<Event> query = events.Where(x => x != null && x.Date >= DateTime.Today);

            if (sport.HasValue)
            {
                query = query.Where(x => x!.Sport == sport.Value);
            }

            if (!string.IsNullOrWhiteSpace(kid))
            {
                query = query.Where(x =>
                    (x!.Kids ?? Enumerable.Empty<string>()).Any(k =>
                        string.Equals(k, kid, StringComparison.OrdinalIgnoreCase)));
            }

            return query
                .OrderBy(x => x.Date)
                .ThenBy(x => x.StartTime ?? TimeSpan.MaxValue)
                .ToList();
        }
    }
}
