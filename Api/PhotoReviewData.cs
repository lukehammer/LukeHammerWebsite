#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlazorApp.Shared.PhotoReview;
using BlazorApp.Shared.PhotoShoot;
using LuckyLuke.PhotoReviewStorage;

namespace ApiIsolated
{
    internal sealed class ReviewContext
    {
        public PhotoShootDocument Planner { get; init; } = new PhotoShootDocument();
        public ReviewCatalog Catalog { get; init; } = new ReviewCatalog();
        public string CatalogETag { get; init; } = "";
    }

    /// <summary>
    /// Persistence for the photo review: catalog and participation documents with ETag optimistic
    /// concurrency, plus a short in-memory cache of the planner and catalog for asset requests.
    /// </summary>
    internal static class PhotoReviewData
    {
        private const int MaxAttempts = 30;
        private static IReviewBlobStore? _override;
        private static readonly SemaphoreSlim CacheLock = new SemaphoreSlim(1, 1);
        private static (string ShootId, string PlannerETag, string CatalogETag, DateTime CheckedUtc, ReviewContext Context)? _cache;

        internal static void UseStore(IReviewBlobStore? store)
        {
            _override = store;
            _cache = null;
            _binding = null;
        }

        internal static IReviewBlobStore Store
        {
            get
            {
                if (_override != null)
                {
                    return _override;
                }

                var connectionString = Environment.GetEnvironmentVariable("LUKE_HAMMER_WEBSITE")
                    ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                if (!string.IsNullOrWhiteSpace(connectionString) && !connectionString.StartsWith("UseDevelopmentStorage", StringComparison.OrdinalIgnoreCase))
                {
                    return new BlobReviewStore(connectionString);
                }

                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")))
                {
                    throw new InvalidOperationException("Shared storage is not configured (LUKE_HAMMER_WEBSITE).");
                }

                var dir = Environment.GetEnvironmentVariable("PHOTOSHOOT_LOCAL_DIR");
                return new LocalReviewStore(string.IsNullOrWhiteSpace(dir) ? Path.Combine(Path.GetTempPath(), "lukehammer-photoshoot") : dir);
            }
        }

        public static async Task<string> DefaultShootIdAsync() => (await PhotoShootStorage.LoadSettingsAsync()).DefaultShootId ?? "";

        public static Task<IReadOnlyList<string>> AllShootIdsAsync() => PhotoShootStorage.ListShootIdsAsync();

        /// <summary>Planner + catalog, revalidated at most every few seconds per instance.</summary>
        public static async Task<ReviewContext?> LoadContextAsync(string shootId, bool fresh = false)
        {
            var cached = _cache;
            if (!fresh && cached != null && cached.Value.ShootId == shootId && DateTime.UtcNow - cached.Value.CheckedUtc < TimeSpan.FromSeconds(5))
            {
                return cached.Value.Context;
            }

            await CacheLock.WaitAsync();
            try
            {
                var plannerETag = await PhotoShootStorage.GetETagAsync(shootId);
                if (plannerETag == null)
                {
                    return null;
                }

                var catalogETag = await Store.GetETagAsync(ReviewBlobNames.Catalog(shootId)) ?? "";
                cached = _cache;
                if (cached != null && cached.Value.ShootId == shootId && cached.Value.PlannerETag == plannerETag && cached.Value.CatalogETag == catalogETag)
                {
                    _cache = (shootId, plannerETag, catalogETag, DateTime.UtcNow, cached.Value.Context);
                    return cached.Value.Context;
                }

                var planner = await PhotoShootStorage.LoadAsync(shootId);
                if (planner == null)
                {
                    return null;
                }

                var (catalog, etag) = await LoadCatalogAsync(shootId);
                var context = new ReviewContext { Planner = planner.Document, Catalog = catalog, CatalogETag = etag };
                _cache = (shootId, planner.ETag, etag, DateTime.UtcNow, context);
                return context;
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task<(ReviewCatalog Catalog, string ETag)> LoadCatalogAsync(string shootId)
        {
            var stored = await Store.ReadAsync(ReviewBlobNames.Catalog(shootId));
            if (stored == null)
            {
                return (new ReviewCatalog { ShootId = shootId }, "");
            }

            var catalog = JsonSerializer.Deserialize<ReviewCatalog>(stored.Bytes, PhotoShootStorage.JsonOptions) ?? new ReviewCatalog();
            catalog.ShootId = shootId;
            return (PhotoReviewIngest.Normalize(catalog), stored.ETag);
        }

        public static async Task<(ParticipationDocument Doc, string ETag)> LoadParticipationAsync(string shootId)
        {
            var stored = await Store.ReadAsync(ReviewBlobNames.Participation(shootId));
            if (stored == null)
            {
                return (new ParticipationDocument { ShootId = shootId }, "");
            }

            var doc = JsonSerializer.Deserialize<ParticipationDocument>(stored.Bytes, PhotoShootStorage.JsonOptions) ?? new ParticipationDocument();
            doc.ShootId = shootId;
            return (PhotoReviewParticipation.Normalize(doc), stored.ETag);
        }

        /// <summary>Applies <paramref name="mutate"/> to the latest participation document and saves it atomically.</summary>
        public static async Task<(ReviewResult Result, ParticipationDocument Doc)> UpdateParticipationAsync(string shootId, Func<ParticipationDocument, ReviewResult> mutate)
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var (doc, etag) = await LoadParticipationAsync(shootId);
                var result = mutate(doc);
                if (!result.Ok || !result.Changed)
                {
                    return (result, doc);
                }

                doc.Revision++;
                doc.UpdatedUtc = DateTime.UtcNow;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(doc, PhotoShootStorage.JsonOptions);
                var saved = await Store.TryWriteAsync(ReviewBlobNames.Participation(shootId), bytes, "application/json", etag.Length == 0 ? null : etag, createOnly: etag.Length == 0);
                if (saved != null)
                {
                    return (result, doc);
                }

                await Task.Delay(Random.Shared.Next(15, 60) * Math.Min(attempt + 1, 6));
            }

            return (ReviewResult.Fail(409, "Lots of people are saving at once. Please try again."), new ParticipationDocument());
        }

