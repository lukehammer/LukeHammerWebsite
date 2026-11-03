using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorApp.Shared.PhotoReview;

namespace BlazorApp.Client.PhotoReview
{
    public class PhotoReviewApiException : Exception
    {
        public PhotoReviewApiException(HttpStatusCode? status, string message) : base(message)
        {
            Status = status;
        }

        public HttpStatusCode? Status { get; }
    }

    public class MutationResult
    {
        public bool Ok { get; set; }
        public bool Changed { get; set; }
        public string Id { get; set; } = "";
    }

    /// <summary>Typed wrapper over /api/photoreview. Same origin, so the visitor cookie rides along automatically.</summary>
    public class PhotoReviewApi
    {
        private readonly HttpClient _http;
        internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        public PhotoReviewApi(HttpClient http)
        {
            _http = http;
        }

        private string Url(string path) => new Uri(_http.BaseAddress!, "api/photoreview/" + path).ToString();

        public string ThumbUrl(string photoId, string versionId, int width) => Url($"photos/{photoId}/versions/{versionId}/thumb/{width}");

        public string ThumbUrl(PhotoSummary photo, int preferred)
        {
            var widths = photo.Thumb.Widths;
            if (widths.Count == 0) return "";
            var width = widths.Where(w => w >= preferred).DefaultIfEmpty(widths.Max()).Min();
            return ThumbUrl(photo.Id, photo.Thumb.VersionId, width);
        }

        public string SrcSet(string photoId, string versionId, IEnumerable<int> widths) =>
            string.Join(", ", widths.OrderBy(w => w).Select(w => $"{ThumbUrl(photoId, versionId, w)} {w}w"));

        public string FileUrl(string photoId, string versionId, bool download) => Url($"photos/{photoId}/versions/{versionId}/file" + (download ? "?download=1" : ""));

        public string ReferenceImageUrl(ReferenceView reference) => Url($"references/{reference.Id}/image?v={reference.Version}");

        public string MicrosoftSignInUrl(string returnPath) => "/.auth/login/aad?post_login_redirect_uri=" + Uri.EscapeDataString(returnPath);

        public string SignOutUrl(string returnPath) => "/.auth/logout?post_logout_redirect_uri=" + Uri.EscapeDataString(returnPath);

        public Task<ReviewViewer> MeAsync() => SendAsync<ReviewViewer>(HttpMethod.Get, "me");
        public Task<ReviewSummaryResponse> SummaryAsync() => SendAsync<ReviewSummaryResponse>(HttpMethod.Get, "summary");
        public Task<ShotDetailResponse> ShotAsync(string shotId) => SendAsync<ShotDetailResponse>(HttpMethod.Get, "shots/" + Uri.EscapeDataString(shotId));
        public Task<SuggestionsResponse> SuggestionsAsync() => SendAsync<SuggestionsResponse>(HttpMethod.Get, "suggestions");
        public Task<SecondShootResponse> SecondShootAsync() => SendAsync<SecondShootResponse>(HttpMethod.Get, "second-shoot");

        public Task<ReviewViewer> SetNameAsync(string displayName) => SendAsync<ReviewViewer>(HttpMethod.Post, "visitor", new VisitorRequest { DisplayName = displayName });
        public Task<ReviewViewer> ForgetAsync() => SendAsync<ReviewViewer>(HttpMethod.Post, "visitor/forget", new { });

        public Task<MutationResult> RateAsync(string photoId, int value) => SendAsync<MutationResult>(HttpMethod.Put, $"photos/{photoId}/rating", new RateRequest { Value = value });
        public Task<MutationResult> ClearRatingAsync(string photoId) => SendAsync<MutationResult>(HttpMethod.Delete, $"photos/{photoId}/rating");

        public Task<ShareLinkResponse> CreateShareAsync(string photoId, string versionId) => SendAsync<ShareLinkResponse>(HttpMethod.Post, $"admin/photos/{photoId}/versions/{versionId}/share");
        public Task<ShareLinkResponse> RevokeShareAsync(string photoId, string versionId) => SendAsync<ShareLinkResponse>(HttpMethod.Delete, $"admin/photos/{photoId}/versions/{versionId}/share");

