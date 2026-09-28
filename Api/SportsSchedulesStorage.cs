using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BlazorApp.Shared;

namespace ApiIsolated
{
    internal sealed class StoredSportsSchedules
    {
        public SportsSchedulesData Data { get; init; } = new SportsSchedulesData();
        /// <summary>Azure blob version token from the last read; required for safe concurrent saves.</summary>
        public string? BlobSaveVersion { get; init; }
    }

    internal static class SportsSchedulesStorage
    {
        // Same storage account as camping potluck (LUKE_HAMMER_WEBSITE / AzureWebJobsStorage); dedicated blob.
        private const string BlobContainer = "camping-potluck";
        private const string BlobName = "sports-schedules.json";
        private const string LegacyBlobName = "family-schedules.json";
        private const string SeedHashBlobName = "sports-schedules.seed-hash";
        private const int MaxSaveAttempts = 8;

        private static string? GetStorageConnectionString()
        {
            var value = Environment.GetEnvironmentVariable("LUKE_HAMMER_WEBSITE")
                ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // Local dev often sets UseDevelopmentStorage without Azurite; use temp JSON instead of failing blob calls.
            if (value.Contains("UseDevelopmentStorage", StringComparison.OrdinalIgnoreCase)
                && !IsAzureEnvironment())
            {
                return null;
            }

            return value;
        }

        public static async Task<SportsSchedulesData> LoadAsync()
        {
            var stored = await LoadStoredAsync();
            stored.Data.Events ??= new System.Collections.Generic.List<Event>();

            var idsAdded = SportsSchedules.EnsureEventIds(stored.Data);
            if (idsAdded)
            {
                await SaveAsync(stored.Data, stored.BlobSaveVersion);
            }

            return stored.Data;
        }

        public static async Task<SportsSchedulesData> MutateAsync(
            Func<SportsSchedulesData, SportsSchedulesData> mutate)
        {
            for (var attempt = 0; attempt < MaxSaveAttempts; attempt++)
            {
                var stored = await LoadStoredAsync();
                var current = stored.Data;
                current.Events ??= new System.Collections.Generic.List<Event>();

                var updated = mutate(current);
                updated.Events ??= new System.Collections.Generic.List<Event>();
                SportsSchedules.EnsureEventIds(updated);

                if (await TrySaveAsync(updated, stored.BlobSaveVersion))
                {
                    return updated;
                }
            }

            throw new InvalidOperationException(
                "Could not save sports schedules because of concurrent updates. Please try again.");
        }

        private static async Task<StoredSportsSchedules> LoadStoredAsync()
        {
            var connectionString = GetStorageConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                if (IsAzureEnvironment())
                {
                    throw new InvalidOperationException(
                        "Shared storage is not configured. In Azure Portal, add app setting LUKE_HAMMER_WEBSITE with your storage account connection string.");
                }

                return await LoadFromLocalFileAsync();
            }

            var client = new BlobContainerClient(connectionString, BlobContainer);
            await client.CreateIfNotExistsAsync();
            var blob = client.GetBlobClient(BlobName);

            await TrySyncDeployedSeedToBlobAsync(client, blob);

            if (!await blob.ExistsAsync())
            {
                var legacyBlob = client.GetBlobClient(LegacyBlobName);
                if (await legacyBlob.ExistsAsync())
                {
                    var legacyDownload = await legacyBlob.DownloadContentAsync();
                    var legacyJson = legacyDownload.Value.Content.ToString();
                    await blob.UploadAsync(BinaryData.FromString(legacyJson), overwrite: true);
                    return new StoredSportsSchedules
                    {
                        Data = Deserialize(legacyJson),
                        BlobSaveVersion = (await blob.DownloadContentAsync()).Value.Details.ETag.ToString()
                    };
                }

                return await SeedAndPersistToBlobAsync(blob);
            }

            var download = await blob.DownloadContentAsync();
            return new StoredSportsSchedules
            {
                Data = Deserialize(download.Value.Content.ToString()),
                BlobSaveVersion = download.Value.Details.ETag.ToString()
            };
        }