        public static async Task<(ReviewResult Result, ReviewCatalog Catalog)> UpdateCatalogAsync(string shootId, Func<ReviewCatalog, ReviewResult> mutate)
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var (catalog, etag) = await LoadCatalogAsync(shootId);
                var result = mutate(catalog);
                if (!result.Ok || !result.Changed)
                {
                    return (result, catalog);
                }

                catalog.Revision++;
                catalog.UpdatedUtc = DateTime.UtcNow;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, PhotoShootStorage.JsonOptions);
                var saved = await Store.TryWriteAsync(ReviewBlobNames.Catalog(shootId), bytes, "application/json", etag.Length == 0 ? null : etag, createOnly: etag.Length == 0);
                if (saved != null)
                {
                    _cache = null;
                    return (result, catalog);
                }

                await Task.Delay(Random.Shared.Next(15, 60) * Math.Min(attempt + 1, 6));
            }

            return (ReviewResult.Fail(409, "Could not save because of concurrent updates. Please try again."), new ReviewCatalog());
        }

        // ---------- admin binding ----------

        private static (AdminBinding? Binding, DateTime LoadedUtc)? _binding;

        public static AdminBinding? CachedBinding => _binding?.Binding;

        public static async Task<AdminBinding?> LoadBindingAsync()
        {
            var cached = _binding;
            if (cached != null && DateTime.UtcNow - cached.Value.LoadedUtc < TimeSpan.FromSeconds(60))
            {
                return cached.Value.Binding;
            }

            var stored = await Store.ReadAsync(ReviewBlobNames.AdminBinding);
            AdminBinding? binding = null;
            if (stored != null)
            {
                try
                {
                    binding = JsonSerializer.Deserialize<AdminBinding>(stored.Bytes, PhotoShootStorage.JsonOptions);
                }
                catch (JsonException)
                {
                    binding = null;
                }
            }

            _binding = (binding, DateTime.UtcNow);
            return binding;
        }

        /// <summary>Records the first matching Microsoft sign-in as the admin. Create-only, so it can never be overwritten.</summary>
        public static async Task<bool> TryBindAsync(SwaClientPrincipal principal)
        {
            var binding = new AdminBinding
            {
                Provider = principal.IdentityProvider,
                UserId = principal.UserId,
                UserDetails = principal.UserDetails,
                BoundUtc = DateTime.UtcNow,
                Source = "first matching Microsoft sign-in"
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(binding, PhotoShootStorage.JsonOptions);
            var saved = await Store.TryWriteAsync(ReviewBlobNames.AdminBinding, bytes, "application/json", null, createOnly: true);
            _binding = null;
            return saved != null;
        }

        public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

        public static bool IsShotId(string? id) => PhotoShootIds.IsValid(id);

        public static bool IsPhotoId(string? id) => PhotoReviewParticipation.IsHexId(id, 12);

        public static bool IsVersionId(string? id) => !string.IsNullOrEmpty(id) && id!.Length <= 20 && id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || c == '-');
    }
}
