using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BlazorApp.Shared;
using Microsoft.Extensions.Logging;

namespace ApiIsolated.Notifications
{
    /// <summary>
    /// Sends a message to every configured channel at once. NOTIFY_CHANNELS (comma separated, e.g. "ntfy,email")
    /// limits which channels are used; when unset, every configured channel is used.
    /// </summary>
    internal static class NotificationDispatcher
    {
        private static readonly TimeSpan ChannelTimeout = TimeSpan.FromSeconds(10);
        private static readonly HttpClient Http = new HttpClient();

        public static readonly IReadOnlyList<INotificationChannel> Channels = new INotificationChannel[]
        {
            new NtfyNotificationChannel(Http),
            new EmailNotificationChannel(Http),
            new TwilioSmsNotificationChannel(Http)
        };

        public static string SiteUrl(string path) =>
            $"{(Environment.GetEnvironmentVariable("SITE_BASE_URL") ?? "https://lukehammer.com").TrimEnd('/')}/{path.TrimStart('/')}";

        /// <summary>Notifies Luke about a change, unless Luke made it himself.</summary>
        public static async Task NotifyChangeAsync(ILogger logger, string actor, NotificationMessage message)
        {
            var lukeAliases = LukeNameMatcher.ParseAliasesFromConfig(Environment.GetEnvironmentVariable("LUKE_NAME_ALIASES"));
            if (!ScheduleChangeNotificationPolicy.ShouldNotify(actor, lukeAliases))
            {
                return;
            }

            try
            {
                var results = await SendAsync(message);
                foreach (var failed in results.Where(r => r.Configured && !r.Sent))
                {
                    logger.LogWarning("Notification channel {Channel} failed: {Error}", failed.Channel, failed.Error);
                }

                if (!results.Any(r => r.Sent))
                {
                    logger.LogWarning(
                        "Notification not delivered (no working channels). Title={Title}, Body={Body}",
                        message.Title,
                        message.Body);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Notification dispatch failed.");
            }
        }

        /// <summary>Sends to all enabled channels and reports each one, including channels that are not configured.</summary>
        public static async Task<List<NotificationChannelResult>> SendAsync(NotificationMessage message)
        {
            var enabled = EnabledChannelNames();
            var tasks = Channels
                .Where(c => enabled == null || enabled.Contains(c.Name))
                .Select(c => SendOneAsync(c, message));

            return (await Task.WhenAll(tasks)).ToList();
        }

        private static HashSet<string>? EnabledChannelNames()
        {
            var raw = Environment.GetEnvironmentVariable("NOTIFY_CHANNELS");
            return string.IsNullOrWhiteSpace(raw)
                ? null
                : new HashSet<string>(
                    raw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static async Task<NotificationChannelResult> SendOneAsync(INotificationChannel channel, NotificationMessage message)
        {
            var result = new NotificationChannelResult { Channel = channel.Name, Configured = channel.IsConfigured };
            if (!result.Configured)
            {
                result.Error = "Not configured.";
                return result;
            }

            try
            {
                using var timeoutSource = new CancellationTokenSource(ChannelTimeout);
                result.Error = await channel.SendAsync(message, timeoutSource.Token);
                result.Sent = result.Error == null;
            }
            catch (OperationCanceledException)
            {
                result.Error = $"Timed out after {ChannelTimeout.TotalSeconds:0} seconds.";
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }
    }
}
