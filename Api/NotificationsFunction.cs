using System;
using System.Net;
using System.Threading.Tasks;
using ApiIsolated.Notifications;
using BlazorApp.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ApiIsolated
{
    public class NotificationsFunction
    {
        /// <summary>Sends a test message to every channel and reports which ones are configured and working.</summary>
        [Function("TestNotifications")]
        public async Task<HttpResponseData> Test(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/test")] HttpRequestData httpRequest)
        {
            var request = await httpRequest.ReadFromJsonAsync<NotificationTestRequest>();
            if (!string.Equals(request?.SubmittedBy?.Trim(), ScheduleSubmitters.LukeAuthCode, StringComparison.Ordinal))
            {
                var forbidden = httpRequest.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteStringAsync("Only Luke can send test notifications.");
                return forbidden;
            }

            var results = await NotificationDispatcher.SendAsync(new NotificationMessage
            {
                Title = "Test notification",
                Body = $"Notifications from the family site are working ({WashingtonScheduleTime.Now:MMM d, h:mm tt} Pacific).",
                ClickUrl = NotificationDispatcher.SiteUrl("FamilyEvents")
            });

            var response = httpRequest.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(results);
            return response;
        }
    }
}
