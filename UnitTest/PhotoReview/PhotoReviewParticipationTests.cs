using BlazorApp.Shared.PhotoReview;
using FluentAssertions;
using static UnitTest.PhotoReview.PhotoReviewTestData;

namespace UnitTest.PhotoReview;

public class PhotoReviewParticipationTests
{
    private static readonly Func<string, bool> AnyPhoto = id => id.StartsWith("p");

    // ---------- identity ----------

    [Fact]
    public void Register_StoresOnlyTheHash_AndTokenAuthenticates()
    {
        var (doc, actor, token) = WithVisitor();
        var visitor = doc.Visitors.Single();
        visitor.SecretHash.Should().HaveLength(64);
        token.Should().NotContain(visitor.SecretHash);
        PhotoReviewParticipation.Authenticate(doc, token)!.Id.Should().Be(actor.VisitorId);
    }

    [Fact]
    public void ForgedTokens_DoNotAuthenticate()
    {
        var (doc, actor, token) = WithVisitor();
        var secret = token.Split('.')[1];
        PhotoReviewParticipation.Authenticate(doc, actor.VisitorId + "." + new string('0', 64)).Should().BeNull("a guessed secret must not work");
        PhotoReviewParticipation.Authenticate(doc, actor.VisitorId).Should().BeNull();
        PhotoReviewParticipation.Authenticate(doc, PhotoReviewParticipation.NewId(12) + "." + secret).Should().BeNull("a secret is bound to its own id");
        PhotoReviewParticipation.Authenticate(doc, "Bard." + secret).Should().BeNull("display names are never identities");
        PhotoReviewParticipation.Authenticate(doc, "").Should().BeNull();
        PhotoReviewParticipation.Authenticate(doc, token.ToUpperInvariant()).Should().BeNull();
    }

