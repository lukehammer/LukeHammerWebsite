using BlazorApp.Shared.PhotoShoot;

namespace UnitTest.PhotoShoot;

internal static class PhotoShootTestData
{
    public static readonly DateTime Now = new(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>Two setups (A: 3 shots, B: 2 shots) with mixed priorities.</summary>
    public static PhotoShootDocument TwoSetups()
    {
        var doc = PhotoShootEditor.CreateShoot("shoot1", "Test Shoot", "", "", Now);
        doc.BreakEveryMinutes = 0;
        var a = PhotoShootEditor.CreateSetup(doc, new PhotoSetup { Name = "A", Background = "White seamless", ChangeoverMinutes = 15 }).Value!;
        var b = PhotoShootEditor.CreateSetup(doc, new PhotoSetup { Name = "B", Background = "Teal backdrop", ChangeoverMinutes = 10 }).Value!;
        AddShot(doc, a.Id, "a1", ShotPriority.Standard, 2);
        AddShot(doc, a.Id, "a2", ShotPriority.MustHave, 3);
        AddShot(doc, a.Id, "a3", ShotPriority.Bonus, 4);
        AddShot(doc, b.Id, "b1", ShotPriority.MustHave, 5);
        AddShot(doc, b.Id, "b2", ShotPriority.Standard, 6);
        return doc;
    }

    public static PhotoShot AddShot(PhotoShootDocument doc, string setupId, string title, ShotPriority priority, int minutes)
    {
        var result = PhotoShootEditor.CreateShot(doc, new PhotoShot
        {
            Title = title,
            SetupId = setupId,
            Priority = priority,
            EstimatedMinutes = minutes,
            Category = ShotCategory.EssentialPortrait
        }, Now);
        Assert.True(result.Ok, string.Join(";", result.Errors));
        return result.Value!;
    }

    public static PhotoShot Shot(PhotoShootDocument doc, string title) => doc.Shots.Single(s => s.Title == title);

    public static string[] OrderTitles(PhotoShootDocument doc) => PhotoShootPlanner.OrderedShots(doc).Select(s => s.Title).ToArray();

    public static PhotoShootCatalog Catalog() => new()
    {
        CatalogId = "test-catalog",
        CatalogVersion = 1,
        ShootName = "Catalog Shoot",
        Setups =
        {
            new PhotoShootCatalogSetup { Key = "01-white", Name = "White headshots", Background = "White seamless", ChangeoverMinutes = 15 },
            new PhotoShootCatalogSetup { Key = "02-kids", Name = "Kids", Background = "White seamless", RequiresChildren = true, ChangeoverMinutes = 5 }
        },
        Shots =
        {
            new PhotoShootCatalogShot { Key = "warm-smile", SetupKey = "01-white", Title = "Warm Smile", Priority = ShotPriority.MustHave, EstimatedMinutes = 2 },
            new PhotoShootCatalogShot { Key = "side-eye", SetupKey = "01-white", Title = "Suspicious Side-Eye", Priority = ShotPriority.Standard, EstimatedMinutes = 2 },
            new PhotoShootCatalogShot { Key = "kid-amazed", SetupKey = "02-kids", Title = "Child Amazed", Category = ShotCategory.ChildVolunteer, EstimatedMinutes = 3 }
        }
    };
}
