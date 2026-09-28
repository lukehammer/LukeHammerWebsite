using System.Net.Http;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared;

namespace BlazorApp.Client
{
    public readonly struct ScheduleLoadRetryUpdate
    {
        public ScheduleLoadRetryUpdate(
            string errorMessage,
            int consecutiveFailures,
            int secondsRemaining,
            int totalDelaySeconds)
        {
            ErrorMessage = errorMessage;
            ConsecutiveFailures = consecutiveFailures;
            SecondsRemaining = secondsRemaining;
            TotalDelaySeconds = totalDelaySeconds;
        }

        public string ErrorMessage { get; }
        public int ConsecutiveFailures { get; }
        public int SecondsRemaining { get; }
        public int TotalDelaySeconds { get; }
    }

    public static class SportsScheduleApiClient
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        public static async Task<(IReadOnlyList<Event> Upcoming, string? Error)> LoadUpcomingAsync(
            HttpClient http,
            string? sport = null,
            string? kid = null)
        {
            var (data, error) = await LoadAllAsync(http, sport);
            if (error != null)
            {
                return (Array.Empty<Event>(), error);
            }

            return (SportsSchedules.GetUpcoming(data.Events, sport, kid), null);
        }

        public static async Task<(IReadOnlyList<Event> Upcoming, string? Error)> LoadUpcomingWithRetryAsync(
            HttpClient http,
            Func<ScheduleLoadRetryUpdate, Task>? onWaitingForRetryAsync,
            CancellationToken cancellationToken = default,
            string? sport = null,
            string? kid = null)
        {
            var (data, error) = await LoadAllWithRetryAsync(http, onWaitingForRetryAsync, cancellationToken, sport);
            if (error != null)
            {
                return (Array.Empty<Event>(), error);
            }

            return (SportsSchedules.GetUpcoming(data.Events, sport, kid), null);
        }

        public static async Task<(SportsSchedulesData Data, string? Error)> LoadAllWithRetryAsync(
            HttpClient http,
            Func<ScheduleLoadRetryUpdate, Task>? onWaitingForRetryAsync,
            CancellationToken cancellationToken = default,
            string? sport = null)
        {
            var consecutiveFailures = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (data, error) = await LoadAllOnceAsync(http, sport);
                if (error == null || !ScheduleLoadRetryPolicy.IsRetriableError(error))
                {
                    return (data, error);
                }

                consecutiveFailures++;
                var delaySeconds = ScheduleLoadRetryPolicy.GetSecondsBeforeRetry(consecutiveFailures);
                for (var secondsRemaining = delaySeconds; secondsRemaining > 0; secondsRemaining--)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (onWaitingForRetryAsync != null)
                    {
                        await onWaitingForRetryAsync(
                            new ScheduleLoadRetryUpdate(
                                error,
                                consecutiveFailures,
                                secondsRemaining,
                                delaySeconds));
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
        }

        public static async Task<(SportsSchedulesData Data, string? Error)> LoadAllAsync(
            HttpClient http,
            string? sport = null) =>
            await LoadAllOnceAsync(http, sport);

        private static async Task<(SportsSchedulesData Data, string? Error)> LoadAllOnceAsync(
            HttpClient http,
            string? sport = null)
        {
            try
            {
                var url = string.IsNullOrWhiteSpace(sport)
                    ? "/api/sports/schedules"
                    : $"/api/sports/schedules?sport={Uri.EscapeDataString(sport.Trim())}";

                using var cts = new CancellationTokenSource(RequestTimeout);
                var response = await http.GetAsync(url, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return (new SportsSchedulesData(), ScheduleLoadRetryPolicy.ApiUnavailableMessage);
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
                return (new SportsSchedulesData(), ScheduleLoadRetryPolicy.TimedOutMessage);
            }
            catch (Exception)
            {
                return (new SportsSchedulesData(), ScheduleLoadRetryPolicy.ApiUnavailableMessage);
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
