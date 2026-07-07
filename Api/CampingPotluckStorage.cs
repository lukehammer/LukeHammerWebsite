using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using BlazorApp.Shared;

namespace ApiIsolated
{
    internal static class CampingPotluckStorage
    {
        private const string BlobContainer = "camping-potluck";
        private const string BlobName = "survey.json";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static async Task<PotluckSurveyState> LoadAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return await LoadFromLocalFileAsync();
            }

            var client = new BlobContainerClient(connectionString, BlobContainer);
            await client.CreateIfNotExistsAsync();
            var blob = client.GetBlobClient(BlobName);

            if (!await blob.ExistsAsync())
            {
                return new PotluckSurveyState();
            }

            var download = await blob.DownloadContentAsync();
            return JsonSerializer.Deserialize<PotluckSurveyState>(download.Value.Content.ToString(), JsonOptions)
                ?? new PotluckSurveyState();
        }

        public static async Task SaveAsync(PotluckSurveyState state)
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                await SaveToLocalFileAsync(json);
                return;
            }

            var client = new BlobContainerClient(connectionString, BlobContainer);
            await client.CreateIfNotExistsAsync();
            var blob = client.GetBlobClient(BlobName);
            await blob.UploadAsync(BinaryData.FromString(json), overwrite: true);
        }

        private static string LocalFilePath =>
            Path.Combine(Path.GetTempPath(), "lukehammer-camping-potluck-survey.json");

        private static async Task<PotluckSurveyState> LoadFromLocalFileAsync()
        {
            if (!File.Exists(LocalFilePath))
            {
                return new PotluckSurveyState();
            }

            var json = await File.ReadAllTextAsync(LocalFilePath);
            return JsonSerializer.Deserialize<PotluckSurveyState>(json, JsonOptions)
                ?? new PotluckSurveyState();
        }

        private static async Task SaveToLocalFileAsync(string json)
        {
            await File.WriteAllTextAsync(LocalFilePath, json);
        }
    }
}
