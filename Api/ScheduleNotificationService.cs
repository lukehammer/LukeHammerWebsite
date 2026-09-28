using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorApp.Shared;
using Microsoft.Extensions.Logging;

namespace ApiIsolated
{
    internal static class ScheduleNotificationService
    {
        private static readonly HttpClient Http = new HttpClient();

        public static async Task NotifyScheduleChangeAsync(
            ILogger logger,
            string submittedBy,
            string actionSummary,
            Event? affectedEvent)
        {
            var lukeAliases = LukeNameMatcher.ParseAliasesFromConfig(
                Environment.GetEnvironmentVariable("LUKE_NAME_ALIASES"));

            if (!ScheduleChangeNotificationPolicy.ShouldNotify(submittedBy, lukeAliases))
            {
                logger.LogInformation(
                    "Skipping schedule notification; actor is Luke ({SubmittedBy}).",
                    submittedBy);
                return;
            }

            var emailTo = Environment.GetEnvironmentVariable("NOTIFY_EMAIL_TO")
                ?? "luke@lukehammermagic.com";
            var smsTo = Environment.GetEnvironmentVariable("NOTIFY_SMS_TO")
                ?? "+15035053005";

            var body = BuildMessage(submittedBy, actionSummary, affectedEvent);
            var subject = $"Sports schedule: {actionSummary}";

            var emailSent = await TrySendEmailAsync(logger, emailTo, subject, body);
            var smsSent = await TrySendSmsAsync(logger, smsTo, body);

            if (!emailSent && !smsSent)
            {
                logger.LogWarning(
                    "Schedule notification (no providers configured). EmailTo={EmailTo}, SmsTo={SmsTo}, Subject={Subject}, Body={Body}",
                    emailTo,
                    smsTo,
                    subject,
                    body);
            }
        }

        private static string BuildMessage(string submittedBy, string actionSummary, Event? affectedEvent)
        {
            var lines = new List<string>
            {
                $"{submittedBy} {actionSummary}."
            };

            if (affectedEvent != null)
            {
                lines.Add(
                    $"{affectedEvent.SportLabel} ({affectedEvent.KidsDisplay}): {affectedEvent.Name} on {affectedEvent.Date:yyyy-MM-dd} at {affectedEvent.Location}. Time: {affectedEvent.TimeDisplay}.");
            }

            return string.Join(" ", lines);
        }

        private static async Task<bool> TrySendEmailAsync(
            ILogger logger,
            string to,
            string subject,
            string body)
        {
            var sendGridKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
            if (!string.IsNullOrWhiteSpace(sendGridKey))
            {
                try
                {
                    var from = Environment.GetEnvironmentVariable("NOTIFY_EMAIL_FROM")
                        ?? "noreply@lukehammermagic.com";
                    var payload = new
                    {
                        personalizations = new[] { new { to = new[] { new { email = to } } } },
                        from = new { email = from },
                        subject,
                        content = new[] { new { type = "text/plain", value = body } }
                    };

                    using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sendGridKey);
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(payload),
                        Encoding.UTF8,
                        "application/json");

                    var response = await Http.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }

                    logger.LogWarning(
                        "SendGrid returned {StatusCode}: {Body}",
                        (int)response.StatusCode,
                        await response.Content.ReadAsStringAsync());
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "SendGrid email failed.");
                }
            }

            var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST");
            if (!string.IsNullOrWhiteSpace(smtpHost))
            {
                try
                {
                    var port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var p) ? p : 587;
                    var user = Environment.GetEnvironmentVariable("SMTP_USER");
                    var password = Environment.GetEnvironmentVariable("SMTP_PASSWORD");
                    var from = Environment.GetEnvironmentVariable("NOTIFY_EMAIL_FROM")
                        ?? user
                        ?? "noreply@lukehammermagic.com";

                    using var client = new SmtpClient(smtpHost, port)
                    {
                        EnableSsl = true,
                        Credentials = string.IsNullOrWhiteSpace(user)
                            ? null
                            : new NetworkCredential(user, password)
                    };

                    using var message = new MailMessage(from, to, subject, body);
                    await client.SendMailAsync(message);
                    return true;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "SMTP email failed.");
                }
            }

            return false;
        }

        private static async Task<bool> TrySendSmsAsync(ILogger logger, string to, string body)
        {
            var sid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID");
            var token = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN");
            var from = Environment.GetEnvironmentVariable("TWILIO_FROM_NUMBER");

            if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(from))
            {
                return false;
            }

            try
            {
                var smsBody = body.Length > 1500 ? body[..1500] : body;
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json");

                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["To"] = to,
                    ["From"] = from,
                    ["Body"] = smsBody
                });

                var response = await Http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                logger.LogWarning(
                    "Twilio returned {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Twilio SMS failed.");
            }

            return false;
        }
    }
}