        /// <summary>
        /// After deploy, repo seed is merged into the blob when its hash changes (live-only events kept; conflicts by lastModifiedAt).
        /// </summary>
        private static async Task TrySyncDeployedSeedToBlobAsync(BlobContainerClient client, BlobClient scheduleBlob)
        {
            if (!IsAzureEnvironment() || !File.Exists(SeedFilePath))
            {
                return;
            }

            var seedJson = await File.ReadAllTextAsync(SeedFilePath);
            var deployedHash = ComputeSha256Hex(seedJson);

            var hashBlob = client.GetBlobClient(SeedHashBlobName);
            string? appliedHash = null;
            if (await hashBlob.ExistsAsync())
            {
                appliedHash = (await hashBlob.DownloadContentAsync()).Value.Content.ToString().Trim();
            }

            if (string.Equals(appliedHash, deployedHash, StringComparison.Ordinal))
            {
                return;
            }

            if (appliedHash == null && await scheduleBlob.ExistsAsync())
            {
                var existingJson = (await scheduleBlob.DownloadContentAsync()).Value.Content.ToString();
                if (string.Equals(ComputeSha256Hex(existingJson), deployedHash, StringComparison.Ordinal))
                {
                    await hashBlob.UploadAsync(BinaryData.FromString(deployedHash), overwrite: true);
                    return;
                }
            }

            var seedData = Deserialize(seedJson);
            SportsSchedulesData merged;
            if (await scheduleBlob.ExistsAsync())
            {
                var existingJson = (await scheduleBlob.DownloadContentAsync()).Value.Content.ToString();
                merged = SportsSchedules.MergeDeployedSeed(seedData, Deserialize(existingJson));
            }
            else
            {
                merged = seedData;
            }

            var mergedJson = SportsSchedules.ToJson(merged);
            await scheduleBlob.UploadAsync(BinaryData.FromString(mergedJson), overwrite: true);
            await hashBlob.UploadAsync(BinaryData.FromString(deployedHash), overwrite: true);
        }

        private static async Task<StoredSportsSchedules> SeedAndPersistToBlobAsync(BlobClient blob)
        {
            if (!File.Exists(SeedFilePath))
            {
                return new StoredSportsSchedules();
            }

            var json = await File.ReadAllTextAsync(SeedFilePath);
            var data = Deserialize(json);
            await blob.UploadAsync(BinaryData.FromString(json), overwrite: true);

            var connectionString = GetStorageConnectionString();
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                var client = new BlobContainerClient(connectionString, BlobContainer);
                var hashBlob = client.GetBlobClient(SeedHashBlobName);
                await hashBlob.UploadAsync(BinaryData.FromString(ComputeSha256Hex(json)), overwrite: true);
            }

