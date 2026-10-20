using System.Threading;
using System.Threading.Tasks;

namespace ApiIsolated.Notifications
{
    public sealed class NotificationMessage
    {
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        /// <summary>Page to open when the notification is tapped, if the channel supports it.</summary>
        public string? ClickUrl { get; set; }
    }

    /// <summary>One delivery provider (email, SMS, push app). Register new ones in <see cref="NotificationDispatcher"/>.</summary>
    public interface INotificationChannel
    {
        /// <summary>Short id used in NOTIFY_CHANNELS, e.g. "ntfy".</summary>
        string Name { get; }

        bool IsConfigured { get; }

        /// <summary>Returns null on success, otherwise a short error description.</summary>
        Task<string?> SendAsync(NotificationMessage message, CancellationToken cancellationToken);
    }
}
