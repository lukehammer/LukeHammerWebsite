using System.Net.Http;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared;

namespace BlazorApp.Client
{
    public static class SportsScheduleApiClient
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        public static async Task<(IReadOnlyList<Event> Upcoming, string? Error)> LoadUpcomingAsync(
            HttpClient http,
            Sport? sport = null,
            string? kid = null)
        {
            var (data, error) = await LoadAllAsync(http, sport);
            if (error != null)
            {
                return (Array.Empty<Event>(), error);
            }

            return (SportsSchedules.GetUpcoming(data.Events, sport, kid), null);
        }

        public static async Task<(SportsSchedulesData Data, string? Error)> LoadAllAsync(
            HttpClient http,
            Sport? sport = null)
        {
            try
            {
                var url = sport.HasValue
                    ? $"/api/sports/schedules?sport={sport.Value}"
                    : "/api/sports/schedules";

                using var cts = new CancellationTokenSource(RequestTimeout);
                var response = await http.GetAsync(url, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return (new SportsSchedulesData(), "Could not load schedule. Make sure the API is running.");
                }

                var body = await response.Content.ReadAsStringAsync(cts.Token);
                if (!SportsSchedules.TryParseJson(body, out var data, out var parseError))
                {
                    return (new SportsSchedulesData(), parseError);
                }

                return (data, null);
            }
            catch (OperationCanceledException)
            {
                return (new SportsSchedulesData(), "Schedule request timed out. Check that the API is running at the configured address.");
            }
            catch (Exception)
            {
                return (new SportsSchedulesData(), "Could not load schedule. Make sure the API is running.");
            }
        }

        public static async Task<(SportsSchedulesData? Data, string? Error)> AddEventAsync(
            HttpClient http,
            string submittedBy,
            Event evt)
        {
            return await PostWriteAsync(http, "/api/sports/schedules/events", submittedBy, evt);
        }

        public static async Task<(SportsSchedulesData? Data, string? Error)> UpdateEventAsync(
            HttpClient http,
            string eventId,
            string submittedBy,
            Event evt)
        {
            return await PostWriteAsync(http, $"/api/sports/schedules/events/{Uri.EscapeDataString(eventId)}", submittedBy, evt, HttpMethod.Put);
        }

        public static async Task<(SportsSchedulesData? Data, string? Error)> DeleteEventAsync(
            HttpClient http,
            string eventId,
            string submittedBy)
        {
            try
            {
                var url =
                    $"/api/sports/schedules/events/{Uri.EscapeDataString(eventId)}?submittedBy={Uri.EscapeDataString(submittedBy)}";

                using var cts = new CancellationTokenSource(RequestTimeout);
                var response = await http.DeleteAsync(url, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(cts.Token);
                    return (null, string.IsNullOrWhiteSpace(detail)
                        ? "Could not delete event."
                        : detail);
                }

                var body = await response.Content.ReadAsStringAsync(cts.Token);
                if (!SportsSchedules.TryParseJson(body, out var data, out var parseError))
                {
                    return (null, parseError);
                }

                return (data, null);
            }
            catch (Exception)
            {
                return (null, "Could not delete event. Make sure the API is running.");
            }
        }

        private static async Task<(SportsSchedulesData? Data, string? Error)> PostWriteAsync(
            HttpClient http,
            string url,
            string submittedBy,
            Event evt,
            HttpMethod? method = null)
        {
            try
            {
                var payload = new SportsEventWriteRequest
                {
                    SubmittedBy = submittedBy,
                    Event = evt
                };

                using var cts = new CancellationTokenSource(RequestTimeout);
                using var request = new HttpRequestMessage(method ?? HttpMethod.Post, url)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload, SportsSchedules.JsonOptions),
                        Encoding.UTF8,
                        "application/json")
                };

                var response = await http.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(cts.Token);
                    return (null, string.IsNullOrWhiteSpace(detail)
                        ? "Could not save event."
                        : detail);
                }

                var body = await response.Content.ReadAsStringAsync(cts.Token);
                if (!SportsSchedules.TryParseJson(body, out var data, out var parseError))
                {
                    return (null, parseError);
                }

                return (data, null);
            }
            catch (Exception)
            {
                return (null, "Could not save event. Make sure the API is running.");
            }
        }
    }
}
