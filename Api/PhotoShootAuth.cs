#nullable enable
using System;
using System.Linq;
using BlazorApp.Shared.PhotoShoot;
using Microsoft.Azure.Functions.Worker.Http;

namespace ApiIsolated
{
    /// <summary>
    /// Resolves the caller from the Static Web Apps <c>x-ms-client-principal</c> header (set by SWA for its managed
    /// API). The local dev bypass is only honoured outside Azure with PHOTOSHOOT_DEV_AUTH=true.
    /// </summary>
    internal static class PhotoShootAuth
    {
        public static PhotoShootMe Resolve(HttpRequestData req) =>
            PhotoShootAuthorization.Resolve(
                Header(req, PhotoShootAuthorization.PrincipalHeader),
                Header(req, PhotoShootAuthorization.DevRoleHeader),
                Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"),
                Environment.GetEnvironmentVariable("PHOTOSHOOT_DEV_AUTH"),
                Environment.GetEnvironmentVariable("PHOTOSHOOT_DEV_ROLE"),
                Header(req, PhotoShootAuthorization.PasswordHeader),
                Header(req, "Cookie"));

        public static bool IsAuthenticated(HttpRequestData req) =>
            PhotoShootAuthorization.IsAuthenticated(
                Header(req, PhotoShootAuthorization.PrincipalHeader),
                Header(req, PhotoShootAuthorization.PasswordHeader),
                Header(req, "Cookie"));

        private static string? Header(HttpRequestData req, string name) =>
            req.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}
