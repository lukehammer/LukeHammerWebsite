using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;
using static UnitTest.PhotoShoot.PhotoShootTestData;

namespace UnitTest.PhotoShoot;

public class PhotoShootPlannerTests
{
    [Fact]
    public void CreateShot_AppendsToSetupWithFreshIdAndNotStarted()
    {
        var doc = TwoSetups();
        var setupA = doc.Setups.Single(s => s.Name == "A");

        var result = PhotoShootEditor.CreateShot(doc, new PhotoShot { Title = "  New Shot  ", SetupId = setupA.Id, Status = ShotStatus.Completed }, Now);

        result.Ok.Should().BeTrue();
        var shot = result.Value!;
        shot.Id.Should().NotBeNullOrEmpty();
        shot.Title.Should().Be("New Shot");
        shot.Status.Should().Be(ShotStatus.NotStarted, "new shots always start fresh");
        shot.ShootId.Should().Be(doc.Id);
        OrderTitles(doc).Should().Equal("a1", "a2", "a3", "New Shot", "b1", "b2");
    }

    [Fact]
    public void CreateShot_RejectsMissingTitleAndUnknownSetup()
    {
        var doc = TwoSetups();
        var result = PhotoShootEditor.CreateShot(doc, new PhotoShot { Title = " ", SetupId = "nope", EstimatedMinutes = 999 }, Now);

        result.Ok.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Title"));
        result.Errors.Should().Contain(e => e.Contains("Setup not found"));
        result.Errors.Should().Contain(e => e.Contains("Estimated time"));
        doc.Shots.Should().HaveCount(5);
    }

    [Fact]
    public void UpdateShot_CopiesFieldsMovesSetupAndAppliesStatus()
    {
        var doc = TwoSetups();
        var a1 = Shot(doc, "a1");
        var setupB = doc.Setups.Single(s => s.Name == "B");
        var edit = new PhotoShot
        {
            Title = "a1 edited",
            SetupId = setupB.Id,
            Priority = ShotPriority.MustHave,
            Status = ShotStatus.Completed,
            Props = new List<string> { "Wand", " wand ", "", "Cards" },
            EstimatedMinutes = 7,
            PhotographerInstructions = "Tight crop"
        };

        var result = PhotoShootEditor.UpdateShot(doc, a1.Id, edit, Now);

        result.Ok.Should().BeTrue();
        a1.Title.Should().Be("a1 edited");
        a1.SetupId.Should().Be(setupB.Id);
        a1.Status.Should().Be(ShotStatus.Completed);
        a1.CompletedUtc.Should().Be(Now);
        a1.Props.Should().Equal("Wand", "Cards");
        a1.PhotographerInstructions.Should().Be("Tight crop");
        OrderTitles(doc).Should().Equal("a2", "a3", "b1", "b2", "a1 edited");
    }

    [Fact]
    public void UpdateShot_UnknownId_IsNotFound()
    {
        var doc = TwoSetups();
        var result = PhotoShootEditor.UpdateShot(doc, "missing", new PhotoShot { Title = "x" }, Now);
        result.NotFound.Should().BeTrue();
    }

    [Fact]
    public void DeleteShot_RemembersSeedKeyAndUnlinksReferences()
    {
        var doc = TwoSetups();
        var a2 = Shot(doc, "a2");
        a2.SeedKey = "shot:a2";
        var reference = PhotoShootEditor.CreateReference(doc, new PhotoReference { Title = "Ref", ShotIds = new List<string> { a2.Id } }, Now).Value!;

        PhotoShootEditor.DeleteShot(doc, a2.Id).Should().BeTrue();

        doc.Shots.Should().NotContain(s => s.Id == a2.Id);
        doc.DeletedSeedKeys.Should().Contain("shot:a2");
        reference.ShotIds.Should().BeEmpty();
        OrderTitles(doc).Should().Equal("a1", "a3", "b1", "b2");
    }

    [Fact]
    public void OrderedShots_FollowsSetupSequenceThenShotSequence_UnassignedLast()
    {
        var doc = TwoSetups();
        AddShot(doc, "", "loose", ShotPriority.Bonus, 1);

        OrderTitles(doc).Should().Equal("a1", "a2", "a3", "b1", "b2", "loose");
        PhotoShootPlanner.OrderNumber(doc, Shot(doc, "b1").Id).Should().Be(4);
    }

