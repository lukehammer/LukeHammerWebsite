using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BlazorApp.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ApiIsolated
{
    public class CampingPotluckFunction
    {
        private readonly ILogger _logger;

        public CampingPotluckFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<CampingPotluckFunction>();
        }

        [Function("GetCampingPotluck")]
        public async Task<HttpResponseData> Get(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "camping/potluck")] HttpRequestData req)
        {
            var state = CampingPotluckMigration.Normalize(await CampingPotluckStorage.LoadAsync());
            await CampingPotluckStorage.SaveAsync(state);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(state);
            return response;
        }

        [Function("SaveCampingParticipant")]
        public async Task<HttpResponseData> SaveParticipant(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "camping/potluck/participant")] HttpRequestData req)
        {
            var request = await req.ReadFromJsonAsync<PotluckParticipantRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                return await BadRequest(req, "Name is required.");
            }

            var state = CampingPotluckMigration.Normalize(await CampingPotluckStorage.LoadAsync());
            var name = request.Name.Trim();
            var bringing = request.Bringing?.Trim() ?? "";

            var participant = state.Participants.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (participant == null)
            {
                participant = new PotluckParticipant { Name = name };
                state.Participants.Add(participant);
            }

            participant.Bringing = bringing;
            participant.SubmittedAt = DateTime.UtcNow;

            await CampingPotluckStorage.SaveAsync(state);
            _logger.LogInformation("Camping participant saved for {Name}", name);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(state);
            return response;
        }

        [Function("RateCampingOption")]
        public async Task<HttpResponseData> RateOption(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "camping/potluck/rate")] HttpRequestData req)
        {
            var request = await req.ReadFromJsonAsync<PotluckRateRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.OptionId))
            {
                return await BadRequest(req, "Name and option id are required.");
            }

            if (request.Rating is not (1 or -1 or 0))
            {
                return await BadRequest(req, "Rating must be 1, -1, or 0.");
            }

            var state = CampingPotluckMigration.Normalize(await CampingPotluckStorage.LoadAsync());
            var name = request.Name.Trim();
            var optionId = request.OptionId.Trim().ToLowerInvariant();

            if (!state.Options.Any(option => string.Equals(option.Id, optionId, StringComparison.OrdinalIgnoreCase)))
            {
                return await BadRequest(req, "Option not found.");
            }

            var participant = state.Participants.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (participant == null)
            {
                participant = new PotluckParticipant { Name = name };
                state.Participants.Add(participant);
            }

            if (request.Rating == 0)
            {
                participant.Ratings.Remove(optionId);
            }
            else
            {
                participant.Ratings[optionId] = request.Rating;
            }

            participant.SubmittedAt = DateTime.UtcNow;
            await CampingPotluckStorage.SaveAsync(state);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(state);
            return response;
        }

        [Function("AddCampingOption")]
        public async Task<HttpResponseData> AddOption(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "camping/potluck/options")] HttpRequestData req)
        {
            var request = await req.ReadFromJsonAsync<PotluckAddOptionRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.Label))
            {
                return await BadRequest(req, "Option label is required.");
            }

            var state = CampingPotluckMigration.Normalize(await CampingPotluckStorage.LoadAsync());
            var label = request.Label.Trim();
            var optionId = PotluckOptionIds.ToCustomOptionId(label);

            if (state.Options.Any(option => string.Equals(option.Id, optionId, StringComparison.OrdinalIgnoreCase)))
            {
                return await BadRequest(req, "That option already exists.");
            }

            state.Options.Add(new PotluckOptionDefinition
            {
                Id = optionId,
                Label = label,
                IsBuiltIn = false,
                CreatedBy = request.CreatedBy?.Trim() ?? "",
                CreatedAt = DateTime.UtcNow
            });

            await CampingPotluckStorage.SaveAsync(state);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(state);
            return response;
        }

        [Function("RemoveCampingOption")]
        public async Task<HttpResponseData> RemoveOption(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "camping/potluck/options/remove")] HttpRequestData req)
        {
            var request = await req.ReadFromJsonAsync<PotluckRemoveOptionRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.OptionId))
            {
                return await BadRequest(req, "Option id is required.");
            }

            var state = CampingPotluckMigration.Normalize(await CampingPotluckStorage.LoadAsync());
            var option = state.Options.FirstOrDefault(o =>
                string.Equals(o.Id, request.OptionId.Trim(), StringComparison.OrdinalIgnoreCase));

            if (option == null)
            {
                return await BadRequest(req, "Option not found.");
            }

            if (option.IsBuiltIn)
            {
                return await BadRequest(req, "Built-in options cannot be removed.");
            }

            state.Options.Remove(option);

            foreach (var participant in state.Participants)
            {
                participant.Ratings.Remove(option.Id);
            }

            await CampingPotluckStorage.SaveAsync(state);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(state);
            return response;
        }

        private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync(message);
            return response;
        }
    }
}
