using System.Text;
using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;

namespace UnitTest.PhotoShoot;

public class PhotoShootAuthorizationTests
{
    private static string Principal(params string[] roles) => PhotoShootAuthorization.EncodePrincipal(new SwaClientPrincipal
    {
        IdentityProvider = "github",
        UserId = "user-123",
        UserDetails = "lukehammer",
        UserRoles = roles.ToList()
    });

    [Fact]
    public void ResolveAccess_MapsRoles()
    {
        PhotoShootAuthorization.ResolveAccess(new[] { "anonymous", "authenticated" }).Should().Be(PhotoShootAccess.None);
        PhotoShootAuthorization.ResolveAccess(new[] { "authenticated", "photoshoot-photographer" }).Should().Be(PhotoShootAccess.Photographer);
        PhotoShootAuthorization.ResolveAccess(new[] { "photoshoot-photographer", "PhotoShoot-Owner" }).Should().Be(PhotoShootAccess.Owner);
        PhotoShootAuthorization.ResolveAccess(null).Should().Be(PhotoShootAccess.None);
    }

    [Fact]
    public void CanReadAndEdit_FollowAccessLevels()
    {
        PhotoShootAuthorization.CanRead(PhotoShootAccess.None).Should().BeFalse();
        PhotoShootAuthorization.CanRead(PhotoShootAccess.Photographer).Should().BeTrue();
        PhotoShootAuthorization.CanEdit(PhotoShootAccess.Photographer).Should().BeFalse();
        PhotoShootAuthorization.CanEdit(PhotoShootAccess.Owner).Should().BeTrue();
    }

    [Fact]
    public void Resolve_InProduction_UsesOnlyThePrincipalHeader()
    {
        var owner = PhotoShootAuthorization.Resolve(Principal("authenticated", "photoshoot-owner"), "photographer", "lukehammer-swa", "true", "owner");
        owner.Access.Should().Be(PhotoShootAccess.Owner);
        owner.UserName.Should().Be("lukehammer");
        owner.DevBypass.Should().BeFalse("dev settings are ignored when WEBSITE_SITE_NAME is set");

        var anonymous = PhotoShootAuthorization.Resolve(null, "owner", "lukehammer-swa", "true", "owner");
        anonymous.Access.Should().Be(PhotoShootAccess.None, "a dev header must never grant access in Azure");

        var authenticatedNoRole = PhotoShootAuthorization.Resolve(Principal("authenticated"), null, "lukehammer-swa", null, null);
        authenticatedNoRole.Access.Should().Be(PhotoShootAccess.None);
    }

    [Fact]
    public void Resolve_LocalWithoutExplicitDevSetting_RequiresPrincipal()
    {
        PhotoShootAuthorization.Resolve(null, "owner", null, null, null).Access.Should().Be(PhotoShootAccess.None);
        PhotoShootAuthorization.Resolve(null, "owner", null, "false", null).Access.Should().Be(PhotoShootAccess.None);
    }

    [Fact]
    public void Resolve_LocalDevBypass_DefaultsToOwnerAndHonoursRoleHeader()
    {
        var dev = PhotoShootAuthorization.Resolve(null, null, null, "true", null);
        dev.Access.Should().Be(PhotoShootAccess.Owner);
        dev.DevBypass.Should().BeTrue();

        PhotoShootAuthorization.Resolve(null, "photographer", null, "TRUE", null).Access.Should().Be(PhotoShootAccess.Photographer);
        PhotoShootAuthorization.Resolve(null, null, "", "true", "photographer").Access.Should().Be(PhotoShootAccess.Photographer);
        PhotoShootAuthorization.Resolve(null, "none", null, "true", null).Access.Should().Be(PhotoShootAccess.None);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!!")]
    public void TryDecodePrincipal_RejectsMissingOrMalformed(string? header)
    {
        PhotoShootAuthorization.TryDecodePrincipal(header, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDecodePrincipal_RejectsJsonWithoutUserId()
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"userRoles\":[\"photoshoot-owner\"]}"));
        PhotoShootAuthorization.TryDecodePrincipal(header, out _).Should().BeFalse();
        PhotoShootAuthorization.Resolve(header, null, "site", null, null).Access.Should().Be(PhotoShootAccess.None);
    }

