using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace BlazorApp.Client.PhotoShoot
{
    /// <summary>
    /// Optional label, icon, and access for a page that uses <see cref="PhotoShootLiveLayout"/>.
    /// A new live page is listed automatically; set this when the route name is not the label you want.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PhotoShootLiveModeAttribute : Attribute
    {
        public string? Label { get; set; }

        /// <summary>Bootstrap icon class, without the leading "bi ".</summary>
        public string? Icon { get; set; }

        public bool OwnerOnly { get; set; }

        public int Order { get; set; } = 100;
    }

    public sealed record PhotoShootLiveMode(string Href, string Label, string Icon, bool OwnerOnly, int Order);

    /// <summary>
    /// Live views shown in the header menu. Built-in entries cover director, photographer, and marker.
    /// Any other page with <c>@layout PhotoShootLiveLayout</c> is added when the app loads.
    /// </summary>
    public static class PhotoShootLiveModes
    {
        private const string DefaultIcon = "bi-display";

        private static readonly PhotoShootLiveMode[] BuiltIn =
        {
            new("photoshoot/director", "Director", "bi-megaphone", true, 0),
            new("photoshoot/photographer", "Photographer", "bi-camera", false, 1),
            new("photoshoot/marker", "Marker", "bi-bookmark", false, 2),
            new("photoshoot/uploads", "Uploads", "bi-cloud-arrow-up", false, 3),
        };

        public static IReadOnlyList<PhotoShootLiveMode> All { get; } = Discover();

        private static IReadOnlyList<PhotoShootLiveMode> Discover()
        {
            var byHref = BuiltIn.ToDictionary(mode => mode.Href, StringComparer.OrdinalIgnoreCase);
            try
            {
                var layout = typeof(PhotoShootLiveLayout);
                foreach (var type in layout.Assembly.GetTypes())
                {
                    if (type.GetCustomAttribute<LayoutAttribute>()?.LayoutType != layout)
                    {
                        continue;
                    }

                    var meta = type.GetCustomAttribute<PhotoShootLiveModeAttribute>();
                    foreach (var route in type.GetCustomAttributes<RouteAttribute>())
                    {
                        var href = Normalize(route.Template);
                        if (href == null || byHref.ContainsKey(href))
                        {
                            continue;
                        }

                        var label = string.IsNullOrWhiteSpace(meta?.Label) ? LabelFromHref(href) : meta!.Label.Trim();
                        var icon = string.IsNullOrWhiteSpace(meta?.Icon) ? DefaultIcon : meta!.Icon.Trim();
                        byHref[href] = new PhotoShootLiveMode(
                            href,
                            label,
                            icon,
                            meta?.OwnerOnly ?? false,
                            meta?.Order ?? 100);
                    }
                }
            }
            catch (ReflectionTypeLoadException)
            {
            }

            return byHref.Values
                .OrderBy(mode => mode.Order)
                .ThenBy(mode => mode.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? Normalize(string? template)
        {
            if (string.IsNullOrWhiteSpace(template) || template.Contains('{', StringComparison.Ordinal))
            {
                return null;
            }

            return template.Trim().TrimStart('/').TrimEnd('/');
        }

        private static string LabelFromHref(string href)
        {
            var segment = href.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Mode";
            var words = segment.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);
            var label = string.Join(' ', words);
            return label.Length == 0 ? "Mode" : label;
        }
    }
}