    [Fact]
    public void MoveShot_SwapsWithinSetup()
    {
        var doc = TwoSetups();
        PhotoShootPlanner.MoveShot(doc, Shot(doc, "a2").Id, -1).Should().BeTrue();
        OrderTitles(doc).Should().Equal("a2", "a1", "a3", "b1", "b2");

        PhotoShootPlanner.MoveShot(doc, Shot(doc, "a2").Id, +1).Should().BeTrue();
        OrderTitles(doc).Should().Equal("a1", "a2", "a3", "b1", "b2");
    }

    [Fact]
    public void MoveShot_AtSetupEdge_CrossesIntoNeighbourSetup()
    {
        var doc = TwoSetups();
        var setupA = doc.Setups.Single(s => s.Name == "A");
        var setupB = doc.Setups.Single(s => s.Name == "B");

        PhotoShootPlanner.MoveShot(doc, Shot(doc, "a3").Id, +1).Should().BeTrue();
        Shot(doc, "a3").SetupId.Should().Be(setupB.Id);
        OrderTitles(doc).Should().Equal("a1", "a2", "a3", "b1", "b2");

        PhotoShootPlanner.MoveShot(doc, Shot(doc, "a3").Id, -1).Should().BeTrue();
        Shot(doc, "a3").SetupId.Should().Be(setupA.Id);
        OrderTitles(doc).Should().Equal("a1", "a2", "a3", "b1", "b2");
    }

    [Fact]
    public void MoveShot_AtVeryEnds_DoesNothing()
    {
        var doc = TwoSetups();
        PhotoShootPlanner.MoveShot(doc, Shot(doc, "a1").Id, -1).Should().BeFalse();
        PhotoShootPlanner.MoveShot(doc, Shot(doc, "b2").Id, +1).Should().BeFalse();
        OrderTitles(doc).Should().Equal("a1", "a2", "a3", "b1", "b2");
    }

    [Fact]
    public void MoveShotToSetup_AppendsAtEnd()
    {
        var doc = TwoSetups();
        var setupB = doc.Setups.Single(s => s.Name == "B");
        PhotoShootPlanner.MoveShotToSetup(doc, Shot(doc, "a1").Id, setupB.Id).Should().BeTrue();
        OrderTitles(doc).Should().Equal("a2", "a3", "b1", "b2", "a1");
        PhotoShootPlanner.MoveShotToSetup(doc, Shot(doc, "a1").Id, "unknown").Should().BeFalse();
    }

    [Fact]
    public void PlaceAfter_PutsShotImmediatelyAfterAnchor()
    {
        var doc = TwoSetups();
        PhotoShootPlanner.PlaceAfter(doc, Shot(doc, "b2").Id, Shot(doc, "a1").Id).Should().BeTrue();
        OrderTitles(doc).Should().Equal("a1", "b2", "a2", "a3", "b1");
    }

    [Fact]
    public void MoveSetup_ReordersGroups()
    {
        var doc = TwoSetups();
        var setupB = doc.Setups.Single(s => s.Name == "B");
        PhotoShootPlanner.MoveSetup(doc, setupB.Id, -1).Should().BeTrue();
        OrderTitles(doc).Should().Equal("b1", "b2", "a1", "a2", "a3");
        PhotoShootPlanner.MoveSetup(doc, setupB.Id, -1).Should().BeFalse();
    }

    [Fact]
    public void Groups_IncludeEmptySetupsAndUnassignedBucket()
    {
        var doc = TwoSetups();
        PhotoShootEditor.CreateSetup(doc, new PhotoSetup { Name = "Empty" });
        AddShot(doc, "", "loose", ShotPriority.Bonus, 1);

        var groups = PhotoShootPlanner.Groups(doc);
        groups.Select(g => g.Name).Should().Equal("A", "B", "Empty", "Unassigned");
        groups[0].MustHaveCount.Should().Be(1);
        groups[0].EstimatedMinutes.Should().Be(9);
        PhotoShootPlanner.Groups(doc, includeEmptySetups: false).Select(g => g.Name).Should().Equal("A", "B", "Unassigned");
    }

