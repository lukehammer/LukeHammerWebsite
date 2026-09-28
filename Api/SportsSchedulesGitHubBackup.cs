using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ApiIsolated
{
    internal static class SportsSchedulesGitHubBackup
    {
        private static readonly HttpClient Http = new HttpClient
        {
            DefaultRequestHeaders =
            {
                UserAgent = { new ProductInfoHeaderValue("LukeHammerWebsite", "1.0") }
            }
        };

        public static async Task TryBackupAsync(
            ILogger logger,
            string json,
            string commitMessage,
            string? filePathOverride = null)
        {
            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogWarning(
                    "GITHUB_TOKEN not set; skipping GitHub backup.");
                return;
            }

            var repo = Environment.GetEnvironmentVariable("GITHUB_REPO");
            if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/'))
            {
                logger.LogWarning(
                    "GITHUB_REPO must be set to owner/name; skipping GitHub backup.");
                return;
            }

            var branch = Environment.GetEnvironmentVariable("GITHUB_BRANCH") ?? "main";
            var path = filePathOverride
                ?? Environment.GetEnvironmentVariable("GITHUB_SCHEDULE_FILE_PATH")
                ?? "Api/data/sports-schedules.json";

            try
            {
                var sha = await GetFileShaAsync(token, repo, path, branch);
                var encodedContent = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

                var payload = new
                {
                    message = commitMessage,
                    content = encodedContent,
                    branch,
                    sha
                };

                using var request = new HttpRequestMessage(
                    HttpMethod.Put,
                    $"https://api.github.com/repos/{repo}/contents/{path}");

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                var response = await Http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Sports schedules backed up to GitHub ({Repo}/{Path}).", repo, path);
                    return;
                }

                logger.LogWarning(
                    "GitHub backup failed with {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GitHub backup failed.");
            }
        }

        private static async Task<string?> GetFileShaAsync(string token, string repo, string path, string branch)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repo}/contents/{path}?ref={Uri.EscapeDataString(branch)}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var response = await Http.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("sha", out var shaProp))
            {
                return shaProp.GetString();
            }

            return null;
        }
    }
}
