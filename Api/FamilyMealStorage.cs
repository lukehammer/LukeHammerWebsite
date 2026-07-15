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
    internal sealed class StoredFamilyMealSchedule
    {
        public FamilyMealScheduleState State { get; init; } = new FamilyMealScheduleState();
        public string? ETag { get; init; }
    }

    internal static class FamilyMealStorage
    {
        private const string BlobContainer = "family-meals";
        private const string BlobName = "schedule.json";
        private const int MaxSaveAttempts = 8;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static string? GetStorageConnectionString() =>
            Environment.GetEnvironmentVariable("LUKE_HAMMER_WEBSITE")
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        public static async Task<FamilyMealScheduleState> LoadAsync()
        {
            var stored = await LoadStoredAsync();
            return stored.State.Normalize();
        }

        public static async Task<FamilyMealScheduleState> UpdateAsync(Func<FamilyMealScheduleState, FamilyMealScheduleState> mutate)
        {
            for (var attempt = 0; attempt < MaxSaveAttempts; attempt++)
            {
                var stored = await LoadStoredAsync();
                var current = stored.State.Normalize();
                var updated = mutate(current).Normalize();

                if (await TrySaveAsync(updated, stored.ETag))
                {
                    return updated;
                }
            }

            throw new InvalidOperationException("Could not save meal schedule because of concurrent updates. Please try again.");
        }

        private static async Task<StoredFamilyMealSchedule> LoadStoredAsync()
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

            if (!await blob.ExistsAsync())
            {
                return new StoredFamilyMealSchedule
                {
                    State = FamilyMealScheduleState.CreateDefault()
                };
            }

            var download = await blob.DownloadContentAsync();
            var state = JsonSerializer.Deserialize<FamilyMealScheduleState>(download.Value.Content.ToString(), JsonOptions)
                ?? FamilyMealScheduleState.CreateDefault();

            return new StoredFamilyMealSchedule
            {
                State = state,
                ETag = download.Value.Details.ETag.ToString()
            };
        }

        private static async Task<bool> TrySaveAsync(FamilyMealScheduleState state, string? etag)
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
            Path.Combine(Path.GetTempPath(), "lukehammer-family-meal-schedule.json");

        private static async Task<StoredFamilyMealSchedule> LoadFromLocalFileAsync()
        {
            if (!File.Exists(LocalFilePath))
            {
                return new StoredFamilyMealSchedule
                {
                    State = FamilyMealScheduleState.CreateDefault()
                };
            }

            var json = await File.ReadAllTextAsync(LocalFilePath);
            return new StoredFamilyMealSchedule
            {
                State = JsonSerializer.Deserialize<FamilyMealScheduleState>(json, JsonOptions)
                    ?? FamilyMealScheduleState.CreateDefault()
            };
        }

        private static async Task SaveToLocalFileAsync(string json)
        {
            await File.WriteAllTextAsync(LocalFilePath, json);
        }
    }
}