    [Fact]
    public void DuplicateDisplayNames_AreDistinctPeople()
    {
        var (doc, first, _) = WithVisitor(name: "Sam");
        var (_, second, _) = WithVisitor(doc, "Sam");
        first.VisitorId.Should().NotBe(second.VisitorId);
        PhotoReviewParticipation.Rate(doc, first, "p1", 9, AnyPhoto, Now).Ok.Should().BeTrue();
        PhotoReviewParticipation.Rate(doc, second, "p1", 3, AnyPhoto, Now).Ok.Should().BeTrue();
        doc.Totals["p1"].Count.Should().Be(2);
        doc.Totals["p1"].Sum.Should().Be(12);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void Register_RejectsBadNames(string name)
    {
        var doc = new ParticipationDocument();
        PhotoReviewParticipation.Register(doc, name, PhotoReviewParticipation.NewId(12), PhotoReviewParticipation.NewId(32), Now).Status.Should().Be(400);
        doc.Visitors.Should().BeEmpty();
    }

    [Fact]
    public void Contributions_RequireAVisitor()
    {
        var doc = new ParticipationDocument();
        PhotoReviewParticipation.Rate(doc, new ReviewActor(), "p1", 5, AnyPhoto, Now).Status.Should().Be(401);
        PhotoReviewParticipation.Rate(doc, new ReviewActor { VisitorId = "nobody" }, "p1", 5, AnyPhoto, Now).Status.Should().Be(401);
        PhotoReviewParticipation.Rate(doc, Admin, "p1", 5, AnyPhoto, Now).Status.Should().Be(401, "admin rates as a visitor too, never anonymously");
    }

    // ---------- ratings ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(100)]
    public void Rate_RejectsOutOfRange(int value)
    {
        var (doc, actor, _) = WithVisitor();
        PhotoReviewParticipation.Rate(doc, actor, "p1", value, AnyPhoto, Now).Status.Should().Be(400);
        doc.Votes.Should().BeEmpty();
        doc.Totals.Should().BeEmpty();
    }

    [Fact]
    public void Rate_OneCurrentVotePerVisitor_UpdateReplaces()
    {
        var (doc, actor, _) = WithVisitor();
        PhotoReviewParticipation.Rate(doc, actor, "p1", 1, AnyPhoto, Now).Changed.Should().BeTrue();
        PhotoReviewParticipation.Rate(doc, actor, "p1", 10, AnyPhoto, Now).Changed.Should().BeTrue();
        PhotoReviewParticipation.Rate(doc, actor, "p1", 10, AnyPhoto, Now).Changed.Should().BeFalse("repeating a vote is idempotent");
        doc.Votes.Should().ContainSingle();
        doc.Totals["p1"].Should().BeEquivalentTo(new RatingTotal { Sum = 10, Count = 1 });
        doc.Totals["p1"].Average.Should().Be(10);

        PhotoReviewParticipation.ClearRating(doc, actor, "p1", Now).Changed.Should().BeTrue();
        doc.Totals.Should().NotContainKey("p1");
    }

    [Fact]
    public void Rate_UnknownPhoto_IsNotFound()
    {
        var (doc, actor, _) = WithVisitor();
        PhotoReviewParticipation.Rate(doc, actor, "zzz", 5, AnyPhoto, Now).Status.Should().Be(404);
    }

    [Fact]
    public void Totals_AlwaysMatchTheVotes_AfterManyRandomOperations()
    {
        var doc = new ParticipationDocument();
        var actors = Enumerable.Range(0, 12).Select(i => WithVisitor(doc, "Person " + i).Actor).ToList();
        var random = new Random(42);
        for (var i = 0; i < 2000; i++)
        {
            var actor = actors[random.Next(actors.Count)];
            var photo = "p" + random.Next(5);
            if (random.Next(5) == 0) PhotoReviewParticipation.ClearRating(doc, actor, photo, Now);
            else PhotoReviewParticipation.Rate(doc, actor, photo, random.Next(1, 11), AnyPhoto, Now);
            if (i == 1000) PhotoReviewParticipation.RemovePerson(doc, Admin, actors[0].VisitorId, null, Now);
        }

        foreach (var group in doc.Votes.Where(vote => !vote.IsRemoved).GroupBy(vote => vote.PhotoId))
        {
            doc.Totals[group.Key].Sum.Should().Be(group.Sum(vote => vote.Value));
            doc.Totals[group.Key].Count.Should().Be(group.Count());
        }

        doc.Votes.Where(vote => !vote.IsRemoved).GroupBy(vote => (vote.VisitorId, vote.PhotoId)).Should().OnlyContain(group => group.Count() == 1);
        doc.Votes.Should().OnlyContain(vote => vote.Value >= 1 && vote.Value <= 10);
    }

    // ---------- comments and ownership ----------

    private static bool Exists(CommentTarget target, string id) => id.Length > 0;

    [Fact]
    public void Comments_OwnersOnlyCanEditOrDelete()
    {
        var (doc, alice, _) = WithVisitor(name: "Alice");
        var (_, bob, _) = WithVisitor(doc, "Bob");
        var created = PhotoReviewParticipation.AddComment(doc, alice, CommentTarget.Photo, "p1", null, "Love it", "req-1", Exists, Now);
        created.Ok.Should().BeTrue();

        PhotoReviewParticipation.EditComment(doc, bob, created.CreatedId, "hacked", Now).Status.Should().Be(403);
        PhotoReviewParticipation.DeleteComment(doc, bob, created.CreatedId, Now).Status.Should().Be(403);
        PhotoReviewParticipation.EditComment(doc, Admin, created.CreatedId, "hacked", Now).Status.Should().Be(401, "admin moderates by removing people, not by editing their words");
        doc.Comments.Single().Text.Should().Be("Love it");

        PhotoReviewParticipation.EditComment(doc, alice, created.CreatedId, "Love it!", Now).Ok.Should().BeTrue();
        PhotoReviewParticipation.DeleteComment(doc, alice, created.CreatedId, Now).Ok.Should().BeTrue();
        doc.Comments.Single().DeletedUtc.Should().NotBeNull();
    }

    [Fact]
    public void Comments_ClientRequestId_MakesRetriesIdempotent()
    {
        var (doc, alice, _) = WithVisitor();
        var first = PhotoReviewParticipation.AddComment(doc, alice, CommentTarget.Shot, "shot-001", null, "Hello", "abc", Exists, Now);
        var retry = PhotoReviewParticipation.AddComment(doc, alice, CommentTarget.Shot, "shot-001", null, "Hello", "abc", Exists, Now);
        retry.Changed.Should().BeFalse();
        retry.CreatedId.Should().Be(first.CreatedId);
        doc.Comments.Should().ContainSingle();
    }

    [Fact]
    public void Replies_MustStayOnTheSameTarget_AndDepthIsLimited()
    {
        var (doc, alice, _) = WithVisitor();
        var root = PhotoReviewParticipation.AddComment(doc, alice, CommentTarget.Photo, "p1", null, "Root", "", Exists, Now).CreatedId;
        PhotoReviewParticipation.AddComment(doc, alice, CommentTarget.Photo, "p2", root, "Wrong photo", "", Exists, Now).Status.Should().Be(404);

        var parent = root;
        for (var depth = 2; depth <= PhotoReviewParticipation.MaxThreadDepth; depth++)
        {
            parent = PhotoReviewParticipation.AddComment(doc, alice, CommentTarget.Photo, "p1", parent, "Reply " + depth, "", Exists, Now).CreatedId;
        }

        PhotoReviewParticipation.AddComment(doc, alice, CommentTarget.Photo, "p1", parent, "Too deep", "", Exists, Now).Status.Should().Be(400);
    }

    [Fact]
    public void Comments_RejectEmptyAndOversized()
    {
        var (doc, alice, _) = WithVisitor();
        PhotoReviewParticipation.AddComment(doc, alice, CommentTarget.Photo, "p1", null, "  ", "", Exists, Now).Status.Should().Be(400);
        PhotoReviewParticipation.AddComment(doc, alice, CommentTarget.Photo, "p1", null, new string('x', PhotoReviewParticipation.MaxCommentLength + 1), "", Exists, Now).Status.Should().Be(400);
        PhotoReviewParticipation.AddComment(doc, alice, CommentTarget.Photo, "", null, "Hi", "", Exists, Now).Status.Should().Be(404);
    }

    // ---------- suggestions ----------

    private static bool ShotExists(string id) => id.StartsWith("shot-");

    [Fact]
    public void Suggestions_ValidateKind_AndOnlyAdminDecides()
    {
        var (doc, bard, _) = WithVisitor();
        PhotoReviewParticipation.AddSuggestion(doc, bard, SuggestionKind.StatusChange, "shot-001", null, "", "", "", ShotExists, Now).Status.Should().Be(400, "a status suggestion needs a status");
        PhotoReviewParticipation.AddSuggestion(doc, bard, SuggestionKind.PoseChange, "nope", null, "", "Lower the chin", "", ShotExists, Now).Status.Should().Be(404);
        PhotoReviewParticipation.AddSuggestion(doc, bard, SuggestionKind.NewPose, "", null, "", "Describe", "", ShotExists, Now).Status.Should().Be(400, "a new pose needs a name");

        var status = PhotoReviewParticipation.AddSuggestion(doc, bard, SuggestionKind.StatusChange, "shot-001", ShotReviewStatus.PartiallySuccessful, "", "Eyes are closed", "", ShotExists, Now);
        var pose = PhotoReviewParticipation.AddSuggestion(doc, bard, SuggestionKind.NewPose, "", null, "Card fan behind head", "Fan cards behind the head like a peacock.", "", ShotExists, Now);
        status.Ok.Should().BeTrue();
        pose.Ok.Should().BeTrue();

        PhotoReviewParticipation.DecideSuggestion(doc, bard, pose.CreatedId, SuggestionState.Approved, null, Now).Status.Should().Be(403);
        PhotoReviewParticipation.DecideSuggestion(doc, Admin, pose.CreatedId, SuggestionState.Approved, "Yes", Now).Ok.Should().BeTrue();
        PhotoReviewParticipation.EditSuggestion(doc, bard, pose.CreatedId, null, "Changed", "Changed", ShotExists, Now).Status.Should().Be(409, "decided suggestions are frozen");
    }

    [Fact]
    public void Suggestions_OnlyAuthorEditsOrWithdraws()
    {
        var (doc, bard, _) = WithVisitor(name: "Bard");
        var (_, other, _) = WithVisitor(doc, "Other");
        var id = PhotoReviewParticipation.AddSuggestion(doc, bard, SuggestionKind.PoseChange, "shot-002", null, "", "Turn the shoulders", "", ShotExists, Now).CreatedId;
        PhotoReviewParticipation.EditSuggestion(doc, other, id, null, "", "Mine now", ShotExists, Now).Status.Should().Be(403);
        PhotoReviewParticipation.DeleteSuggestion(doc, other, id, Now).Status.Should().Be(403);
        PhotoReviewParticipation.EditSuggestion(doc, bard, id, null, "", "Turn the shoulders more", ShotExists, Now).Ok.Should().BeTrue();
        PhotoReviewParticipation.DeleteSuggestion(doc, bard, id, Now).Ok.Should().BeTrue();
    }

    // ---------- moderation ----------

    [Fact]
    public void RemovePerson_SoftRemovesEverything_RecalculatesTotals_AndBlocksThem()
    {
        var (doc, troll, _) = WithVisitor(name: "Troll");
        var (_, bard, _) = WithVisitor(doc, "Bard");
        PhotoReviewParticipation.Rate(doc, troll, "p1", 1, AnyPhoto, Now);
        PhotoReviewParticipation.Rate(doc, bard, "p1", 9, AnyPhoto, Now);
        var trollComment = PhotoReviewParticipation.AddComment(doc, troll, CommentTarget.Photo, "p1", null, "Bad", "", Exists, Now).CreatedId;
        var bardReply = PhotoReviewParticipation.AddComment(doc, bard, CommentTarget.Photo, "p1", trollComment, "I disagree", "", Exists, Now).CreatedId;
        var selfDeleted = PhotoReviewParticipation.AddComment(doc, troll, CommentTarget.Photo, "p1", null, "Oops", "", Exists, Now).CreatedId;
        PhotoReviewParticipation.DeleteComment(doc, troll, selfDeleted, Now);
        PhotoReviewParticipation.AddSuggestion(doc, troll, SuggestionKind.PoseChange, "shot-001", null, "", "Spam", "", ShotExists, Now);

        PhotoReviewParticipation.RemovePerson(doc, bard, troll.VisitorId, null, Now).Status.Should().Be(403, "visitors can't moderate");
        var removal = PhotoReviewParticipation.RemovePerson(doc, Admin, troll.VisitorId, "spam", Now);
        removal.Ok.Should().BeTrue();

        doc.Totals["p1"].Should().BeEquivalentTo(new RatingTotal { Sum = 9, Count = 1 });
        doc.Comments.Single(c => c.Id == trollComment).IsRemoved.Should().BeTrue();
        doc.Comments.Single(c => c.Id == bardReply).IsRemoved.Should().BeFalse("other people's replies survive");
        doc.Suggestions.Single().IsRemoved.Should().BeTrue();
        doc.Audit.Should().ContainSingle(a => a.Action == "RemovePerson" && a.Votes == 1 && a.Comments == 2 && a.Suggestions == 1);

        PhotoReviewParticipation.Rate(doc, troll, "p2", 5, AnyPhoto, Now).Status.Should().Be(403);
        PhotoReviewParticipation.AddComment(doc, troll, CommentTarget.Photo, "p1", null, "Again", "", Exists, Now).Status.Should().Be(403);
        PhotoReviewParticipation.Rename(doc, troll, "New name", Now).Status.Should().Be(403);

        var planner = Planner();
        var projection = new PhotoReviewProjection(planner, new ReviewCatalog(), doc, bard, new ReviewViewer());
        var thread = projection.VisibleComments(CommentTarget.Photo, "p1");
        thread.Should().HaveCount(2);
        thread[0].Placeholder.Should().BeTrue();
        thread[0].Text.Should().BeEmpty();
        thread[0].AuthorName.Should().BeEmpty();
        thread[1].ParentId.Should().Be(trollComment);
        thread[1].Text.Should().Be("I disagree");
    }

    [Fact]
    public void PurgeTestVisitors_RemovesOnlyLabeledTestPeople()
    {
        var (doc, real, _) = WithVisitor(name: "Bard");
        var (_, test, _) = WithVisitor(doc, "[SMOKE TEST] probe");
        PhotoReviewParticipation.Rate(doc, real, "p1", 8, AnyPhoto, Now);
        PhotoReviewParticipation.Rate(doc, test, "p1", 2, AnyPhoto, Now);
        PhotoReviewParticipation.AddComment(doc, test, CommentTarget.Photo, "p1", null, "test", "", Exists, Now);
        var keep = PhotoReviewParticipation.AddComment(doc, real, CommentTarget.Photo, "p1", null, "real", "", Exists, Now).CreatedId;

        var act = () => PhotoReviewParticipation.PurgeTestVisitors(doc, "[S");
        act.Should().Throw<ArgumentException>("a short prefix could match real people");
        PhotoReviewParticipation.PurgeTestVisitors(doc, "[SMOKE TEST]").Should().Be(1);
        doc.Visitors.Should().ContainSingle(v => v.DisplayName == "Bard");
        doc.Comments.Should().ContainSingle(c => c.Id == keep);
        doc.Totals["p1"].Should().BeEquivalentTo(new RatingTotal { Sum = 8, Count = 1 });
    }

    [Fact]
    public void RestorePerson_BringsBackExactlyWhatWasRemoved()
    {
        var (doc, person, _) = WithVisitor(name: "Pat");
        PhotoReviewParticipation.Rate(doc, person, "p1", 4, AnyPhoto, Now);
        var kept = PhotoReviewParticipation.AddComment(doc, person, CommentTarget.Photo, "p1", null, "Nice", "", Exists, Now).CreatedId;
        var deleted = PhotoReviewParticipation.AddComment(doc, person, CommentTarget.Photo, "p1", null, "Never mind", "", Exists, Now).CreatedId;
        PhotoReviewParticipation.DeleteComment(doc, person, deleted, Now);

        PhotoReviewParticipation.RemovePerson(doc, Admin, person.VisitorId, null, Now);
        doc.Totals.Should().NotContainKey("p1");
        PhotoReviewParticipation.RestorePerson(doc, person, person.VisitorId, null, Now).Status.Should().Be(403);
        PhotoReviewParticipation.RestorePerson(doc, Admin, person.VisitorId, "mistake", Now).Ok.Should().BeTrue();

        doc.Totals["p1"].Should().BeEquivalentTo(new RatingTotal { Sum = 4, Count = 1 });
        doc.Comments.Single(c => c.Id == kept).IsRemoved.Should().BeFalse();
        doc.Comments.Single(c => c.Id == deleted).DeletedUtc.Should().NotBeNull("their own deletion is not undone by a restore");
        doc.Visitors.Single().RemovedUtc.Should().BeNull();
        doc.Audit.Select(a => a.Action).Should().Equal("RemovePerson", "RestorePerson");
        PhotoReviewParticipation.Rate(doc, person, "p1", 6, AnyPhoto, Now).Ok.Should().BeTrue();
        doc.Totals["p1"].Count.Should().Be(1);
    }
}
