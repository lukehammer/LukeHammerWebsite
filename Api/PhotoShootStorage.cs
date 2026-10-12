#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BlazorApp.Shared.PhotoShoot;

namespace ApiIsolated
{
    internal sealed class StoredPhotoShoot
    {
        public PhotoShootDocument Document { get; init; } = new PhotoShootDocument();
        public string ETag { get; init; } = "";
    }

    internal sealed class StoredImage
    {
        public byte[] Bytes { get; init; } = Array.Empty<byte>();
        public string ContentType { get; init; } = "application/octet-stream";
    }

    /// <summary>Raw document/image persistence. Shoot ids and image ids must already be validated.</summary>
    internal interface IPhotoShootStore
    {
        Task<IReadOnlyList<string>> ListShootIdsAsync();
        Task<(string Json, string ETag)?> ReadShootAsync(string shootId);
        Task<string?> GetShootETagAsync(string shootId);
        /// <summary>Returns the new ETag, or null when the ETag condition failed (concurrent update).</summary>
        Task<string?> TryWriteShootAsync(string shootId, string json, string? ifMatchETag, bool createOnly);
        Task DeleteShootAsync(string shootId);
        Task<string?> ReadSettingsAsync();
        Task WriteSettingsAsync(string json);
        Task WriteImageAsync(string imageId, byte[] bytes, string contentType);
        Task<StoredImage?> ReadImageAsync(string imageId);
        Task<bool> CopyImageAsync(string sourceImageId, string targetImageId);
        Task DeleteImageAsync(string imageId);
    }

    internal sealed class PhotoShootSettings
    {
        public string DefaultShootId { get; set; } = "";
    }

    /// <summary>
    /// Photo shoot persistence: one JSON document per shoot in the private "photoshoot" blob container
    /// (ETag optimistic concurrency with retry), with a local temp-folder fallback for development.
    /// </summary>
    internal static class PhotoShootStorage
    {
        private const int MaxSaveAttempts = 8;

        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private static IPhotoShootStore? _storeOverride;

        /// <summary>Test hook.</summary>
        internal static void UseStore(IPhotoShootStore? store) => _storeOverride = store;

        internal static IPhotoShootStore Store
        {
            get
            {
                if (_storeOverride != null)
                {
                    return _storeOverride;
                }

                var connectionString = Environment.GetEnvironmentVariable("LUKE_HAMMER_WEBSITE")
                    ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                if (!string.IsNullOrWhiteSpace(connectionString) && !connectionString.StartsWith("UseDevelopmentStorage", StringComparison.OrdinalIgnoreCase))
                {
                    return new BlobPhotoShootStore(connectionString);
                }

                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")))
                {
                    throw new InvalidOperationException(
                        "Shared storage is not configured. In Azure Portal, add app setting LUKE_HAMMER_WEBSITE with your storage account connection string.");
                }

                var dir = Environment.GetEnvironmentVariable("PHOTOSHOOT_LOCAL_DIR");
                return new LocalPhotoShootStore(string.IsNullOrWhiteSpace(dir)
                    ? Path.Combine(Path.GetTempPath(), "lukehammer-photoshoot")
                    : dir);
            }
        }

        public static PhotoShootDocument Deserialize(string json) =>
            PhotoShootEditor.Normalize(JsonSerializer.Deserialize<PhotoShootDocument>(json, JsonOptions) ?? new PhotoShootDocument());

        public static async Task<StoredPhotoShoot?> LoadAsync(string shootId)
        {
            var raw = await Store.ReadShootAsync(shootId);
            if (raw == null)
            {
                return null;
            }

            var doc = Deserialize(raw.Value.Json);
            doc.Id = shootId;
            return new StoredPhotoShoot { Document = doc, ETag = raw.Value.ETag };
        }

        public static Task<string?> GetETagAsync(string shootId) => Store.GetShootETagAsync(shootId);

