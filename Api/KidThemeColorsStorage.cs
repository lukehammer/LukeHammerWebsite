using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using BlazorApp.Shared;

namespace ApiIsolated
{
    internal static class KidThemeColorsStorage
    {
        private const string BlobContainer = "camping-potluck";
        private const string BlobName = "kid-theme-colors.json";

        private static string? GetStorageConnectionString() =>
            Environment.GetEnvironmentVariable("LUKE_HAMMER_WEBSITE")
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        public static async Task<KidThemeColorsData> LoadAsync()
        {
            var json = await LoadRawAsync();
            return KidThemeColors.ParseJson(json);
        }

        public static async Task<KidThemeColorsData> SaveAsync(KidThemeColorsData colors)
        {
            var json = KidThemeColors.ToJson(colors);
            await PersistAsync(json);
            return KidThemeColors.ParseJson(json);
        }

        private static async Task<string> LoadRawAsync()
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
                return await SeedAndPersistAsync();
            }

            var download = await blob.DownloadContentAsync();
            return download.Value.Content.ToString();
        }

        private static bool IsAzureEnvironment() =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

        private static string LocalFilePath =>
            Path.Combine(Path.GetTempPath(), "lukehammer-kid-theme-colors.json");

        private static string SeedFilePath =>
            Path.Combine(AppContext.BaseDirectory, "data", "kid-theme-colors.json");

        private static async Task<string> LoadFromLocalFileAsync()
        {
            if (!File.Exists(LocalFilePath))
            {
                return await SeedAndPersistAsync();
            }

            return await File.ReadAllTextAsync(LocalFilePath);
        }

        private static async Task<string> SeedAndPersistAsync()
        {
            string json;
            if (File.Exists(SeedFilePath))
            {
                json = await File.ReadAllTextAsync(SeedFilePath);
            }
            else
            {
                json = KidThemeColors.ToJson(KidThemeColors.CreateDefaultData());
            }

            await PersistAsync(json);
            return json;
        }

        private static async Task PersistAsync(string json)
        {
            var connectionString = GetStorageConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                await File.WriteAllTextAsync(LocalFilePath, json);
                return;
            }

            var client = new BlobContainerClient(connectionString, BlobContainer);
            await client.CreateIfNotExistsAsync();
            var blob = client.GetBlobClient(BlobName);
            await blob.UploadAsync(BinaryData.FromString(json), overwrite: true);
        }
    }
}
