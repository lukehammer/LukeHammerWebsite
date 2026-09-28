namespace BlazorApp.Shared
{
    public class SportsEventWriteRequest
    {
        public string SubmittedBy { get; set; } = string.Empty;
        public Event Event { get; set; } = new Event();
    }
}