    [Fact]
    public void DeleteSetup_MovesShotsToUnassignedOrTarget()
    {
        var doc = TwoSetups();
        var setupA = doc.Setups.Single(s => s.Name == "A");
        var setupB = doc.Setups.Single(s => s.Name == "B");

        PhotoShootEditor.DeleteSetup(doc, setupA.Id, setupB.Id).Should().BeTrue();

        doc.Setups.Should().ContainSingle();
        doc.Shots.Should().OnlyContain(s => s.SetupId == setupB.Id);
        OrderTitles(doc).Should().Equal("b1", "b2", "a1", "a2", "a3");
    }

    [Fact]
    public void Progress_CountsOverallAndMustHaveSeparately()
    {
        var doc = TwoSetups();
        PhotoShootEditor.SetStatus(doc, Shot(doc, "a2").Id, ShotStatus.Completed, Now);
        PhotoShootEditor.SetStatus(doc, Shot(doc, "a1").Id, ShotStatus.Completed, Now);
        PhotoShootEditor.SetStatus(doc, Shot(doc, "b2").Id, ShotStatus.Skipped, Now);

        var progress = PhotoShootPlanner.Progress(doc);

        progress.Total.Should().Be(5);
        progress.Completed.Should().Be(2);
        progress.Remaining.Should().Be(3);
        progress.Skipped.Should().Be(1);
        progress.Percent.Should().Be(40);
        progress.MustHaveTotal.Should().Be(2);
        progress.MustHaveCompleted.Should().Be(1);
        progress.MustHaveRemaining.Should().Be(1);
        progress.MustHavePercent.Should().Be(50);
    }

    [Fact]
    public void Progress_EmptyShoot_IsZeroNotDivideByZero()
    {
        var doc = PhotoShootEditor.CreateShoot("x", "Empty", "", "", Now);
        var progress = PhotoShootPlanner.Progress(doc);
        progress.Percent.Should().Be(0);
        progress.MustHavePercent.Should().Be(0);
        PhotoShootPlanner.EstimateRemaining(doc).TotalMinutes.Should().Be(0);
    }

    [Fact]
    public void EstimateTotal_SumsShotsAndChangeoversAndBreaks()
    {
        var doc = TwoSetups();
        PhotoShootPlanner.EstimateTotal(doc).TotalMinutes.Should().Be(20 + 25);

        doc.BreakEveryMinutes = 20;
        doc.BreakDurationMinutes = 5;
        var estimate = PhotoShootPlanner.EstimateTotal(doc);
        estimate.ShootingMinutes.Should().Be(20);
        estimate.ChangeoverMinutes.Should().Be(25);
        estimate.BreakMinutes.Should().Be(10);
        estimate.TotalMinutes.Should().Be(55);
    }

    [Fact]
    public void EstimateRemaining_ExcludesCompletedShotsAndFinishedSetupChangeovers()
    {
        var doc = TwoSetups();
        foreach (var title in new[] { "a1", "a2", "a3" })
        {
            PhotoShootEditor.SetStatus(doc, Shot(doc, title).Id, ShotStatus.Completed, Now);
        }

        var remaining = PhotoShootPlanner.EstimateRemaining(doc);
        remaining.ShootingMinutes.Should().Be(11);
        remaining.ChangeoverMinutes.Should().Be(10);

        var mustHaveOnly = PhotoShootPlanner.EstimateRemaining(doc, mustHaveOnly: true);
        mustHaveOnly.ShootingMinutes.Should().Be(5);
    }

    [Fact]
    public void EstimateRemaining_DuringActiveSession_SkipsChangeoverOfCurrentSetup()
    {
        var doc = TwoSetups();
        doc.Session.IsActive = true;
        doc.Session.CurrentShotId = Shot(doc, "a1").Id;
        PhotoShootPlanner.EstimateRemaining(doc).ChangeoverMinutes.Should().Be(10);
    }

