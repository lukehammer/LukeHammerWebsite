using System;
using BlazorApp.Shared.PhotoShoot;

namespace BlazorApp.Shared.PhotoReview
{
    /// <summary>The Microsoft account subject bound as the site admin.</summary>
    public class AdminBinding
    {
        public string Provider { get; set; } = "";
        public string UserId { get; set; } = "";
        public string UserDetails { get; set; } = "";
        public DateTime BoundUtc { get; set; }
        public string Source { get; set; } = "";
    }

    public enum AdminCheck
    {
        /// <summary>No usable principal, or a different account.</summary>
        NotAdmin = 0,
        /// <summary>Matches the bound Microsoft subject and admin email.</summary>
        Admin = 1,
        /// <summary>Right email and provider, nothing bound yet: the caller may bind it.</summary>
        CanBind = 2,
        /// <summary>Right email, but a different subject is bound. Possible spoof; never admin.</summary>
        SubjectMismatch = 3
    }

    /// <summary>
    /// Admin = the Static Web Apps principal (set by SWA, not the browser) from the Microsoft provider, whose
    /// email is the admin email and whose provider subject equals the bound one. Display names, visitor
    /// cookies, SWA roles and passwords never grant admin.
    /// </summary>
    public static class PhotoReviewAdmin
    {
        /// <summary>The one admin account (Luke's personal Microsoft account). Replaced the business address on 2026-11-03.</summary>
        public const string AdminEmail = "lukeroberthammer@outlook.com";
        public const string MicrosoftProvider = "aad";

        public static AdminCheck Check(SwaClientPrincipal? principal, string? configuredUserId, AdminBinding? stored, string? adminEmail = null)
        {
            if (principal == null || string.IsNullOrWhiteSpace(principal.UserId))
            {
                return AdminCheck.NotAdmin;
            }

            if (!string.Equals((principal.IdentityProvider ?? "").Trim(), MicrosoftProvider, StringComparison.Ordinal))
            {
                return AdminCheck.NotAdmin;
            }

            var email = string.IsNullOrWhiteSpace(adminEmail) ? AdminEmail : adminEmail!.Trim();
            if (!string.Equals((principal.UserDetails ?? "").Trim(), email, StringComparison.OrdinalIgnoreCase))
            {
                return AdminCheck.NotAdmin;
            }

            // A stored binding recorded for another account (e.g. the previous admin email) is stale. It never
            // authorizes anyone and must be backed up and reset deliberately; it is not reused.
            if (string.IsNullOrWhiteSpace(configuredUserId) && stored != null && !string.IsNullOrWhiteSpace(stored.UserDetails)
                && !string.Equals(stored.UserDetails.Trim(), email, StringComparison.OrdinalIgnoreCase))
            {
                return AdminCheck.SubjectMismatch;
            }

            var bound = !string.IsNullOrWhiteSpace(configuredUserId)
                ? configuredUserId!.Trim()
                : stored != null && stored.Provider == MicrosoftProvider && !string.IsNullOrWhiteSpace(stored.UserId) ? stored.UserId : null;
            if (bound == null)
            {
                return AdminCheck.CanBind;
            }

            return string.Equals(bound, principal.UserId.Trim(), StringComparison.Ordinal) ? AdminCheck.Admin : AdminCheck.SubjectMismatch;
        }

        /// <summary>Planner access granted by the old shared password once (and before) the admin is bound.</summary>
        /// <summary>
        /// Planner access granted by the old shared password, which is public in the repository. It is
        /// photographer level (read, upload finished photos, limited session commands) by default, before and after
        /// the admin is bound. Owner level only with an explicit, temporary PHOTOSHOOT_LEGACY_PASSWORD=owner; "off"
        /// disables it. It can never reach Admin.
        /// </summary>
        public static PhotoShootAccess LegacyPasswordAccess(string? setting)
        {
            switch ((setting ?? "").Trim().ToLowerInvariant())
            {
                case "off":
                    return PhotoShootAccess.None;
                case "owner":
                    return PhotoShootAccess.Owner;
                default:
                    return PhotoShootAccess.Photographer;
            }
        }
    }
}
