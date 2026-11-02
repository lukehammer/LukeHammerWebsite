using BlazorApp.Shared.PhotoReview;
using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;

namespace UnitTest.PhotoReview;

public class PhotoReviewAuthTests
{
    private static SwaClientPrincipal Principal(string provider = "aad", string userId = "subject-1", string details = "luke@lukehammermagic.com", params string[] roles) =>
        new SwaClientPrincipal { IdentityProvider = provider, UserId = userId, UserDetails = details, UserRoles = roles.ToList() };

    private static AdminBinding Bound(string userId = "subject-1") => new AdminBinding { Provider = "aad", UserId = userId, UserDetails = "luke@lukehammermagic.com" };

    [Fact]
    public void Admin_RequiresMicrosoft_TheAdminEmail_AndTheBoundSubject()
    {
        PhotoReviewAdmin.Check(Principal(), null, Bound()).Should().Be(AdminCheck.Admin);
        PhotoReviewAdmin.Check(Principal(details: "LUKE@LukeHammerMagic.com"), null, Bound()).Should().Be(AdminCheck.Admin, "email case doesn't matter");
        PhotoReviewAdmin.Check(Principal(), "subject-1", null).Should().Be(AdminCheck.Admin, "the app setting binding works on its own");
    }

    [Fact]
    public void SpoofedAccounts_AreNeverAdmin()
    {
        PhotoReviewAdmin.Check(Principal(provider: "github"), null, Bound()).Should().Be(AdminCheck.NotAdmin, "same email on another provider");
        PhotoReviewAdmin.Check(Principal(details: "luke@lukehammermagic.com.evil.com"), null, Bound()).Should().Be(AdminCheck.NotAdmin);
        PhotoReviewAdmin.Check(Principal(details: "Luke"), null, Bound()).Should().Be(AdminCheck.NotAdmin, "display names are not identities");
        PhotoReviewAdmin.Check(Principal(details: "someone@else.com", roles: new[] { "admin", "photoshoot-owner" }), null, Bound()).Should().Be(AdminCheck.NotAdmin, "roles never grant review admin");
        PhotoReviewAdmin.Check(Principal(userId: "attacker"), null, Bound()).Should().Be(AdminCheck.SubjectMismatch, "right email, wrong subject");
        PhotoReviewAdmin.Check(Principal(), "other-subject", Bound()).Should().Be(AdminCheck.SubjectMismatch, "the app setting wins over the stored binding");
        PhotoReviewAdmin.Check(Principal(userId: ""), null, Bound()).Should().Be(AdminCheck.NotAdmin);
        PhotoReviewAdmin.Check(null, null, Bound()).Should().Be(AdminCheck.NotAdmin);
    }

    [Fact]
    public void FirstMatchingSignIn_CanBind_OnlyWhenNothingIsBound()
    {
        PhotoReviewAdmin.Check(Principal(), null, null).Should().Be(AdminCheck.CanBind);
        PhotoReviewAdmin.Check(Principal(provider: "github"), null, null).Should().Be(AdminCheck.NotAdmin);
        PhotoReviewAdmin.Check(Principal(details: "bard@example.com"), null, null).Should().Be(AdminCheck.NotAdmin);
    }

