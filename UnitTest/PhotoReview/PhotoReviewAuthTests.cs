using BlazorApp.Shared.PhotoReview;
using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;

namespace UnitTest.PhotoReview;

public class PhotoReviewAuthTests
{
    private static SwaClientPrincipal Principal(string provider = "aad", string userId = "subject-1", string details = "lukeroberthammer@outlook.com", params string[] roles) =>
        new SwaClientPrincipal { IdentityProvider = provider, UserId = userId, UserDetails = details, UserRoles = roles.ToList() };

    private static AdminBinding Bound(string userId = "subject-1") => new AdminBinding { Provider = "aad", UserId = userId, UserDetails = "lukeroberthammer@outlook.com" };

    [Fact]
    public void Admin_RequiresMicrosoft_TheAdminEmail_AndTheBoundSubject()
    {
        PhotoReviewAdmin.Check(Principal(), null, Bound()).Should().Be(AdminCheck.Admin);
        PhotoReviewAdmin.Check(Principal(details: "LukeRobertHammer@Outlook.com"), null, Bound()).Should().Be(AdminCheck.Admin, "email case doesn't matter");
        PhotoReviewAdmin.Check(Principal(), "subject-1", null).Should().Be(AdminCheck.Admin, "the app setting binding works on its own");
    }

    [Fact]
    public void SpoofedAccounts_AreNeverAdmin()
    {
        PhotoReviewAdmin.Check(Principal(provider: "github"), null, Bound()).Should().Be(AdminCheck.NotAdmin, "same email on another provider");
        PhotoReviewAdmin.Check(Principal(details: "lukeroberthammer@outlook.com.evil.com"), null, Bound()).Should().Be(AdminCheck.NotAdmin);
        PhotoReviewAdmin.Check(Principal(details: "Luke"), null, Bound()).Should().Be(AdminCheck.NotAdmin, "display names are not identities");
        PhotoReviewAdmin.Check(Principal(details: "someone@else.com", roles: new[] { "admin", "photoshoot-owner" }), null, Bound()).Should().Be(AdminCheck.NotAdmin, "roles never grant review admin");
        PhotoReviewAdmin.Check(Principal(userId: "attacker"), null, Bound()).Should().Be(AdminCheck.SubjectMismatch, "right email, wrong subject");
        PhotoReviewAdmin.Check(Principal(), "other-subject", Bound()).Should().Be(AdminCheck.SubjectMismatch, "the app setting wins over the stored binding");
        PhotoReviewAdmin.Check(Principal(userId: ""), null, Bound()).Should().Be(AdminCheck.NotAdmin);
        PhotoReviewAdmin.Check(null, null, Bound()).Should().Be(AdminCheck.NotAdmin);
    }

    private const string Outlook = "lukeroberthammer@outlook.com";
    private const string Business = "luke@lukehammermagic.com";

    [Fact]
    public void TheSoleAdminIsTheOutlookAccount()
    {
        PhotoReviewAdmin.AdminEmail.Should().Be(Outlook);
        PhotoReviewAdmin.Check(Principal(details: Outlook), null, null).Should().Be(AdminCheck.CanBind, "matching Outlook sign-in with nothing bound");
        PhotoReviewAdmin.Check(Principal(details: Outlook), null, Bound()).Should().Be(AdminCheck.Admin, "bound Outlook subject");
        PhotoReviewAdmin.Check(Principal(details: Outlook, userId: "someone-else"), null, Bound()).Should().Be(AdminCheck.SubjectMismatch, "wrong subject");
    }

    [Fact]
    public void TheFormerBusinessAccount_IsNoLongerAdmin()
    {
        var businessBinding = new AdminBinding { Provider = "aad", UserId = "business-subject", UserDetails = Business };
        PhotoReviewAdmin.Check(Principal(details: Business), null, null).Should().Be(AdminCheck.NotAdmin);
        PhotoReviewAdmin.Check(Principal(details: Business, userId: "business-subject"), null, businessBinding).Should().Be(AdminCheck.NotAdmin, "even with its old binding");
        PhotoReviewAdmin.Check(Principal(details: Business), null, Bound()).Should().Be(AdminCheck.NotAdmin);
    }

    [Fact]
    public void AStaleBusinessBinding_IsNeverReused_ForTheOutlookAccount()
    {
        // Production still holds a binding made for the business email until it is backed up and reset.
        var stale = new AdminBinding { Provider = "aad", UserId = "business-subject", UserDetails = Business };
        PhotoReviewAdmin.Check(Principal(details: Outlook), null, stale).Should().Be(AdminCheck.SubjectMismatch, "must not bind over or reuse it");
        PhotoReviewAdmin.Check(Principal(details: Outlook, userId: "business-subject"), null, stale).Should().Be(AdminCheck.SubjectMismatch,
            "a binding recorded for another email never authorizes, even if the subject matches");
        PhotoReviewAdmin.Check(Principal(details: Outlook), "subject-1", stale).Should().Be(AdminCheck.Admin, "an explicit app-setting pin is the deliberate override");
    }

    [Fact]
    public void ForgedOutlookPrincipal_IsStillNotTrusted()
    {
        // In production SWA drops client-supplied principal headers (verified with a forged header: 401). Even if one
        // got through, the rule still needs the bound subject: naming the Outlook email with a guessed subject fails.
        var forged = PhotoShootAuthorization.EncodePrincipal(new SwaClientPrincipal { IdentityProvider = "aad", UserId = "guessed", UserDetails = Outlook, UserRoles = new List<string> { "admin" } });
        PhotoShootAuthorization.TryDecodePrincipal(forged, out var decoded).Should().BeTrue();
        PhotoReviewAdmin.Check(decoded, null, Bound()).Should().Be(AdminCheck.SubjectMismatch);
        PhotoShootAuthorization.Resolve(forged, null, "site", null, null, microsoftAdmin: false).Access.Should().Be(PhotoShootAccess.None);
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
        PhotoShootAuthorization.TryDecodePrincipal(Convert.ToBase64String("{\"userDetails\":\"lukeroberthammer@outlook.com\"}"u8.ToArray()), out _).Should().BeFalse("a principal without a subject is rejected");
        PhotoShootAuthorization.TryDecodePrincipal(Convert.ToBase64String("[1,2]"u8.ToArray()), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anything-else")]
    public void LegacyPassword_IsOwnerByDefault_BeforeAndAfterBinding(string? setting)
    {
        PhotoReviewAdmin.LegacyPasswordAccess(setting).Should().Be(PhotoShootAccess.Owner);
    }

    [Fact]
    public void LegacyPassword_IsPhotographerOnlyWhenSetToPhotographer()
    {
        PhotoReviewAdmin.LegacyPasswordAccess("photographer").Should().Be(PhotoShootAccess.Photographer);
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
                caller.Access.Should().Be(PhotoShootAccess.Owner, "the public password opens owner level (binding: {0})", binding == null ? "none" : "bound");
                PhotoShootAuthorization.CanRead(caller.Access).Should().BeTrue();
                PhotoShootAuthorization.CanEdit(caller.Access).Should().BeTrue("temporary: the shared password opens the director view");
                PhotoShootAuthorization.CanHardDelete(caller.Access).Should().BeFalse("hard delete stays with the Microsoft admin");
                PhotoShootAuthorization.CanSeeRemoved(caller.Access).Should().BeTrue();
                PhotoShootAuthorization.CanDownloadFinals(caller.Access).Should().BeTrue();
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
