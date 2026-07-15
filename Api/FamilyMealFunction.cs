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
    public class FamilyMealFunction
    {
        private readonly ILogger _logger;

        public FamilyMealFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<FamilyMealFunction>();
        }

        [Function("GetFamilyMealSchedule")]
        public async Task<HttpResponseData> Get(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "family/meals")] HttpRequestData req)
        {
            try
            {
                var state = await FamilyMealStorage.LoadAsync();
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(state);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load family meal schedule.");
                return await ServerError(req, ex.Message);
            }
        }

        [Function("SaveFamilyMealAssignment")]
        public async Task<HttpResponseData> SaveAssignment(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family/meals/assignment")] HttpRequestData req)
        {
            var request = await req.ReadFromJsonAsync<FamilyMealAssignmentRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.SlotId))
            {
                return await BadRequest(req, "Meal slot is required.");
            }

            var slotExists = FamilyMealScheduleState.MealSlots.Any(slot =>
                string.Equals(slot.Id, request.SlotId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!slotExists)
            {
                return await BadRequest(req, "Unknown meal slot.");
            }

            try
            {
                var slotId = request.SlotId.Trim();
                var cookFamilyId = request.CookFamilyId?.Trim() ?? "";
                var dish = request.Dish?.Trim() ?? "";
                var cookFor = (request.CookForFamilyIds ?? new System.Collections.Generic.List<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (string.IsNullOrWhiteSpace(cookFamilyId))
                {
                    dish = "";
                }

                var state = await FamilyMealStorage.UpdateAsync(current =>
                {
                    var assignment = current.GetOrCreateAssignment(slotId);
                    assignment.CookFamilyId = cookFamilyId;
                    assignment.Dish = dish;
                    assignment.CookForFamilyIds = cookFor;
                    assignment.UpdatedAt = DateTime.UtcNow;
                    return current;
                });

                _logger.LogInformation("Family meal assignment saved for {SlotId}", slotId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(state);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save family meal assignment.");
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
