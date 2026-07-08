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
            try
            {
                var state = await CampingPotluckStorage.LoadAsync();
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(state);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load camping potluck survey.");
                return await ServerError(req, ex.Message);
            }
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

            try
            {
                var name = request.Name.Trim();
                var bringing = request.Bringing?.Trim() ?? "";

                var state = await CampingPotluckStorage.UpdateAsync(current =>
                {
                    var participant = current.Participants.FirstOrDefault(p =>
                        string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

                    if (participant == null)
                    {
                        participant = new PotluckParticipant { Name = name };
                        current.Participants.Add(participant);
                    }

                    participant.Bringing = bringing;
                    participant.SubmittedAt = DateTime.UtcNow;
                    return current;
                });

                _logger.LogInformation("Camping participant saved for {Name}", name);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(state);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save camping participant.");
                return await ServerError(req, ex.Message);
            }
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

            try
            {
                var name = request.Name.Trim();
                var optionId = request.OptionId.Trim().ToLowerInvariant();

                var state = await CampingPotluckStorage.UpdateAsync(current =>
                {
                    if (!current.Options.Any(option => string.Equals(option.Id, optionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException("Option not found.");
                    }

                    var participant = current.Participants.FirstOrDefault(p =>
                        string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

                    if (participant == null)
                    {
                        participant = new PotluckParticipant { Name = name };
                        current.Participants.Add(participant);
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
                    return current;
                });

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(state);
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message == "Option not found.")
            {
                return await BadRequest(req, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save camping vote.");
                return await ServerError(req, ex.Message);
            }
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

            try
            {
                var label = request.Label.Trim();
                var optionId = PotluckOptionIds.ToCustomOptionId(label);

                var state = await CampingPotluckStorage.UpdateAsync(current =>
                {
                    if (current.Options.Any(option => string.Equals(option.Id, optionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException("That option already exists.");
                    }

                    current.Options.Add(new PotluckOptionDefinition
                    {
                        Id = optionId,
                        Label = label,
                        IsBuiltIn = false,
                        CreatedBy = request.CreatedBy?.Trim() ?? "",
                        CreatedAt = DateTime.UtcNow
                    });

                    return current;
                });

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(state);
                return response;
            }
            catch (InvalidOperationException ex) when (ex.Message == "That option already exists.")
            {
                return await BadRequest(req, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add camping option.");
                return await ServerError(req, ex.Message);
            }
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

            try
            {
                var optionId = request.OptionId.Trim();

                var state = await CampingPotluckStorage.UpdateAsync(current =>
                {
                    var option = current.Options.FirstOrDefault(o =>
                        string.Equals(o.Id, optionId, StringComparison.OrdinalIgnoreCase));

                    if (option == null)
                    {
                        throw new InvalidOperationException("Option not found.");
                    }

                    if (option.IsBuiltIn)
                    {
                        throw new InvalidOperationException("Built-in options cannot be removed.");
                    }

                    current.Options.Remove(option);

                    foreach (var participant in current.Participants)
                    {
                        participant.Ratings.Remove(option.Id);
                    }

                    return current;
                });

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(state);
                return response;
            }
            catch (InvalidOperationException ex)
            {
                return await BadRequest(req, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove camping option.");
                return await ServerError(req, ex.Message);
            }
        }

        private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync(message);
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
