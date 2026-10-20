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

    /// <summary>Where a planned photograph is in editing. Captured on set is not the same as finished.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeliverableStatus
    {
        Planned = 0,
        Captured = 1,
        Selected = 2,
        Edited = 3,
        Final = 4
    }

    /// <summary>How a reference relates to the planned photograph. Unspecified leaves older references unlabeled.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ReferenceProvenance
    {
        Unspecified = 0,
        ExactUserProvided = 1,
        ClosePublicMatch = 2,
        AlternateInspiration = 3,
        MissingOriginal = 4,
        SubjectPortrait = 5,
        AiGenerated = 6
    }

    /// <summary>Which required reference slot a photo fills for one shot. Unassigned keeps older links as extra references.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ReferenceSlot
    {
        Unassigned = 0,
        AiA = 1,
        AiB = 2,
        Celebrity = 3,
        RealWorld = 4,
        Additional = 5
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EquipmentCategory
    {
        Magic = 0,
        Furniture = 1,
        Wardrobe = 2,
        Studio = 3,
        Miscellaneous = 4
    }

    /// <summary>Who is responsible for having the item on set.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EquipmentSupplier
    {
        Bring = 0,
        Photographer = 1,
        Confirm = 2
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
        Owner = 2,
        Admin = 3
    }

    /// <summary>Which show a finished photo is retrieved for. A pose can belong to more than one.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ShowMarket
    {
        AnyShow = 0,
        Kids = 1,
        Family = 2,
        Bachelorette = 3,
        Corporate = 4
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

        public static string Deliverable(DeliverableStatus status) => status switch
        {
            DeliverableStatus.Planned => "Planned",
            DeliverableStatus.Captured => "Captured",
            DeliverableStatus.Selected => "Selected",
            DeliverableStatus.Edited => "Edited",
            DeliverableStatus.Final => "Final",
            _ => status.ToString()
        };

        public static string Provenance(ReferenceProvenance provenance) => provenance switch
        {
            ReferenceProvenance.ExactUserProvided => "Exact reference",
            ReferenceProvenance.ClosePublicMatch => "Close public match",
            ReferenceProvenance.AlternateInspiration => "Alternate inspiration",
            ReferenceProvenance.MissingOriginal => "Missing original",
            ReferenceProvenance.SubjectPortrait => "Luke portrait",
            ReferenceProvenance.AiGenerated => "AI-generated",
            _ => ""
        };

        public static string Slot(ReferenceSlot slot) => slot switch
        {
            ReferenceSlot.AiA => "AI Reference A",
            ReferenceSlot.AiB => "AI Reference B",
            ReferenceSlot.Celebrity => "Celebrity reference",
            ReferenceSlot.RealWorld => "Real-world reference",
            ReferenceSlot.Additional => "Additional",
            _ => ""
        };

        public static string EquipmentCategoryName(EquipmentCategory category) => category switch
        {
            EquipmentCategory.Magic => "Magic props",
            EquipmentCategory.Furniture => "Furniture",
            EquipmentCategory.Wardrobe => "Wardrobe",
            EquipmentCategory.Studio => "Studio equipment",
            _ => "Miscellaneous"
        };

        public static string Supplier(EquipmentSupplier supplier) => supplier switch
        {
            EquipmentSupplier.Bring => "Bring",
            EquipmentSupplier.Photographer => "Photographer provides",
            _ => "Confirm availability"
        };

        public static string Market(ShowMarket market) => market switch
        {
            ShowMarket.Kids => "Kids",
            ShowMarket.Family => "Family",
            ShowMarket.Bachelorette => "Bachelorette",
            ShowMarket.Corporate => "Corporate",
            _ => "Any show"
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
