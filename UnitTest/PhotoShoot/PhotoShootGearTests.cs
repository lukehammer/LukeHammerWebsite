using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;

namespace UnitTest.PhotoShoot;

public class PhotoShootGearTests
{
    [Fact]
    public void AssignExisting_FillsEmptySlots_AndKeepsExtraPhotos()
    {
        var doc = Shoot();
        var shot = doc.Shots[0];
        doc.References.Add(Photo("ai-a", shot.Id, "AI mock-up of Luke — pointing", "blob-a"));
        doc.References.Add(Photo("ai-b", shot.Id, "AI mock-up of Luke — both hands", "blob-b"));
        doc.References.Add(Photo("celeb", shot.Id, "Dick Van Dyke - laugh", "blob-c", "Dick Van Dyke"));
        doc.References.Add(Photo("real", shot.Id, "Studio portrait, open hands", "blob-d"));
        doc.References.Add(Photo("extra", shot.Id, "Another Dick Van Dyke frame", "blob-e", "Dick Van Dyke"));
        doc.References.Add(Photo("portrait", shot.Id, "Luke cartoon portrait, close", "blob-f"));
        doc.References[^1].Provenance = ReferenceProvenance.SubjectPortrait;

        PhotoShootReferenceSlots.AssignExisting(doc).Should().BeTrue();
        PhotoShootReferenceSlots.AssignExisting(doc).Should().BeFalse();

        var slots = PhotoShootReferenceSlots.ForShot(doc, shot.Id);
        slots.Filled.Should().Be(4);
        slots.AiA!.ImageId.Should().Be("blob-a");
        slots.AiB!.ImageId.Should().Be("blob-b");
        slots.Celebrity!.ImageId.Should().Be("blob-c");
        slots.RealWorld!.ImageId.Should().Be("blob-d");
        slots.Additional.Select(reference => reference.ImageId).Should().Contain(new[] { "blob-e", "blob-f" });
        doc.References.Should().HaveCount(6);
    }

    [Fact]
    public void Equipment_UsesNamedProps_AndPackingKeepsOneWand()
    {
        var doc = Shoot();
        doc.Setups.Add(new PhotoSetup { Id = "cards", Name = "Cards", Props = new List<string> { "Fresh deck of red-back playing cards", "Silk handkerchiefs (burgundy, gold, teal)" } });
        doc.Setups.Add(new PhotoSetup { Id = "empty", Name = "Headshots", Props = new List<string>() });
        doc.Shots.Add(new PhotoShot { Id = "card-shot", SetupId = "cards", Title = "Card fan" });
        doc.Shots.Add(new PhotoShot { Id = "head", SetupId = "empty", Title = "Warm smile", PhotographerInstructions = "Empty hands." });
        doc.Shots.Add(new PhotoShot { Id = "wand-1", SetupId = "empty", Title = "Wand point", Props = new List<string> { "Magic wand" } });
        doc.Shots.Add(new PhotoShot { Id = "wand-2", SetupId = "empty", Title = "Wand again", Props = new List<string> { "Magic wand (optional)" } });

        PhotoShootGear.SpecifyFromExistingText(doc).Should().BeTrue();
        PhotoShootGear.SpecifyFromExistingText(doc).Should().BeFalse();

        doc.Shots.Single(shot => shot.Id == "card-shot").Equipment.Select(item => item.Key).Should().Contain(new[] { "cards", "silk" });
        doc.Shots.Single(shot => shot.Id == "card-shot").Equipment.Single(item => item.Key == "silk").Quantity.Should().Be(3);
        doc.Shots.Single(shot => shot.Id == "head").Equipment.Should().BeEmpty();
        doc.Shots.Single(shot => shot.Id == "head").EquipmentSpecified.Should().BeTrue();

        var wand = PhotoShootGear.PackingList(doc).Single(line => line.Key == "wand");
        wand.Quantity.Should().Be(1);
        wand.ShotIds.Should().BeEquivalentTo(new[] { "wand-1", "wand-2" });
        wand.Optional.Should().BeFalse();
        wand.Supplier.Should().Be(EquipmentSupplier.Bring);

        PhotoShootGear.ApplyCommand(doc, new PackingCommand { Action = "pack", Key = "wand" });
        PhotoShootGear.PackingList(doc).Single(line => line.Key == "wand").Packed.Should().BeTrue();
        PhotoShootGear.ApplyCommand(doc, new PackingCommand { Action = "add", Name = "Gaffer tape", Quantity = 1 });
        PhotoShootGear.PackingList(doc).Should().Contain(line => line.Custom && line.Name == "Gaffer tape");
    }

    private static PhotoShootDocument Shoot() => new()
    {
        Id = "shoot",
        Shots = new List<PhotoShot> { new() { Id = "shot-1", Title = "Warm smile", SetupId = "empty" } },
        Setups = new List<PhotoSetup> { new() { Id = "empty", Name = "Headshots" } }
    };

    private static PhotoReference Photo(string id, string shotId, string title, string imageId, string performer = "") => new()
    {
        Id = id,
        ShotIds = new List<string> { shotId },
        Title = title,
        ImageId = imageId,
        ImageContentType = "image/jpeg",
        PerformerOrCharacter = performer
    };
}
