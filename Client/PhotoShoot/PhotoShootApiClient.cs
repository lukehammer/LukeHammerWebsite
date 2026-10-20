using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlazorApp.Shared.PhotoShoot;

namespace BlazorApp.Client.PhotoShoot
{
    public class PhotoShootApiException : Exception
    {
        public PhotoShootApiException(HttpStatusCode? status, string message) : base(message)
        {
            Status = status;
        }

        public HttpStatusCode? Status { get; }
        public bool IsAuthProblem => Status == HttpStatusCode.Unauthorized || Status == HttpStatusCode.Forbidden;
        public bool IsNetworkProblem => Status == null;
    }

    /// <summary>Typed wrapper over the /api/photoshoot endpoints.</summary>
    public class PhotoShootApiClient
    {
        private readonly HttpClient _http;
        internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public PhotoShootApiClient(HttpClient http)
        {
            _http = http;
        }

        /// <summary>Only honoured by the API when its local dev bypass is enabled (never in Azure).</summary>
        public string DevRole { get; set; } = "";
        public string Password { get; set; } = "";

        public string ImageUrl(string shootId, PhotoReference reference) =>
            new Uri(_http.BaseAddress!, $"api/photoshoot/shoots/{shootId}/references/{reference.Id}/image?v={reference.UpdatedUtc.Ticks}").ToString();

        public string FinishedImageUrl(string shootId, FinishedPhoto photo, bool download = false) =>
            new Uri(_http.BaseAddress!, $"api/photoshoot/shoots/{shootId}/finished/{photo.Id}/image?v={photo.UploadedUtc.Ticks}{(download ? "&download=1" : "")}").ToString();

        public string FinishedZipUrl(string shootId, bool removed) =>
            new Uri(_http.BaseAddress!, $"api/photoshoot/shoots/{shootId}/finished.zip{(removed ? "?removed=1" : "")}").ToString();

        public Task<PhotoShootMe> GetMeAsync() => SendAsync<PhotoShootMe>(HttpMethod.Get, "api/photoshoot/me");

        public Task<PhotoShootIndex> ListShootsAsync() => SendAsync<PhotoShootIndex>(HttpMethod.Get, "api/photoshoot/shoots");

        public Task<LiveShootResponse> GetShootAsync(string shootId, string? sinceETag = null) =>
            SendAsync<LiveShootResponse>(HttpMethod.Get,
                $"api/photoshoot/shoots/{shootId}" + (string.IsNullOrEmpty(sinceETag) ? "" : "?since=" + Uri.EscapeDataString(sinceETag)));

        public Task<LiveShootResponse> CreateShootAsync(CreateShootRequest request) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, "api/photoshoot/shoots", request);

