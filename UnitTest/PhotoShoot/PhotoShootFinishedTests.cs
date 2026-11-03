using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;

namespace UnitTest.PhotoShoot;

public class PhotoShootFinishedTests
{
    private static PhotoShootDocument Doc()
    {
        var doc = PhotoShootEditor.CreateShoot("shoot", "Shoot", "", "", DateTime.UtcNow);
        doc.Shots.Add(new PhotoShot { Id = "a", Title = "Warm smile", MarketingPurpose = "Birthday party ads", Status = ShotStatus.Completed });
        doc.Shots.Add(new PhotoShot { Id = "b", Title = "LinkedIn portrait", MarketingPurpose = "Corporate event proposals; LinkedIn" });
        return PhotoShootEditor.Normalize(doc);
    }

    [Fact]
    public void Rank1_IsTheFinal_AndTheNextOnePromotes()
    {
        var doc = Doc();
        var first = PhotoShootFinished.Add(doc, "a", "img1", "image/jpeg", null, "Photographer", DateTime.UtcNow);
        var second = PhotoShootFinished.Add(doc, "a", "img2", "image/jpeg", null, "Photographer", DateTime.UtcNow.AddMinutes(1));
        first.Value!.Rank.Should().Be(1);
        second.Value!.Rank.Should().Be(2);
        doc.Shots[0].DeliverableStatus.Should().Be(DeliverableStatus.Final);

        PhotoShootFinished.Move(doc, second.Value.Id, new FinishedMove { MakeFinal = true });
        second.Value.Rank.Should().Be(1);
        first.Value.Rank.Should().Be(2);

        PhotoShootFinished.Hide(doc, second.Value.Id, "Photographer", DateTime.UtcNow);
        first.Value.Rank.Should().Be(1);
        PhotoShootFinished.ForCaller(doc, PhotoShootAccess.Photographer).FinishedPhotos.Should().ContainSingle(photo => photo.Id == first.Value.Id);

        PhotoShootFinished.Hide(doc, first.Value.Id, "Photographer", DateTime.UtcNow);
        doc.Shots.Single(shot => shot.Id == "a").DeliverableStatus.Should().Be(DeliverableStatus.Captured);
    }