        /// <summary>Absolute URL for a share path returned by CreateShareAsync.</summary>
        public string ShareUrl(string path) => Url(path);

        public Task<MutationResult> AddCommentAsync(CommentRequest request) => SendAsync<MutationResult>(HttpMethod.Post, "comments", request);
        public Task<MutationResult> EditCommentAsync(string id, string text) => SendAsync<MutationResult>(HttpMethod.Put, "comments/" + id, new CommentRequest { Text = text });
        public Task<MutationResult> DeleteCommentAsync(string id) => SendAsync<MutationResult>(HttpMethod.Delete, "comments/" + id);

        public Task<MutationResult> AddSuggestionAsync(SuggestionRequest request) => SendAsync<MutationResult>(HttpMethod.Post, "suggestions", request);
        public Task<MutationResult> EditSuggestionAsync(string id, SuggestionRequest request) => SendAsync<MutationResult>(HttpMethod.Put, "suggestions/" + id, request);
        public Task<MutationResult> DeleteSuggestionAsync(string id) => SendAsync<MutationResult>(HttpMethod.Delete, "suggestions/" + id);

        public Task<MutationResult> DecideAsync(string id, SuggestionState state, string note) => SendAsync<MutationResult>(HttpMethod.Post, $"admin/suggestions/{id}/decision", new DecisionRequest { State = state, Note = note });
        public Task<MutationResult> SetStatusAsync(string shotId, ShotReviewStatus? status, string note) => SendAsync<MutationResult>(HttpMethod.Post, $"admin/shots/{shotId}/status", new ShotStatusRequest { Status = status, Note = note });
        public Task<MutationResult> SetHiddenAsync(string photoId, bool hidden) => SendAsync<MutationResult>(HttpMethod.Post, $"admin/photos/{photoId}/hidden", new PhotoHideRequest { Hidden = hidden });
        public Task<MutationResult> UpdateQueueAsync(string key, QueueUpdateRequest request) => SendAsync<MutationResult>(HttpMethod.Post, "admin/queue?key=" + Uri.EscapeDataString(key), request);
        public Task<PeopleResponse> PeopleAsync() => SendAsync<PeopleResponse>(HttpMethod.Get, "admin/people");
        public Task<PersonDetailResponse> PersonAsync(string visitorId) => SendAsync<PersonDetailResponse>(HttpMethod.Get, "admin/people/" + visitorId);
        public Task<MutationResult> RemovePersonAsync(string visitorId, string confirmName, string note) => SendAsync<MutationResult>(HttpMethod.Post, $"admin/people/{visitorId}/remove", new ModerationRequest { ConfirmName = confirmName, Note = note });
        public Task<MutationResult> RestorePersonAsync(string visitorId, string note) => SendAsync<MutationResult>(HttpMethod.Post, $"admin/people/{visitorId}/restore", new ModerationRequest { Note = note });

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
        {
            // One automatic retry when the server reports a save conflict under heavy use.
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, Url(path));
                if (method != HttpMethod.Get)
                {
                    request.Headers.TryAddWithoutValidation("X-Photo-Review", "1");
                    request.Content = JsonContent.Create(body ?? new { }, options: JsonOptions);
                }

                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request);
                }
                catch (HttpRequestException)
                {
                    throw new PhotoReviewApiException(null, "Can't reach the server. Check your connection.");
                }
                catch (TaskCanceledException)
                {
                    throw new PhotoReviewApiException(null, "The server took too long to respond.");
                }

                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.Conflict && attempt == 0 && method != HttpMethod.Get)
                    {
                        await Task.Delay(400);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var text = "";
                        try { text = await response.Content.ReadAsStringAsync(); } catch { }
                        if (text.TrimStart().StartsWith("<", StringComparison.Ordinal)) text = "";
                        throw new PhotoReviewApiException(response.StatusCode, string.IsNullOrWhiteSpace(text) ? $"Request failed ({(int)response.StatusCode})." : text.Trim());
                    }

                    return await response.Content.ReadFromJsonAsync<T>(JsonOptions) ?? throw new PhotoReviewApiException(response.StatusCode, "Empty response.");
                }
            }
        }
    }
}