    [Fact]
    public void ApplySuggestedOrder_PutsMustHaveFirstWithinSetups()
    {
        var doc = TwoSetups();
        PhotoShootPlanner.ApplySuggestedOrder(doc);
        OrderTitles(doc).Should().Equal("a2", "a1", "a3", "b1", "b2");
    }

    [Fact]
    public void ApplySuggestedOrder_GroupsSameBackgroundAndKeepsKidSetupSlot()
    {
        var doc = PhotoShootEditor.CreateShoot("s", "S", "", "", Now);
        var white1 = PhotoShootEditor.CreateSetup(doc, new PhotoSetup { Name = "white1", Background = "White seamless paper" }).Value!;
        var teal = PhotoShootEditor.CreateSetup(doc, new PhotoSetup { Name = "teal", Background = "Teal backdrop" }).Value!;
        var kids = PhotoShootEditor.CreateSetup(doc, new PhotoSetup { Name = "kids", Background = "Teal backdrop", RequiresChildren = true }).Value!;
        var white2 = PhotoShootEditor.CreateSetup(doc, new PhotoSetup { Name = "white2", Background = "white sweep" }).Value!;
        AddShot(doc, white1.Id, "w1", ShotPriority.Standard, 1);
        AddShot(doc, teal.Id, "t1", ShotPriority.MustHave, 1);
        AddShot(doc, kids.Id, "k1", ShotPriority.MustHave, 1);
        AddShot(doc, white2.Id, "w2", ShotPriority.MustHave, 1);
        AddShot(doc, white2.Id, "w3", ShotPriority.MustHave, 1);

        PhotoShootPlanner.ApplySuggestedOrder(doc);

        PhotoShootPlanner.OrderedSetups(doc).Select(s => s.Name).Should().Equal("white2", "white1", "kids", "teal");
    }

    [Theory]
    [InlineData("White seamless paper", "white")]
    [InlineData("Seamless white, lit to pure white", "white")]
    [InlineData("Grey mottled canvas", "gray")]
    [InlineData("Teal (#08AAB9) paper", "teal")]
    [InlineData("Brick wall", "brick wall")]
    [InlineData("", "")]
    public void BackgroundFamily_GroupsByColourWord(string background, string expected)
    {
        PhotoShootPlanner.BackgroundFamily(background).Should().Be(expected);
    }

    [Fact]
    public void Normalize_RepairsNullsAndDanglingIds()
    {
        var doc = new PhotoShootDocument
        {
            Id = "abc",
            Setups = null!,
            Shots = new List<PhotoShot> { new() { Id = "s1", SetupId = "ghost", Title = "x", Props = null! } },
            References = new List<PhotoReference> { new() { Id = "r1", Title = "r", ShotIds = new List<string> { "s1", "gone" } } },
            Session = new PhotoShootSession { CurrentShotId = "gone" }
        };

        PhotoShootEditor.Normalize(doc);

        doc.Setups.Should().BeEmpty();
        doc.Shots[0].SetupId.Should().BeEmpty();
        doc.Shots[0].Props.Should().BeEmpty();
        doc.Shots[0].ShootId.Should().Be("abc");
        doc.References[0].ShotIds.Should().Equal("s1");
        doc.Session.CurrentShotId.Should().BeEmpty();
    }

    [Fact]
    public void ReferencesForShot_ReturnsLinkedOnly_AndShotWithoutReferencesIsEmpty()
    {
        var doc = TwoSetups();
        var a1 = Shot(doc, "a1");
        PhotoShootEditor.CreateReference(doc, new PhotoReference { Title = "R", ShotIds = new List<string> { a1.Id } }, Now);

        PhotoShootEditor.ReferencesForShot(doc, a1.Id).Should().ContainSingle();
        PhotoShootEditor.ReferencesForShot(doc, Shot(doc, "a2").Id).Should().BeEmpty();
    }

    [Fact]
    public void EstimateFormat_IsHumanReadable()
    {
        PhotoShootEstimate.Format(0).Should().Be("0 min");
        PhotoShootEstimate.Format(45).Should().Be("45 min");
        PhotoShootEstimate.Format(60).Should().Be("1 hr");
        PhotoShootEstimate.Format(135).Should().Be("2 hr 15 min");
    }
}
