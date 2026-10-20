using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApiIsolated.Notifications
{
    /// <summary>Text messages through Twilio (TWILIO_ACCOUNT_SID, TWILIO_AUTH_TOKEN, TWILIO_FROM_NUMBER).</summary>
    internal sealed class TwilioSmsNotificationChannel : INotificationChannel
    {
        private readonly HttpClient _http;

        public TwilioSmsNotificationChannel(HttpClient http)
        {
            _http = http;
        }

        public string Name => "sms";

        private static string? Sid => Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID");
        private static string? Token => Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN");
        private static string? From => Environment.GetEnvironmentVariable("TWILIO_FROM_NUMBER");

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Sid) && !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(From);

        public async Task<string?> SendAsync(NotificationMessage message, CancellationToken cancellationToken)
        {
            var to = Environment.GetEnvironmentVariable("NOTIFY_SMS_TO") ?? "+15035053005";
            var text = $"{message.Title}: {message.Body}";
            if (text.Length > 1500)
            {
                text = text[..1500];
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.twilio.com/2010-04-01/Accounts/{Sid}/Messages.json");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Sid}:{Token}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = to,
                ["From"] = From!,
                ["Body"] = text
            });

            var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? null
                : $"Twilio returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}";
        }
    }
}