            var download = await blob.DownloadContentAsync();
            return new StoredSportsSchedules
            {
                Data = data,
                BlobSaveVersion = download.Value.Details.ETag.ToString()
            };
        }

        private static async Task<bool> TrySaveAsync(SportsSchedulesData data, string? savedBlobVersion)
        {
            var json = SportsSchedules.ToJson(data);
            var connectionString = GetStorageConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                await File.WriteAllTextAsync(LocalFilePath, json);
                return true;
            }

            var client = new BlobContainerClient(connectionString, BlobContainer);
            await client.CreateIfNotExistsAsync();
            var blob = client.GetBlobClient(BlobName);

            var uploadOptions = new BlobUploadOptions
            {
                Conditions = string.IsNullOrWhiteSpace(savedBlobVersion)
                    ? null
                    : new BlobRequestConditions { IfMatch = new ETag(savedBlobVersion) }
            };

            try
            {
                await blob.UploadAsync(BinaryData.FromString(json), uploadOptions);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 412 || ex.Status == 409)
            {
                return false;
            }
        }

        private static async Task SaveAsync(SportsSchedulesData data, string? savedBlobVersion)
        {
            if (!await TrySaveAsync(data, savedBlobVersion))
            {
                throw new InvalidOperationException(
                    "Could not save sports schedules because of concurrent updates. Please try again.");
            }
        }

        private static bool IsAzureEnvironment() =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

        private static string LocalFilePath =>
            Path.Combine(Path.GetTempPath(), "lukehammer-sports-schedules.json");

        private static string LocalSeedHashPath =>
            Path.Combine(Path.GetTempPath(), "lukehammer-sports-schedules.seed-hash");

        private static string LegacyLocalFilePath =>
            Path.Combine(Path.GetTempPath(), "lukehammer-family-schedules.json");

        private static string SeedFilePath =>
            Path.Combine(AppContext.BaseDirectory, "data", "sports-schedules.json");

        private static async Task<StoredSportsSchedules> LoadFromLocalFileAsync()
        {
            await TrySyncDeployedSeedToLocalAsync();

            if (!File.Exists(LocalFilePath))
            {
                if (File.Exists(LegacyLocalFilePath))
                {
                    var legacyJson = await File.ReadAllTextAsync(LegacyLocalFilePath);
                    await File.WriteAllTextAsync(LocalFilePath, legacyJson);

                    var legacyData = Deserialize(legacyJson);
                    await UpgradeLocalFileIfNeededAsync(legacyJson, legacyData);
                    return new StoredSportsSchedules { Data = legacyData };
                }

                return await SeedAndPersistLocalAsync();
            }

            var json = await File.ReadAllTextAsync(LocalFilePath);
            var data = Deserialize(json);
            await UpgradeLocalFileIfNeededAsync(json, data);
            return new StoredSportsSchedules { Data = data };
        }

        private static async Task<StoredSportsSchedules> SeedAndPersistLocalAsync()
        {
            if (!File.Exists(SeedFilePath))
            {
                return new StoredSportsSchedules();
            }

            var json = await File.ReadAllTextAsync(SeedFilePath);
            var data = Deserialize(json);
            await File.WriteAllTextAsync(LocalFilePath, json);
            await File.WriteAllTextAsync(LocalSeedHashPath, ComputeSha256Hex(json));
            return new StoredSportsSchedules { Data = data };
        }

        /// <summary>
        /// Local dev: when <c>Api/data/sports-schedules.json</c> changes (e.g. seed metadata), refresh temp storage.
        /// </summary>
        private static async Task TrySyncDeployedSeedToLocalAsync()
        {
            if (!File.Exists(SeedFilePath))
            {
                return;
            }

            var seedJson = await File.ReadAllTextAsync(SeedFilePath);
            var deployedHash = ComputeSha256Hex(seedJson);

            string? appliedHash = null;
            if (File.Exists(LocalSeedHashPath))
            {
                appliedHash = (await File.ReadAllTextAsync(LocalSeedHashPath)).Trim();
            }

            if (string.Equals(appliedHash, deployedHash, StringComparison.Ordinal))
            {
                return;
            }

            if (File.Exists(LocalFilePath))
            {
                var existingJson = await File.ReadAllTextAsync(LocalFilePath);
                if (string.Equals(ComputeSha256Hex(existingJson), deployedHash, StringComparison.Ordinal))
                {
                    await File.WriteAllTextAsync(LocalSeedHashPath, deployedHash);
                    return;
                }
            }

            var seedData = Deserialize(seedJson);
            SportsSchedulesData merged;
            if (File.Exists(LocalFilePath))
            {
                var existingJson = await File.ReadAllTextAsync(LocalFilePath);
                merged = SportsSchedules.MergeDeployedSeed(seedData, Deserialize(existingJson));
            }
            else
            {
                merged = seedData;
            }

            var mergedJson = SportsSchedules.ToJson(merged);
            await File.WriteAllTextAsync(LocalFilePath, mergedJson);
            await File.WriteAllTextAsync(LocalSeedHashPath, deployedHash);
        }

        private static SportsSchedulesData Deserialize(string json) =>
            SportsSchedules.ParseJson(json);

        private static async Task UpgradeLocalFileIfNeededAsync(string rawJson, SportsSchedulesData data)
        {
            var upgraded = SportsSchedules.ToJson(data);
            if (string.Equals(rawJson, upgraded, StringComparison.Ordinal))
            {
                return;
            }

            await File.WriteAllTextAsync(LocalFilePath, upgraded);
        }

        private static string ComputeSha256Hex(string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