        public static async Task<List<PhotoShootSummary>> ListAsync()
        {
            var settings = await LoadSettingsAsync();
            var summaries = new List<PhotoShootSummary>();
            foreach (var id in await Store.ListShootIdsAsync())
            {
                var stored = await LoadAsync(id);
                if (stored == null)
                {
                    continue;
                }

                var doc = stored.Document;
                summaries.Add(new PhotoShootSummary
                {
                    Id = doc.Id,
                    Name = doc.Name,
                    ShootDate = doc.ShootDate,
                    ShotCount = doc.Shots.Count,
                    CompletedCount = doc.Shots.Count(s => s.Status == ShotStatus.Completed),
                    SetupCount = doc.Setups.Count,
                    UpdatedUtc = doc.UpdatedUtc,
                    IsDefault = doc.Id == settings.DefaultShootId
                });
            }

            if (summaries.Count > 0 && !summaries.Any(s => s.IsDefault))
            {
                summaries.OrderBy(s => s.UpdatedUtc).First().IsDefault = true;
            }

            return summaries.OrderByDescending(s => s.IsDefault).ThenByDescending(s => s.UpdatedUtc).ToList();
        }

        public static async Task<PhotoShootSettings> LoadSettingsAsync()
        {
            var json = await Store.ReadSettingsAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                return new PhotoShootSettings();
            }

            try
            {
                return JsonSerializer.Deserialize<PhotoShootSettings>(json, JsonOptions) ?? new PhotoShootSettings();
            }
            catch (JsonException)
            {
                return new PhotoShootSettings();
            }
        }

        public static Task SaveSettingsAsync(PhotoShootSettings settings) =>
            Store.WriteSettingsAsync(JsonSerializer.Serialize(settings, JsonOptions));

        /// <summary>Creates a new shoot document. Fails if the id already exists.</summary>
        public static async Task<StoredPhotoShoot> CreateAsync(PhotoShootDocument doc)
        {
            PhotoShootEditor.Normalize(doc);
            doc.Revision = Math.Max(1, doc.Revision);
            var json = JsonSerializer.Serialize(doc, JsonOptions);
            var etag = await Store.TryWriteShootAsync(doc.Id, json, null, createOnly: true)
                ?? throw new InvalidOperationException("A photo shoot with that id already exists.");
            return new StoredPhotoShoot { Document = doc, ETag = etag };
        }

        /// <summary>
        /// Load → mutate → conditional save, retried on concurrent updates. The mutation runs again on the fresh
        /// document after a conflict, so it must be deterministic given the document.
        /// </summary>
        public static async Task<MutationOutcome> UpdateAsync(string shootId, Func<PhotoShootDocument, MutationOutcome> mutate)
        {
            for (var attempt = 0; attempt < MaxSaveAttempts; attempt++)
            {
                var stored = await LoadAsync(shootId);
                if (stored == null)
                {
                    return MutationOutcome.Fail(404, "Photo shoot not found.");
                }

                var doc = stored.Document;
                var outcome = mutate(doc);
                if (!outcome.Ok || !outcome.Save)
                {
                    outcome.Document = doc;
                    outcome.ETag = stored.ETag;
                    return outcome;
                }

                PhotoShootEditor.Normalize(doc);
                doc.Revision++;
                doc.UpdatedUtc = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(doc, JsonOptions);
                var etag = await Store.TryWriteShootAsync(shootId, json, stored.ETag, createOnly: false);
                if (etag != null)
                {
                    outcome.Document = doc;
                    outcome.ETag = etag;
                    return outcome;
                }

                await Task.Delay(40 * (attempt + 1));
            }

            return MutationOutcome.Fail(409, "Could not save because of concurrent updates. Please try again.");
        }

