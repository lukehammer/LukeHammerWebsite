using BlazorApp.Shared.PhotoReview;
using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;
using static UnitTest.PhotoReview.PhotoReviewTestData;

namespace UnitTest.PhotoReview;

public class PhotoReviewProjectionTests
{
    [Fact]
    public void Summary_ListsEveryPlannedShot_AndNothingIsSuccessfulUntilLukeSaysSo()
    {
        var planner = Planner();
        var catalog = ImportedCatalog(planner, Photo(1, "shot-001"), Photo(2, "shot-001"), Photo(3, "shot-003"));
        var summary = new PhotoReviewProjection(planner, catalog, new ParticipationDocument(), new ReviewActor(), new ReviewViewer()).Summary();

        summary.Shots.Should().HaveCount(6);
        summary.Shots.Select(s => s.Number).Should().Equal(1, 2, 3, 4, 5, 6);
        summary.Counts.ShotsWithPhotos.Should().Be(2);
        summary.Counts.ShotsWithoutMatch.Should().Be(4);
        summary.Shots.Should().OnlyContain(s => s.Status == ShotReviewStatus.NeedsReview && !s.StatusConfirmed);
        summary.Shots[0].PhotoIds.Should().HaveCount(2);
        summary.Categories.Should().HaveCount(9);
        summary.Counts.VersionsByKind[ArtifactKind.Original].Should().Be(3);
        summary.Counts.VersionsByKind[ArtifactKind.Transparent].Should().Be(0);
    }

    [Fact]
    public void PhotoDetail_SaysWhichVersionsAreNotCreatedYet()
    {
        var planner = Planner();
        var catalog = ImportedCatalog(planner, Photo(1, "shot-001"));
        var detail = new PhotoReviewProjection(planner, catalog, new ParticipationDocument(), new ReviewActor(), new ReviewViewer()).ShotDetail("shot-001")!;
        var photo = detail.Photos.Single();
        photo.Versions.Should().ContainSingle(v => v.Kind == ArtifactKind.Original);
        photo.MissingKinds.Should().BeEquivalentTo(new[]
        {
            ArtifactKind.Cropped, ArtifactKind.Transparent, ArtifactKind.CroppedTransparent, ArtifactKind.WhiteBackground, ArtifactKind.CroppedWhiteBackground
        });
        detail.Brief.FacialExpression.Should().Be("Big smile");
        detail.PreviousShotId.Should().BeEmpty();
        detail.NextShotId.Should().Be("shot-002");
    }

    [Fact]
    public void HiddenPhotos_AreOnlyVisibleToTheAdmin()
    {
        var planner = Planner();
        var catalog = ImportedCatalog(planner, Photo(1, "shot-001"), Photo(2, "shot-001"));
        PhotoReviewAdminOps.SetPhotoHidden(catalog, Admin, catalog.Photos[1].Id, true, Now);
        new PhotoReviewProjection(planner, catalog, new ParticipationDocument(), new ReviewActor(), new ReviewViewer()).Summary().Photos.Should().HaveCount(1);
        new PhotoReviewProjection(planner, catalog, new ParticipationDocument(), Admin, new ReviewViewer()).Summary().Photos.Should().HaveCount(2);
        PhotoReviewAdminOps.SetPhotoHidden(catalog, new ReviewActor { VisitorId = "x" }, catalog.Photos[0].Id, true, Now).Status.Should().Be(403);
    }

    [Fact]
    public void Ratings_ShowAggregateAndOnlyMyOwnVote()
    {
        var planner = Planner();
        var catalog = ImportedCatalog(planner, Photo(1, "shot-001"));
        var photoId = catalog.Photos[0].Id;
        var (people, alice, _) = WithVisitor(name: "Alice");
        var (_, bob, _) = WithVisitor(people, "Bob");
        bool Exists(string id) => id == photoId;
        PhotoReviewParticipation.Rate(people, alice, photoId, 8, Exists, Now);
        PhotoReviewParticipation.Rate(people, bob, photoId, 5, Exists, Now);

        var forAlice = new PhotoReviewProjection(planner, catalog, people, alice, new ReviewViewer()).Summary().Photos.Single();
        forAlice.Average.Should().Be(6.5);
        forAlice.RatingCount.Should().Be(2);
        forAlice.MyRating.Should().Be(8);
        new PhotoReviewProjection(planner, catalog, people, new ReviewActor(), new ReviewViewer()).Summary().Photos.Single().MyRating.Should().BeNull();
    }

