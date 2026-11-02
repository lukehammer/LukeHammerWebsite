#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

// Shared by the API and tools/PhotoReviewImport (linked source), so both read and write the same layout.
namespace LuckyLuke.PhotoReviewStorage
{
    public sealed class StoredBytes
    {
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "application/octet-stream";
        public string ETag { get; set; } = "";
    }

    /// <summary>
    /// Minimal blob access for the photo review, under the private "photoshoot" container (or a local folder).
    /// Names are built by the caller from validated ids only.
    /// </summary>
    public interface IReviewBlobStore
    {
        bool IsLocal { get; }
        Task<StoredBytes?> ReadAsync(string name);
        Task<string?> GetETagAsync(string name);
        /// <summary>Returns the new ETag, or null when the condition failed.</summary>
        Task<string?> TryWriteAsync(string name, byte[] bytes, string contentType, string? ifMatchETag, bool createOnly);
        Task<bool> ExistsAsync(string name);
        Task<long?> SizeAsync(string name);
        /// <summary>Short-lived read-only URL for one blob, or null when the store can't make one (local).</summary>
        string? ReadUrl(string name, string downloadFileName, string contentType, bool attachment, TimeSpan lifetime);
    }

    public static class ReviewBlobNames
    {
        public const string Container = "photoshoot";
        public const string AdminBinding = "review/admin-binding.json";

        public static string Catalog(string shootId) => $"review/{shootId}/catalog.json";
        public static string Participation(string shootId) => $"review/{shootId}/participation.json";
        public static string Asset(string sha256) => $"review/assets/{sha256}";
        public static string Thumb(string sha256, int width) => $"review/thumbs/{sha256}-{width}.webp";
    }

    public sealed class BlobReviewStore : IReviewBlobStore
    {
        private readonly BlobContainerClient _container;
        private static readonly SemaphoreSlim EnsureLock = new SemaphoreSlim(1, 1);
        private static bool _ready;

        public BlobReviewStore(string connectionString)
        {
            _container = new BlobContainerClient(connectionString, ReviewBlobNames.Container);
        }

        public bool IsLocal => false;

        private async Task<BlobContainerClient> ContainerAsync()
        {
            if (_ready)
            {
                return _container;
            }

            await EnsureLock.WaitAsync();
            try
            {
                if (!_ready)
                {
                    // Private container: never public. Assets are served by the API or short-lived single-blob SAS.
                    await _container.CreateIfNotExistsAsync(PublicAccessType.None);
                    _ready = true;
                }
            }
            finally
            {
                EnsureLock.Release();
            }

            return _container;
        }