        public static Task DeleteAsync(string shootId) => Store.DeleteShootAsync(shootId);
    }

    internal sealed class MutationOutcome
    {
        public bool Ok { get; set; } = true;
        public bool Save { get; set; } = true;
        public int ErrorStatus { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public string Message { get; set; } = "";
        public PhotoShootDocument? Document { get; set; }
        public string ETag { get; set; } = "";
        /// <summary>Optional side-channel value produced by the mutation (e.g. a deleted image id).</summary>
        public string Extra { get; set; } = "";
        public string CreatedId { get; set; } = "";

        public static MutationOutcome Saved(string message = "") => new MutationOutcome { Message = message };
        public static MutationOutcome NoChange(string message = "") => new MutationOutcome { Save = false, Message = message };
        public static MutationOutcome Fail(int status, params string[] errors) =>
            new MutationOutcome { Ok = false, Save = false, ErrorStatus = status, Errors = errors.ToList() };
        public static MutationOutcome Fail(int status, List<string> errors) =>
            new MutationOutcome { Ok = false, Save = false, ErrorStatus = status, Errors = errors };
    }

    internal sealed class BlobPhotoShootStore : IPhotoShootStore
    {
        private const string ContainerName = "photoshoot";
        private const string ShootPrefix = "shoots/";
        private const string ImagePrefix = "images/";
        private const string SettingsBlob = "settings.json";
        private readonly string _connectionString;
        private static readonly SemaphoreSlim EnsureLock = new SemaphoreSlim(1, 1);
        private static bool _containerReady;

        public BlobPhotoShootStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        private async Task<BlobContainerClient> ContainerAsync()
        {
            var client = new BlobContainerClient(_connectionString, ContainerName);
            if (_containerReady)
            {
                return client;
            }

            await EnsureLock.WaitAsync();
            try
            {
                if (!_containerReady)
                {
                    // Private container: no public access. Images are only served through the authorized API.
                    await client.CreateIfNotExistsAsync(PublicAccessType.None);
                    _containerReady = true;
                }
            }
            finally
            {
                EnsureLock.Release();
            }

            return client;
        }

        public async Task<IReadOnlyList<string>> ListShootIdsAsync()
        {
            var container = await ContainerAsync();
            var ids = new List<string>();
            await foreach (var item in container.GetBlobsAsync(prefix: ShootPrefix))
            {
                var name = item.Name.Substring(ShootPrefix.Length);
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var id = name.Substring(0, name.Length - 5);
                    if (PhotoShootIds.IsValid(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            return ids;
        }

        public async Task<(string Json, string ETag)?> ReadShootAsync(string shootId)
        {
            var blob = (await ContainerAsync()).GetBlobClient(ShootPrefix + shootId + ".json");
            try
            {
                var download = await blob.DownloadContentAsync();
                return (download.Value.Content.ToString(), download.Value.Details.ETag.ToString());
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<string?> GetShootETagAsync(string shootId)
        {
            var blob = (await ContainerAsync()).GetBlobClient(ShootPrefix + shootId + ".json");
            try
            {
                var props = await blob.GetPropertiesAsync();
                return props.Value.ETag.ToString();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<string?> TryWriteShootAsync(string shootId, string json, string? ifMatchETag, bool createOnly)
        {
            var blob = (await ContainerAsync()).GetBlobClient(ShootPrefix + shootId + ".json");
            var conditions = createOnly
                ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                : string.IsNullOrWhiteSpace(ifMatchETag) ? null : new BlobRequestConditions { IfMatch = new ETag(ifMatchETag) };
            try
            {
                var result = await blob.UploadAsync(BinaryData.FromString(json), new BlobUploadOptions
                {
                    Conditions = conditions,
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                });
                return result.Value.ETag.ToString();
            }
            catch (RequestFailedException ex) when (ex.Status == 412 || ex.Status == 409)
            {
                return null;
            }
        }

        public async Task DeleteShootAsync(string shootId)
        {
            await (await ContainerAsync()).GetBlobClient(ShootPrefix + shootId + ".json").DeleteIfExistsAsync();
        }

        public async Task<string?> ReadSettingsAsync()
        {
            var blob = (await ContainerAsync()).GetBlobClient(SettingsBlob);
            try
            {
                return (await blob.DownloadContentAsync()).Value.Content.ToString();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task WriteSettingsAsync(string json)
        {
            await (await ContainerAsync()).GetBlobClient(SettingsBlob).UploadAsync(BinaryData.FromString(json), overwrite: true);
        }

        public async Task WriteImageAsync(string imageId, byte[] bytes, string contentType)
        {
            var blob = (await ContainerAsync()).GetBlobClient(ImagePrefix + imageId);
            await blob.UploadAsync(new BinaryData(bytes), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            });
        }

        public async Task<StoredImage?> ReadImageAsync(string imageId)
        {
            var blob = (await ContainerAsync()).GetBlobClient(ImagePrefix + imageId);
            try
            {
                var download = await blob.DownloadContentAsync();
                return new StoredImage
                {
                    Bytes = download.Value.Content.ToArray(),
                    ContentType = download.Value.Details.ContentType ?? "application/octet-stream"
                };
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<bool> CopyImageAsync(string sourceImageId, string targetImageId)
        {
            var image = await ReadImageAsync(sourceImageId);
            if (image == null)
            {
                return false;
            }

            await WriteImageAsync(targetImageId, image.Bytes, image.ContentType);
            return true;
        }

        public async Task DeleteImageAsync(string imageId)
        {
            await (await ContainerAsync()).GetBlobClient(ImagePrefix + imageId).DeleteIfExistsAsync();
        }
    }

    /// <summary>Development fallback when no storage connection string is configured (never used in Azure).</summary>
    internal sealed class LocalPhotoShootStore : IPhotoShootStore
    {
        private static readonly SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);
        private readonly string _root;

        public LocalPhotoShootStore(string root)
        {
            _root = root;
            Directory.CreateDirectory(ShootDir);
            Directory.CreateDirectory(ImageDir);
        }

        private string ShootDir => Path.Combine(_root, "shoots");
        private string ImageDir => Path.Combine(_root, "images");
        private string ShootPath(string id) => Path.Combine(ShootDir, id + ".json");
        private string ImagePath(string id) => Path.Combine(ImageDir, id);
        private string ImageTypePath(string id) => Path.Combine(ImageDir, id + ".type");
        private string SettingsPath => Path.Combine(_root, "settings.json");

        private static string Hash(string json)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            return "\"" + BitConverter.ToString(bytes, 0, 12).Replace("-", "").ToLowerInvariant() + "\"";
        }

        public Task<IReadOnlyList<string>> ListShootIdsAsync()
        {
            IReadOnlyList<string> ids = Directory.GetFiles(ShootDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(id => PhotoShootIds.IsValid(id))
                .Select(id => id!)
                .ToList();
            return Task.FromResult(ids);
        }

        public async Task<(string Json, string ETag)?> ReadShootAsync(string shootId)
        {
            var path = ShootPath(shootId);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path);
            return (json, Hash(json));
        }

        public async Task<string?> GetShootETagAsync(string shootId) => (await ReadShootAsync(shootId))?.ETag;

        public async Task<string?> TryWriteShootAsync(string shootId, string json, string? ifMatchETag, bool createOnly)
        {
            await WriteLock.WaitAsync();
            try
            {
                var path = ShootPath(shootId);
                var exists = File.Exists(path);
                if (createOnly && exists)
                {
                    return null;
                }

                if (!createOnly && !string.IsNullOrWhiteSpace(ifMatchETag))
                {
                    var currentEtag = exists ? Hash(await File.ReadAllTextAsync(path)) : null;
                    if (currentEtag != ifMatchETag)
                    {
                        return null;
                    }
                }

                var temp = path + ".tmp";
                await File.WriteAllTextAsync(temp, json);
                File.Move(temp, path, overwrite: true);
                return Hash(json);
            }
            finally
            {
                WriteLock.Release();
            }
        }

        public Task DeleteShootAsync(string shootId)
        {
            var path = ShootPath(shootId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return Task.CompletedTask;
        }

        public async Task<string?> ReadSettingsAsync() =>
            File.Exists(SettingsPath) ? await File.ReadAllTextAsync(SettingsPath) : null;

        public Task WriteSettingsAsync(string json) => File.WriteAllTextAsync(SettingsPath, json);

        public async Task WriteImageAsync(string imageId, byte[] bytes, string contentType)
        {
            await File.WriteAllBytesAsync(ImagePath(imageId), bytes);
            await File.WriteAllTextAsync(ImageTypePath(imageId), contentType);
        }

        public async Task<StoredImage?> ReadImageAsync(string imageId)
        {
            var path = ImagePath(imageId);
            if (!File.Exists(path))
            {
                return null;
            }

            var type = File.Exists(ImageTypePath(imageId)) ? await File.ReadAllTextAsync(ImageTypePath(imageId)) : "application/octet-stream";
            return new StoredImage { Bytes = await File.ReadAllBytesAsync(path), ContentType = type };
        }

        public async Task<bool> CopyImageAsync(string sourceImageId, string targetImageId)
        {
            var image = await ReadImageAsync(sourceImageId);
            if (image == null)
            {
                return false;
            }

            await WriteImageAsync(targetImageId, image.Bytes, image.ContentType);
            return true;
        }

        public Task DeleteImageAsync(string imageId)
        {
            foreach (var path in new[] { ImagePath(imageId), ImageTypePath(imageId) })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            return Task.CompletedTask;
        }
    }
}
