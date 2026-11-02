#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Shared.PhotoReview;
using BlazorApp.Shared.PhotoShoot;
using Microsoft.Azure.Functions.Worker.Http;

namespace ApiIsolated
{
    /// <summary>
    /// Resolves the caller from the Static Web Apps <c>x-ms-client-principal</c> header (set by SWA for its managed
    /// API; a browser cannot supply it). Admin requires the bound Microsoft account. The local dev bypass is only
    /// honoured outside Azure with PHOTOSHOOT_DEV_AUTH=true.
    /// </summary>
    internal static class PhotoShootAuth
    {
        private const string ItemKey = "photoshoot-me";

        /// <summary>Resolves once per request (loads the admin binding) and caches the result for <see cref="Resolve"/>.</summary>
        public static async Task<PhotoShootMe> ResolveAsync(HttpRequestData req, bool allowBind = false)
        {
            var admin = await EvaluateAdminAsync(req, allowBind);
            var me = PhotoShootAuthorization.Resolve(
                Header(req, PhotoShootAuthorization.PrincipalHeader),
                Header(req, PhotoShootAuthorization.DevRoleHeader),
                Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"),
                Environment.GetEnvironmentVariable("PHOTOSHOOT_DEV_AUTH"),
                Environment.GetEnvironmentVariable("PHOTOSHOOT_DEV_ROLE"),
                Header(req, PhotoShootAuthorization.PasswordHeader),
                Header(req, "Cookie"),
                microsoftAdmin: admin.Check == AdminCheck.Admin,
                passwordAccess: PhotoReviewAdmin.LegacyPasswordAccess(Environment.GetEnvironmentVariable("PHOTOSHOOT_LEGACY_PASSWORD")));
            req.FunctionContext.Items[ItemKey] = me;
            return me;
        }

        /// <summary>The result of <see cref="ResolveAsync"/> for this request (None if it was never called).</summary>
        public static PhotoShootMe Resolve(HttpRequestData req) =>
            req.FunctionContext.Items.TryGetValue(ItemKey, out var cached) && cached is PhotoShootMe me ? me : new PhotoShootMe { Access = PhotoShootAccess.None };

        public static bool IsAuthenticated(HttpRequestData req) =>
            PhotoShootAuthorization.IsAuthenticated(
                Header(req, PhotoShootAuthorization.PrincipalHeader),
                Header(req, PhotoShootAuthorization.PasswordHeader),
                Header(req, "Cookie"));

        public static SwaClientPrincipal? Principal(HttpRequestData req) =>
            PhotoShootAuthorization.TryDecodePrincipal(Header(req, PhotoShootAuthorization.PrincipalHeader), out var principal) ? principal : null;

        /// <summary>
        /// Checks the principal against the bound admin subject. With <paramref name="allowBind"/>, the first
        /// Microsoft sign-in as the admin email is recorded (create-only), unless PHOTOREVIEW_ADMIN_AUTOBIND=false.
        /// </summary>
        public static async Task<(AdminCheck Check, SwaClientPrincipal? Principal, bool Bound)> EvaluateAdminAsync(HttpRequestData req, bool allowBind)
        {
            var principal = Principal(req);
            var configured = Environment.GetEnvironmentVariable("PHOTOREVIEW_ADMIN_USER_ID");
            var binding = await PhotoReviewData.LoadBindingAsync();
            var check = PhotoReviewAdmin.Check(principal, configured, binding);
            if (check == AdminCheck.CanBind && allowBind
                && !string.Equals(Environment.GetEnvironmentVariable("PHOTOREVIEW_ADMIN_AUTOBIND"), "false", StringComparison.OrdinalIgnoreCase))
            {
                await PhotoReviewData.TryBindAsync(principal!);
                binding = await PhotoReviewData.LoadBindingAsync();
                check = PhotoReviewAdmin.Check(principal, configured, binding);
            }

            var bound = !string.IsNullOrWhiteSpace(configured) || binding != null;
            return (check, principal, bound);
        }

        public static string? Header(HttpRequestData req, string name) =>
            req.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}