    [Fact]
    public void PlaceInbox_FollowsCameraTime_OntoEmptyPoses()
    {
        var doc = Doc();
        var later = PhotoShootFinished.Add(doc, "", "later", "image/jpeg", new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc), "Photographer", DateTime.UtcNow);
        var earlier = PhotoShootFinished.Add(doc, "", "earlier", "image/jpeg", new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc), "Photographer", DateTime.UtcNow);
        PhotoShootFinished.Add(doc, "b", "kept", "image/jpeg", null, "Photographer", DateTime.UtcNow);

        PhotoShootFinished.PlaceInbox(doc).Should().Be(1);
        earlier.Value!.ShotId.Should().Be("a");
        later.Value!.ShotId.Should().Be("");
        doc.FinishedPhotos.Single(photo => photo.ImageId == "kept").ShotId.Should().Be("b");
    }

    [Fact]
    public void HardDelete_IsNotAvailableToThePhotographerRole()
    {
        PhotoShootAuthorization.CanHardDelete(PhotoShootAccess.Photographer).Should().BeFalse();
        PhotoShootAuthorization.CanHardDelete(PhotoShootAccess.Owner).Should().BeFalse();
        PhotoShootAuthorization.CanHardDelete(PhotoShootAccess.Admin).Should().BeTrue();
        PhotoShootAuthorization.CanUploadPhotos(PhotoShootAccess.Photographer).Should().BeTrue();
        // The old public "LukeAdmin" password no longer grants anything; admin only comes from the bound Microsoft account.
        PhotoShootAuthorization.Resolve(null, null, "site", null, null, "LukeAdmin", null).Access.Should().Be(PhotoShootAccess.None);
        var principal = PhotoShootAuthorization.EncodePrincipal(new SwaClientPrincipal { IdentityProvider = "aad", UserId = "u1", UserDetails = "lukeroberthammer@outlook.com" });
        PhotoShootAuthorization.Resolve(principal, null, "site", null, null, microsoftAdmin: true).Access.Should().Be(PhotoShootAccess.Admin);
        PhotoShootAuthorization.Resolve(null, null, "site", null, null, PhotoShootAuthorization.MasterPassword, null, microsoftAdmin: true, passwordAccess: PhotoShootAccess.Admin)
            .Access.Should().Be(PhotoShootAccess.Owner, "the password can never reach admin, even if misconfigured");
    }

    [Fact]
    public void Owner_CanDoEverythingExceptHardDelete()
    {
        // Owner level from the password now needs the explicit PHOTOSHOOT_LEGACY_PASSWORD=owner setting.
        PhotoShootAuthorization.Resolve(null, null, "site", null, null, PhotoShootAuthorization.MasterPassword, null).Access.Should().Be(PhotoShootAccess.Photographer);
        var owner = PhotoShootAuthorization.Resolve(null, null, "site", null, null, PhotoShootAuthorization.MasterPassword, null, passwordAccess: PhotoShootAccess.Owner);
        owner.Access.Should().Be(PhotoShootAccess.Owner);
        PhotoShootAuthorization.CanEdit(owner.Access).Should().BeTrue();
        PhotoShootAuthorization.CanUploadPhotos(owner.Access).Should().BeTrue();
        PhotoShootAuthorization.CanDownloadFinals(owner.Access).Should().BeTrue();
        PhotoShootAuthorization.CanSeeRemoved(owner.Access).Should().BeTrue();
        PhotoShootAuthorization.CanHardDelete(owner.Access).Should().BeFalse();
        PhotoShootAuthorization.CanSeeRemoved(PhotoShootAccess.Photographer).Should().BeFalse();
    }

    [Fact]
    public void Admin_HasEveryOwnerPermission()
    {
        PhotoShootAuthorization.CanEdit(PhotoShootAccess.Admin).Should().BeTrue();
        PhotoShootAuthorization.CanSeeRemoved(PhotoShootAccess.Admin).Should().BeTrue();
        PhotoShootAuthorization.CanDownloadFinals(PhotoShootAccess.Admin).Should().BeTrue();
        PhotoShootSessionEngine.IsAllowed(PhotoShootAccess.Admin, SessionAction.End, new PhotoShootSession()).Should().BeTrue();
    }

    [Fact]
    public void Markets_ComeFromTheExistingMarketingText()
    {
        var doc = Doc();
        doc.Shots.Single(shot => shot.Id == "a").Markets.Should().Contain(ShowMarket.Kids);
        doc.Shots.Single(shot => shot.Id == "b").Markets.Should().Contain(ShowMarket.Corporate);
    }

    [Fact]
    public void BlockHeading_DropsWhiteSeamless_AndKeepsTheCountSeparate()
    {
        PhotoShootDisplay.StripWhite("White Seamless - Tight Headshot").Should().Be("Tight Headshot");
        var doc = Doc();
        doc.Setups.Add(new PhotoSetup { Id = "s1", Name = "White Seamless - Tight Headshot", BlockName = "Tight headshots and expressions", ShootingBlock = 1, Sequence = 1 });
        doc.Setups.Add(new PhotoSetup { Id = "s2", Name = "White Seamless - Waist-Up, Space Camera Left", BlockName = "Waist-up advertising gestures", ShootingBlock = 2, Sequence = 2 });
        doc.Shots[0].SetupId = "s1";
        doc.Shots[1].SetupId = "s2";
        var blocks = PhotoShootDisplay.Blocks(doc);
        blocks[0].Title.Should().Be("Tight headshots and expressions");
        blocks[0].ShotCount.Should().Be(1);
        blocks[0].Title.Should().NotContain("5");
        blocks[0].Title.Should().NotContain("White");
    }
}
