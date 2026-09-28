using System.Net.Http;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared;

namespace BlazorApp.Client
{
    public static class KidThemeColorsApiClient
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        public static async Task<(KidThemeColorsData Data, string? Error)> LoadAsync(HttpClient http)
        {
            try
            {
                using var cts = new CancellationTokenSource(RequestTimeout);
                var response = await http.GetAsync("/api/sports/kid-themes", cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return (KidThemeColors.CreateDefaultData(), "Could not load kid colors. Make sure the API is running.");
                }

                var body = await response.Content.ReadAsStringAsync(cts.Token);
                if (!KidThemeColors.TryParseJson(body, out var data, out var parseError))
                {
                    return (KidThemeColors.CreateDefaultData(), parseError);
                }

                return (data, null);
            }
            catch (OperationCanceledException)
            {
                return (KidThemeColors.CreateDefaultData(), "Kid colors request timed out.");
            }
            catch (Exception)
            {
                return (KidThemeColors.CreateDefaultData(), "Could not load kid colors. Make sure the API is running.");
            }
        }

        public static async Task<(KidThemeColorsData? Data, string? Error)> SaveAsync(
            HttpClient http,
            string submittedBy,
            KidThemeColorsData colors)
        {
            try
            {
                var payload = new KidThemeColorsWriteRequest
                {
                    SubmittedBy = submittedBy,
                    Colors = colors
                };

                using var cts = new CancellationTokenSource(RequestTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Put, "/api/sports/kid-themes")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload, KidThemeColors.JsonOptions),
                        Encoding.UTF8,
                        "application/json")
                };

                var response = await http.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(cts.Token);
                    return (null, string.IsNullOrWhiteSpace(detail)
                        ? "Could not save kid colors."
                        : detail);
                }

                var body = await response.Content.ReadAsStringAsync(cts.Token);
                if (!KidThemeColors.TryParseJson(body, out var data, out var parseError))
                {
                    return (null, parseError);
                }

                return (data, null);
            }
            catch (Exception)
            {
                return (null, "Could not save kid colors. Make sure the API is running.");
            }
        }
    }
}
