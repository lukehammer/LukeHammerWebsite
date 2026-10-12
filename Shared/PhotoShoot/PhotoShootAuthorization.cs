using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BlazorApp.Shared.PhotoShoot
{
    /// <summary>Decoded Azure Static Web Apps <c>x-ms-client-principal</c> header.</summary>
    public class SwaClientPrincipal
    {
        public string IdentityProvider { get; set; } = "";
        public string UserId { get; set; } = "";
        public string UserDetails { get; set; } = "";
        public List<string> UserRoles { get; set; } = new List<string>();
    }

    public static class PhotoShootAuthorization
    {
        public const string OwnerRole = "photoshoot-owner";
        public const string PhotographerRole = "photoshoot-photographer";
        public const string PrincipalHeader = "x-ms-client-principal";
        public const string DevRoleHeader = "X-PhotoShoot-Dev-Role";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static PhotoShootAccess ResolveAccess(IEnumerable<string>? roles)
        {
            var set = new HashSet<string>((roles ?? Enumerable.Empty<string>()).Where(r => r != null).Select(r => r.Trim()), StringComparer.OrdinalIgnoreCase);
            if (set.Contains(OwnerRole))
            {
                return PhotoShootAccess.Owner;
            }

            return set.Contains(PhotographerRole) ? PhotoShootAccess.Photographer : PhotoShootAccess.None;
        }

        public static bool CanRead(PhotoShootAccess access) => access >= PhotoShootAccess.Photographer;

        public static bool CanEdit(PhotoShootAccess access) => access == PhotoShootAccess.Owner;

        /// <summary>Decodes the base64 JSON principal header. Returns false for missing or malformed values.</summary>
        public static bool TryDecodePrincipal(string? headerValue, out SwaClientPrincipal principal)
        {
            principal = new SwaClientPrincipal();
            if (string.IsNullOrWhiteSpace(headerValue))
            {
                return false;
            }

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue!.Trim()));
                var decoded = JsonSerializer.Deserialize<SwaClientPrincipal>(json, JsonOptions);
                if (decoded == null || string.IsNullOrWhiteSpace(decoded.UserId))
                {
                    return false;
                }

                decoded.UserRoles ??= new List<string>();
                principal = decoded;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static string EncodePrincipal(SwaClientPrincipal principal) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(principal)));

        /// <summary>
        /// Local development bypass. Only honoured when NOT running in Azure (no WEBSITE_SITE_NAME) AND
        /// PHOTOSHOOT_DEV_AUTH is explicitly "true".
        /// </summary>
        public static bool IsDevBypassEnabled(string? websiteSiteName, string? devAuthSetting) =>
            string.IsNullOrWhiteSpace(websiteSiteName)
            && string.Equals((devAuthSetting ?? "").Trim(), "true", StringComparison.OrdinalIgnoreCase);

        public static PhotoShootAccess ParseDevRole(string? value, PhotoShootAccess fallback)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "owner":
                case "director":
                    return PhotoShootAccess.Owner;
                case "photographer":
                    return PhotoShootAccess.Photographer;
                case "none":
                    return PhotoShootAccess.None;
                default:
                    return fallback;
            }
        }

        /// <summary>
        /// Resolves the caller's access from request inputs. In production only the SWA principal header counts.
        /// </summary>
        public static PhotoShootMe Resolve(string? principalHeader, string? devRoleHeader, string? websiteSiteName, string? devAuthSetting, string? devRoleSetting)
        {
            if (IsDevBypassEnabled(websiteSiteName, devAuthSetting))
            {
                var fallback = ParseDevRole(devRoleSetting, PhotoShootAccess.Owner);
                var access = ParseDevRole(devRoleHeader, fallback);
                return new PhotoShootMe
                {
                    Access = access,
                    UserName = access == PhotoShootAccess.Photographer ? "Local photographer" : "Local developer",
                    IdentityProvider = "dev",
                    DevBypass = true
                };
            }

            if (!TryDecodePrincipal(principalHeader, out var principal))
            {
                return new PhotoShootMe { Access = PhotoShootAccess.None };
            }

            return new PhotoShootMe
            {
                Access = ResolveAccess(principal.UserRoles),
                UserName = principal.UserDetails ?? "",
                IdentityProvider = principal.IdentityProvider ?? ""
            };
        }

        public static bool IsAuthenticated(string? principalHeader) => TryDecodePrincipal(principalHeader, out _);
    }
}
