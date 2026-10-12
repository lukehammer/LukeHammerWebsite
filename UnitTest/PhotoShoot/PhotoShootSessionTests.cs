using System.Text.Json;
using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;
using static UnitTest.PhotoShoot.PhotoShootTestData;

namespace UnitTest.PhotoShoot;

public class PhotoShootSessionTests
{
    private static SessionResult Run(PhotoShootDocument doc, SessionAction action, string shotId = "", PhotoShootAccess access = PhotoShootAccess.Owner, DateTime? at = null, bool flag = false, string text = "") =>
        PhotoShootSessionEngine.Apply(doc, new SessionCommand { Action = action, ShotId = shotId, Flag = flag, Text = text }, access, access == PhotoShootAccess.Owner ? "Director" : "Photographer", at ?? Now);

    private static PhotoShot Current(PhotoShootDocument doc) => PhotoShootPlanner.FindShot(doc, doc.Session.CurrentShotId)!;

    [Theory]
    [InlineData(ShotStatus.NotStarted, ShotStatus.InProgress)]
    [InlineData(ShotStatus.NotStarted, ShotStatus.Completed)]
    [InlineData(ShotStatus.NotStarted, ShotStatus.Skipped)]
    [InlineData(ShotStatus.InProgress, ShotStatus.Completed)]
    [InlineData(ShotStatus.Completed, ShotStatus.NotStarted)]
    [InlineData(ShotStatus.Skipped, ShotStatus.InProgress)]
    public void StatusTransitions_BetweenDifferentStatuses_AreAllowed(ShotStatus from, ShotStatus to)
    {
        ShotStatusRules.IsAllowed(from, to).Should().BeTrue();
        ShotStatusRules.IsAllowed(from, from).Should().BeFalse();
    }

    [Fact]
    public void StatusTransitions_MaintainTimestampsAndActualTime()
    {
        var shot = new PhotoShot { Title = "x" };

        ShotStatusRules.Apply(shot, ShotStatus.InProgress, Now).Should().BeTrue();
        shot.StartedUtc.Should().Be(Now);

        ShotStatusRules.Apply(shot, ShotStatus.Completed, Now.AddSeconds(95)).Should().BeTrue();
        shot.CompletedUtc.Should().Be(Now.AddSeconds(95));
        shot.ActualSeconds.Should().Be(95);

        ShotStatusRules.Apply(shot, ShotStatus.Completed, Now.AddSeconds(200)).Should().BeFalse("no-op when unchanged");
        shot.CompletedUtc.Should().Be(Now.AddSeconds(95));

        ShotStatusRules.Apply(shot, ShotStatus.NotStarted, Now).Should().BeTrue();
        shot.CompletedUtc.Should().BeNull();
        shot.StartedUtc.Should().BeNull();
        shot.ActualSeconds.Should().BeNull();
    }

    [Fact]
    public void CompletingWithoutStart_HasNoActualTime()
    {
        var shot = new PhotoShot { Title = "x" };
        ShotStatusRules.Apply(shot, ShotStatus.Completed, Now);
        shot.ActualSeconds.Should().BeNull();
        shot.CompletedUtc.Should().Be(Now);
    }

