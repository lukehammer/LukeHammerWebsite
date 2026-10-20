using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ApiIsolated.Notifications
{
    /// <summary>
    /// Push notifications through the ntfy phone app (https://ntfy.sh). Subscribe to NTFY_TOPIC in the app;
    /// the topic name is the only secret, so make it long and random.
    /// </summary>
    internal sealed class NtfyNotificationChannel : INotificationChannel
    {
        private readonly HttpClient _http;

        public NtfyNotificationChannel(HttpClient http)
        {
            _http = http;
        }

        public string Name => "ntfy";

        private static string? Topic => Environment.GetEnvironmentVariable("NTFY_TOPIC")?.Trim();

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Topic);

        public async Task<string?> SendAsync(NotificationMessage message, CancellationToken cancellationToken)
        {
            var server = (Environment.GetEnvironmentVariable("NTFY_SERVER") ?? "https://ntfy.sh").TrimEnd('/');
            var payload = new Dictionary<string, object?>
            {
                ["topic"] = Topic,
                ["title"] = message.Title,
                ["message"] = message.Body
            };
            if (!string.IsNullOrWhiteSpace(message.ClickUrl))
            {
                payload["click"] = message.ClickUrl;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, server)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            var token = Environment.GetEnvironmentVariable("NTFY_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? null
                : $"ntfy returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}";
        }
    }
}
