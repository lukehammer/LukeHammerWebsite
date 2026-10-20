using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BlazorApp.Shared;

namespace ApiIsolated
{
    internal static class FamilyEventsStorage
    {
        public const string LegacyCampingEventId = "camping-potluck";

        private const string BlobContainer = "camping-potluck";
        private const string BlobName = "family-events.json";
        private const int MaxSaveAttempts = 8;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private sealed class StoredEvents
        {
            public FamilyEventsData Data { get; init; } = new FamilyEventsData();
            public string? BlobSaveVersion { get; init; }
            public bool Exists { get; init; }
        }

        private static string? GetStorageConnectionString() =>
            Environment.GetEnvironmentVariable("LUKE_HAMMER_WEBSITE")
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        public static async Task<FamilyEventsData> LoadAsync()
        {
            var stored = await LoadStoredAsync();
            return Normalize(stored.Data);
        }

        public static async Task<FamilyEventsData> UpdateAsync(Func<FamilyEventsData, FamilyEventsData> mutate)
        {
            for (var attempt = 0; attempt < MaxSaveAttempts; attempt++)
            {
                var stored = await LoadStoredAsync();
                var updated = Normalize(mutate(Normalize(stored.Data)));

                if (await TrySaveAsync(updated, stored))
                {
                    return updated;
                }
            }

            throw new InvalidOperationException("Could not save events because of concurrent updates. Please try again.");
        }

        private static FamilyEventsData Normalize(FamilyEventsData data)
        {
            data.Events ??= new List<FamilyEvent>();
            data.AppliedSeedIds ??= new List<string>();
            foreach (var seed in FamilyEventsSeed.Create())
            {
                if (data.AppliedSeedIds.Contains(seed.Id, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                data.AppliedSeedIds.Add(seed.Id);
                if (!data.Events.Any(e => string.Equals(e.Id, seed.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    data.Events.Add(seed);
                }
            }

            foreach (var evt in data.Events)
            {
                evt.Rsvps ??= new List<FamilyEventRsvp>();
                evt.SignupLists ??= new List<FamilyEventSignupList>();
                foreach (var list in evt.SignupLists)
                {
                    list.Entries ??= new List<FamilyEventSignupEntry>();
                }

                evt.Potluck ??= new PotluckSurveyState();
                evt.Potluck.Options ??= new List<PotluckOptionDefinition>();
                evt.Potluck.Participants ??= new List<PotluckParticipant>();
                evt.Potluck.Votes = new List<PotluckVote>();
                evt.Questions ??= new List<FamilyEventQuestion>();
                foreach (var question in evt.Questions)
                {
                    question.Answers ??= new List<FamilyEventAnswer>();
                }

                foreach (var participant in evt.Potluck.Participants)
                {
                    participant.Ratings = new Dictionary<string, int>(
                        participant.Ratings ?? new Dictionary<string, int>(),
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            return data;
        }

        /// <summary>Seeds the event list from the original single camping survey the first time it is loaded.</summary>
        private static async Task<FamilyEventsData> CreateInitialAsync()
        {
            var data = new FamilyEventsData();
            var legacy = await CampingPotluckStorage.LoadAsync();
            if (legacy.Participants.Count == 0)
            {
                return data;
            }

            var lastActivity = legacy.Participants.Max(p => p.SubmittedAt);
            var eventDate = lastActivity == default
                ? WashingtonScheduleTime.PacificToday.AddDays(-(FamilyEventRules.EditableDaysAfterEvent + 1))
                : WashingtonScheduleTime.ToPacific(
                    new DateTimeOffset(DateTime.SpecifyKind(lastActivity, DateTimeKind.Utc))).Date;

            data.Events.Add(new FamilyEvent
            {
                Id = LegacyCampingEventId,
                Title = "Camping Potluck",
                Date = eventDate,
                Potluck = legacy
            });

            return data;
        }

        private static async Task<StoredEvents> LoadStoredAsync()
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
                return new StoredEvents { Data = await CreateInitialAsync() };
            }

            var download = await blob.DownloadContentAsync();
            var data = JsonSerializer.Deserialize<FamilyEventsData>(download.Value.Content.ToString(), JsonOptions)
                ?? new FamilyEventsData();

            return new StoredEvents
            {
                Data = data,
                BlobSaveVersion = download.Value.Details.ETag.ToString(),
                Exists = true
            };
        }

        private static async Task<bool> TrySaveAsync(FamilyEventsData data, StoredEvents stored)
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
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
                Conditions = stored.Exists
                    ? new BlobRequestConditions { IfMatch = new ETag(stored.BlobSaveVersion!) }
                    : new BlobRequestConditions { IfNoneMatch = ETag.All }
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
            Path.Combine(Path.GetTempPath(), "lukehammer-family-events.json");

        private static async Task<StoredEvents> LoadFromLocalFileAsync()
        {
            if (!File.Exists(LocalFilePath))
            {
                return new StoredEvents { Data = await CreateInitialAsync() };
            }

            var json = await File.ReadAllTextAsync(LocalFilePath);
            return new StoredEvents
            {
                Data = JsonSerializer.Deserialize<FamilyEventsData>(json, JsonOptions) ?? new FamilyEventsData()
            };
        }
    }
}
