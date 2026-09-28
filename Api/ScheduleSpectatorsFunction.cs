using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorApp.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ApiIsolated
{
    public class ScheduleSpectatorsFunction
    {
        private readonly ILogger _logger;

        public ScheduleSpectatorsFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ScheduleSpectatorsFunction>();
        }

        [Function("GetScheduleSpectators")]
        public async Task<HttpResponseData> Get(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sports/spectators")] HttpRequestData req)
        {
            try
            {
                var data = await ScheduleSpectatorsStorage.LoadAsync();
                var response = new ScheduleSpectatorsResponse
                {
                    Names = ScheduleSpectators.MergeNames(data).ToList()
                };

                return await OkJson(req, JsonSerializer.Serialize(response, ScheduleSpectators.JsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load schedule spectators.");
                return await ServerError(req, ex.Message);
            }
        }

        [Function("PostScheduleSpectators")]
        public async Task<HttpResponseData> Post(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sports/spectators")] HttpRequestData req)
        {
            ScheduleSpectatorsAppendRequest? request;
            try
            {
                request = await req.ReadFromJsonAsync<ScheduleSpectatorsAppendRequest>();
            }
            catch (JsonException)
            {
                return await BadRequest(req, "Invalid JSON body.");
            }

            if (request?.AddNames == null || request.AddNames.Count == 0)
            {
                return await BadRequest(req, "addNames is required.");
            }

            try
            {
                var current = await ScheduleSpectatorsStorage.LoadAsync();
                var merged = ScheduleSpectators.MergeCustomNames(current, request.AddNames);
                await ScheduleSpectatorsStorage.SaveAsync(merged);

                var response = new ScheduleSpectatorsResponse
                {
                    Names = ScheduleSpectators.MergeNames(merged).ToList()
                };

                return await OkJson(req, JsonSerializer.Serialize(response, ScheduleSpectators.JsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save schedule spectators.");
                return await ServerError(req, ex.Message);
            }
        }

        private static async Task<HttpResponseData> OkJson(HttpRequestData req, string json)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(json);
            return response;
        }

        private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(new { error = message }));
            return response;
        }

        private static async Task<HttpResponseData> ServerError(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(new { error = message }));
            return response;
        }

        private sealed class ScheduleSpectatorsAppendRequest
        {
            public List<string>? AddNames { get; set; }
        }

        private sealed class ScheduleSpectatorsResponse
        {
            public List<string> Names { get; set; } = new();
        }
    }
}