    [Fact]
    public void SecondShootQueue_HasUncapturedAndWeakShots_AndApprovedIdeas_ButNotSuccesses()
    {
        var planner = Planner();
        var planned = PhotoReviewProjection.PlannedRefs(planner);
        var catalog = ImportedCatalog(planner, Photo(1, "shot-001"), Photo(2, "shot-002"), Photo(3, "shot-003"));
        PhotoReviewAdminOps.SetShotStatus(catalog, planned, Admin, "shot-001", ShotReviewStatus.Successful, "", Now);
        PhotoReviewAdminOps.SetShotStatus(catalog, planned, Admin, "shot-002", ShotReviewStatus.PartiallySuccessful, "Hands cut off", Now);
        PhotoReviewAdminOps.SetShotStatus(catalog, planned, Admin, "shot-004", ShotReviewStatus.NotCaptured, "", Now);
        PhotoReviewAdminOps.SetShotStatus(catalog, planned, new ReviewActor { VisitorId = "v" }, "shot-003", ShotReviewStatus.Successful, "", Now).Status.Should().Be(403, "visitors can only suggest statuses");

        var (people, bard, _) = WithVisitor();
        var approved = PhotoReviewParticipation.AddSuggestion(people, bard, SuggestionKind.NewPose, "", null, "Floating card", "Card between fingertips", "", _ => true, Now).CreatedId;
        PhotoReviewParticipation.AddSuggestion(people, bard, SuggestionKind.NewPose, "", null, "Pending idea", "Not decided yet", "", _ => true, Now);
        PhotoReviewParticipation.DecideSuggestion(people, Admin, approved, SuggestionState.Approved, "", Now);

        var projection = new PhotoReviewProjection(planner, catalog, people, new ReviewActor(), new ReviewViewer());
        var queue = projection.Queue();
        queue.Select(item => item.Key).Should().BeEquivalentTo("shot:shot-002", "shot:shot-004", "shot:shot-005", "shot:shot-006", "suggestion:" + approved);
        queue.Single(i => i.Key == "shot:shot-002").Reason.Should().Be("Partially successful (Luke)");
        queue.Single(i => i.Key == "shot:shot-002").ReasonConfirmed.Should().BeTrue();
        queue.Single(i => i.Key == "shot:shot-002").Examples.Should().ContainSingle("earlier attempts are shown on set");
        queue.Single(i => i.Key == "shot:shot-005").ReasonConfirmed.Should().BeFalse("no photo yet, but Luke hasn't confirmed it");
        projection.SecondShoot(Now).PendingSuggestions.Should().ContainSingle(s => s.Title == "Pending idea");

        var keys = queue.Select(i => i.Key).ToHashSet();
        PhotoReviewAdminOps.UpdateQueue(catalog, Admin, "shot:shot-004", new QueueUpdateRequest { Priority = QueuePriority.High, Notes = "Bring the top hat", Excluded = false }, keys.Contains, Now).Ok.Should().BeTrue();
        PhotoReviewAdminOps.UpdateQueue(catalog, Admin, "shot:shot-001", new QueueUpdateRequest(), keys.Contains, Now).Status.Should().Be(404);
        new PhotoReviewProjection(planner, catalog, people, new ReviewActor(), new ReviewViewer()).Queue().First().Notes.Should().Be("Bring the top hat");

        planner.Shots.Should().HaveCount(6, "suggestions never change the approved plan");
    }

