using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BlazorApp.Shared;

namespace ApiIsolated
{
    internal sealed class StoredSurvey
    {
        public PotluckSurveyState State { get; init; } = new PotluckSurveyState();
        public string? ETag { get; init; }
    }

    internal static class CampingPotluckStorage
    {
        private const string BlobContainer = "camping-potluck";
        private const string BlobName = "survey.json";
        private const int MaxSaveAttempts = 8;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static string? GetStorageConnectionString() =>
            Environment.GetEnvironmentVariable("CAMPING_POTLUCK_STORAGE")
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        public static async Task<PotluckSurveyState> LoadAsync()
        {
            var stored = await LoadStoredAsync();
            return CampingPotluckMigration.Normalize(stored.State);
        }

        public static async Task<PotluckSurveyState> UpdateAsync(Func<PotluckSurveyState, PotluckSurveyState> mutate)
        {
            for (var attempt = 0; attempt < MaxSaveAttempts; attempt++)
            {
                var stored = await LoadStoredAsync();
                var current = CampingPotluckMigration.Normalize(stored.State);
                var updated = CampingPotluckMigration.Normalize(mutate(current));

                if (await TrySaveAsync(updated, stored.ETag))
                {
                    return updated;
                }
            }

            throw new InvalidOperationException("Could not save survey state because of concurrent updates. Please try again.");
        }

        private static async Task<StoredSurvey> LoadStoredAsync()
        {
            var connectionString = GetStorageConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                if (IsAzureEnvironment())
                {
                    throw new InvalidOperationException(
                        "Shared storage is not configured. In Azure Portal, add app setting CAMPING_POTLUCK_STORAGE with your storage account connection string.");
                }

                return await LoadFromLocalFileAsync();
            }

            var client = new BlobContainerClient(connectionString, BlobContainer);
            await client.CreateIfNotExistsAsync();
            var blob = client.GetBlobClient(BlobName);

            if (!await blob.ExistsAsync())
            {
                return new StoredSurvey();
            }

            var download = await blob.DownloadContentAsync();
            var state = JsonSerializer.Deserialize<PotluckSurveyState>(download.Value.Content.ToString(), JsonOptions)
                ?? new PotluckSurveyState();

            return new StoredSurvey
            {
                State = state,
                ETag = download.Value.Details.ETag.ToString()
            };
        }

        private static async Task<bool> TrySaveAsync(PotluckSurveyState state, string? etag)
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            var connectionString = GetStorageConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                await SaveToLocalFileAsync(json);
                return true;
            }

            var client = new BlobContainerClient(connectionString, BlobContainer);
            await client.CreateIfNotExistsAsync();
            var blob = client.GetBlobClient(BlobName);

            var uploadOptions = new BlobUploadOptions
            {
                Conditions = string.IsNullOrWhiteSpace(etag)
                    ? null
                    : new BlobRequestConditions { IfMatch = new ETag(etag) }
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

        private static bool IsAzureEnvironment() =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

        private static string LocalFilePath =>
            Path.Combine(Path.GetTempPath(), "lukehammer-camping-potluck-survey.json");

        private static async Task<StoredSurvey> LoadFromLocalFileAsync()
        {
            if (!File.Exists(LocalFilePath))
            {
                return new StoredSurvey();
            }

            var json = await File.ReadAllTextAsync(LocalFilePath);
            return new StoredSurvey
            {
                State = JsonSerializer.Deserialize<PotluckSurveyState>(json, JsonOptions) ?? new PotluckSurveyState()
            };
        }

        private static async Task SaveToLocalFileAsync(string json)
        {
            await File.WriteAllTextAsync(LocalFilePath, json);
        }
    }
}
