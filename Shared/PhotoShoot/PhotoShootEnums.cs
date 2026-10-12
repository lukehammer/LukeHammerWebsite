using System.Text.Json.Serialization;

namespace BlazorApp.Shared.PhotoShoot
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ShotStatus
    {
        NotStarted = 0,
        InProgress = 1,
        Completed = 2,
        Skipped = 3
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ShotPriority
    {
        MustHave = 0,
        Standard = 1,
        Bonus = 2
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ShotCategory
    {
        EssentialPortrait = 0,
        AdvertisingNegativeSpace = 1,
        FacialStorytelling = 2,
        PhysicalComedy = 3,
        AbsurdComedy = 4,
        MagicianSpecific = 5,
        ChildVolunteer = 6,
        WebsiteSocial = 7
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CameraOrientation
    {
        Vertical = 0,
        Horizontal = 1,
        Both = 2,
        Square = 3
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NegativeSpaceDirection
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 3,
        Bottom = 4
    }

    /// <summary>Resolved access level for the current caller.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PhotoShootAccess
    {
        None = 0,
        Photographer = 1,
        Owner = 2
    }

    public static class PhotoShootLabels
    {
        public static string Status(ShotStatus status) => status switch
        {
            ShotStatus.NotStarted => "Not Started",
            ShotStatus.InProgress => "In Progress",
            ShotStatus.Completed => "Completed",
            ShotStatus.Skipped => "Skipped",
            _ => status.ToString()
        };

        public static string Priority(ShotPriority priority) => priority switch
        {
            ShotPriority.MustHave => "Must Have",
            ShotPriority.Standard => "Standard",
            ShotPriority.Bonus => "Bonus",
            _ => priority.ToString()
        };

        public static string Category(ShotCategory category) => category switch
        {
            ShotCategory.EssentialPortrait => "Essential Portrait",
            ShotCategory.AdvertisingNegativeSpace => "Advertising / Negative Space",
            ShotCategory.FacialStorytelling => "Facial Storytelling",
            ShotCategory.PhysicalComedy => "Physical Comedy",
            ShotCategory.AbsurdComedy => "Absurd Comedy",
            ShotCategory.MagicianSpecific => "Magician",
            ShotCategory.ChildVolunteer => "Child Volunteers",
            ShotCategory.WebsiteSocial => "Website & Social",
            _ => category.ToString()
        };

        public static string Orientation(CameraOrientation orientation) => orientation switch
        {
            CameraOrientation.Vertical => "Vertical",
            CameraOrientation.Horizontal => "Horizontal",
            CameraOrientation.Both => "Vertical + Horizontal",
            CameraOrientation.Square => "Square",
            _ => orientation.ToString()
        };

        public static string NegativeSpace(NegativeSpaceDirection direction) => direction switch
        {
            NegativeSpaceDirection.None => "None",
            NegativeSpaceDirection.Left => "Space on left",
            NegativeSpaceDirection.Right => "Space on right",
            NegativeSpaceDirection.Top => "Space above",
            NegativeSpaceDirection.Bottom => "Space below",
            _ => direction.ToString()
        };
    }
}
