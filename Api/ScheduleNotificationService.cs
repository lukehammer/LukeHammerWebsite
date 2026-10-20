using System.Threading.Tasks;
using ApiIsolated.Notifications;
using BlazorApp.Shared;
using Microsoft.Extensions.Logging;

namespace ApiIsolated
{
    internal static class ScheduleNotificationService
    {
        public static Task NotifyScheduleChangeAsync(
            ILogger logger,
            string submittedBy,
            string actionSummary,
            Event? affectedEvent)
        {
            var body = $"{submittedBy} {actionSummary}.";
            if (affectedEvent != null)
            {
                body +=
                    $" {affectedEvent.SportLabel} ({affectedEvent.KidsDisplay}): {affectedEvent.Name} on {affectedEvent.Date:yyyy-MM-dd} at {affectedEvent.Location}. Time: {affectedEvent.TimeDisplay}.";
            }

            return NotificationDispatcher.NotifyChangeAsync(logger, submittedBy, new NotificationMessage
            {
                Title = $"Sports schedule: {actionSummary}",
                Body = body,
                ClickUrl = NotificationDispatcher.SiteUrl("Sports")
            });
        }
    }
}