    [Fact]
    public void ForgedOrMalformedPrincipalHeaders_DoNotDecode()
    {
        PhotoShootAuthorization.TryDecodePrincipal("not base64!!", out _).Should().BeFalse();
        PhotoShootAuthorization.TryDecodePrincipal(Convert.ToBase64String("{\"userDetails\":\"luke@lukehammermagic.com\"}"u8.ToArray()), out _).Should().BeFalse("a principal without a subject is rejected");
        PhotoShootAuthorization.TryDecodePrincipal(Convert.ToBase64String("[1,2]"u8.ToArray()), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("photographer")]
    [InlineData("anything-else")]
    public void LegacyPassword_IsPhotographerByDefault_BeforeAndAfterBinding(string? setting)
    {
        PhotoReviewAdmin.LegacyPasswordAccess(setting).Should().Be(PhotoShootAccess.Photographer);
    }

    [Fact]
    public void LegacyPassword_OwnerOnlyWithExplicitTemporarySetting_AndOffDisablesIt()
    {
        PhotoReviewAdmin.LegacyPasswordAccess("owner").Should().Be(PhotoShootAccess.Owner);
        PhotoReviewAdmin.LegacyPasswordAccess("OFF").Should().Be(PhotoShootAccess.None);
    }

    /// <summary>
    /// The planner as the API resolves it for each caller in production (WEBSITE_SITE_NAME set, default settings),
    /// before any Microsoft admin binding exists and after it.
    /// </summary>
    [Fact]
    public void Planner_AuthorizationMatrix_ForTheLegacyMigration()
    {
        var password = PhotoShootAuthorization.MasterPassword;
        var defaultPassword = PhotoReviewAdmin.LegacyPasswordAccess(null);
        PhotoShootMe Resolve(SwaClientPrincipal? principal, string? pw, AdminBinding? binding, string? devRole = null)
        {
            var header = principal == null ? null : PhotoShootAuthorization.EncodePrincipal(principal);
            var adminCheck = PhotoReviewAdmin.Check(principal, null, binding);
            return PhotoShootAuthorization.Resolve(header, devRole, "lukehammer-swa", "true", "owner", pw, null,
                microsoftAdmin: adminCheck == AdminCheck.Admin, passwordAccess: defaultPassword);
        }

        foreach (var binding in new[] { null, Bound() })
        {
            var anonymous = Resolve(null, null, binding);
            var passwordHolder = Resolve(null, password, binding);
            var visitorWithDevHeader = Resolve(null, null, binding, devRole: "admin");
            var githubLuke = Resolve(Principal(provider: "github"), null, binding);
            var spoofedSubject = Resolve(Principal(userId: "attacker"), password, binding);
            var otherMicrosoft = Resolve(Principal(details: "bard@example.com"), password, binding);

            anonymous.Access.Should().Be(PhotoShootAccess.None);
            visitorWithDevHeader.Access.Should().Be(PhotoShootAccess.None, "the dev bypass never works in Azure");
            githubLuke.Access.Should().Be(PhotoShootAccess.None);
            foreach (var caller in new[] { passwordHolder, spoofedSubject, otherMicrosoft })
            {
                caller.Access.Should().Be(PhotoShootAccess.Photographer, "the public password only reads and uploads (binding: {0})", binding == null ? "none" : "bound");
                PhotoShootAuthorization.CanRead(caller.Access).Should().BeTrue();
                PhotoShootAuthorization.CanEdit(caller.Access).Should().BeFalse("no plan edits without the Microsoft admin");
                PhotoShootAuthorization.CanHardDelete(caller.Access).Should().BeFalse();
                PhotoShootAuthorization.CanSeeRemoved(caller.Access).Should().BeFalse();
                PhotoShootAuthorization.CanDownloadFinals(caller.Access).Should().BeFalse();
            }
        }

        // Before binding even the real account isn't admin on non-binding requests; the /me call binds it first.
        Resolve(Principal(), null, null).Access.Should().Be(PhotoShootAccess.None);
        var luke = Resolve(Principal(), null, Bound());
        luke.Access.Should().Be(PhotoShootAccess.Admin);
        PhotoShootAuthorization.CanEdit(luke.Access).Should().BeTrue();
        PhotoShootAuthorization.CanHardDelete(luke.Access).Should().BeTrue();
    }

    [Fact]
    public void Review_AdminOperations_RefuseVisitorsAndPasswordHolders()
    {
        var planner = UnitTest.PhotoReview.PhotoReviewTestData.Planner();
        var planned = PhotoReviewProjection.PlannedRefs(planner);
        var catalog = new ReviewCatalog();
        var (people, visitor, _) = UnitTest.PhotoReview.PhotoReviewTestData.WithVisitor();
        var now = UnitTest.PhotoReview.PhotoReviewTestData.Now;
        // A password holder or a visitor is never IsAdmin; only the API sets that, from the bound Microsoft account.
        foreach (var actor in new[] { visitor, new ReviewActor(), new ReviewActor { VisitorId = visitor.VisitorId, AdminName = "Luke" } })
        {
            PhotoReviewAdminOps.SetShotStatus(catalog, planned, actor, "shot-001", ShotReviewStatus.Successful, "", now).Status.Should().Be(403);
            PhotoReviewAdminOps.SetPhotoHidden(catalog, actor, "p1", true, now).Status.Should().Be(403);
            PhotoReviewAdminOps.UpdateQueue(catalog, actor, "shot:shot-001", new QueueUpdateRequest(), _ => true, now).Status.Should().Be(403);
            PhotoReviewParticipation.RemovePerson(people, actor, visitor.VisitorId, null, now).Status.Should().Be(403);
            PhotoReviewParticipation.RestorePerson(people, actor, visitor.VisitorId, null, now).Status.Should().Be(403);
            PhotoReviewParticipation.DecideSuggestion(people, actor, "x", SuggestionState.Approved, null, now).Status.Should().Be(403);
        }

        catalog.Shots.Should().BeEmpty("nothing was written");
        people.Visitors.Should().OnlyContain(v => v.RemovedUtc == null);
    }

    [Fact]
    public void MicrosoftAdmin_FlagWithoutAPrincipal_GrantsNothing()
    {
        PhotoShootAuthorization.Resolve(null, null, "site", null, null, microsoftAdmin: true).Access.Should().Be(PhotoShootAccess.None);
    }
}
