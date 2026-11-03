#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using BlazorApp.Shared.PhotoReview;
using BlazorApp.Shared.PhotoShoot;
using LuckyLuke.PhotoReviewStorage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ApiIsolated
{
    /// <summary>
    /// Public photo review (/api/photoreview). Anyone may read. Visitors take part with a server-issued,
    /// cookie-held identity (display name only, no password). Admin actions need the bound Microsoft account.
    /// Every handler authorizes itself; there is no route-level rule.
    /// </summary>
    public class PhotoReviewFunction
    {
        public const string VisitorCookie = "llm_review";
        public const string CsrfHeader = "X-Photo-Review";
        private const int MaxJsonBytes = 64 * 1024;
        private const int CookieMaxAgeSeconds = 400 * 24 * 3600;
        private readonly ILogger _logger;

        public PhotoReviewFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<PhotoReviewFunction>();
        }

        // ---------- reads ----------

        [Function("PhotoReviewMe")]
        public Task<HttpResponseData> Me([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/me")] HttpRequestData req) =>
            Safe(req, async () =>
            {
                var read = await ReadAsync(req, allowBind: true);
                return read == null ? await NoShoot(req) : await Json(req, HttpStatusCode.OK, read.Viewer);
            });

        [Function("PhotoReviewSummary")]
        public Task<HttpResponseData> Summary([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/summary")] HttpRequestData req) =>
            Safe(req, async () =>
            {
                var read = await ReadAsync(req);
                return read == null ? await NoShoot(req) : await Json(req, HttpStatusCode.OK, read.Projection.Summary());
            });

        /// <summary>Public photo library page: server-rendered HTML with every accepted photo and its download links.</summary>
        [Function("PhotoLibraryPage")]
        public Task<HttpResponseData> LibraryPage([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/library")] HttpRequestData req) =>
            Safe(req, async () =>
            {
                var items = PhotoLibrary.Build(await LibrarySourcesAsync(), PublicApi);
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "text/html; charset=utf-8");
                response.Headers.Add("Cache-Control", "public, max-age=300");
                response.Headers.Add("X-Content-Type-Options", "nosniff");
                await response.WriteStringAsync(PhotoLibrary.Html(items, PublicApi + "/library"));
                return response;
            });

        /// <summary>The same photo library as JSON, for moving the files into another system.</summary>
        [Function("PhotoLibraryManifest")]
        public Task<HttpResponseData> LibraryManifest([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/library/manifest")] HttpRequestData req) =>
            Safe(req, async () =>
            {
                return await Json(req, HttpStatusCode.OK, PhotoLibrary.Build(await LibrarySourcesAsync(), PublicApi));
            });

        /// <summary>
        /// The public API base for library links and the canonical page. Built from the public site, not the request host:
        /// behind the static web app the Functions host itself does not serve these files to visitors.
        /// </summary>
        private const string PublicApi = "https://lukehammer.com/api/photoreview";

        /// <summary>Every shoot's review data for the public library. Read without a viewer, so only public photos come through.</summary>
        private static async Task<List<PhotoLibrary.Source>> LibrarySourcesAsync()
        {
            var sources = new List<PhotoLibrary.Source>();
            foreach (var shootId in await PhotoReviewData.AllShootIdsAsync())
            {
                var context = await PhotoReviewData.LoadContextAsync(shootId);
                if (context == null) continue;
                var (people, _) = await PhotoReviewData.LoadParticipationAsync(shootId);
                sources.Add(new PhotoLibrary.Source
                {
                    ShootId = shootId,
                    ShootDate = context.Planner.ShootDate,
                    CreatedUtc = context.Planner.CreatedUtc,
                    Projection = new PhotoReviewProjection(context.Planner, context.Catalog, people, new ReviewActor(), new ReviewViewer())
                });
            }

            return sources;
        }

        [Function("PhotoReviewShot")]
        public Task<HttpResponseData> Shot([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/shots/{shotId}")] HttpRequestData req, string shotId) =>
            Safe(req, async () =>
            {
                if (!PhotoReviewData.IsShotId(shotId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shot id.");
                var read = await ReadAsync(req);
                if (read == null) return await NoShoot(req);
                var detail = read.Projection.ShotDetail(shotId);
                return detail == null ? await Text(req, HttpStatusCode.NotFound, "Shot not found.") : await Json(req, HttpStatusCode.OK, detail);
            });

        [Function("PhotoReviewSuggestions")]
        public Task<HttpResponseData> Suggestions([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/suggestions")] HttpRequestData req) =>
            Safe(req, async () =>
            {
                var read = await ReadAsync(req);
                if (read == null) return await NoShoot(req);
                return await Json(req, HttpStatusCode.OK, new SuggestionsResponse { Viewer = read.Viewer, Suggestions = read.Projection.SuggestionViews(_ => true) });
            });

        [Function("PhotoReviewSecondShoot")]
        public Task<HttpResponseData> SecondShoot([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/second-shoot")] HttpRequestData req) =>
            Safe(req, async () =>
            {
                var read = await ReadAsync(req);
                return read == null ? await NoShoot(req) : await Json(req, HttpStatusCode.OK, read.Projection.SecondShoot(DateTime.UtcNow));
            });

        // ---------- assets ----------

        [Function("PhotoReviewThumb")]
        public Task<HttpResponseData> Thumb(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/photos/{photoId}/versions/{versionId}/thumb/{width:int}")] HttpRequestData req,
            string photoId, string versionId, int width) =>
            Safe(req, async () =>
            {
                var found = await FindVersionAsync(req, photoId, versionId);
                if (found.Error != null) return found.Error;
                if (!found.Version!.ThumbWidths.Contains(width)) return await Text(req, HttpStatusCode.NotFound, "No thumbnail at that size.");
                var stored = await PhotoReviewData.Store.ReadAsync(ReviewBlobNames.Thumb(found.Version.Sha256, width));
                if (stored == null) return await Text(req, HttpStatusCode.NotFound, "Thumbnail not found.");
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", stored.ContentType);
                // Version ids are content hashes, so the URL never changes meaning. Hidden photos are not cached publicly.
                response.Headers.Add("Cache-Control", found.Public ? "public, max-age=31536000, immutable" : "private, no-store");
                response.Headers.Add("X-Content-Type-Options", "nosniff");
                response.Headers.Add("X-Robots-Tag", "noindex");
                await response.Body.WriteAsync(stored.Bytes);
                return response;
            });

        /// <summary>Full-size file. Redirects to a short-lived single-blob read URL so large originals never pass through Functions.</summary>
        [Function("PhotoReviewFile")]
        public Task<HttpResponseData> File(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/photos/{photoId}/versions/{versionId}/file")] HttpRequestData req,
            string photoId, string versionId) =>
            Safe(req, async () =>
            {
                var found = await FindVersionAsync(req, photoId, versionId);
                if (found.Error != null) return found.Error;
                var version = found.Version!;
                var attachment = Query(req, "download") == "1";
                var name = ReviewBlobNames.Asset(version.Sha256);
                var url = PhotoReviewData.Store.ReadUrl(name, version.FileName, version.ContentType, attachment, TimeSpan.FromMinutes(15));
                if (url != null)
                {
                    var redirect = req.CreateResponse(HttpStatusCode.Redirect);
                    redirect.Headers.Add("Location", url);
                    redirect.Headers.Add("Cache-Control", "private, max-age=300");
                    redirect.Headers.Add("Referrer-Policy", "no-referrer");
                    return redirect;
                }

                var stored = await PhotoReviewData.Store.ReadAsync(name);
                if (stored == null) return await Text(req, HttpStatusCode.NotFound, "File not found.");
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", version.ContentType);
                response.Headers.Add("Content-Disposition", (attachment ? "attachment" : "inline") + "; filename=\"" + BlobReviewStore.SafeFileName(version.FileName) + "\"");
                response.Headers.Add("Cache-Control", "private, max-age=3600");
                response.Headers.Add("X-Content-Type-Options", "nosniff");
                await response.Body.WriteAsync(stored.Bytes);
                return response;
            });

        /// <summary>Creates (POST) or revokes (DELETE) the read-only share link for one version. Admin only. Returns the link path.</summary>
        [Function("PhotoReviewShareLink")]
        public Task<HttpResponseData> ShareLink(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "delete", Route = "photoreview/photos/{photoId}/versions/{versionId}/share")] HttpRequestData req,
            string photoId, string versionId) =>
            Write(req, async (shootId, context, admin) =>
            {
                var actor = new ReviewActor { IsAdmin = admin.Check == AdminCheck.Admin, AdminName = admin.Check == AdminCheck.Admin ? "Luke" : "" };
                if (!PhotoReviewData.IsPhotoId(photoId) || !PhotoReviewData.IsVersionId(versionId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid id.");
                var revoke = req.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
                var token = revoke ? null : NewShareToken();
                var (result, saved) = await PhotoReviewData.UpdateCatalogAsync(shootId, catalog => PhotoReviewAdminOps.SetVersionShare(catalog, actor, photoId, versionId, token, DateTime.UtcNow));
                if (!result.Ok) return await Done(req, result);
                var current = saved.Photos.First(item => item.Id == photoId).Versions.First(item => item.Id == versionId).ShareToken;
                return await Json(req, HttpStatusCode.OK, new ShareLinkResponse { Shared = current != "", Path = current == "" ? "" : $"share/{photoId}/{versionId}/{current}" });
            });

        /// <summary>Read-only share link for one version. Works only while that version is visible and the token matches. Redirects to a short-lived read URL.</summary>
        [Function("PhotoReviewShare")]
        public Task<HttpResponseData> Share(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/share/{photoId}/{versionId}/{token}")] HttpRequestData req,
            string photoId, string versionId, string token) =>
            Safe(req, async () =>
            {
                var found = await FindVersionAsync(req, photoId, versionId);
                if (found.Error != null) return found.Error;
                var version = found.Version!;
                if (!found.Public || version.ShareToken.Length == 0 || !TokenMatches(version.ShareToken, token))
                {
                    return await Text(req, HttpStatusCode.NotFound, "Not found.");
                }

                var name = ReviewBlobNames.Asset(version.Sha256);
                var url = PhotoReviewData.Store.ReadUrl(name, version.FileName, version.ContentType, false, TimeSpan.FromMinutes(15));
                if (url != null)
                {
                    var redirect = req.CreateResponse(HttpStatusCode.Redirect);
                    redirect.Headers.Add("Location", url);
                    redirect.Headers.Add("Cache-Control", "no-store");
                    redirect.Headers.Add("Referrer-Policy", "no-referrer");
                    redirect.Headers.Add("X-Robots-Tag", "noindex");
                    return redirect;
                }

                var stored = await PhotoReviewData.Store.ReadAsync(name);
                if (stored == null) return await Text(req, HttpStatusCode.NotFound, "File not found.");
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", version.ContentType);
                response.Headers.Add("Content-Disposition", "inline; filename=\"" + BlobReviewStore.SafeFileName(version.FileName) + "\"");
                response.Headers.Add("Cache-Control", "no-store");
                response.Headers.Add("X-Content-Type-Options", "nosniff");
                response.Headers.Add("X-Robots-Tag", "noindex");
                await response.Body.WriteAsync(stored.Bytes);
                return response;
            });

        /// <summary>Unguessable 192-bit token for a share link.</summary>
        private static string NewShareToken() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        private static bool TokenMatches(string expected, string given) =>
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(given));

        /// <summary>Planner reference images, for references linked to a planned shot only.</summary>
        [Function("PhotoReviewReferenceImage")]
        public Task<HttpResponseData> ReferenceImage(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/references/{referenceId}/image")] HttpRequestData req, string referenceId) =>
            Safe(req, async () =>
            {
                if (!PhotoShootIds.IsValid(referenceId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid reference id.");
                var shootId = await PhotoReviewData.DefaultShootIdAsync();
                var context = string.IsNullOrEmpty(shootId) ? null : await PhotoReviewData.LoadContextAsync(shootId);
                if (context == null || !PhotoReviewProjection.IsPublicReference(context.Planner, referenceId)) return await Text(req, HttpStatusCode.NotFound, "Reference not found.");
                var reference = context.Planner.References.First(item => item.Id == referenceId);
                var image = await PhotoShootStorage.Store.ReadImageAsync(reference.ImageId);
                if (image == null) return await Text(req, HttpStatusCode.NotFound, "Image not found.");
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", image.ContentType);
                response.Headers.Add("Cache-Control", "public, max-age=86400");
                response.Headers.Add("X-Content-Type-Options", "nosniff");
                response.Headers.Add("X-Robots-Tag", "noindex");
                await response.Body.WriteAsync(image.Bytes);
                return response;
            });

        // ---------- visitor identity ----------

        /// <summary>Creates this browser's identity (or renames it). The cookie holds id + secret; only the hash is stored.</summary>
        [Function("PhotoReviewVisitor")]
        public Task<HttpResponseData> Visitor([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoreview/visitor")] HttpRequestData req) =>
            Write(req, async (shootId, context, admin) =>
            {
                var body = await ReadJson<VisitorRequest>(req);
                if (body == null) return await Text(req, HttpStatusCode.BadRequest, "Enter a display name.");
                var token = VisitorToken(req);
                string? newCookie = null;
                var (result, doc) = await PhotoReviewData.UpdateParticipationAsync(shootId, people =>
                {
                    var existing = PhotoReviewParticipation.Authenticate(people, token);
                    if (existing != null)
                    {
                        newCookie = null;
                        return PhotoReviewParticipation.Rename(people, new ReviewActor { VisitorId = existing.Id }, body.DisplayName, DateTime.UtcNow);
                    }

                    var id = PhotoReviewParticipation.NewId(12);
                    var secret = PhotoReviewParticipation.NewId(32);
                    newCookie = id + "." + secret;
                    return PhotoReviewParticipation.Register(people, body.DisplayName, id, secret, DateTime.UtcNow);
                });
                if (!result.Ok) return await Fail(req, result);
                var visitor = PhotoReviewParticipation.Authenticate(doc, newCookie ?? token);
                var viewer = Viewer(doc, visitor, admin);
                var response = await Json(req, HttpStatusCode.OK, viewer);
                if (newCookie != null) SetVisitorCookie(req, response, newCookie);
                return response;
            });

        /// <summary>Forgets this browser's identity (the cookie only; contributions stay).</summary>
        [Function("PhotoReviewForget")]
        public Task<HttpResponseData> Forget([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoreview/visitor/forget")] HttpRequestData req) =>
            Write(req, async (shootId, context, admin) =>
            {
                var response = await Json(req, HttpStatusCode.OK, new ReviewViewer { IsAdmin = admin.Check == AdminCheck.Admin });
                SetVisitorCookie(req, response, "", expire: true);
                return response;
            });

        // ---------- ratings ----------

        [Function("PhotoReviewRate")]
        public Task<HttpResponseData> Rate([HttpTrigger(AuthorizationLevel.Anonymous, "put", "delete", Route = "photoreview/photos/{photoId}/rating")] HttpRequestData req, string photoId) =>
            Participate(req, async (context, admin) =>
            {
                if (!PhotoReviewData.IsPhotoId(photoId)) return Bad("Invalid photo id.");
                var clear = req.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
                var body = clear ? new RateRequest() : await ReadJson<RateRequest>(req);
                if (body == null) return Bad("Send a rating from 1 to 10.");
                var exists = PhotoExists(context, admin);
                return (people, actor) => clear
                    ? PhotoReviewParticipation.ClearRating(people, actor, photoId, DateTime.UtcNow)
                    : PhotoReviewParticipation.Rate(people, actor, photoId, body.Value, exists, DateTime.UtcNow);
            });

        // ---------- comments ----------

        [Function("PhotoReviewAddComment")]
        public Task<HttpResponseData> AddComment([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoreview/comments")] HttpRequestData req) =>
            Participate(req, async (context, admin) =>
            {
                var body = await ReadJson<CommentRequest>(req);
                if (body == null) return Bad("Write a comment first.");
                var photoExists = PhotoExists(context, admin);
                return (people, actor) => PhotoReviewParticipation.AddComment(people, actor, body.Target, body.TargetId ?? "", body.ParentId, body.Text, body.ClientRequestId,
                    (target, id) => target switch
                    {
                        CommentTarget.Photo => photoExists(id),
                        CommentTarget.Shot => context.Planner.Shots.Any(shot => shot.Id == id),
                        CommentTarget.Suggestion => people.Suggestions.Any(item => item.Id == id && !item.IsRemoved && item.DeletedUtc == null),
                        _ => false
                    },
                    DateTime.UtcNow);
            });

        [Function("PhotoReviewEditComment")]
        public Task<HttpResponseData> EditComment([HttpTrigger(AuthorizationLevel.Anonymous, "put", "delete", Route = "photoreview/comments/{commentId}")] HttpRequestData req, string commentId) =>
            Participate(req, async (context, admin) =>
            {
                if (!PhotoReviewParticipation.IsHexId(commentId, 24)) return Bad("Invalid comment id.");
                if (req.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    return (people, actor) => PhotoReviewParticipation.DeleteComment(people, actor, commentId, DateTime.UtcNow);
                }

                var body = await ReadJson<CommentRequest>(req);
                if (body == null) return Bad("Write a comment first.");
                return (people, actor) => PhotoReviewParticipation.EditComment(people, actor, commentId, body.Text, DateTime.UtcNow);
            });

        // ---------- suggestions ----------

        [Function("PhotoReviewAddSuggestion")]
        public Task<HttpResponseData> AddSuggestion([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoreview/suggestions")] HttpRequestData req) =>
            Participate(req, async (context, admin) =>
            {
                var body = await ReadJson<SuggestionRequest>(req);
                if (body == null) return Bad("Describe your suggestion.");
                return (people, actor) => PhotoReviewParticipation.AddSuggestion(people, actor, body.Kind, body.ShotId, body.SuggestedStatus, body.Title, body.Description,
                    body.ClientRequestId, id => context.Planner.Shots.Any(shot => shot.Id == id), DateTime.UtcNow);
            });

        [Function("PhotoReviewEditSuggestion")]
        public Task<HttpResponseData> EditSuggestion([HttpTrigger(AuthorizationLevel.Anonymous, "put", "delete", Route = "photoreview/suggestions/{suggestionId}")] HttpRequestData req, string suggestionId) =>
            Participate(req, async (context, admin) =>
            {
                if (!PhotoReviewParticipation.IsHexId(suggestionId, 24)) return Bad("Invalid suggestion id.");
                if (req.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    return (people, actor) => PhotoReviewParticipation.DeleteSuggestion(people, actor, suggestionId, DateTime.UtcNow);
                }

                var body = await ReadJson<SuggestionRequest>(req);
                if (body == null) return Bad("Describe your suggestion.");
                return (people, actor) => PhotoReviewParticipation.EditSuggestion(people, actor, suggestionId, body.SuggestedStatus, body.Title, body.Description,
                    id => context.Planner.Shots.Any(shot => shot.Id == id), DateTime.UtcNow);
            });

        // ---------- admin ----------

        [Function("PhotoReviewDecideSuggestion")]
        public Task<HttpResponseData> DecideSuggestion([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoreview/admin/suggestions/{suggestionId}/decision")] HttpRequestData req, string suggestionId) =>
            AdminWrite(req, async (shootId, context, actor) =>
            {
                if (!PhotoReviewParticipation.IsHexId(suggestionId, 24)) return await Text(req, HttpStatusCode.BadRequest, "Invalid suggestion id.");
                var body = await ReadJson<DecisionRequest>(req);
                if (body == null || !Enum.IsDefined(typeof(SuggestionState), body.State)) return await Text(req, HttpStatusCode.BadRequest, "Choose approve, decline or reopen.");
                var (result, _) = await PhotoReviewData.UpdateParticipationAsync(shootId, people => PhotoReviewParticipation.DecideSuggestion(people, actor, suggestionId, body.State, body.Note, DateTime.UtcNow));
                return await Done(req, result);
            });

        [Function("PhotoReviewShotStatus")]
        public Task<HttpResponseData> ShotStatus([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoreview/admin/shots/{shotId}/status")] HttpRequestData req, string shotId) =>
            AdminWrite(req, async (shootId, context, actor) =>
            {
                if (!PhotoReviewData.IsShotId(shotId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shot id.");
                var body = await ReadJson<ShotStatusRequest>(req);
                if (body == null) return await Text(req, HttpStatusCode.BadRequest, "Choose a status.");
                var planned = PhotoReviewProjection.PlannedRefs(context.Planner);
                var (result, _) = await PhotoReviewData.UpdateCatalogAsync(shootId, catalog => PhotoReviewAdminOps.SetShotStatus(catalog, planned, actor, shotId, body.Status, body.Note, DateTime.UtcNow));
                return await Done(req, result);
            });

        [Function("PhotoReviewPhotoHidden")]
        public Task<HttpResponseData> PhotoHidden([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoreview/admin/photos/{photoId}/hidden")] HttpRequestData req, string photoId) =>
            AdminWrite(req, async (shootId, context, actor) =>
            {
                if (!PhotoReviewData.IsPhotoId(photoId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid photo id.");
                var body = await ReadJson<PhotoHideRequest>(req);
                if (body == null) return await Text(req, HttpStatusCode.BadRequest, "Say whether to hide the photo.");
                var (result, _) = await PhotoReviewData.UpdateCatalogAsync(shootId, catalog => PhotoReviewAdminOps.SetPhotoHidden(catalog, actor, photoId, body.Hidden, DateTime.UtcNow));
                return await Done(req, result);
            });

        [Function("PhotoReviewQueue")]
        public Task<HttpResponseData> Queue([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoreview/admin/queue")] HttpRequestData req) =>
            AdminWrite(req, async (shootId, context, actor) =>
            {
                var key = Query(req, "key");
                if (key.Length == 0 || key.Length > 80) return await Text(req, HttpStatusCode.BadRequest, "Missing queue key.");
                var body = await ReadJson<QueueUpdateRequest>(req);
                if (body == null) return await Text(req, HttpStatusCode.BadRequest, "Nothing to update.");
                var (people, _) = await PhotoReviewData.LoadParticipationAsync(shootId);
                var keys = new PhotoReviewProjection(context.Planner, context.Catalog, people, actor, new ReviewViewer()).Queue().Select(item => item.Key).ToHashSet();
                var (result, _) = await PhotoReviewData.UpdateCatalogAsync(shootId, catalog => PhotoReviewAdminOps.UpdateQueue(catalog, actor, key, body, keys.Contains, DateTime.UtcNow));
                return await Done(req, result);
            });

        [Function("PhotoReviewPeople")]
        public Task<HttpResponseData> People([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/admin/people")] HttpRequestData req) =>
            AdminRead(req, async (read) => await Json(req, HttpStatusCode.OK, read.Projection.People()));

        [Function("PhotoReviewPerson")]
        public Task<HttpResponseData> Person([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoreview/admin/people/{visitorId}")] HttpRequestData req, string visitorId) =>
            AdminRead(req, async (read) =>
            {
                if (!PhotoReviewParticipation.IsHexId(visitorId, 24)) return await Text(req, HttpStatusCode.BadRequest, "Invalid person id.");
                var detail = read.Projection.PersonDetail(visitorId);
                return detail == null ? await Text(req, HttpStatusCode.NotFound, "Person not found.") : await Json(req, HttpStatusCode.OK, detail);
            });

        [Function("PhotoReviewRemovePerson")]
        public Task<HttpResponseData> RemovePerson([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoreview/admin/people/{visitorId}/{action}")] HttpRequestData req, string visitorId, string action) =>
            AdminWrite(req, async (shootId, context, actor) =>
            {
                if (!PhotoReviewParticipation.IsHexId(visitorId, 24)) return await Text(req, HttpStatusCode.BadRequest, "Invalid person id.");
                if (action != "remove" && action != "restore") return await Text(req, HttpStatusCode.NotFound, "Unknown action.");
                var body = await ReadJson<ModerationRequest>(req) ?? new ModerationRequest();
                var (result, _) = await PhotoReviewData.UpdateParticipationAsync(shootId, people =>
                {
                    if (action == "restore") return PhotoReviewParticipation.RestorePerson(people, actor, visitorId, body.Note, DateTime.UtcNow);
                    var visitor = people.Visitors.FirstOrDefault(item => item.Id == visitorId);
                    if (visitor != null && !string.Equals(PhotoReviewParticipation.CleanName(body.ConfirmName), visitor.DisplayName, StringComparison.OrdinalIgnoreCase))
                    {
                        return ReviewResult.BadRequest("Type the person's display name exactly to confirm.");
                    }

                    return PhotoReviewParticipation.RemovePerson(people, actor, visitorId, body.Note, DateTime.UtcNow);
                });
                return await Done(req, result);
            });

        // ---------- plumbing ----------

        private sealed class ReadModel
        {
            public string ShootId { get; init; } = "";
            public ReviewContext Context { get; init; } = new ReviewContext();
            public ParticipationDocument People { get; init; } = new ParticipationDocument();
            public ReviewActor Actor { get; init; } = new ReviewActor();
            public ReviewViewer Viewer { get; init; } = new ReviewViewer();
            public PhotoReviewProjection Projection { get; init; } = null!;
        }

        private async Task<ReadModel?> ReadAsync(HttpRequestData req, bool allowBind = false)
        {
            var shootId = await PhotoReviewData.DefaultShootIdAsync();
            if (!PhotoShootIds.IsValid(shootId)) return null;
            var context = await PhotoReviewData.LoadContextAsync(shootId);
            if (context == null) return null;
            var admin = await PhotoShootAuth.EvaluateAdminAsync(req, allowBind);
            var (people, _) = await PhotoReviewData.LoadParticipationAsync(shootId);
            var visitor = PhotoReviewParticipation.Authenticate(people, VisitorToken(req));
            var actor = new ReviewActor { VisitorId = visitor?.Id ?? "", IsAdmin = admin.Check == AdminCheck.Admin, AdminName = admin.Check == AdminCheck.Admin ? "Luke" : "" };
            var viewer = Viewer(people, visitor, admin);
            return new ReadModel
            {
                ShootId = shootId,
                Context = context,
                People = people,
                Actor = actor,
                Viewer = viewer,
                Projection = new PhotoReviewProjection(context.Planner, context.Catalog, people, actor, viewer)
            };
        }

        private static ReviewViewer Viewer(ParticipationDocument people, Visitor? visitor, (AdminCheck Check, SwaClientPrincipal? Principal, bool Bound) admin) => new ReviewViewer
        {
            HasIdentity = visitor != null,
            DisplayName = visitor?.DisplayName ?? "",
            Tag = visitor == null ? "" : "#" + visitor.Id.Substring(0, 4),
            Suspended = visitor?.RemovedUtc != null,
            IsAdmin = admin.Check == AdminCheck.Admin,
            AdminName = admin.Check == AdminCheck.Admin ? "Luke" : "",
            SignedInWithMicrosoft = admin.Principal?.IdentityProvider == PhotoReviewAdmin.MicrosoftProvider,
            AdminProblem = admin.Check == AdminCheck.SubjectMismatch
                ? "This Microsoft sign-in uses the admin email but is not the bound admin account."
                : admin.Principal != null && admin.Check != AdminCheck.Admin ? "Signed in, but this account is not the site admin." : ""
        };

        private static Func<string, bool> PhotoExists(ReviewContext context, (AdminCheck Check, SwaClientPrincipal? Principal, bool Bound) admin)
        {
            var isAdmin = admin.Check == AdminCheck.Admin;
            var ids = context.Catalog.Photos
                .Where(photo => (isAdmin || photo.HiddenUtc == null) && photo.Versions.Any(version => version.Kind == ArtifactKind.Original))
                .Select(photo => photo.Id)
                .ToHashSet();
            return ids.Contains;
        }

        private async Task<(ArtifactVersion? Version, bool Public, HttpResponseData? Error)> FindVersionAsync(HttpRequestData req, string photoId, string versionId)
        {
            if (!PhotoReviewData.IsPhotoId(photoId) || !PhotoReviewData.IsVersionId(versionId))
            {
                return (null, false, await Text(req, HttpStatusCode.BadRequest, "Invalid id."));
            }

            var shootId = await PhotoReviewData.DefaultShootIdAsync();
            var context = PhotoShootIds.IsValid(shootId) ? await PhotoReviewData.LoadContextAsync(shootId) : null;
            var photo = context?.Catalog.Photos.FirstOrDefault(item => item.Id == photoId);
            var version = photo?.Versions.FirstOrDefault(item => item.Id == versionId);
            if (photo == null || version == null)
            {
                return (null, false, await Text(req, HttpStatusCode.NotFound, "Not found."));
            }

            var isPublic = photo.HiddenUtc == null && version.IsCurrent;
            if (!isPublic && (await PhotoShootAuth.EvaluateAdminAsync(req, false)).Check != AdminCheck.Admin)
            {
                return (null, false, await Text(req, HttpStatusCode.NotFound, "Not found."));
            }

            return (version, isPublic, null);
        }

        private static Func<ParticipationDocument, ReviewActor, ReviewResult> Bad(string message) => (_, _) => ReviewResult.BadRequest(message);

        /// <summary>A visitor contribution: CSRF check, then the operation runs on the latest document with the verified actor.</summary>
        private Task<HttpResponseData> Participate(
            HttpRequestData req,
            Func<ReviewContext, (AdminCheck Check, SwaClientPrincipal? Principal, bool Bound), Task<Func<ParticipationDocument, ReviewActor, ReviewResult>>> prepare) =>
            Write(req, async (shootId, context, admin) =>
            {
                var operation = await prepare(context, admin);
                var token = VisitorToken(req);
                var isAdmin = admin.Check == AdminCheck.Admin;
                var (result, _) = await PhotoReviewData.UpdateParticipationAsync(shootId, people =>
                {
                    // Identity is re-verified against the stored hash on every attempt. Nothing in the body can choose it.
                    var visitor = PhotoReviewParticipation.Authenticate(people, token);
                    var actor = new ReviewActor { VisitorId = visitor?.Id ?? "", IsAdmin = isAdmin, AdminName = isAdmin ? "Luke" : "" };
                    return operation(people, actor);
                });
                return await Done(req, result);
            });

        private Task<HttpResponseData> AdminWrite(HttpRequestData req, Func<string, ReviewContext, ReviewActor, Task<HttpResponseData>> handler) =>
            Write(req, async (shootId, context, admin) =>
            {
                if (admin.Check != AdminCheck.Admin) return await Text(req, admin.Principal == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, "Only Luke can do that. Sign in with his Microsoft account.");
                return await handler(shootId, context, new ReviewActor { IsAdmin = true, AdminName = "Luke" });
            });

        private Task<HttpResponseData> AdminRead(HttpRequestData req, Func<ReadModel, Task<HttpResponseData>> handler) =>
            Safe(req, async () =>
            {
                var read = await ReadAsync(req);
                if (read == null) return await NoShoot(req);
                if (!read.Actor.IsAdmin) return await Text(req, read.Viewer.SignedInWithMicrosoft ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, "Only Luke can see this.");
                return await handler(read);
            });

        private Task<HttpResponseData> Write(HttpRequestData req, Func<string, ReviewContext, (AdminCheck Check, SwaClientPrincipal? Principal, bool Bound), Task<HttpResponseData>> handler) =>
            Safe(req, async () =>
            {
                if (!CsrfOk(req)) return await Text(req, HttpStatusCode.Forbidden, "Request blocked. Reload the page and try again.");
                var shootId = await PhotoReviewData.DefaultShootIdAsync();
                if (!PhotoShootIds.IsValid(shootId)) return await NoShoot(req);
                var context = await PhotoReviewData.LoadContextAsync(shootId);
                if (context == null) return await NoShoot(req);
                var admin = await PhotoShootAuth.EvaluateAdminAsync(req, false);
                return await handler(shootId, context, admin);
            });

        /// <summary>
        /// Writes need a custom header (forces a CORS preflight that this API never approves) and must not come
        /// from another site. The visitor cookie is also SameSite=Lax.
        /// </summary>
        internal static bool CsrfOk(HttpRequestData req)
        {
            if (PhotoShootAuth.Header(req, CsrfHeader) != "1") return false;
            var site = PhotoShootAuth.Header(req, "Sec-Fetch-Site");
            return !string.Equals(site, "cross-site", StringComparison.OrdinalIgnoreCase);
        }

        internal static string? VisitorToken(HttpRequestData req)
        {
            var header = PhotoShootAuth.Header(req, "Cookie");
            if (string.IsNullOrEmpty(header)) return null;
            foreach (var part in header.Split(';'))
            {
                var piece = part.Trim();
                if (piece.StartsWith(VisitorCookie + "=", StringComparison.Ordinal))
                {
                    return piece.Substring(VisitorCookie.Length + 1);
                }
            }

            return null;
        }

        private static void SetVisitorCookie(HttpRequestData req, HttpResponseData response, string value, bool expire = false)
        {
            // Secure is required in production; browsers also accept it on http://localhost.
            response.Headers.Add("Set-Cookie",
                $"{VisitorCookie}={value}; Path=/api/photoreview; Max-Age={(expire ? 0 : CookieMaxAgeSeconds)}; HttpOnly; Secure; SameSite=Lax");
        }

        private async Task<HttpResponseData> Safe(HttpRequestData req, Func<Task<HttpResponseData>> handler)
        {
            try
            {
                return await handler();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Photo review request failed: {Path}", req.Url.AbsolutePath);
                return await Text(req, HttpStatusCode.InternalServerError, "Something went wrong. Please try again.");
            }
        }

        private static async Task<HttpResponseData> Done(HttpRequestData req, ReviewResult result) =>
            result.Ok ? await Json(req, HttpStatusCode.OK, new { ok = true, changed = result.Changed, id = result.CreatedId }) : await Fail(req, result);

        private static Task<HttpResponseData> Fail(HttpRequestData req, ReviewResult result) =>
            Text(req, (HttpStatusCode)(result.Status is >= 400 and < 600 ? result.Status : 400), string.IsNullOrWhiteSpace(result.Error) ? "Request failed." : result.Error);

        private static Task<HttpResponseData> NoShoot(HttpRequestData req) => Text(req, HttpStatusCode.NotFound, "The photo review isn't set up yet.");

        private static string Query(HttpRequestData req, string name) => HttpUtility.ParseQueryString(req.Url.Query).Get(name) ?? "";

        private static async Task<T?> ReadJson<T>(HttpRequestData req) where T : class
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await req.Body.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + read > MaxJsonBytes) return null;
                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length == 0) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(buffer.ToArray(), PhotoShootStorage.JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static async Task<HttpResponseData> Json<T>(HttpRequestData req, HttpStatusCode status, T value)
        {
            var response = req.CreateResponse(status);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            response.Headers.Add("Cache-Control", "no-store");
            response.Headers.Add("X-Content-Type-Options", "nosniff");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, PhotoShootStorage.JsonOptions);
            var accepts = PhotoShootAuth.Header(req, "Accept-Encoding") ?? "";
            if (bytes.Length > 8192 && accepts.Contains("gzip", StringComparison.OrdinalIgnoreCase))
            {
                using var compressed = new MemoryStream();
                using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                {
                    gzip.Write(bytes, 0, bytes.Length);
                }

                response.Headers.Add("Content-Encoding", "gzip");
                response.Headers.Add("Vary", "Accept-Encoding");
                bytes = compressed.ToArray();
            }

            await response.Body.WriteAsync(bytes);
            return response;
        }

        private static async Task<HttpResponseData> Text(HttpRequestData req, HttpStatusCode status, string message)
        {
            var response = req.CreateResponse(status);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            response.Headers.Add("Cache-Control", "no-store");
            await response.WriteStringAsync(message);
            return response;
        }
    }
}
