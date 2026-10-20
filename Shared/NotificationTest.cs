namespace BlazorApp.Shared
{
    public class NotificationTestRequest
    {
        public string SubmittedBy { get; set; } = "";
    }

    /// <summary>Outcome for one notification channel (email, SMS, push app).</summary>
    public class NotificationChannelResult
    {
        public string Channel { get; set; } = "";
        public bool Configured { get; set; }
        public bool Sent { get; set; }
        public string? Error { get; set; }
    }
}