    [Fact]
    public void CodexSuggestions_DriveProvisionalStatusAndQueue_ButLukeAlwaysWins()
    {
        var planner = Planner();
        var planned = PhotoReviewProjection.PlannedRefs(planner);
        var manifest = Manifest(Photo(1, "shot-001"), Photo(2, "shot-002"), Photo(3, "shot-003"));
        ManifestShot Suggest(string id, string status) => new ManifestShot { ShotId = id, SuggestedStatus = status, SuggestionReason = "Codex reason for " + id, SuggestedBy = "Codex" };
        manifest.Shots.AddRange(new[]
        {
            Suggest("shot-001", "Successful"), Suggest("shot-002", "PartiallySuccessful"), Suggest("shot-003", "PartiallySuccessful"),
            Suggest("shot-004", "NotCaptured"), Suggest("shot-005", "NotCaptured"), Suggest("shot-006", "NotCaptured")
        });
        var catalog = new ReviewCatalog();
        PhotoReviewIngest.Apply(catalog, manifest, planned, new Dictionary<string, ThumbInfo>(), _ => null, Now).Ok.Should().BeTrue();

        // Luke overrides two: Codex said partial, Luke says successful; Codex said not captured, Luke agrees.
        PhotoReviewAdminOps.SetShotStatus(catalog, planned, Admin, "shot-003", ShotReviewStatus.Successful, "Good enough", Now);
        PhotoReviewAdminOps.SetShotStatus(catalog, planned, Admin, "shot-004", ShotReviewStatus.NotCaptured, "", Now);

        var projection = new PhotoReviewProjection(planner, catalog, new ParticipationDocument(), new ReviewActor(), new ReviewViewer());
        var summary = projection.Summary();
        var byId = summary.Shots.ToDictionary(s => s.ShotId);
        byId["shot-001"].Status.Should().Be(ShotReviewStatus.NeedsReview, "a suggestion is not Luke's verdict");
        byId["shot-001"].ProvisionalStatus.Should().Be(ShotReviewStatus.Successful);
        byId["shot-001"].ProvisionalSource.Should().Be("suggestion");
        byId["shot-001"].SuggestionReason.Should().Be("Codex reason for shot-001");
        byId["shot-003"].ProvisionalStatus.Should().Be(ShotReviewStatus.Successful, "owner override wins");
        byId["shot-003"].ProvisionalSource.Should().Be("owner");
        summary.Counts.ByStatus[ShotReviewStatus.Successful].Should().Be(1);
        summary.Counts.ByProvisionalStatus[ShotReviewStatus.Successful].Should().Be(2);
        summary.Counts.ByProvisionalStatus[ShotReviewStatus.PartiallySuccessful].Should().Be(1);
        summary.Counts.ByProvisionalStatus[ShotReviewStatus.NotCaptured].Should().Be(3);
        summary.Counts.OwnerConfirmed.Should().Be(2);

        var queue = projection.Queue().ToDictionary(i => i.ShotId);
        queue.Keys.Should().BeEquivalentTo("shot-002", "shot-004", "shot-005", "shot-006");
        queue["shot-002"].ReasonConfirmed.Should().BeFalse();
        queue["shot-002"].Reason.Should().Contain("provisional").And.Contain("Codex");
        queue["shot-002"].ReasonDetail.Should().Be("Codex reason for shot-002");
        queue["shot-002"].Status.Should().Be(ShotReviewStatus.PartiallySuccessful);
        queue["shot-004"].ReasonConfirmed.Should().BeTrue();

        // Re-importing the same suggestions never undoes Luke's decisions.
        PhotoReviewIngest.Apply(catalog, manifest, planned, new Dictionary<string, ThumbInfo>(), _ => null, Now.AddDays(1)).Ok.Should().BeTrue();
        catalog.Shots.Single(s => s.ShotId == "shot-003").OwnerStatus.Should().Be(ShotReviewStatus.Successful);
    }

    [Fact]
    public void PublicReferences_AreOnlyThoseLinkedToShots()
    {
        var planner = Planner();
        planner.References.Add(new PhotoReference { Id = "ref-linked", ImageId = "img1", ShotIds = new List<string> { "shot-001" } });
        planner.References.Add(new PhotoReference { Id = "ref-loose", ImageId = "img2" });
        PhotoReviewProjection.IsPublicReference(planner, "ref-linked").Should().BeTrue();
        PhotoReviewProjection.IsPublicReference(planner, "ref-loose").Should().BeFalse();
        PhotoReviewProjection.IsPublicReference(planner, "missing").Should().BeFalse();
    }

    [Fact]
    public void Viewers_NeverSeeVisitorIdsOrSecrets_ExceptAdminSeesIds()
    {
        var planner = Planner();
        var (people, alice, _) = WithVisitor();
        PhotoReviewParticipation.AddComment(people, alice, CommentTarget.Shot, "shot-001", null, "Hi", "", (_, _) => true, Now);
        var publicView = new PhotoReviewProjection(planner, new ReviewCatalog(), people, new ReviewActor(), new ReviewViewer()).ShotDetail("shot-001")!;
        publicView.Comments.Single().AuthorId.Should().BeEmpty();
        publicView.Comments.Single().AuthorTag.Should().StartWith("#").And.HaveLength(5);
        System.Text.Json.JsonSerializer.Serialize(publicView).Should().NotContain(people.Visitors[0].SecretHash).And.NotContain(alice.VisitorId);
        new PhotoReviewProjection(planner, new ReviewCatalog(), people, Admin, new ReviewViewer()).ShotDetail("shot-001")!.Comments.Single().AuthorId.Should().Be(alice.VisitorId);
    }
}
