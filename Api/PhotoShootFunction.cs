#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using BlazorApp.Shared.PhotoShoot;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ApiIsolated
{
    /// <summary>
    /// Private photo shoot planner API. Callers need the master password, or a photoshoot-owner /
    /// photoshoot-photographer role. Editing requires owner.
    /// </summary>
    public class PhotoShootFunction
    {
        private const int MaxImageBytes = 6 * 1024 * 1024;
        private const int MaxFinishedBytes = 100 * 1024 * 1024;
        private const int MaxJsonBytes = 8 * 1024 * 1024;
        private readonly ILogger _logger;

        public PhotoShootFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<PhotoShootFunction>();
        }

        // ---------- identity ----------

        [Function("PhotoShootMe")]
        public async Task<HttpResponseData> Me(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoshoot/me")] HttpRequestData req)
        {
            var me = PhotoShootAuth.Resolve(req);
            if (me.Access == PhotoShootAccess.None)
            {
                return await Text(req, PhotoShootAuth.IsAuthenticated(req) ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized,
                    "Photo shoot access requires an invitation.");
            }

            return await Json(req, HttpStatusCode.OK, me);
        }

        // ---------- shoots ----------

        [Function("PhotoShootList")]
        public Task<HttpResponseData> ListShoots(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoshoot/shoots")] HttpRequestData req) =>
            Guarded(req, ownerOnly: false, async me =>
            {
                var shoots = await PhotoShootStorage.ListAsync();
                return await Json(req, HttpStatusCode.OK, new PhotoShootIndex
                {
                    Shoots = shoots,
                    DefaultShootId = shoots.FirstOrDefault(s => s.IsDefault)?.Id ?? ""
                });
            });

        [Function("PhotoShootCreate")]
        public Task<HttpResponseData> CreateShoot(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots")] HttpRequestData req) =>
            Guarded(req, ownerOnly: true, async me =>
            {
                var body = await ReadJson<CreateShootRequest>(req);
                if (body == null) return await Text(req, HttpStatusCode.BadRequest, "Invalid request body.");
                var errors = PhotoShootValidator.ValidateShoot(body.Name, 60, 10);
                if (errors.Count > 0) return await Text(req, HttpStatusCode.BadRequest, string.Join("\n", errors));

                var doc = PhotoShootEditor.CreateShoot(PhotoShootIds.New(), body.Name, body.Description, body.ShootDate, DateTime.UtcNow);
                var stored = await PhotoShootStorage.CreateAsync(doc);
                await EnsureDefaultAsync(doc.Id);
                return await ShootResponse(req, stored.Document, stored.ETag, "Photo shoot created.", doc.Id);
            });

        [Function("PhotoShootSeed")]
        public Task<HttpResponseData> Seed(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/seed")] HttpRequestData req) =>
            Guarded(req, ownerOnly: true, async me =>
            {
                var catalog = await LoadCatalogAsync();
                var catalogErrors = PhotoShootValidator.ValidateCatalog(catalog);
                if (catalogErrors.Count > 0)
                {
                    return await Text(req, HttpStatusCode.InternalServerError, "Seed catalog is invalid:\n" + string.Join("\n", catalogErrors.Take(20)));
                }

                var shootId = Query(req, "shootId");
                if (string.IsNullOrEmpty(shootId))
                {
                    var existing = (await PhotoShootStorage.ListAsync()).FirstOrDefault(s => s.IsDefault);
                    shootId = existing?.Id ?? "";
                }

                if (string.IsNullOrEmpty(shootId))
                {
                    var doc = PhotoShootSeeder.CreateFromCatalog(catalog, PhotoShootIds.New(), DateTime.UtcNow);
                    PhotoShootPlanUpgrade.Apply(doc, DateTime.UtcNow);
                    var stored = await PhotoShootStorage.CreateAsync(doc);
                    await EnsureDefaultAsync(doc.Id);
                    _logger.LogInformation("Seeded new photo shoot {ShootId} with {Shots} shots.", doc.Id, doc.Shots.Count);
                    return await ShootResponse(req, stored.Document, stored.ETag,
                        $"Created \"{doc.Name}\" with {doc.Setups.Count} setups and {doc.Shots.Count} shots.", doc.Id);
                }

                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                SeedResult? seed = null;
                var outcome = await PhotoShootStorage.UpdateAsync(shootId, doc =>
                {
                    seed = PhotoShootSeeder.Merge(doc, catalog, DateTime.UtcNow);
                    var plan = PhotoShootPlanUpgrade.Apply(doc, DateTime.UtcNow);
                    return seed.AddedSetups + seed.AddedShots > 0 || plan ? MutationOutcome.Saved() : MutationOutcome.NoChange();
                });
                if (!outcome.Ok) return await Failure(req, outcome);
                outcome.Message = $"Catalog check complete: added {seed?.AddedSetups ?? 0} setups and {seed?.AddedShots ?? 0} shots; " +
                                  $"{seed?.SkippedExisting ?? 0} already present, {seed?.SkippedDeleted ?? 0} previously deleted (not re-added).";
                return await ShootResponse(req, outcome);
            });

        [Function("PhotoShootGet")]
        public Task<HttpResponseData> GetShoot(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoshoot/shoots/{shootId}")] HttpRequestData req, string shootId) =>
            Guarded(req, ownerOnly: false, async me =>
            {
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");

                var since = Query(req, "since");
                if (!string.IsNullOrEmpty(since))
                {
                    var etag = await PhotoShootStorage.GetETagAsync(shootId);
                    if (etag == null) return await Text(req, HttpStatusCode.NotFound, "Photo shoot not found.");
                    if (etag == since)
                    {
                        return await Json(req, HttpStatusCode.OK, new LiveShootResponse { Changed = false, ETag = etag });
                    }
                }

                var stored = await PhotoShootStorage.LoadAsync(shootId);
                if (stored == null) return await Text(req, HttpStatusCode.NotFound, "Photo shoot not found.");
                if (PhotoShootPlanUpgrade.NeedsWork(stored.Document))
                {
                    var catalog = await LoadCatalogAsync();
                    var outcome = await PhotoShootStorage.UpdateAsync(shootId, doc =>
                    {
                        var seed = PhotoShootSeeder.Merge(doc, catalog, DateTime.UtcNow);
                        var plan = PhotoShootPlanUpgrade.Apply(doc, DateTime.UtcNow);
                        return seed.AddedSetups + seed.AddedShots > 0 || plan
                            ? MutationOutcome.Saved("Shooting plan updated.")
                            : MutationOutcome.NoChange();
                    });
                    if (outcome.Ok && outcome.Document != null)
                    {
                        stored = new StoredPhotoShoot { Document = outcome.Document, ETag = outcome.ETag };
                    }
                }

                return await ShootResponse(req, stored.Document, stored.ETag);
            });

        [Function("PhotoShootUpdate")]
        public Task<HttpResponseData> UpdateShoot(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "photoshoot/shoots/{shootId}")] HttpRequestData req, string shootId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<UpdateShootRequest>(request);
                return doc =>
                {
                    if (body == null) return MutationOutcome.Fail(400, "Invalid request body.");
                    var errors = PhotoShootValidator.ValidateShoot(body.Name, body.BreakEveryMinutes, body.BreakDurationMinutes);
                    if (errors.Count > 0) return MutationOutcome.Fail(400, errors);
                    doc.Name = body.Name.Trim();
                    doc.Description = body.Description ?? "";
                    doc.ShootDate = (body.ShootDate ?? "").Trim();
                    doc.BreakEveryMinutes = body.BreakEveryMinutes;
                    doc.BreakDurationMinutes = body.BreakDurationMinutes;
                    return MutationOutcome.Saved("Shoot details saved.");
                };
            }, after: async (me, request, outcome) =>
            {
                var body = await ReadJson<UpdateShootRequest>(request);
                if (body?.MakeDefault == true) await PhotoShootStorage.SaveSettingsAsync(new PhotoShootSettings { DefaultShootId = shootId });
            });

        [Function("PhotoShootDelete")]
        public Task<HttpResponseData> DeleteShoot(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "photoshoot/shoots/{shootId}")] HttpRequestData req, string shootId) =>
            Guarded(req, ownerOnly: true, async me =>
            {
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                if (!string.Equals(Query(req, "confirm"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    return await Text(req, HttpStatusCode.BadRequest, "Deleting a photo shoot requires confirm=true.");
                }

                var stored = await PhotoShootStorage.LoadAsync(shootId);
                if (stored == null) return await Text(req, HttpStatusCode.NotFound, "Photo shoot not found.");
                await PhotoShootStorage.DeleteAsync(shootId);
                foreach (var reference in stored.Document.References.Where(r => PhotoShootIds.IsValid(r.ImageId)))
                {
                    await PhotoShootStorage.Store.DeleteImageAsync(reference.ImageId);
                }

                var settings = await PhotoShootStorage.LoadSettingsAsync();
                if (settings.DefaultShootId == shootId) await PhotoShootStorage.SaveSettingsAsync(new PhotoShootSettings());
                _logger.LogInformation("Deleted photo shoot {ShootId}.", shootId);
                return await Text(req, HttpStatusCode.OK, "Deleted.");
            });

        [Function("PhotoShootDuplicate")]
        public Task<HttpResponseData> Duplicate(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/duplicate")] HttpRequestData req, string shootId) =>
            Guarded(req, ownerOnly: true, async me =>
            {
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var body = await ReadJson<DuplicateShootRequest>(req) ?? new DuplicateShootRequest();
                var stored = await PhotoShootStorage.LoadAsync(shootId);
                if (stored == null) return await Text(req, HttpStatusCode.NotFound, "Photo shoot not found.");

                var name = string.IsNullOrWhiteSpace(body.Name) ? stored.Document.Name + " (copy)" : body.Name;
                var clone = PhotoShootCloner.Clone(stored.Document, PhotoShootIds.New(), name, body.ResetProgress, DateTime.UtcNow);
                await CopyImagesAsync(clone);
                var created = await PhotoShootStorage.CreateAsync(clone.Document);
                return await ShootResponse(req, created.Document, created.ETag, $"Created \"{clone.Document.Name}\".", clone.Document.Id);
            });

        [Function("PhotoShootExport")]
        public Task<HttpResponseData> Export(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoshoot/shoots/{shootId}/export")] HttpRequestData req, string shootId) =>
            Guarded(req, ownerOnly: true, async me =>
            {
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var stored = await PhotoShootStorage.LoadAsync(shootId);
                if (stored == null) return await Text(req, HttpStatusCode.NotFound, "Photo shoot not found.");
                return await Json(req, HttpStatusCode.OK, new PhotoShootExport { ExportedUtc = DateTime.UtcNow, Shoot = stored.Document });
            });

        [Function("PhotoShootImport")]
        public Task<HttpResponseData> Import(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/import")] HttpRequestData req) =>
            Guarded(req, ownerOnly: true, async me =>
            {
                var body = await ReadJson<ImportRequest>(req);
                if (body?.Export == null) return await Text(req, HttpStatusCode.BadRequest, "The file is not a photo shoot export.");
                if (body.Export.Format != PhotoShootExport.FormatName)
                {
                    return await Text(req, HttpStatusCode.BadRequest, $"Unrecognized file format (expected \"{PhotoShootExport.FormatName}\").");
                }

                if (body.Export.FormatVersion > PhotoShootExport.CurrentFormatVersion)
                {
                    return await Text(req, HttpStatusCode.BadRequest, "This export was created by a newer version of the planner.");
                }

                var (errors, warnings) = PhotoShootValidator.ValidateDocument(body.Export.Shoot);
                if (errors.Count > 0) return await Text(req, HttpStatusCode.BadRequest, "Import rejected:\n" + string.Join("\n", errors));
                var source = body.Export.Shoot!;
                var now = DateTime.UtcNow;

                if (body.Mode == ImportMode.ReplaceExisting)
                {
                    if (!PhotoShootIds.IsValid(body.TargetShootId)) return await Text(req, HttpStatusCode.BadRequest, "Choose the shoot to replace.");
                    if (!body.ConfirmOverwrite)
                    {
                        return await Text(req, HttpStatusCode.Conflict, "Replacing an existing shoot requires explicit confirmation.");
                    }

                    var existing = await PhotoShootStorage.LoadAsync(body.TargetShootId);
                    if (existing == null) return await Text(req, HttpStatusCode.NotFound, "Target photo shoot not found.");
                    var clone = PhotoShootCloner.Clone(source, body.TargetShootId, source.Name, resetProgress: false, now);
                    warnings.AddRange(await CopyImagesAsync(clone));
                    var outcome = await PhotoShootStorage.UpdateAsync(body.TargetShootId, doc =>
                    {
                        var replacement = clone.Document;
                        replacement.CreatedUtc = doc.CreatedUtc;
                        replacement.Revision = doc.Revision;
                        CopyInto(replacement, doc);
                        return MutationOutcome.Saved();
                    });
                    if (!outcome.Ok) return await Failure(req, outcome);
                    return await Json(req, HttpStatusCode.OK, Summarize(outcome.Document!, warnings));
                }

                var name = string.IsNullOrWhiteSpace(source.Name) ? "Imported photo shoot" : source.Name.Trim();
                var existingNames = (await PhotoShootStorage.ListAsync()).Select(s => s.Name).ToList();
                if (existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    name += $" (imported {now:yyyy-MM-dd})";
                }

                var created = PhotoShootCloner.Clone(source, PhotoShootIds.New(), name, resetProgress: false, now);
                warnings.AddRange(await CopyImagesAsync(created));
                var saved = await PhotoShootStorage.CreateAsync(created.Document);
                await EnsureDefaultAsync(saved.Document.Id);
                return await Json(req, HttpStatusCode.OK, Summarize(saved.Document, warnings));
            });

        // ---------- shots ----------

        [Function("PhotoShootCreateShot")]
        public Task<HttpResponseData> CreateShot(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/shots")] HttpRequestData req, string shootId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<PhotoShot>(request);
                return doc =>
                {
                    if (body == null) return MutationOutcome.Fail(400, "Invalid shot.");
                    var result = PhotoShootEditor.CreateShot(doc, body, DateTime.UtcNow);
                    return result.Ok
                        ? new MutationOutcome { Message = "Shot created.", CreatedId = result.Value!.Id }
                        : MutationOutcome.Fail(400, result.Errors);
                };
            });

        [Function("PhotoShootUpdateShot")]
        public Task<HttpResponseData> UpdateShot(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "photoshoot/shoots/{shootId}/shots/{shotId}")] HttpRequestData req, string shootId, string shotId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<PhotoShot>(request);
                return doc =>
                {
                    if (body == null) return MutationOutcome.Fail(400, "Invalid shot.");
                    var result = PhotoShootEditor.UpdateShot(doc, shotId, body, DateTime.UtcNow);
                    return result.Ok ? MutationOutcome.Saved("Shot saved.")
                        : MutationOutcome.Fail(result.NotFound ? 404 : 400, result.Errors);
                };
            });

        [Function("PhotoShootDeleteShot")]
        public Task<HttpResponseData> DeleteShot(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "photoshoot/shoots/{shootId}/shots/{shotId}")] HttpRequestData req, string shootId, string shotId) =>
            Mutate(req, shootId, ownerOnly: true, (me, request) => Task.FromResult<Func<PhotoShootDocument, MutationOutcome>>(doc =>
                PhotoShootEditor.DeleteShot(doc, shotId) ? MutationOutcome.Saved("Shot deleted.") : MutationOutcome.Fail(404, "Shot not found.")));

        [Function("PhotoShootShotStatus")]
        public Task<HttpResponseData> SetStatus(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/shots/{shotId}/status")] HttpRequestData req, string shootId, string shotId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<SetStatusRequest>(request);
                return doc =>
                {
                    if (body == null || !Enum.IsDefined(typeof(ShotStatus), body.Status)) return MutationOutcome.Fail(400, "Invalid status.");
                    var shot = PhotoShootPlanner.FindShot(doc, shotId);
                    if (shot == null) return MutationOutcome.Fail(404, "Shot not found.");
                    return PhotoShootEditor.SetStatus(doc, shotId, body.Status, DateTime.UtcNow)
                        ? MutationOutcome.Saved($"Marked {PhotoShootLabels.Status(body.Status)}.")
                        : MutationOutcome.NoChange();
                };
            });

        [Function("PhotoShootShotFavorite")]
        public Task<HttpResponseData> SetFavorite(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/shots/{shotId}/favorite")] HttpRequestData req, string shootId, string shotId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<FavoriteRequest>(request);
                return doc =>
                {
                    var shot = PhotoShootPlanner.FindShot(doc, shotId);
                    if (shot == null || body == null) return MutationOutcome.Fail(404, "Shot not found.");
                    shot.IsFavorite = body.IsFavorite;
                    shot.UpdatedUtc = DateTime.UtcNow;
                    return MutationOutcome.Saved();
                };
            });

        [Function("PhotoShootMoveShot")]
        public Task<HttpResponseData> MoveShot(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/shots/{shotId}/move")] HttpRequestData req, string shootId, string shotId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<MoveShotRequest>(request);
                return doc =>
                {
                    if (body == null) return MutationOutcome.Fail(400, "Invalid move.");
                    if (PhotoShootPlanner.FindShot(doc, shotId) == null) return MutationOutcome.Fail(404, "Shot not found.");
                    var moved = body.TargetSetupId != null
                        ? PhotoShootPlanner.MoveShotToSetup(doc, shotId, body.TargetSetupId)
                        : PhotoShootPlanner.MoveShot(doc, shotId, body.Delta);
                    return moved ? MutationOutcome.Saved() : MutationOutcome.NoChange("Already there.");
                };
            });

        [Function("PhotoShootSuggestedOrder")]
        public Task<HttpResponseData> SuggestedOrder(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/suggested-order")] HttpRequestData req, string shootId) =>
            Mutate(req, shootId, ownerOnly: true, (me, request) => Task.FromResult<Func<PhotoShootDocument, MutationOutcome>>(doc =>
            {
                PhotoShootPlanner.ApplySuggestedOrder(doc);
                return MutationOutcome.Saved("Suggested order applied: must-haves first within each setup, similar backgrounds together.");
            }));

        // ---------- setups ----------

        [Function("PhotoShootCreateSetup")]
        public Task<HttpResponseData> CreateSetup(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/setups")] HttpRequestData req, string shootId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<PhotoSetup>(request);
                return doc =>
                {
                    if (body == null) return MutationOutcome.Fail(400, "Invalid setup.");
                    var result = PhotoShootEditor.CreateSetup(doc, body);
                    return result.Ok ? new MutationOutcome { Message = "Setup created.", CreatedId = result.Value!.Id } : MutationOutcome.Fail(400, result.Errors);
                };
            });

        [Function("PhotoShootUpdateSetup")]
        public Task<HttpResponseData> UpdateSetup(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "photoshoot/shoots/{shootId}/setups/{setupId}")] HttpRequestData req, string shootId, string setupId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<PhotoSetup>(request);
                return doc =>
                {
                    if (body == null) return MutationOutcome.Fail(400, "Invalid setup.");
                    var result = PhotoShootEditor.UpdateSetup(doc, setupId, body);
                    return result.Ok ? MutationOutcome.Saved("Setup saved.") : MutationOutcome.Fail(result.NotFound ? 404 : 400, result.Errors);
                };
            });

        [Function("PhotoShootDeleteSetup")]
        public Task<HttpResponseData> DeleteSetup(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "photoshoot/shoots/{shootId}/setups/{setupId}")] HttpRequestData req, string shootId, string setupId) =>
            Mutate(req, shootId, ownerOnly: true, (me, request) =>
            {
                var moveTo = Query(request, "moveTo");
                return Task.FromResult<Func<PhotoShootDocument, MutationOutcome>>(doc =>
                    PhotoShootEditor.DeleteSetup(doc, setupId, moveTo) ? MutationOutcome.Saved("Setup deleted.") : MutationOutcome.Fail(404, "Setup not found."));
            });

        [Function("PhotoShootMoveSetup")]
        public Task<HttpResponseData> MoveSetup(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/setups/{setupId}/move")] HttpRequestData req, string shootId, string setupId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<MoveSetupRequest>(request);
                return doc =>
                {
                    if (body == null) return MutationOutcome.Fail(400, "Invalid move.");
                    if (PhotoShootPlanner.FindSetup(doc, setupId) == null) return MutationOutcome.Fail(404, "Setup not found.");
                    return PhotoShootPlanner.MoveSetup(doc, setupId, body.Delta) ? MutationOutcome.Saved() : MutationOutcome.NoChange();
                };
            });

        // ---------- live session ----------

        [Function("PhotoShootSession")]
        public Task<HttpResponseData> Session(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/session")] HttpRequestData req, string shootId) =>
            Mutate(req, shootId, ownerOnly: false, async (me, request) =>
            {
                var body = await ReadJson<SessionCommand>(request);
                return doc =>
                {
                    if (body == null || !Enum.IsDefined(typeof(SessionAction), body.Action)) return MutationOutcome.Fail(400, "Invalid command.");
                    var result = PhotoShootSessionEngine.Apply(doc, body, me.Access, ActorName(me), DateTime.UtcNow);
                    return result.Ok ? MutationOutcome.Saved(result.Message) : MutationOutcome.Fail(result.ErrorStatus, result.Message);
                };
            });

        [Function("PhotoShootPacking")]
        public Task<HttpResponseData> UpdatePacking(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/packing")] HttpRequestData req, string shootId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<PackingCommand>(request);
                return doc =>
                {
                    var message = PhotoShootGear.ApplyCommand(doc, body);
                    return message.StartsWith("Invalid") || message.StartsWith("Name") || message.StartsWith("That item") || message.StartsWith("Only items")
                        ? MutationOutcome.Fail(400, message)
                        : MutationOutcome.Saved(message);
                };
            });

        // ---------- references ----------

        [Function("PhotoShootCreateReference")]
        public Task<HttpResponseData> CreateReference(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/references")] HttpRequestData req, string shootId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<PhotoReference>(request);
                return doc =>
                {
                    if (body == null) return MutationOutcome.Fail(400, "Invalid reference.");
                    var result = PhotoShootEditor.CreateReference(doc, body, DateTime.UtcNow);
                    return result.Ok ? new MutationOutcome { Message = "Reference added.", CreatedId = result.Value!.Id } : MutationOutcome.Fail(400, result.Errors);
                };
            });

        [Function("PhotoShootUpdateReference")]
        public Task<HttpResponseData> UpdateReference(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "photoshoot/shoots/{shootId}/references/{referenceId}")] HttpRequestData req, string shootId, string referenceId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<PhotoReference>(request);
                return doc =>
                {
                    if (body == null) return MutationOutcome.Fail(400, "Invalid reference.");
                    var result = PhotoShootEditor.UpdateReference(doc, referenceId, body, DateTime.UtcNow);
                    return result.Ok ? MutationOutcome.Saved("Reference saved.") : MutationOutcome.Fail(result.NotFound ? 404 : 400, result.Errors);
                };
            });

        [Function("PhotoShootDeleteReference")]
        public async Task<HttpResponseData> DeleteReference(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "photoshoot/shoots/{shootId}/references/{referenceId}")] HttpRequestData req, string shootId, string referenceId)
        {
            return await Mutate(req, shootId, ownerOnly: true, (me, request) => Task.FromResult<Func<PhotoShootDocument, MutationOutcome>>(doc =>
            {
                var imageId = PhotoShootEditor.DeleteReference(doc, referenceId);
                return imageId == null ? MutationOutcome.Fail(404, "Reference not found.") : new MutationOutcome { Message = "Reference deleted.", Extra = imageId };
            }), after: async (me, request, outcome) =>
            {
                if (PhotoShootIds.IsValid(outcome.Extra)) await PhotoShootStorage.Store.DeleteImageAsync(outcome.Extra);
            });
        }

        [Function("PhotoShootUploadReferenceImage")]
        public Task<HttpResponseData> UploadImage(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/references/{referenceId}/image")] HttpRequestData req, string shootId, string referenceId) =>
            Guarded(req, ownerOnly: true, async me =>
            {
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var bytes = await ReadBytes(req.Body, MaxImageBytes);
                if (bytes == null) return await Text(req, HttpStatusCode.RequestEntityTooLarge, $"Images must be {MaxImageBytes / (1024 * 1024)} MB or smaller.");
                var contentType = PhotoShootImageTypes.Detect(bytes);
                if (contentType == null) return await Text(req, HttpStatusCode.UnsupportedMediaType, "Only JPEG, PNG, GIF, or WebP images are allowed.");

                var imageId = PhotoShootIds.New();
                await PhotoShootStorage.Store.WriteImageAsync(imageId, bytes, contentType);
                var oldImage = "";
                var outcome = await PhotoShootStorage.UpdateAsync(shootId, doc =>
                {
                    var reference = doc.References.FirstOrDefault(r => r.Id == referenceId);
                    if (reference == null) return MutationOutcome.Fail(404, "Reference not found.");
                    oldImage = reference.ImageId;
                    reference.ImageId = imageId;
                    reference.ImageContentType = contentType;
                    reference.UpdatedUtc = DateTime.UtcNow;
                    return MutationOutcome.Saved("Image uploaded.");
                });

                if (!outcome.Ok)
                {
                    await PhotoShootStorage.Store.DeleteImageAsync(imageId);
                    return await Failure(req, outcome);
                }

                if (PhotoShootIds.IsValid(oldImage)) await PhotoShootStorage.Store.DeleteImageAsync(oldImage);
                return await ShootResponse(req, outcome);
            });

        [Function("PhotoShootDeleteReferenceImage")]
        public Task<HttpResponseData> DeleteImage(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "photoshoot/shoots/{shootId}/references/{referenceId}/image")] HttpRequestData req, string shootId, string referenceId) =>
            Mutate(req, shootId, ownerOnly: true, (me, request) => Task.FromResult<Func<PhotoShootDocument, MutationOutcome>>(doc =>
            {
                var reference = doc.References.FirstOrDefault(r => r.Id == referenceId);
                if (reference == null) return MutationOutcome.Fail(404, "Reference not found.");
                var imageId = reference.ImageId;
                reference.ImageId = "";
                reference.ImageContentType = "";
                return new MutationOutcome { Message = "Image removed.", Extra = imageId };
            }), after: async (me, request, outcome) =>
            {
                if (PhotoShootIds.IsValid(outcome.Extra)) await PhotoShootStorage.Store.DeleteImageAsync(outcome.Extra);
            });

        [Function("PhotoShootGetReferenceImage")]
        public Task<HttpResponseData> GetImage(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoshoot/shoots/{shootId}/references/{referenceId}/image")] HttpRequestData req, string shootId, string referenceId) =>
            Guarded(req, ownerOnly: false, async me =>
            {
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var stored = await PhotoShootStorage.LoadAsync(shootId);
                var reference = stored?.Document.References.FirstOrDefault(r => r.Id == referenceId);
                if (reference == null || !PhotoShootIds.IsValid(reference.ImageId)) return await Text(req, HttpStatusCode.NotFound, "Image not found.");

                var image = await PhotoShootStorage.Store.ReadImageAsync(reference.ImageId);
                if (image == null) return await Text(req, HttpStatusCode.NotFound, "Image not found.");

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", image.ContentType);
                response.Headers.Add("Cache-Control", "private, max-age=86400");
                response.Headers.Add("X-Content-Type-Options", "nosniff");
                await response.Body.WriteAsync(image.Bytes, 0, image.Bytes.Length);
                return response;
            });

        // ---------- helpers ----------

        private async Task<HttpResponseData> Guarded(HttpRequestData req, bool ownerOnly, Func<PhotoShootMe, Task<HttpResponseData>> handler)
        {
            var me = PhotoShootAuth.Resolve(req);
            if (me.Access == PhotoShootAccess.None)
            {
                return await Text(req, PhotoShootAuth.IsAuthenticated(req) ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized,
                    "Photo shoot access requires an invitation.");
            }

            if (ownerOnly && !PhotoShootAuthorization.CanEdit(me.Access))
            {
                return await Text(req, HttpStatusCode.Forbidden, "Only the shoot owner can do that.");
            }

            try
            {
                return await handler(me);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Photo shoot request failed: {Url}", req.Url);
                return await Text(req, HttpStatusCode.InternalServerError, "Something went wrong saving the photo shoot. Please try again.");
            }
        }

        /// <summary>Reads the request first (once), then runs the mutation inside the retry loop.</summary>
        private Task<HttpResponseData> Mutate(
            HttpRequestData req,
            string shootId,
            bool ownerOnly,
            Func<PhotoShootMe, HttpRequestData, Task<Func<PhotoShootDocument, MutationOutcome>>> prepare,
            Func<PhotoShootMe, HttpRequestData, MutationOutcome, Task>? after = null) =>
            Guarded(req, ownerOnly, async me =>
            {
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var mutate = await prepare(me, req);
                var outcome = await PhotoShootStorage.UpdateAsync(shootId, mutate);
                if (!outcome.Ok) return await Failure(req, outcome);
                if (after != null) await after(me, req, outcome);
                return await ShootResponse(req, outcome);
            });

        private static string ActorName(PhotoShootMe me) => me.Access switch
        {
            PhotoShootAccess.Photographer => "Photographer",
            PhotoShootAccess.Admin => "Admin",
            _ => "Director"
        };

        private async Task EnsureDefaultAsync(string shootId)
        {
            var settings = await PhotoShootStorage.LoadSettingsAsync();
            if (string.IsNullOrEmpty(settings.DefaultShootId) || await PhotoShootStorage.GetETagAsync(settings.DefaultShootId) == null)
            {
                await PhotoShootStorage.SaveSettingsAsync(new PhotoShootSettings { DefaultShootId = shootId });
            }
        }

        private async Task<System.Collections.Generic.List<string>> CopyImagesAsync(CloneResult clone)
        {
            var warnings = new System.Collections.Generic.List<string>();
            foreach (var copy in clone.ImageCopies)
            {
                if (!await PhotoShootStorage.Store.CopyImageAsync(copy.Key, copy.Value))
                {
                    var reference = clone.Document.References.FirstOrDefault(r => r.ImageId == copy.Value);
                    if (reference != null)
                    {
                        reference.ImageId = "";
                        reference.ImageContentType = "";
                        warnings.Add($"Uploaded image for \"{reference.Title}\" was not found and was not copied.");
                    }
                }
            }

            return warnings;
        }

        private static void CopyInto(PhotoShootDocument from, PhotoShootDocument to)
        {
            to.Name = from.Name;
            to.Description = from.Description;
            to.ShootDate = from.ShootDate;
            to.BreakEveryMinutes = from.BreakEveryMinutes;
            to.BreakDurationMinutes = from.BreakDurationMinutes;
            to.SeedCatalogId = from.SeedCatalogId;
            to.SeedCatalogVersion = from.SeedCatalogVersion;
            to.DeletedSeedKeys = from.DeletedSeedKeys;
            to.Setups = from.Setups;
            to.Shots = from.Shots;
            to.References = from.References;
            to.FinishedPhotos = from.FinishedPhotos;
            to.Session = from.Session;
            foreach (var shot in to.Shots)
            {
                shot.ShootId = to.Id;
            }
        }

        private static ImportResult Summarize(PhotoShootDocument doc, System.Collections.Generic.List<string> warnings) => new ImportResult
        {
            ShootId = doc.Id,
            SetupCount = doc.Setups.Count,
            ShotCount = doc.Shots.Count,
            ReferenceCount = doc.References.Count,
            Warnings = warnings
        };

        private static PhotoShootCatalog? _catalog;

        private static async Task<PhotoShootCatalog> LoadCatalogAsync()
        {
            if (_catalog != null)
            {
                return _catalog;
            }

            var path = Path.Combine(AppContext.BaseDirectory, "data", "photoshoot-catalog.json");
            var json = await File.ReadAllTextAsync(path);
            _catalog = JsonSerializer.Deserialize<PhotoShootCatalog>(json, PhotoShootStorage.JsonOptions) ?? new PhotoShootCatalog();
            return _catalog;
        }

        private static string Query(HttpRequestData req, string name) =>
            HttpUtility.ParseQueryString(req.Url.Query).Get(name) ?? "";

        private const string CachedBodyKey = "photoshoot-body";

        private static async Task<T?> ReadJson<T>(HttpRequestData req) where T : class
        {
            var text = await ReadBodyText(req);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(text, PhotoShootStorage.JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static async Task<string?> ReadBodyText(HttpRequestData req)
        {
            if (req.FunctionContext.Items.TryGetValue(CachedBodyKey, out var cached))
            {
                return cached as string;
            }

            var bytes = await ReadBytes(req.Body, MaxJsonBytes);
            var text = bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
            req.FunctionContext.Items[CachedBodyKey] = text!;
            return text;
        }

        /// <summary>Reads at most <paramref name="max"/> bytes; returns null when the body is larger.</summary>
        private static async Task<byte[]?> ReadBytes(Stream body, int max)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > max)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        private static async Task<HttpResponseData> ShootResponse(HttpRequestData req, MutationOutcome outcome) =>
            await ShootResponse(req, outcome.Document!, outcome.ETag, outcome.Message, outcome.CreatedId);

        private static async Task<HttpResponseData> ShootResponse(HttpRequestData req, PhotoShootDocument doc, string etag, string message = "", string createdId = "") =>
            await Json(req, HttpStatusCode.OK, new LiveShootResponse
            {
                Changed = true,
                ETag = etag,
                Shoot = PhotoShootFinished.ForCaller(doc, PhotoShootAuth.Resolve(req).Access),
                Message = message ?? "",
                CreatedId = PhotoShootIds.IsValid(createdId) ? createdId : ""
            });

        private static async Task<HttpResponseData> Failure(HttpRequestData req, MutationOutcome outcome)
        {
            var status = outcome.ErrorStatus is >= 400 and < 600 ? (HttpStatusCode)outcome.ErrorStatus : HttpStatusCode.BadRequest;
            var message = outcome.Errors.Count > 0 ? string.Join("\n", outcome.Errors) : outcome.Message;
            return await Text(req, status, string.IsNullOrWhiteSpace(message) ? "Request failed." : message);
        }

        private static async Task<HttpResponseData> Json<T>(HttpRequestData req, HttpStatusCode status, T value)
        {
            var response = req.CreateResponse(status);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            response.Headers.Add("Cache-Control", "no-store");
            await response.WriteStringAsync(JsonSerializer.Serialize(value, PhotoShootStorage.JsonOptions));
            return response;
        }

        // ---------- finished photos ----------

        [Function("PhotoShootAddFinished")]
        public Task<HttpResponseData> AddFinished(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/finished")] HttpRequestData req, string shootId) =>
            SaveFinished(req, shootId, "");

        [Function("PhotoShootAddFinishedToShot")]
        public Task<HttpResponseData> AddFinishedToShot(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/shots/{shotId}/finished")] HttpRequestData req, string shootId, string shotId) =>
            SaveFinished(req, shootId, shotId);

        [Function("PhotoShootPlaceFinished")]
        public Task<HttpResponseData> PlaceFinished(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/finished/place")] HttpRequestData req, string shootId) =>
            MutatePhotos(req, shootId, (me, request) => Task.FromResult<Func<PhotoShootDocument, MutationOutcome>>(doc =>
            {
                var placed = PhotoShootFinished.PlaceInbox(doc);
                return MutationOutcome.Saved(placed == 0 ? "No photos were waiting to be placed." : $"Placed {placed} photo{(placed == 1 ? "" : "s")}.");
            }));

        [Function("PhotoShootMoveFinished")]
        public Task<HttpResponseData> MoveFinished(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/finished/{photoId}/move")] HttpRequestData req, string shootId, string photoId) =>
            MutatePhotos(req, shootId, async (me, request) =>
            {
                var body = await ReadJson<FinishedMove>(request);
                return doc =>
                {
                    var result = PhotoShootFinished.Move(doc, photoId, body ?? new FinishedMove());
                    return result.Ok ? MutationOutcome.Saved("Photo moved.") : MutationOutcome.Fail(result.NotFound ? 404 : 400, result.Errors);
                };
            });

        [Function("PhotoShootReplaceFinished")]
        public Task<HttpResponseData> ReplaceFinished(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/finished/{photoId}/image")] HttpRequestData req, string shootId, string photoId) =>
            Guarded(req, ownerOnly: false, async me =>
            {
                if (!PhotoShootAuthorization.CanUploadPhotos(me.Access)) return await Text(req, HttpStatusCode.Forbidden, "Only the shoot owner or photographer can do that.");
                if (!PhotoShootIds.IsValid(shootId) || !PhotoShootIds.IsValid(photoId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid id.");
                var prepared = await ReadFinishedImage(req);
                if (prepared.Error != null) return prepared.Error;
                var imageId = PhotoShootIds.New();
                await PhotoShootStorage.Store.WriteImageAsync(imageId, prepared.Bytes!, prepared.ContentType!);
                var oldImage = "";
                var outcome = await PhotoShootStorage.UpdateAsync(shootId, doc =>
                {
                    var result = PhotoShootFinished.Replace(doc, photoId, imageId, prepared.ContentType!, out oldImage);
                    return result.Ok ? MutationOutcome.Saved("Photo replaced.") : MutationOutcome.Fail(result.NotFound ? 404 : 400, result.Errors);
                });
                if (!outcome.Ok)
                {
                    await PhotoShootStorage.Store.DeleteImageAsync(imageId);
                    return await Failure(req, outcome);
                }

                if (PhotoShootIds.IsValid(oldImage)) await PhotoShootStorage.Store.DeleteImageAsync(oldImage);
                return await ShootResponse(req, outcome);
            });

        [Function("PhotoShootHideFinished")]
        public Task<HttpResponseData> HideFinished(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/finished/{photoId}/hide")] HttpRequestData req, string shootId, string photoId) =>
            MutatePhotos(req, shootId, (me, request) => Task.FromResult<Func<PhotoShootDocument, MutationOutcome>>(doc =>
            {
                var result = PhotoShootFinished.Hide(doc, photoId, ActorName(me), DateTime.UtcNow);
                return result.Ok ? MutationOutcome.Saved("Photo removed.") : MutationOutcome.Fail(result.NotFound ? 404 : 400, result.Errors);
            }));

        [Function("PhotoShootRestoreFinished")]
        public Task<HttpResponseData> RestoreFinished(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/finished/{photoId}/restore")] HttpRequestData req, string shootId, string photoId) =>
            Guarded(req, ownerOnly: false, async me =>
            {
                if (!PhotoShootAuthorization.CanSeeRemoved(me.Access)) return await Text(req, HttpStatusCode.Forbidden, "Only the shoot owner can do that.");
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var outcome = await PhotoShootStorage.UpdateAsync(shootId, doc =>
                {
                    var result = PhotoShootFinished.Restore(doc, photoId);
                    return result.Ok ? MutationOutcome.Saved("Photo restored.") : MutationOutcome.Fail(result.NotFound ? 404 : 400, result.Errors);
                });
                return outcome.Ok ? await ShootResponse(req, outcome) : await Failure(req, outcome);
            });

        [Function("PhotoShootHardDeleteFinished")]
        public Task<HttpResponseData> HardDeleteFinished(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "photoshoot/shoots/{shootId}/finished/{photoId}")] HttpRequestData req, string shootId, string photoId) =>
            Guarded(req, ownerOnly: false, async me =>
            {
                if (!PhotoShootAuthorization.CanHardDelete(me.Access)) return await Text(req, HttpStatusCode.Forbidden, "Only the admin login can permanently delete a photo.");
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var imageId = "";
                var outcome = await PhotoShootStorage.UpdateAsync(shootId, doc =>
                {
                    var result = PhotoShootFinished.HardDelete(doc, photoId, out imageId);
                    return result.Ok ? MutationOutcome.Saved("Photo deleted.") : MutationOutcome.Fail(result.NotFound ? 404 : 400, result.Errors);
                });
                if (!outcome.Ok) return await Failure(req, outcome);
                if (PhotoShootIds.IsValid(imageId)) await PhotoShootStorage.Store.DeleteImageAsync(imageId);
                return await ShootResponse(req, outcome);
            });

        [Function("PhotoShootSetMarkets")]
        public Task<HttpResponseData> SetMarkets(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photoshoot/shoots/{shootId}/shots/{shotId}/markets")] HttpRequestData req, string shootId, string shotId) =>
            Mutate(req, shootId, ownerOnly: true, async (me, request) =>
            {
                var body = await ReadJson<MarketUpdate>(request);
                return doc =>
                {
                    var result = PhotoShootFinished.SetMarkets(doc, shotId, body?.Markets ?? new System.Collections.Generic.List<ShowMarket>());
                    return result.Ok ? MutationOutcome.Saved("Shows saved.") : MutationOutcome.Fail(result.NotFound ? 404 : 400, result.Errors);
                };
            });

        [Function("PhotoShootGetFinishedImage")]
        public Task<HttpResponseData> GetFinishedImage(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoshoot/shoots/{shootId}/finished/{photoId}/image")] HttpRequestData req, string shootId, string photoId) =>
            Guarded(req, ownerOnly: false, async me =>
            {
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var stored = await PhotoShootStorage.LoadAsync(shootId);
                var photo = stored?.Document.FinishedPhotos.FirstOrDefault(item => item.Id == photoId);
                if (photo == null || !PhotoShootIds.IsValid(photo.ImageId) || (photo.IsHidden && !PhotoShootAuthorization.CanSeeRemoved(me.Access)))
                {
                    return await Text(req, HttpStatusCode.NotFound, "Image not found.");
                }

                var image = await PhotoShootStorage.Store.ReadImageAsync(photo.ImageId);
                if (image == null) return await Text(req, HttpStatusCode.NotFound, "Image not found.");
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", image.ContentType);
                response.Headers.Add("Cache-Control", "private, max-age=86400");
                response.Headers.Add("X-Content-Type-Options", "nosniff");
                var download = Query(req, "download");
                if (download == "1")
                {
                    response.Headers.Add("Content-Disposition", "attachment; filename=\"" + PhotoShootFinished.ZipName(stored!.Document, photo, stored.Document.Shots.FirstOrDefault(shot => shot.Id == photo.ShotId)) + "\"");
                }

                await response.Body.WriteAsync(image.Bytes, 0, image.Bytes.Length);
                return response;
            });

        [Function("PhotoShootDownloadFinishedZip")]
        public Task<HttpResponseData> DownloadFinishedZip(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photoshoot/shoots/{shootId}/finished.zip")] HttpRequestData req, string shootId) =>
            Guarded(req, ownerOnly: false, async me =>
            {
                var removed = Query(req, "removed") == "1";
                if (removed && !PhotoShootAuthorization.CanSeeRemoved(me.Access)) return await Text(req, HttpStatusCode.Forbidden, "Only the shoot owner can download removed photos.");
                if (!removed && !PhotoShootAuthorization.CanDownloadFinals(me.Access)) return await Text(req, HttpStatusCode.Forbidden, "Only the shoot owner can download the photo set.");
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var stored = await PhotoShootStorage.LoadAsync(shootId);
                if (stored == null) return await Text(req, HttpStatusCode.NotFound, "Photo shoot not found.");

                var photos = stored.Document.FinishedPhotos.Where(photo => photo.HasImage && photo.IsHidden == removed && photo.ShotId.Length > 0).ToList();
                using var buffer = new System.IO.MemoryStream();
                using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    var used = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var photo in photos)
                    {
                        var image = await PhotoShootStorage.Store.ReadImageAsync(photo.ImageId);
                        if (image == null) continue;
                        var shot = stored.Document.Shots.FirstOrDefault(item => item.Id == photo.ShotId);
                        var markets = shot?.Markets == null || shot.Markets.Count == 0 ? new System.Collections.Generic.List<ShowMarket> { ShowMarket.AnyShow } : shot.Markets;
                        foreach (var market in markets)
                        {
                            var name = PhotoShootFinished.ZipFolder(market) + "/" + PhotoShootFinished.ZipName(stored.Document, photo, shot);
                            var unique = name;
                            var n = 2;
                            while (!used.Add(unique))
                            {
                                unique = name.Substring(0, name.Length - 4) + "-" + n + ".jpg";
                                n++;
                            }

                            var entry = zip.CreateEntry(unique, System.IO.Compression.CompressionLevel.NoCompression);
                            using var entryStream = entry.Open();
                            await entryStream.WriteAsync(image.Bytes, 0, image.Bytes.Length);
                        }
                    }
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/zip");
                response.Headers.Add("Content-Disposition", "attachment; filename=\"" + (removed ? "removed-photos.zip" : "final-photos.zip") + "\"");
                response.Headers.Add("Cache-Control", "no-store");
                var payload = buffer.ToArray();
                await response.Body.WriteAsync(payload, 0, payload.Length);
                return response;
            });

        private Task<HttpResponseData> SaveFinished(HttpRequestData req, string shootId, string shotId) =>
            Guarded(req, ownerOnly: false, async me =>
            {
                if (!PhotoShootAuthorization.CanUploadPhotos(me.Access)) return await Text(req, HttpStatusCode.Forbidden, "Only the shoot owner or photographer can do that.");
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var prepared = await ReadFinishedImage(req);
                if (prepared.Error != null) return prepared.Error;
                var imageId = PhotoShootIds.New();
                await PhotoShootStorage.Store.WriteImageAsync(imageId, prepared.Bytes!, prepared.ContentType!);
                var outcome = await PhotoShootStorage.UpdateAsync(shootId, doc =>
                {
                    var result = PhotoShootFinished.Add(doc, shotId, imageId, prepared.ContentType!, prepared.CapturedUtc, ActorName(me), DateTime.UtcNow);
                    return result.Ok ? new MutationOutcome { Message = "Photo added.", CreatedId = result.Value!.Id } : MutationOutcome.Fail(result.NotFound ? 404 : 400, result.Errors);
                });
                if (!outcome.Ok)
                {
                    await PhotoShootStorage.Store.DeleteImageAsync(imageId);
                    return await Failure(req, outcome);
                }

                return await ShootResponse(req, outcome);
            });

        private Task<HttpResponseData> MutatePhotos(
            HttpRequestData req,
            string shootId,
            Func<PhotoShootMe, HttpRequestData, Task<Func<PhotoShootDocument, MutationOutcome>>> prepare) =>
            Guarded(req, ownerOnly: false, async me =>
            {
                if (!PhotoShootAuthorization.CanUploadPhotos(me.Access)) return await Text(req, HttpStatusCode.Forbidden, "Only the shoot owner or photographer can do that.");
                if (!PhotoShootIds.IsValid(shootId)) return await Text(req, HttpStatusCode.BadRequest, "Invalid shoot id.");
                var mutate = await prepare(me, req);
                var outcome = await PhotoShootStorage.UpdateAsync(shootId, mutate);
                return outcome.Ok ? await ShootResponse(req, outcome) : await Failure(req, outcome);
            });

        private async Task<(byte[]? Bytes, string? ContentType, DateTime? CapturedUtc, HttpResponseData? Error)> ReadFinishedImage(HttpRequestData req)
        {
            var bytes = await ReadBytes(req.Body, MaxFinishedBytes);
            if (bytes == null) return (null, null, null, await Text(req, HttpStatusCode.RequestEntityTooLarge, "Photos must be 100 MB or smaller."));
            var contentType = PhotoShootImageTypes.Detect(bytes);
            if (contentType == null) return (null, null, null, await Text(req, HttpStatusCode.UnsupportedMediaType, "Only a still photo can be uploaded. Video and raw camera files are not accepted."));
            if (contentType == "image/heic")
            {
                var jpeg = PhotoShootHeic.ToJpeg(bytes);
                if (jpeg == null) return (null, null, null, await Text(req, HttpStatusCode.UnsupportedMediaType, "This HEIC photo could not be converted. Export it as a JPEG from the camera and upload that."));
                return (jpeg, "image/jpeg", PhotoShootExif.CapturedUtc(bytes) ?? HeaderCaptured(req), null);
            }

            return (bytes, contentType, PhotoShootExif.CapturedUtc(bytes) ?? HeaderCaptured(req), null);
        }

        private static DateTime? HeaderCaptured(HttpRequestData req)
        {
            if (!req.Headers.TryGetValues("X-Photo-Captured", out var values)) return null;
            return DateTime.TryParse(values.FirstOrDefault(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : null;
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

    internal static class PhotoShootImageTypes
    {
        /// <summary>Detects the image type from magic bytes (the client-supplied content type is not trusted).</summary>
        public static string? Detect(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
            if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return "image/gif";
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return "image/webp";
            if (bytes.Length >= 12 && bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p')
            {
                var brand = System.Text.Encoding.ASCII.GetString(bytes, 8, 4);
                if (brand is "heic" or "heix" or "hevc" or "heim" or "heis" or "mif1" or "msf1") return "image/heic";
            }
            return null;
        }
    }

    internal sealed class PreparedPhoto
    {
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "";
        public DateTime? CapturedUtc { get; set; }
    }

    internal static class PhotoShootExif
    {
        public static DateTime? CapturedUtc(byte[] bytes)
        {
            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return null;
            var offset = 2;
            while (offset + 4 < bytes.Length && bytes[offset] == 0xFF)
            {
                var marker = bytes[offset + 1];
                if (marker == 0xDA || marker == 0xD9) break;
                var size = (bytes[offset + 2] << 8) + bytes[offset + 3];
                if (size < 2 || offset + 2 + size > bytes.Length) break;
                if (marker == 0xE1 && size > 14
                    && bytes[offset + 4] == (byte)'E' && bytes[offset + 5] == (byte)'x' && bytes[offset + 6] == (byte)'i' && bytes[offset + 7] == (byte)'f')
                {
                    return ReadTiffDate(bytes, offset + 10, offset + 2 + size);
                }

                offset += 2 + size;
            }

            return null;
        }

        private static DateTime? ReadTiffDate(byte[] bytes, int start, int end)
        {
            if (start + 8 > end) return null;
            var little = bytes[start] == (byte)'I';
            int U16(int at) => little ? bytes[at] | (bytes[at + 1] << 8) : (bytes[at] << 8) | bytes[at + 1];
            int U32(int at) => little
                ? bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24)
                : (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
            if (U16(start + 2) != 42) return null;
            var ifd = start + U32(start + 4);
            int? exifOffset = null;
            if (!Walk(bytes, start, end, ifd, U16, U32, (tag, value) => { if (tag == 0x8769) exifOffset = U32(value); })) return null;
            if (exifOffset == null) return null;
            DateTime? found = null;
            Walk(bytes, start, end, start + exifOffset.Value, U16, U32, (tag, value) =>
            {
                if (tag == 0x9003 && found == null)
                {
                    var text = System.Text.Encoding.ASCII.GetString(bytes, value, Math.Min(19, end - value));
                    if (DateTime.TryParseExact(text, "yyyy:MM:dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
                    {
                        found = parsed;
                    }
                }
            });
            return found;
        }

        private static bool Walk(byte[] bytes, int tiff, int end, int ifd, Func<int, int> u16, Func<int, int> u32, Action<int, int> visit)
        {
            if (ifd < tiff || ifd + 2 > end) return false;
            var count = u16(ifd);
            for (var i = 0; i < count; i++)
            {
                var entry = ifd + 2 + i * 12;
                if (entry + 12 > end) return false;
                var tag = u16(entry);
                var type = u16(entry + 2);
                var components = u32(entry + 4);
                var size = type == 2 ? components : 4;
                var valueAt = size <= 4 ? entry + 8 : tiff + u32(entry + 8);
                if (valueAt < tiff || valueAt >= end) continue;
                visit(tag, valueAt);
            }

            return true;
        }
    }

    internal static class PhotoShootHeic
    {
        /// <summary>
        /// The browser converts HEIC before upload when it can decode it. There is no safe server decoder
        /// in this host, so a raw HEIC that arrives here is refused instead of being stored unviewable.
        /// </summary>
        public static byte[]? ToJpeg(byte[] heic) => null;
    }
}