        public Task<LiveShootResponse> SeedAsync(string? shootId) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, "api/photoshoot/shoots/seed" + (string.IsNullOrEmpty(shootId) ? "" : "?shootId=" + shootId), new { });

        public Task<LiveShootResponse> UpdateShootAsync(string shootId, UpdateShootRequest request) =>
            SendAsync<LiveShootResponse>(HttpMethod.Put, $"api/photoshoot/shoots/{shootId}", request);

        public Task DeleteShootAsync(string shootId) =>
            SendAsync<string>(HttpMethod.Delete, $"api/photoshoot/shoots/{shootId}?confirm=true");

        public Task<LiveShootResponse> DuplicateShootAsync(string shootId, DuplicateShootRequest request) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/duplicate", request);

        public Task<string> ExportJsonAsync(string shootId) =>
            SendAsync<string>(HttpMethod.Get, $"api/photoshoot/shoots/{shootId}/export");

        public Task<ImportResult> ImportAsync(ImportRequest request) =>
            SendAsync<ImportResult>(HttpMethod.Post, "api/photoshoot/import", request);

        public Task<LiveShootResponse> CreateShotAsync(string shootId, PhotoShot shot) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/shots", shot);

        public Task<LiveShootResponse> UpdateShotAsync(string shootId, PhotoShot shot) =>
            SendAsync<LiveShootResponse>(HttpMethod.Put, $"api/photoshoot/shoots/{shootId}/shots/{shot.Id}", shot);

        public Task<LiveShootResponse> DeleteShotAsync(string shootId, string shotId) =>
            SendAsync<LiveShootResponse>(HttpMethod.Delete, $"api/photoshoot/shoots/{shootId}/shots/{shotId}");

        public Task<LiveShootResponse> SetStatusAsync(string shootId, string shotId, ShotStatus status) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/shots/{shotId}/status", new SetStatusRequest { Status = status });

        public Task<LiveShootResponse> SetFavoriteAsync(string shootId, string shotId, bool isFavorite) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/shots/{shotId}/favorite", new FavoriteRequest { IsFavorite = isFavorite });

        public Task<LiveShootResponse> MoveShotAsync(string shootId, string shotId, MoveShotRequest request) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/shots/{shotId}/move", request);

        public Task<LiveShootResponse> ApplySuggestedOrderAsync(string shootId) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/suggested-order", new { });

        public Task<LiveShootResponse> CreateSetupAsync(string shootId, PhotoSetup setup) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/setups", setup);

        public Task<LiveShootResponse> UpdateSetupAsync(string shootId, PhotoSetup setup) =>
            SendAsync<LiveShootResponse>(HttpMethod.Put, $"api/photoshoot/shoots/{shootId}/setups/{setup.Id}", setup);

        public Task<LiveShootResponse> DeleteSetupAsync(string shootId, string setupId, string? moveTo) =>
            SendAsync<LiveShootResponse>(HttpMethod.Delete, $"api/photoshoot/shoots/{shootId}/setups/{setupId}" + (string.IsNullOrEmpty(moveTo) ? "" : "?moveTo=" + moveTo));

        public Task<LiveShootResponse> MoveSetupAsync(string shootId, string setupId, int delta) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/setups/{setupId}/move", new MoveSetupRequest { Delta = delta });

        public Task<LiveShootResponse> SessionAsync(string shootId, SessionCommand command) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/session", command);

        public Task<LiveShootResponse> PackingAsync(string shootId, PackingCommand command) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/packing", command);

        public Task<LiveShootResponse> CreateReferenceAsync(string shootId, PhotoReference reference) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/references", reference);

        public Task<LiveShootResponse> UpdateReferenceAsync(string shootId, PhotoReference reference) =>
            SendAsync<LiveShootResponse>(HttpMethod.Put, $"api/photoshoot/shoots/{shootId}/references/{reference.Id}", reference);

        public Task<LiveShootResponse> DeleteReferenceAsync(string shootId, string referenceId) =>
            SendAsync<LiveShootResponse>(HttpMethod.Delete, $"api/photoshoot/shoots/{shootId}/references/{referenceId}");

        public Task<LiveShootResponse> DeleteReferenceImageAsync(string shootId, string referenceId) =>
            SendAsync<LiveShootResponse>(HttpMethod.Delete, $"api/photoshoot/shoots/{shootId}/references/{referenceId}/image");

        public async Task<LiveShootResponse> UploadReferenceImageAsync(string shootId, string referenceId, Stream image, string contentType)
        {
            using var content = new StreamContent(image);
            content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/references/{referenceId}/image") { Content = content };
            return await SendRequestAsync<LiveShootResponse>(request);
        }

        public Task<LiveShootResponse> UploadFinishedAsync(string shootId, string? shotId, Stream image, DateTime? capturedUtc) =>
            UploadFinishedCore(string.IsNullOrEmpty(shotId)
                ? $"api/photoshoot/shoots/{shootId}/finished"
                : $"api/photoshoot/shoots/{shootId}/shots/{shotId}/finished", image, capturedUtc);

        public Task<LiveShootResponse> ReplaceFinishedAsync(string shootId, string photoId, Stream image) =>
            UploadFinishedCore($"api/photoshoot/shoots/{shootId}/finished/{photoId}/image", image, null);

        public Task<LiveShootResponse> PlaceFinishedAsync(string shootId) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/finished/place", new { });

        public Task<LiveShootResponse> MoveFinishedAsync(string shootId, string photoId, FinishedMove move) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/finished/{photoId}/move", move);

        public Task<LiveShootResponse> HideFinishedAsync(string shootId, string photoId) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/finished/{photoId}/hide", new { });

        public Task<LiveShootResponse> RestoreFinishedAsync(string shootId, string photoId) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/finished/{photoId}/restore", new { });

        public Task<LiveShootResponse> HardDeleteFinishedAsync(string shootId, string photoId) =>
            SendAsync<LiveShootResponse>(HttpMethod.Delete, $"api/photoshoot/shoots/{shootId}/finished/{photoId}");

        public Task<LiveShootResponse> SetMarketsAsync(string shootId, string shotId, IEnumerable<ShowMarket> markets) =>
            SendAsync<LiveShootResponse>(HttpMethod.Post, $"api/photoshoot/shoots/{shootId}/shots/{shotId}/markets", new MarketUpdate { Markets = markets.ToList() });

        private async Task<LiveShootResponse> UploadFinishedCore(string url, Stream image, DateTime? capturedUtc)
        {
            using var content = new StreamContent(image);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            if (capturedUtc != null)
            {
                request.Headers.TryAddWithoutValidation("X-Photo-Captured", capturedUtc.Value.ToUniversalTime().ToString("o"));
            }

            return await SendRequestAsync<LiveShootResponse>(request);
        }

        private Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
        {
            var request = new HttpRequestMessage(method, url);
            if (body != null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
            }

            return SendRequestAsync<T>(request);
        }

        private async Task<T> SendRequestAsync<T>(HttpRequestMessage request)
        {
            if (!string.IsNullOrEmpty(DevRole))
            {
                request.Headers.TryAddWithoutValidation(PhotoShootAuthorization.DevRoleHeader, DevRole);
            }

            if (!string.IsNullOrEmpty(Password))
            {
                request.Headers.TryAddWithoutValidation(PhotoShootAuthorization.PasswordHeader, Password);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request);
            }
            catch (HttpRequestException)
            {
                throw new PhotoShootApiException(null, "Can't reach the server. Check your connection.");
            }
            catch (TaskCanceledException)
            {
                throw new PhotoShootApiException(null, "The server took too long to respond.");
            }
            finally
            {
                request.Dispose();
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var text = "";
                    try
                    {
                        text = await response.Content.ReadAsStringAsync();
                    }
                    catch
                    {
                        // ignore unreadable error bodies
                    }

                    if (text.TrimStart().StartsWith("<", StringComparison.Ordinal))
                    {
                        text = "";
                    }

                    throw new PhotoShootApiException(response.StatusCode, string.IsNullOrWhiteSpace(text)
                        ? $"Request failed ({(int)response.StatusCode})."
                        : text.Trim());
                }

                if (typeof(T) == typeof(string))
                {
                    return (T)(object)await response.Content.ReadAsStringAsync();
                }

                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
                return value ?? throw new PhotoShootApiException(response.StatusCode, "Empty response from server.");
            }
        }
    }
}
