using System;

using System.IO;

using System.Text.Json;

using System.Text.Json.Serialization;

using System.Threading.Tasks;

using Azure.Storage.Blobs;

using BlazorApp.Shared;



namespace ApiIsolated

{

    internal static class SportsSchedulesStorage

    {

        // Same storage account as camping potluck (LUKE_HAMMER_WEBSITE / AzureWebJobsStorage); dedicated blob.

        private const string BlobContainer = "camping-potluck";

        private const string BlobName = "sports-schedules.json";

        private const string LegacyBlobName = "family-schedules.json";

        private static JsonSerializerOptions JsonOptions => SportsSchedules.JsonOptions;



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

            stored.Events ??= new System.Collections.Generic.List<Event>();

            var idsAdded = SportsSchedules.EnsureEventIds(stored);
            if (idsAdded)
            {
                await PersistAsync(SportsSchedules.ToJson(stored));
            }

            return stored;

        }



        public static async Task<SportsSchedulesData> MutateAsync(

            Func<SportsSchedulesData, SportsSchedulesData> mutate)

        {

            var current = await LoadAsync();

            var updated = mutate(current);

            updated.Events ??= new System.Collections.Generic.List<Event>();

            SportsSchedules.EnsureEventIds(updated);

            var json = SportsSchedules.ToJson(updated);

            await PersistAsync(json);

            return updated;

        }



        private static async Task<SportsSchedulesData> LoadStoredAsync()

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

                var legacyBlob = client.GetBlobClient(LegacyBlobName);

                if (await legacyBlob.ExistsAsync())

                {

                    var legacyDownload = await legacyBlob.DownloadContentAsync();

                    var legacyJson = legacyDownload.Value.Content.ToString();

                    await blob.UploadAsync(BinaryData.FromString(legacyJson), overwrite: true);

                    return Deserialize(legacyJson);

                }



                return await SeedAndPersistAsync();

            }



            var download = await blob.DownloadContentAsync();

            return Deserialize(download.Value.Content.ToString());

        }



        private static bool IsAzureEnvironment() =>

            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));



        private static string LocalFilePath =>

            Path.Combine(Path.GetTempPath(), "lukehammer-sports-schedules.json");



        private static string LegacyLocalFilePath =>

            Path.Combine(Path.GetTempPath(), "lukehammer-family-schedules.json");



        private static string SeedFilePath =>

            Path.Combine(AppContext.BaseDirectory, "data", "sports-schedules.json");



        private static async Task<SportsSchedulesData> LoadFromLocalFileAsync()

        {

            if (!File.Exists(LocalFilePath))

            {

                if (File.Exists(LegacyLocalFilePath))

                {

                    var legacyJson = await File.ReadAllTextAsync(LegacyLocalFilePath);

                    await File.WriteAllTextAsync(LocalFilePath, legacyJson);

                    var legacyData = Deserialize(legacyJson);
                    await UpgradeLocalFileIfNeededAsync(legacyJson, legacyData);
                    return legacyData;

                }



                return await SeedAndPersistAsync();

            }



            var json = await File.ReadAllTextAsync(LocalFilePath);

            var data = Deserialize(json);
            await UpgradeLocalFileIfNeededAsync(json, data);
            return data;

        }



        private static async Task<SportsSchedulesData> SeedAndPersistAsync()

        {

            if (!File.Exists(SeedFilePath))

            {

                return new SportsSchedulesData();

            }



            var json = await File.ReadAllTextAsync(SeedFilePath);

            var data = Deserialize(json);

            await PersistAsync(json);

            return data;

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

    }

}