    [Fact]
    public void Start_SelectsFirstShotAndMarksInProgress()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start).Ok.Should().BeTrue();

        doc.Session.IsActive.Should().BeTrue();
        doc.Session.StartedUtc.Should().Be(Now);
        Current(doc).Title.Should().Be("a1");
        Current(doc).Status.Should().Be(ShotStatus.InProgress);
        doc.Session.UpdatedBy.Should().Be("Director");
    }

    [Fact]
    public void CompleteCurrent_MarksCompleteAndAdvances()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.CompleteCurrent, at: Now.AddMinutes(2)).Ok.Should().BeTrue();

        Shot(doc, "a1").Status.Should().Be(ShotStatus.Completed);
        Shot(doc, "a1").ActualSeconds.Should().Be(120);
        Current(doc).Title.Should().Be("a2");
        Current(doc).Status.Should().Be(ShotStatus.InProgress);
    }

    [Fact]
    public void SkipCurrent_ThenReturnToSkippedShotLater()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.SkipCurrent);
        Shot(doc, "a1").Status.Should().Be(ShotStatus.Skipped);
        Current(doc).Title.Should().Be("a2");

        foreach (var _ in Enumerable.Range(0, 4))
        {
            Run(doc, SessionAction.CompleteCurrent).Ok.Should().BeTrue();
        }

        Current(doc).Title.Should().Be("a1", "skipped shots come back once everything else is done");
        Current(doc).Status.Should().Be(ShotStatus.InProgress);

        var last = Run(doc, SessionAction.CompleteCurrent);
        last.Ok.Should().BeTrue();
        last.Message.Should().Contain("last");
        PhotoShootPlanner.Progress(doc).Completed.Should().Be(5);
        Run(doc, SessionAction.Next).Ok.Should().BeFalse();
    }

    [Fact]
    public void SetCurrent_LeavingUnfinishedShotPutsItBackInQueue()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.SetCurrent, Shot(doc, "b1").Id).Ok.Should().BeTrue();

        Shot(doc, "a1").Status.Should().Be(ShotStatus.NotStarted);
        Current(doc).Title.Should().Be("b1");
        Current(doc).Status.Should().Be(ShotStatus.InProgress);
    }

    [Fact]
    public void SetCurrent_ToSkippedShot_ReturnsToIt()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.SkipCurrent);
        Run(doc, SessionAction.SetCurrent, Shot(doc, "a1").Id);
        Current(doc).Title.Should().Be("a1");
        Current(doc).Status.Should().Be(ShotStatus.InProgress);
    }

    [Fact]
    public void Next_SkipsCompletedShots_AndPreviousGoesBack()
    {
        var doc = TwoSetups();
        PhotoShootEditor.SetStatus(doc, Shot(doc, "a2").Id, ShotStatus.Completed, Now);
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.Next);
        Current(doc).Title.Should().Be("a3");
        Run(doc, SessionAction.Previous);
        Current(doc).Title.Should().Be("a2", "previous walks the plain order, including completed shots");
        Current(doc).Status.Should().Be(ShotStatus.Completed, "viewing a completed shot does not reopen it");
    }

    [Fact]
    public void NextSetup_JumpsToFirstRemainingShotOfNextSetup()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.NextSetup).Ok.Should().BeTrue();
        Current(doc).Title.Should().Be("b1");
        Run(doc, SessionAction.NextSetup).Ok.Should().BeFalse();
    }

    [Fact]
    public void MustHaveOnly_NextOnlyVisitsMustHaveShots()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.SetMustHaveOnly, flag: true);
        Run(doc, SessionAction.Start);
        Current(doc).Title.Should().Be("a2");
        Run(doc, SessionAction.CompleteCurrent);
        Current(doc).Title.Should().Be("b1");
    }

    [Fact]
    public void ShootNext_ReordersWithoutChangingCurrent()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.ShootNext, Shot(doc, "b2").Id).Ok.Should().BeTrue();

        Current(doc).Title.Should().Be("a1");
        OrderTitles(doc).Should().Equal("a1", "b2", "a2", "a3", "b1");
        Run(doc, SessionAction.CompleteCurrent);
        Current(doc).Title.Should().Be("b2");
    }

    [Fact]
    public void Photographer_CanCompleteAndRequestNext_ButNotSkipOrSelect()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);

        Run(doc, SessionAction.RequestNext, access: PhotoShootAccess.Photographer).Ok.Should().BeTrue();
        doc.Session.NextRequestedUtc.Should().Be(Now);
        doc.Session.NextRequestedBy.Should().Be("Photographer");

        Run(doc, SessionAction.SkipCurrent, access: PhotoShootAccess.Photographer).ErrorStatus.Should().Be(403);
        Run(doc, SessionAction.SetCurrent, Shot(doc, "b1").Id, PhotoShootAccess.Photographer).ErrorStatus.Should().Be(403);
        Run(doc, SessionAction.Next, access: PhotoShootAccess.Photographer).ErrorStatus.Should().Be(403);

        Run(doc, SessionAction.CompleteCurrent, access: PhotoShootAccess.Photographer).Ok.Should().BeTrue();
        Shot(doc, "a1").Status.Should().Be(ShotStatus.Completed);
        doc.Session.NextRequestedUtc.Should().BeNull("moving on clears the request");
    }

    [Fact]
    public void Photographer_CanAdvanceOnlyWhenDirectorAllows()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.Next, access: PhotoShootAccess.Photographer).Ok.Should().BeFalse();

        Run(doc, SessionAction.SetPhotographerCanAdvance, flag: true);
        Run(doc, SessionAction.Next, access: PhotoShootAccess.Photographer).Ok.Should().BeTrue();
        Current(doc).Title.Should().Be("a2");
        Run(doc, SessionAction.SetPhotographerCanAdvance, flag: true, access: PhotoShootAccess.Photographer).ErrorStatus.Should().Be(403);
    }

    [Fact]
    public void NoAccess_CannotDoAnything()
    {
        var doc = TwoSetups();
        foreach (SessionAction action in Enum.GetValues(typeof(SessionAction)))
        {
            Run(doc, action, access: PhotoShootAccess.None).ErrorStatus.Should().Be(403);
        }
    }

    [Fact]
    public void AddNote_AttachesToCurrentShotAndTrims()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.AddNote, text: "  Try with glasses  ").Ok.Should().BeTrue();
        Run(doc, SessionAction.AddNote, text: "   ").Ok.Should().BeFalse();
        Run(doc, SessionAction.AddNote, text: new string('x', 900), access: PhotoShootAccess.Photographer).Ok.Should().BeTrue();

        doc.Session.Notes.Should().HaveCount(2);
        doc.Session.Notes[0].Text.Should().Be("Try with glasses");
        doc.Session.Notes[0].ShotId.Should().Be(Shot(doc, "a1").Id);
        doc.Session.Notes[1].Text.Length.Should().Be(PhotoShootSessionEngine.MaxNoteLength);
    }

    [Fact]
    public void End_KeepsProgress()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.CompleteCurrent);
        Run(doc, SessionAction.End).Ok.Should().BeTrue();

        doc.Session.IsActive.Should().BeFalse();
        Shot(doc, "a1").Status.Should().Be(ShotStatus.Completed);
        Current(doc).Title.Should().Be("a2");
    }

    [Fact]
    public void SessionState_SurvivesSerializationRoundTrip_LikeAPageRefresh()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        Run(doc, SessionAction.CompleteCurrent);
        Run(doc, SessionAction.AddNote, text: "Great take");

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
        var json = JsonSerializer.Serialize(doc, options);
        json.Should().Contain("\"status\":\"Completed\"", "enums are stored as readable strings");

        var reloaded = PhotoShootEditor.Normalize(JsonSerializer.Deserialize<PhotoShootDocument>(json, options)!);
        reloaded.Session.IsActive.Should().BeTrue();
        Current(reloaded).Title.Should().Be("a2");
        Shot(reloaded, "a1").Status.Should().Be(ShotStatus.Completed);
        reloaded.Session.Notes.Should().ContainSingle(n => n.Text == "Great take");

        Run(reloaded, SessionAction.CompleteCurrent);
        Current(reloaded).Title.Should().Be("a3");
    }

    [Fact]
    public void Upcoming_ListsNextActionableShots()
    {
        var doc = TwoSetups();
        PhotoShootEditor.SetStatus(doc, Shot(doc, "a3").Id, ShotStatus.Completed, Now);
        PhotoShootPlanner.Upcoming(doc, Shot(doc, "a1").Id, 2).Select(s => s.Title).Should().Equal("a2", "b1");
        PhotoShootPlanner.Upcoming(doc, null, 10, mustHaveOnly: true).Select(s => s.Title).Should().Equal("a2", "b1");
    }

    [Fact]
    public void DeletingCurrentShot_MovesSessionToNextShot()
    {
        var doc = TwoSetups();
        Run(doc, SessionAction.Start);
        PhotoShootEditor.DeleteShot(doc, Shot(doc, "a1").Id);
        Current(doc).Title.Should().Be("a2");
    }
}