    [Fact]
    public void TryDecodePrincipal_ParsesSwaShape()
    {
        var json = "{\"identityProvider\":\"aad\",\"userId\":\"abc\",\"userDetails\":\"luke@example.com\",\"userRoles\":[\"anonymous\",\"authenticated\",\"photoshoot-photographer\"]}";
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        PhotoShootAuthorization.TryDecodePrincipal(header, out var principal).Should().BeTrue();
        principal.IdentityProvider.Should().Be("aad");
        principal.UserRoles.Should().Contain("photoshoot-photographer");
        PhotoShootAuthorization.Resolve(header, null, "site", null, null).Access.Should().Be(PhotoShootAccess.Photographer);
    }

    [Fact]
    public void SessionPermissions_PhotographerSubset()
    {
        var session = new PhotoShootSession();
        PhotoShootSessionEngine.IsAllowed(PhotoShootAccess.Photographer, SessionAction.CompleteCurrent, session).Should().BeTrue();
        PhotoShootSessionEngine.IsAllowed(PhotoShootAccess.Photographer, SessionAction.RequestNext, session).Should().BeTrue();
        PhotoShootSessionEngine.IsAllowed(PhotoShootAccess.Photographer, SessionAction.ReopenShot, session).Should().BeFalse();
        PhotoShootSessionEngine.IsAllowed(PhotoShootAccess.Photographer, SessionAction.ShootNext, session).Should().BeFalse();
        PhotoShootSessionEngine.IsAllowed(PhotoShootAccess.Photographer, SessionAction.End, session).Should().BeFalse();
        PhotoShootSessionEngine.IsAllowed(PhotoShootAccess.Owner, SessionAction.End, session).Should().BeTrue();
    }

    [Theory]
    [InlineData("abc123", true)]
    [InlineData("a-b-c", true)]
    [InlineData("", false)]
    [InlineData("../etc", false)]
    [InlineData("ABC", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    public void Ids_OnlyAllowSafeCharacters(string id, bool expected)
    {
        PhotoShootIds.IsValid(id).Should().Be(expected);
    }

    [Fact]
    public void Resolve_MasterPassword_GrantsOwnerEvenInProduction()
    {
        var header = PhotoShootAuthorization.Resolve(null, "photographer", "lukehammer-swa", "true", "owner", PhotoShootAuthorization.MasterPassword, null);
        header.Access.Should().Be(PhotoShootAccess.Owner);
        header.UserName.Should().Be("Luke");
        header.DevBypass.Should().BeFalse();

        var cookie = "theme=dark; " + PhotoShootAuthorization.PasswordCookie + "=" + Uri.EscapeDataString(PhotoShootAuthorization.MasterPassword);
        var fromCookie = PhotoShootAuthorization.Resolve(null, null, "lukehammer-swa", null, null, null, cookie);
        fromCookie.Access.Should().Be(PhotoShootAccess.Owner);

        PhotoShootAuthorization.Resolve(null, null, "lukehammer-swa", null, null, "nope", null).Access.Should().Be(PhotoShootAccess.None);
        PhotoShootAuthorization.Resolve(null, null, "lukehammer-swa", null, null, "luke", null).Access.Should().Be(PhotoShootAccess.None);
        PhotoShootAuthorization.HasMasterPassword(null, "other=Luke").Should().BeFalse();
    }

    [Fact]
    public void NewIds_AreValidAndUnique()
    {
        var ids = Enumerable.Range(0, 500).Select(_ => PhotoShootIds.New()).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => PhotoShootIds.IsValid(id));
    }
}