        public async Task<StoredBytes?> ReadAsync(string name)
        {
            var blob = (await ContainerAsync()).GetBlobClient(name);
            try
            {
                var result = await blob.DownloadContentAsync();
                return new StoredBytes
                {
                    Bytes = result.Value.Content.ToArray(),
                    ContentType = result.Value.Details.ContentType ?? "application/octet-stream",
                    ETag = result.Value.Details.ETag.ToString()
                };
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<string?> GetETagAsync(string name)
        {
            try
            {
                var props = await (await ContainerAsync()).GetBlobClient(name).GetPropertiesAsync();
                return props.Value.ETag.ToString();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<string?> TryWriteAsync(string name, byte[] bytes, string contentType, string? ifMatchETag, bool createOnly)
        {
            var blob = (await ContainerAsync()).GetBlobClient(name);
            var conditions = createOnly
                ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                : string.IsNullOrEmpty(ifMatchETag) ? null : new BlobRequestConditions { IfMatch = new ETag(ifMatchETag!) };
            try
            {
                var result = await blob.UploadAsync(new BinaryData(bytes), new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                    Conditions = conditions
                });
                return result.Value.ETag.ToString();
            }
            catch (RequestFailedException ex) when (ex.Status == 412 || ex.Status == 409)
            {
                return null;
            }
        }

        public async Task<bool> ExistsAsync(string name) => (await (await ContainerAsync()).GetBlobClient(name).ExistsAsync()).Value;

        public async Task<long?> SizeAsync(string name)
        {
            try
            {
                return (await (await ContainerAsync()).GetBlobClient(name).GetPropertiesAsync()).Value.ContentLength;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public string? ReadUrl(string name, string downloadFileName, string contentType, bool attachment, TimeSpan lifetime)
        {
            var blob = _container.GetBlobClient(name);
            if (!blob.CanGenerateSasUri)
            {
                return null;
            }

            // Round the expiry so repeated requests within a window reuse the same URL (browser cache friendly).
            var now = DateTimeOffset.UtcNow;
            var window = TimeSpan.FromMinutes(10);
            var expires = new DateTimeOffset(now.Ticks - now.Ticks % window.Ticks, TimeSpan.Zero) + window + lifetime;
            var builder = new BlobSasBuilder(BlobSasPermissions.Read, expires)
            {
                BlobContainerName = ReviewBlobNames.Container,
                BlobName = name,
                Resource = "b",
                Protocol = SasProtocol.Https,
                ContentType = contentType,
                ContentDisposition = (attachment ? "attachment" : "inline") + "; filename=\"" + SafeFileName(downloadFileName) + "\"",
                CacheControl = "private, max-age=3600"
            };
            return blob.GenerateSasUri(builder).ToString();
        }

        public static string SafeFileName(string name)
        {
            var builder = new StringBuilder();
            foreach (var c in name ?? "")
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '-');
            }

            return builder.Length == 0 ? "photo" : builder.ToString();
        }
    }

    /// <summary>Local folder with the same layout (development and tests). ETag = SHA-256 of the content.</summary>
    public sealed class LocalReviewStore : IReviewBlobStore
    {
        private readonly string _root;
        private static readonly SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);

        public LocalReviewStore(string root)
        {
            _root = Path.GetFullPath(root);
        }

        public bool IsLocal => true;

        private string PathFor(string name)
        {
            var full = Path.GetFullPath(Path.Combine(_root, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid storage name.");
            }

            return full;
        }

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return "\"" + Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant().Substring(0, 24) + "\"";
        }

        public async Task<StoredBytes?> ReadAsync(string name)
        {
            var path = PathFor(name);
            if (!File.Exists(path))
            {
                return null;
            }

            // A concurrent writer's atomic replace can briefly lock the file on Windows; retry instead of failing.
            byte[] bytes;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    bytes = await File.ReadAllBytesAsync(path);
                    break;
                }
                catch (IOException) when (attempt < 20)
                {
                    await Task.Delay(10 + attempt * 5);
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
            }

            var typePath = path + ".type";
            return new StoredBytes
            {
                Bytes = bytes,
                ContentType = File.Exists(typePath) ? (await File.ReadAllTextAsync(typePath)).Trim() : "application/octet-stream",
                ETag = Hash(bytes)
            };
        }

        public async Task<string?> GetETagAsync(string name) => (await ReadAsync(name))?.ETag;

        public async Task<string?> TryWriteAsync(string name, byte[] bytes, string contentType, string? ifMatchETag, bool createOnly)
        {
            await WriteLock.WaitAsync();
            try
            {
                var path = PathFor(name);
                var exists = File.Exists(path);
                if (createOnly && exists)
                {
                    return null;
                }

                if (!string.IsNullOrEmpty(ifMatchETag))
                {
                    if (!exists || Hash(await File.ReadAllBytesAsync(path)) != ifMatchETag)
                    {
                        return null;
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes);
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Move(temp, path, true);
                        break;
                    }
                    catch (IOException) when (attempt < 20)
                    {
                        // A reader has the file open; wait for it.
                        await Task.Delay(10 + attempt * 5);
                    }
                }

                await File.WriteAllTextAsync(path + ".type", contentType);
                return Hash(bytes);
            }
            finally
            {
                WriteLock.Release();
            }
        }

        public Task<bool> ExistsAsync(string name) => Task.FromResult(File.Exists(PathFor(name)));

        public Task<long?> SizeAsync(string name)
        {
            var path = PathFor(name);
            return Task.FromResult(File.Exists(path) ? new FileInfo(path).Length : (long?)null);
        }

        public string? ReadUrl(string name, string downloadFileName, string contentType, bool attachment, TimeSpan lifetime) => null;
    }
}
