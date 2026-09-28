using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorApp.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ApiIsolated
{
    public class SportsSchedulesFunction
    {
        private readonly ILogger _logger;

        public SportsSchedulesFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SportsSchedulesFunction>();
        }

        [Function("GetSportsSchedules")]
        public async Task<HttpResponseData> Get(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sports/schedules")] HttpRequestData req)
        {
            try
            {
                var data = await SportsSchedulesStorage.LoadAsync();
                var events = data.Events;

                if (TryGetSportQuery(req.Url.Query, out var sport))
                {
                    events = events
                        .Where(e => string.Equals(e.Sport, sport, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                return await OkEvents(req, new SportsSchedulesData { Events = events });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load sports schedules.");
                return await ServerError(req, ex.Message);
            }
        }

        [Function("AddSportsScheduleEvent")]
        public async Task<HttpResponseData> AddEvent(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sports/schedules/events")] HttpRequestData req)
        {
            var (request, parseError) = await TryReadWriteRequestAsync(req);
            if (parseError != null)
            {
                return await BadRequest(req, parseError);
            }

            if (request == null || string.IsNullOrWhiteSpace(request.SubmittedBy))
            {
                return await BadRequest(req, "submittedBy is required.");
            }

            if (!ScheduleSubmitters.TryResolveSubmitterForWrite(request.SubmittedBy, out var submittedByAdd, out var submitterError))
            {
                return await BadRequest(req, submitterError);
            }

            if (request.Event == null || string.IsNullOrWhiteSpace(request.Event.Name))
            {
                return await BadRequest(req, "event.name is required.");
            }

            if (!ScheduleKids.TryValidateKids(request.Event.Kids, out var kidError))
            {
                return await BadRequest(req, kidError);
            }

            if (!SportNames.TryNormalize(request.Event.Sport, out var sportAdd))
            {
                return await BadRequest(req, "event.sport is required.");
            }

            if (!WashingtonScheduleTime.TryValidateEventNotInPast(
                    request.Event.Date,
                    request.Event.StartTime,
                    out var pastErrorAdd))
            {
                return await BadRequest(req, pastErrorAdd);
            }

            try
            {
                var submittedBy = submittedByAdd;
                var kids = ScheduleKids.NormalizeKidsList(request.Event.Kids);
                Event? created = null;

                var data = await SportsSchedulesStorage.MutateAsync(current =>
                {
                    created = new Event
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        Sport = sportAdd,
                        Kids = kids,
                        Date = request.Event.Date.Date,
                        Name = request.Event.Name.Trim(),
                        Location = request.Event.Location?.Trim() ?? string.Empty,
                        StartTime = request.Event.StartTime
                    };
                    created.ApplyLastModified(submittedBy);
                    current.Events.Add(created);
                    return current;
                });

                await AfterMutationAsync(submittedBy, "added an event", created!, data);
                return await OkEvents(req, data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add sports schedule event.");
                return await ServerError(req, ex.Message);
            }
        }

        [Function("UpdateSportsScheduleEvent")]
        public async Task<HttpResponseData> UpdateEvent(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "sports/schedules/events/{id}")] HttpRequestData req,
            string id)
        {
            var (request, parseError) = await TryReadWriteRequestAsync(req);
            if (parseError != null)
            {
                return await BadRequest(req, parseError);
            }

            if (request == null || string.IsNullOrWhiteSpace(request.SubmittedBy))
            {
                return await BadRequest(req, "submittedBy is required.");
            }

            if (!ScheduleSubmitters.TryResolveSubmitterForWrite(request.SubmittedBy, out var submittedByUpdate, out var submitterError))
            {
                return await BadRequest(req, submitterError);
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return await BadRequest(req, "Event id is required.");
            }

            if (request.Event != null && !ScheduleKids.TryValidateKids(request.Event.Kids, out var kidError))
            {
                return await BadRequest(req, kidError);
            }

            var sportUpdate = string.Empty;
            if (request.Event != null && !SportNames.TryNormalize(request.Event.Sport, out sportUpdate))
            {
                return await BadRequest(req, "event.sport is required.");
            }

            if (request.Event != null
                && !WashingtonScheduleTime.TryValidateEventNotInPast(
                    request.Event.Date,
                    request.Event.StartTime,
                    out var pastErrorUpdate))
            {
                return await BadRequest(req, pastErrorUpdate);
            }

            try
            {
                var submittedBy = submittedByUpdate;
                Event? updatedEvent = null;
                var sportToApply = sportUpdate;

                var data = await SportsSchedulesStorage.MutateAsync(current =>
                {
                    var existing = current.Events.FirstOrDefault(e =>
                        string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        throw new InvalidOperationException($"Event {id} was not found.");
                    }

                    if (request.Event != null)
                    {
                        existing.Sport = sportToApply;
                        existing.Kids = ScheduleKids.NormalizeKidsList(request.Event.Kids);
                        existing.Date = request.Event.Date.Date;
                        existing.Name = request.Event.Name?.Trim() ?? existing.Name;
                        existing.Location = request.Event.Location?.Trim() ?? string.Empty;
                        existing.StartTime = request.Event.StartTime;
                    }

                    existing.ApplyLastModified(submittedBy);
                    updatedEvent = existing;
                    return current;
                });

                await AfterMutationAsync(submittedBy, "updated an event", updatedEvent!, data);
                return await OkEvents(req, data);
            }
            catch (InvalidOperationException ex)
            {
                return await NotFound(req, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update sports schedule event {Id}.", id);
                return await ServerError(req, ex.Message);
            }
        }

        [Function("DeleteSportsScheduleEvent")]
        public async Task<HttpResponseData> DeleteEvent(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "sports/schedules/events/{id}")] HttpRequestData req,
            string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return await BadRequest(req, "Event id is required.");
            }

            var submittedByRaw = TryGetQuery(req.Url.Query, "submittedBy");
            if (string.IsNullOrWhiteSpace(submittedByRaw))
            {
                return await BadRequest(req, "submittedBy query parameter is required.");
            }

            if (!ScheduleSubmitters.TryResolveSubmitterForWrite(submittedByRaw, out var submittedByDelete, out var submitterError))
            {
                return await BadRequest(req, submitterError);
            }

            try
            {
                Event? removed = null;

                var data = await SportsSchedulesStorage.MutateAsync(current =>
                {
                    var existing = current.Events.FirstOrDefault(e =>
                        string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        throw new InvalidOperationException($"Event {id} was not found.");
                    }

                    removed = existing;
                    current.Events.Remove(existing);
                    return current;
                });

                await AfterMutationAsync(submittedByDelete, "deleted an event", removed!, data);
                return await OkEvents(req, data);
            }
            catch (InvalidOperationException ex)
            {
                return await NotFound(req, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete sports schedule event {Id}.", id);
                return await ServerError(req, ex.Message);
            }
        }

        private async Task AfterMutationAsync(
            string submittedBy,
            string actionPhrase,
            Event affectedEvent,
            SportsSchedulesData data)
        {
            await ScheduleNotificationService.NotifyScheduleChangeAsync(
                _logger,
                submittedBy,
                actionPhrase,
                affectedEvent);

            var json = SportsSchedules.ToJson(data);

            var commitMessage =
                $"Sports schedule: {submittedBy} {actionPhrase} ({affectedEvent.SportLabel} {affectedEvent.Name} {affectedEvent.Date:yyyy-MM-dd})";

            await SportsSchedulesGitHubBackup.TryBackupAsync(_logger, json, commitMessage);
        }

        private static async Task<HttpResponseData> OkEvents(HttpRequestData req, SportsSchedulesData data)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(SportsSchedules.ToJson(data), Encoding.UTF8);
            return response;
        }

        private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync(message);
            return response;
        }

        private static async Task<HttpResponseData> NotFound(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.NotFound);
            await response.WriteStringAsync(message);
            return response;
        }

        private static async Task<HttpResponseData> ServerError(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync(message);
            return response;
        }

        private static async Task<(SportsEventWriteRequest? Request, string? ParseError)> TryReadWriteRequestAsync(
            HttpRequestData req)
        {
            try
            {
                using var reader = new StreamReader(req.Body);
                var body = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    return (null, null);
                }

                var request = JsonSerializer.Deserialize<SportsEventWriteRequest>(body, SportsSchedules.JsonOptions);
                return (request, null);
            }
            catch (JsonException ex)
            {
                return (null, $"Invalid request JSON: {ex.Message}");
            }
        }

        private static bool TryGetSportQuery(string? queryString, out string sport)
        {
            sport = string.Empty;
            var value = TryGetQuery(queryString, "sport");
            return !string.IsNullOrWhiteSpace(value)
                && SportNames.TryNormalize(value, out sport);
        }

        private static string? TryGetQuery(string? queryString, string key)
        {
            if (string.IsNullOrWhiteSpace(queryString))
            {
                return null;
            }

            var trimmed = queryString.TrimStart('?');
            foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (pair.Length != 2 || !pair[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return Uri.UnescapeDataString(pair[1]);
            }

            return null;
        }
    }
}
