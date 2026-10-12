using System.Collections.Generic;

namespace BlazorApp.Shared.PhotoShoot
{
    /// <summary>Seed catalog file format (Api/data/photoshoot-catalog.json). Array order is shooting order.</summary>
    public class PhotoShootCatalog
    {
        public string CatalogId { get; set; } = "";
        public int CatalogVersion { get; set; }
        public string ShootName { get; set; } = "";
        public string ShootDescription { get; set; } = "";
        public List<PhotoShootCatalogSetup> Setups { get; set; } = new List<PhotoShootCatalogSetup>();
        public List<PhotoShootCatalogShot> Shots { get; set; } = new List<PhotoShootCatalogShot>();
    }

    public class PhotoShootCatalogSetup
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Background { get; set; } = "";
        public string Lighting { get; set; } = "";
        public string CameraPosition { get; set; } = "";
        public string Outfit { get; set; } = "";
        public List<string> Props { get; set; } = new List<string>();
        public int ChangeoverMinutes { get; set; }
        public bool RequiresChildren { get; set; }
        public string Notes { get; set; } = "";
    }

    public class PhotoShootCatalogShot
    {
        public string Key { get; set; } = "";
        public string SetupKey { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Objective { get; set; } = "";
        public string MarketingPurpose { get; set; } = "";
        public ShotCategory Category { get; set; }
        public ShotPriority Priority { get; set; } = ShotPriority.Standard;
        public string FacialExpression { get; set; } = "";
        public string BodyPosition { get; set; } = "";
        public string HandPosition { get; set; } = "";
        public string EyeDirection { get; set; } = "";
        public string PhotographerInstructions { get; set; } = "";
        public string PerformerInstructions { get; set; } = "";
        public string Framing { get; set; } = "";
        public CameraOrientation Orientation { get; set; }
        public string RecommendedCrop { get; set; } = "";
        public NegativeSpaceDirection NegativeSpace { get; set; }
        public string Background { get; set; } = "";
        public List<string> Props { get; set; } = new List<string>();
        public string LightingNotes { get; set; } = "";
        public int EstimatedMinutes { get; set; } = 3;
        public string Inspiration { get; set; } = "";
        public List<string> ReferenceSearchPhrases { get; set; } = new List<string>();
        public List<string> Tags { get; set; } = new List<string>();
        public string Notes { get; set; } = "";
    }
}
