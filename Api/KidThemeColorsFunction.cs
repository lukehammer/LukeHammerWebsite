using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorApp.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ApiIsolated
{
    public class KidThemeColorsFunction
    {
        private readonly ILogger _logger;

        public KidThemeColorsFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<KidThemeColorsFunction>();
        }

        [Function("GetKidThemeColors")]
        public async Task<HttpResponseData> Get(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sports/kid-themes")] HttpRequestData req)
        {
            try
            {
                var data = await KidThemeColorsStorage.LoadAsync();
                return await OkJson(req, KidThemeColors.ToJson(data));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load kid theme colors.");
                return await ServerError(req, ex.Message);
            }
        }

        [Function("PutKidThemeColors")]
        public async Task<HttpResponseData> Put(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "sports/kid-themes")] HttpRequestData req)
        {
            var request = await req.ReadFromJsonAsync<KidThemeColorsWriteRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.SubmittedBy))
            {
                return await BadRequest(req, "submittedBy is required.");
            }

            if (!ScheduleSubmitters.TryResolveSubmitterForWrite(request.SubmittedBy, out var submittedBy, out var submitterError))
            {
                return await BadRequest(req, submitterError);
            }

            if (request.Colors?.BackgroundByKid == null || request.Colors.BackgroundByKid.Count == 0)
            {
                return await BadRequest(req, "backgroundByKid is required.");
            }

            foreach (var kid in ScheduleKids.AllowedKids)
            {
                if (!request.Colors.BackgroundByKid.TryGetValue(kid, out var hex) || string.IsNullOrWhiteSpace(hex))
                {
                    return await BadRequest(req, $"Background color for {kid} is required.");
                }

                if (!KidThemeColors.IsValidHex(hex.Trim()))
                {
                    return await BadRequest(req, $"Invalid color for {kid}. Use #RRGGBB format.");
                }
            }

            foreach (var pair in request.Colors.BackgroundByKid)
            {
                if (!ScheduleKids.IsAllowed(pair.Key))
                {
                    return await BadRequest(req, $"Unknown kid '{pair.Key}'.");
                }
            }

            if (!KidThemeColors.TryValidateColorRules(request.Colors, out var colorIssues))
            {
                return await BadRequestColorValidation(req, colorIssues);
            }

            try
            {
                var saved = await KidThemeColorsStorage.SaveAsync(request.Colors);
                var json = KidThemeColors.ToJson(saved);

                var commitMessage = $"Kid theme colors: {submittedBy} updated row backgrounds";
                var githubPath = Environment.GetEnvironmentVariable("GITHUB_KID_THEME_FILE_PATH")
                    ?? "Api/data/kid-theme-colors.json";
                await SportsSchedulesGitHubBackup.TryBackupAsync(_logger, json, commitMessage, githubPath);

                return await OkJson(req, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save kid theme colors.");
                return await ServerError(req, ex.Message);
            }
        }

        private static async Task<HttpResponseData> OkJson(HttpRequestData req, string json)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(json, Encoding.UTF8);
            return response;
        }

        private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync(message);
            return response;
        }

        private static async Task<HttpResponseData> BadRequestColorValidation(
            HttpRequestData req,
            IReadOnlyDictionary<string, KidThemeColorValidationIssue> issuesByKid)
        {
            var body = new KidThemeColorsValidationError
            {
                Error = KidThemeColors.FormatColorRuleError(issuesByKid),
                IssuesByKid = new Dictionary<string, KidThemeColorValidationIssue>(issuesByKid, StringComparer.Ordinal)
            };

            var json = JsonSerializer.Serialize(body, KidThemeColors.JsonOptions);
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(json, Encoding.UTF8);
            return response;
        }

        private static async Task<HttpResponseData> ServerError(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync(message);
            return response;
        }
    }
}
