using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ApiIsolated.Notifications
{
    /// <summary>Email through SendGrid (SENDGRID_API_KEY), falling back to SMTP (SMTP_HOST).</summary>
    internal sealed class EmailNotificationChannel : INotificationChannel
    {
        private readonly HttpClient _http;

        public EmailNotificationChannel(HttpClient http)
        {
            _http = http;
        }

        public string Name => "email";

        private static string? SendGridKey => Environment.GetEnvironmentVariable("SENDGRID_API_KEY");

        private static string? SmtpHost => Environment.GetEnvironmentVariable("SMTP_HOST");

        private static string To => Environment.GetEnvironmentVariable("NOTIFY_EMAIL_TO") ?? "luke@lukehammermagic.com";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(SendGridKey) || !string.IsNullOrWhiteSpace(SmtpHost);

        public async Task<string?> SendAsync(NotificationMessage message, CancellationToken cancellationToken)
        {
            var body = string.IsNullOrWhiteSpace(message.ClickUrl)
                ? message.Body
                : $"{message.Body}\n\n{message.ClickUrl}";

            string? sendGridError = null;
            if (!string.IsNullOrWhiteSpace(SendGridKey))
            {
                sendGridError = await TrySendGridAsync(message.Title, body, cancellationToken);
                if (sendGridError == null)
                {
                    return null;
                }
            }

            if (!string.IsNullOrWhiteSpace(SmtpHost))
            {
                var smtpError = await TrySmtpAsync(message.Title, body, cancellationToken);
                return smtpError == null ? null : string.Join("; ", new[] { sendGridError, smtpError }).Trim(' ', ';');
            }

            return sendGridError;
        }

        private async Task<string?> TrySendGridAsync(string subject, string body, CancellationToken cancellationToken)
        {
            var from = Environment.GetEnvironmentVariable("NOTIFY_EMAIL_FROM") ?? "noreply@lukehammermagic.com";
            var payload = new
            {
                personalizations = new[] { new { to = new[] { new { email = To } } } },
                from = new { email = from },
                subject,
                content = new[] { new { type = "text/plain", value = body } }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SendGridKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? null
                : $"SendGrid returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}";
        }

        private static async Task<string?> TrySmtpAsync(string subject, string body, CancellationToken cancellationToken)
        {
            try
            {
                var port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var p) ? p : 587;
                var user = Environment.GetEnvironmentVariable("SMTP_USER");
                var password = Environment.GetEnvironmentVariable("SMTP_PASSWORD");
                var from = Environment.GetEnvironmentVariable("NOTIFY_EMAIL_FROM") ?? user ?? "noreply@lukehammermagic.com";

                using var client = new SmtpClient(SmtpHost, port)
                {
                    EnableSsl = true,
                    Credentials = string.IsNullOrWhiteSpace(user) ? null : new NetworkCredential(user, password)
                };

                using var mail = new MailMessage(from, To, subject, body);
                await client.SendMailAsync(mail, cancellationToken);
                return null;
            }
            catch (Exception ex)
            {
                return $"SMTP failed: {ex.Message}";
            }
        }
    }
}
