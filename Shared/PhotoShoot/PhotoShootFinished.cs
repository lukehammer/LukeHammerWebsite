using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.PhotoShoot
{
    public class FinishedMove
    {
        public int Delta { get; set; }
        public bool MakeFinal { get; set; }
        public string ShotId { get; set; } = "";
        public bool MoveShot { get; set; }
    }

    public class MarketUpdate
    {
        public List<ShowMarket> Markets { get; set; } = new List<ShowMarket>();
    }

    /// <summary>Ranking, hiding, and inbox placement for finished photographs. Pure, no I/O.</summary>
    public static class PhotoShootFinished
    {
        public const int MaxPerShot = 30;
        public const int MaxPerShoot = 4000;

        public static IEnumerable<FinishedPhoto> Visible(PhotoShootDocument doc) =>
            (doc.FinishedPhotos ?? new List<FinishedPhoto>()).Where(photo => photo != null && !photo.IsHidden);

        public static IEnumerable<FinishedPhoto> OnShot(PhotoShootDocument doc, string shotId, bool includeHidden) =>
            (doc.FinishedPhotos ?? new List<FinishedPhoto>())
                .Where(photo => photo != null && photo.ShotId == shotId && (includeHidden || !photo.IsHidden))
                .OrderBy(photo => photo.IsHidden ? 1 : 0)
                .ThenBy(photo => photo.Rank <= 0 ? int.MaxValue : photo.Rank)
                .ThenBy(photo => photo.UploadedUtc);

        public static PhotoShootDocument ForCaller(PhotoShootDocument doc, PhotoShootAccess access)
        {
            if (PhotoShootAuthorization.CanSeeRemoved(access))
            {
                return doc;
            }

            return new PhotoShootDocument
            {
                SchemaVersion = doc.SchemaVersion,
                Id = doc.Id,
                Name = doc.Name,
                Description = doc.Description,
                ShootDate = doc.ShootDate,
                BreakEveryMinutes = doc.BreakEveryMinutes,
                BreakDurationMinutes = doc.BreakDurationMinutes,
                CreatedUtc = doc.CreatedUtc,
                UpdatedUtc = doc.UpdatedUtc,
                Revision = doc.Revision,
                PlanVersion = doc.PlanVersion,
                SeedCatalogId = doc.SeedCatalogId,
                SeedCatalogVersion = doc.SeedCatalogVersion,
                DeletedSeedKeys = doc.DeletedSeedKeys,
                Setups = doc.Setups,
                Shots = doc.Shots,
                References = doc.References,
                FinishedPhotos = (doc.FinishedPhotos ?? new List<FinishedPhoto>()).Where(photo => photo != null && !photo.IsHidden).ToList(),
                Packing = doc.Packing,
                Session = doc.Session
            };
        }

        public static EditResult<FinishedPhoto> Add(PhotoShootDocument doc, string? shotId, string imageId, string contentType, DateTime? capturedUtc, string uploadedBy, DateTime nowUtc)
        {
            doc.FinishedPhotos ??= new List<FinishedPhoto>();
            var target = shotId ?? "";
            if (target.Length > 0 && doc.Shots.All(shot => shot.Id != target))
            {
                return EditResult<FinishedPhoto>.Missing("Shot");
            }

            if (doc.FinishedPhotos.Count >= MaxPerShoot)
            {
                return EditResult<FinishedPhoto>.Invalid(new List<string> { "This shoot has as many finished photos as it can hold." });
            }

            if (target.Length > 0 && Visible(doc).Count(photo => photo.ShotId == target) >= MaxPerShot)
            {
                return EditResult<FinishedPhoto>.Invalid(new List<string> { "That pose has as many photos as it can hold." });
            }

            var photo = new FinishedPhoto
            {
                Id = PhotoShootIds.New(),
                ShotId = target,
                ImageId = imageId,
                ImageContentType = contentType ?? "",
                CapturedUtc = capturedUtc,
                UploadedBy = uploadedBy ?? "",
                UploadedUtc = nowUtc
            };
            doc.FinishedPhotos.Add(photo);
            if (target.Length > 0)
            {
                Renumber(doc, target);
                SyncDeliverable(doc, target);
            }

            return EditResult<FinishedPhoto>.Success(photo);
        }

        /// <summary>Lines inbox photos up by camera time onto poses that do not yet have a visible photo.</summary>
        public static int PlaceInbox(PhotoShootDocument doc)
        {
            var inbox = Visible(doc)
                .Where(photo => string.IsNullOrEmpty(photo.ShotId))
                .OrderBy(photo => photo.CapturedUtc ?? DateTime.MaxValue)
                .ThenBy(photo => photo.UploadedUtc)
                .ToList();
            var open = PhotoShootPlanner.OrderedShots(doc)
                .Where(shot => !Visible(doc).Any(photo => photo.ShotId == shot.Id))
                .ToList();
            var count = Math.Min(inbox.Count, open.Count);
            for (var i = 0; i < count; i++)
            {
                inbox[i].ShotId = open[i].Id;
                Renumber(doc, open[i].Id);
                SyncDeliverable(doc, open[i].Id);
            }

            return count;
        }

        public static EditResult<FinishedPhoto> Move(PhotoShootDocument doc, string photoId, FinishedMove move)
        {
            var photo = doc.FinishedPhotos?.FirstOrDefault(item => item.Id == photoId && !item.IsHidden);
            if (photo == null)
            {
                return EditResult<FinishedPhoto>.Missing("Photo");
            }

            if (move != null && move.MoveShot)
            {
                var target = move.ShotId ?? "";
                if (target.Length > 0 && doc.Shots.All(shot => shot.Id != target))
                {
                    return EditResult<FinishedPhoto>.Missing("Shot");
                }

                var previous = photo.ShotId;
                photo.ShotId = target;
                photo.Rank = int.MaxValue;
                if (previous.Length > 0)
                {
                    Renumber(doc, previous);
                    SyncDeliverable(doc, previous);
                }

                if (target.Length > 0)
                {
                    Renumber(doc, target);
                    SyncDeliverable(doc, target);
                }

                return EditResult<FinishedPhoto>.Success(photo);
            }

            if (string.IsNullOrEmpty(photo.ShotId))
            {
                return EditResult<FinishedPhoto>.Invalid(new List<string> { "Put the photo on a pose before ranking it." });
            }

            var ranked = Visible(doc).Where(item => item.ShotId == photo.ShotId).OrderBy(item => item.Rank).ToList();
            var index = ranked.FindIndex(item => item.Id == photo.Id);
            if (index < 0)
            {
                return EditResult<FinishedPhoto>.Missing("Photo");
            }

            var destination = move != null && move.MakeFinal ? 0 : index + (move?.Delta ?? 0);
            if (destination < 0)
            {
                destination = 0;
            }

            if (destination >= ranked.Count)
            {
                destination = ranked.Count - 1;
            }

            ranked.RemoveAt(index);
            ranked.Insert(destination, photo);
            for (var i = 0; i < ranked.Count; i++)
            {
                ranked[i].Rank = i + 1;
            }

            SyncDeliverable(doc, photo.ShotId);
            return EditResult<FinishedPhoto>.Success(photo);
        }

        public static EditResult<FinishedPhoto> Replace(PhotoShootDocument doc, string photoId, string imageId, string contentType, out string oldImageId)
        {
            oldImageId = "";
            var photo = doc.FinishedPhotos?.FirstOrDefault(item => item.Id == photoId && !item.IsHidden);
            if (photo == null)
            {
                return EditResult<FinishedPhoto>.Missing("Photo");
            }

            oldImageId = photo.ImageId;
            photo.ImageId = imageId;
            photo.ImageContentType = contentType ?? "";
            return EditResult<FinishedPhoto>.Success(photo);
        }

        public static EditResult<FinishedPhoto> Hide(PhotoShootDocument doc, string photoId, string hiddenBy, DateTime nowUtc)
        {
            var photo = doc.FinishedPhotos?.FirstOrDefault(item => item.Id == photoId && !item.IsHidden);
            if (photo == null)
            {
                return EditResult<FinishedPhoto>.Missing("Photo");
            }

            photo.HiddenBy = hiddenBy ?? "";
            photo.HiddenUtc = nowUtc;
            photo.Rank = 0;
            if (photo.ShotId.Length > 0)
            {
                Renumber(doc, photo.ShotId);
                SyncDeliverable(doc, photo.ShotId);
            }

            return EditResult<FinishedPhoto>.Success(photo);
        }

        public static EditResult<FinishedPhoto> Restore(PhotoShootDocument doc, string photoId)
        {
            var photo = doc.FinishedPhotos?.FirstOrDefault(item => item.Id == photoId && item.IsHidden);
            if (photo == null)
            {
                return EditResult<FinishedPhoto>.Missing("Photo");
            }

            photo.HiddenBy = "";
            photo.HiddenUtc = null;
            photo.Rank = int.MaxValue;
            if (photo.ShotId.Length > 0)
            {
                Renumber(doc, photo.ShotId);
                SyncDeliverable(doc, photo.ShotId);
            }

            return EditResult<FinishedPhoto>.Success(photo);
        }

        public static EditResult<FinishedPhoto> HardDelete(PhotoShootDocument doc, string photoId, out string imageId)
        {
            imageId = "";
            var photo = doc.FinishedPhotos?.FirstOrDefault(item => item.Id == photoId);
            if (photo == null)
            {
                return EditResult<FinishedPhoto>.Missing("Photo");
            }

            imageId = photo.ImageId;
            var shotId = photo.ShotId;
            var wasVisible = !photo.IsHidden;
            doc.FinishedPhotos!.Remove(photo);
            if (wasVisible && shotId.Length > 0)
            {
                Renumber(doc, shotId);
                SyncDeliverable(doc, shotId);
            }

            return EditResult<FinishedPhoto>.Success(photo);
        }

        public static EditResult<PhotoShot> SetMarkets(PhotoShootDocument doc, string shotId, IList<ShowMarket> markets)
        {
            var shot = doc.Shots.FirstOrDefault(item => item.Id == shotId);
            if (shot == null)
            {
                return EditResult<PhotoShot>.Missing("Shot");
            }

            var distinct = (markets ?? new List<ShowMarket>()).Distinct().Where(market => Enum.IsDefined(typeof(ShowMarket), market)).ToList();
            if (distinct.Count == 0 || distinct.Contains(ShowMarket.AnyShow))
            {
                distinct = new List<ShowMarket> { ShowMarket.AnyShow };
            }
            else
            {
                distinct.Remove(ShowMarket.AnyShow);
            }

            shot.Markets = distinct;
            shot.MarketsAssigned = true;
            return EditResult<PhotoShot>.Success(shot);
        }

        public static string ZipFolder(ShowMarket market) => PhotoShootLabels.Market(market);

        public static string ZipName(PhotoShootDocument doc, FinishedPhoto photo, PhotoShot? shot)
        {
            var group = "Inbox";
            var title = "Photo";
            if (shot != null)
            {
                var setup = PhotoShootPlanner.FindSetup(doc, shot.SetupId);
                group = setup == null ? "Unassigned" : PhotoShootDisplay.BlockTitle(new SetupGroup { Setup = setup, Shots = new List<PhotoShot> { shot } });
                title = shot.Title;
            }

            var rank = photo.Rank <= 0 ? 0 : photo.Rank;
            return rank.ToString("00") + " " + Safe(group) + " - " + Safe(title) + ".jpg";
        }

        private static string Safe(string value)
        {
            var text = (value ?? "").Trim();
            foreach (var bad in new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })
            {
                text = text.Replace(bad, ' ');
            }

            return text.Length == 0 ? "Photo" : text;
        }

        private static void Renumber(PhotoShootDocument doc, string shotId)
        {
            var ranked = Visible(doc).Where(photo => photo.ShotId == shotId).OrderBy(photo => photo.Rank <= 0 ? int.MaxValue : photo.Rank).ThenBy(photo => photo.UploadedUtc).ToList();
            for (var i = 0; i < ranked.Count; i++)
            {
                ranked[i].Rank = i + 1;
            }
        }

        private static void SyncDeliverable(PhotoShootDocument doc, string shotId)
        {
            var shot = doc.Shots.FirstOrDefault(item => item.Id == shotId);
            if (shot == null)
            {
                return;
            }

            var has = Visible(doc).Any(photo => photo.ShotId == shotId);
            if (has)
            {
                shot.DeliverableStatus = DeliverableStatus.Final;
                return;
            }

            if (shot.DeliverableStatus == DeliverableStatus.Final)
            {
                shot.DeliverableStatus = shot.Status == ShotStatus.Completed ? DeliverableStatus.Captured : DeliverableStatus.Planned;
            }
        }
    }
}
