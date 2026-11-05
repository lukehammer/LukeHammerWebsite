using System;
using System.Collections.Generic;
using System.Linq;
using BlazorApp.Shared.PhotoShoot;

namespace BlazorApp.Shared.PhotoReview
{
    /// <summary>Joins planner, catalog and participation into what one viewer may see. Pure, no I/O.</summary>
    public class PhotoReviewProjection
    {
        private readonly PhotoShootDocument _planner;
        private readonly ReviewCatalog _catalog;
        private readonly ParticipationDocument _people;
        private readonly ReviewActor _actor;
        private readonly ReviewViewer _viewer;
        private readonly List<PhotoShot> _ordered;
        private readonly Dictionary<string, int> _numbers;
        private readonly Dictionary<string, ShotReview> _rows;
        private readonly Dictionary<string, Visitor> _visitors;
        private readonly List<ReviewPhoto> _photos;

        public PhotoReviewProjection(PhotoShootDocument planner, ReviewCatalog catalog, ParticipationDocument people, ReviewActor actor, ReviewViewer viewer)
        {
            _planner = planner;
            _catalog = PhotoReviewIngest.Normalize(catalog ?? new ReviewCatalog());
            _people = PhotoReviewParticipation.Normalize(people ?? new ParticipationDocument());
            _actor = actor ?? new ReviewActor();
            _viewer = viewer ?? new ReviewViewer();
            _ordered = PhotoShootPlanner.OrderedShots(planner).ToList();
            _numbers = new Dictionary<string, int>();
            for (var i = 0; i < _ordered.Count; i++)
            {
                _numbers[_ordered[i].Id] = i + 1;
            }

            var planned = PlannedRefs(planner);
            _rows = new Dictionary<string, ShotReview>();
            foreach (var shot in planned)
            {
                var row = _catalog.Shots.FirstOrDefault(item => item.ShotId == shot.ShotId)
                    ?? new ShotReview { ShotId = shot.ShotId, ShotNumber = shot.ShotNumber, Category = PhotoReviewIngest.DefaultCategory(shot) };
                _rows[shot.ShotId] = row;
            }

            _visitors = _people.Visitors.ToDictionary(item => item.Id);
            _photos = _catalog.Photos
                .Where(photo => _actor.IsAdmin || photo.HiddenUtc == null)
                .Where(photo => photo.Versions.Any(version => version.Kind == ArtifactKind.Original))
                .ToList();
        }

        public static List<PlannedShotRef> PlannedRefs(PhotoShootDocument planner)
        {
            var ordered = PhotoShootPlanner.OrderedShots(planner);
            return ordered.Select((shot, index) => new PlannedShotRef
            {
                ShotId = shot.Id,
                ShotNumber = index + 1,
                Title = shot.Title,
                PlannerCategory = shot.Category.ToString()
            }).ToList();
        }

        public static ShotReviewStatus Effective(ShotReview row) => row.OwnerStatus ?? ShotReviewStatus.NeedsReview;

        /// <summary>Luke's verdict always wins; otherwise the (labeled) suggestion; otherwise Needs review.</summary>
        public static ShotReviewStatus Provisional(ShotReview row) => row.OwnerStatus ?? row.SuggestedStatus ?? ShotReviewStatus.NeedsReview;

        // ---------- summary ----------

        public ReviewSummaryResponse Summary()
        {
            var shots = _ordered.Select(ShotSummaryFor).ToList();
            var photos = _photos.Select(PhotoSummaryFor).ToList();
            var counts = new ReviewCounts
            {
                PlannedShots = shots.Count,
                ShotsWithPhotos = shots.Count(shot => shot.PhotoIds.Count > 0),
                ShotsWithoutMatch = shots.Count(shot => shot.PhotoIds.Count == 0),
                Photos = photos.Count,
                Ratings = _people.Votes.Count(vote => !vote.IsRemoved),
                Comments = _people.Comments.Count(comment => !comment.IsRemoved && comment.DeletedUtc == null),
                OpenSuggestions = _people.Suggestions.Count(item => !item.IsRemoved && item.DeletedUtc == null && item.State == SuggestionState.Open),
                People = _people.Visitors.Count(item => item.RemovedUtc == null),
                SecondShootItems = Queue().Count(item => !item.Excluded)
            };
            foreach (var status in PhotoReviewLabels.Statuses)
            {
                counts.ByStatus[status] = shots.Count(shot => shot.Status == status);
                counts.ByProvisionalStatus[status] = shots.Count(shot => shot.ProvisionalStatus == status);
            }

            counts.OwnerConfirmed = shots.Count(shot => shot.StatusConfirmed);

            foreach (var kind in PhotoReviewLabels.Kinds)
            {
                counts.VersionsByKind[kind] = _photos.Count(photo => photo.Versions.Any(version => version.Kind == kind && version.IsCurrent));
            }

            foreach (MatchEvidence evidence in Enum.GetValues(typeof(MatchEvidence)))
            {
                counts.PhotosByEvidence[evidence] = _photos.Count(photo => photo.Evidence == evidence);
            }

            return new ReviewSummaryResponse
            {
                ShootId = _planner.Id,
                ShootName = _planner.Name,
                Viewer = _viewer,
                Counts = counts,
                Categories = PhotoReviewLabels.Categories.Select(category => new CategoryView
                {
                    Category = category,
                    Label = PhotoReviewLabels.Category(category),
                    Slug = PhotoReviewLabels.CategorySlug(category),
                    ShotCount = shots.Count(shot => shot.Category == category),
                    PhotoCount = photos.Count(photo => photo.Category == category)
                }).ToList(),
                Shots = shots,
                Photos = photos,
                LastImportUtc = _catalog.Imports.Count == 0 ? (DateTime?)null : _catalog.Imports.Max(item => item.Utc)
            };
        }

        public ShotSummary ShotSummaryFor(PhotoShot shot)
        {
            var row = _rows[shot.Id];
            var photos = PhotosOn(shot.Id);
            var setup = PhotoShootPlanner.FindSetup(_planner, shot.SetupId);
            return new ShotSummary
            {
                ShotId = shot.Id,
                Number = _numbers[shot.Id],
                Title = shot.Title,
                Summary = shot.Summary,
                Category = row.Category,
                SetupName = setup?.Name ?? "",
                Priority = PhotoShootLabels.Priority(shot.Priority),
                MustHave = shot.Priority == ShotPriority.MustHave,
                Coverage = photos.Count > 0 ? ShotCoverage.Selected : row.Coverage,
                CoverageNotes = row.CoverageNotes,
                SuggestedStatus = row.SuggestedStatus,
                SuggestionReason = row.SuggestionReason,
                SuggestedBy = row.SuggestedBy,
                Status = Effective(row),
                StatusConfirmed = row.OwnerStatus != null,
                // A shot with no photo and no suggestion is provisionally not captured, the same rule the retake list uses.
                ProvisionalStatus = RetakeStatus(row, photos.Count) ?? Provisional(row),
                ProvisionalSource = row.OwnerStatus != null ? "owner" : row.SuggestedStatus != null ? "suggestion" : "",
                OwnerNote = row.OwnerNote,
                PhotoIds = photos.Select(photo => photo.Id).ToList(),
                CoverPhotoId = photos.FirstOrDefault()?.Id ?? "",
                CommentCount = VisibleComments(CommentTarget.Shot, shot.Id).Count(comment => !comment.Placeholder),
                OpenSuggestions = _people.Suggestions.Count(item => item.ShotId == shot.Id && !item.IsRemoved && item.DeletedUtc == null && item.State == SuggestionState.Open),
                HasWarnings = photos.Any(photo => photo.Warnings.Count > 0),
                BestEvidence = photos.Count == 0 ? (MatchEvidence?)null : photos.Select(photo => photo.Evidence).OrderBy(EvidenceRank).First()
            };
        }

        private static int EvidenceRank(MatchEvidence evidence) => evidence switch
        {
            MatchEvidence.SlateSupported => 0,
            MatchEvidence.Inferred => 1,
            MatchEvidence.Approximate => 2,
            _ => 3
        };

        /// <summary>Primary first, then proposed primary, then alternates by key.</summary>
        public List<ReviewPhoto> PhotosOn(string shotId) =>
            _photos.Where(photo => photo.ShotId == shotId)
                .OrderBy(photo => photo.Selection == SelectionRole.Primary ? 0 : photo.Selection == SelectionRole.ProposedPrimary ? 1 : 2)
                .ThenBy(photo => photo.PhotoKey, StringComparer.Ordinal)
                .ToList();

        public PhotoSummary PhotoSummaryFor(ReviewPhoto photo)
        {
            var display = DisplayVersion(photo);
            _people.Totals.TryGetValue(photo.Id, out var total);
            var mine = string.IsNullOrEmpty(_actor.VisitorId)
                ? null
                : _people.Votes.FirstOrDefault(vote => vote.PhotoId == photo.Id && vote.VisitorId == _actor.VisitorId && !vote.IsRemoved);
            return new PhotoSummary
            {
                Id = photo.Id,
                PhotoKey = photo.PhotoKey,
                ShotId = photo.ShotId,
                AlsoShotIds = photo.AlsoShotIds.ToList(),
                Category = photo.Category,
                Selection = photo.Selection,
                Evidence = photo.Evidence,
                HasWarnings = photo.Warnings.Count > 0,
                Thumb = display == null ? new ThumbView() : new ThumbView { VersionId = display.Id, Widths = display.ThumbWidths.ToList(), Width = display.Width, Height = display.Height },
                Kinds = photo.Versions.Where(version => version.IsCurrent).Select(version => version.Kind).Distinct().OrderBy(kind => kind).ToList(),
                Average = total?.Average ?? 0,
                RatingCount = total?.Count ?? 0,
                MyRating = mine?.Value,
                CommentCount = VisibleComments(CommentTarget.Photo, photo.Id).Count(comment => !comment.Placeholder),
                Hidden = photo.HiddenUtc != null
            };
        }

        /// <summary>The version used for browsing: the cropped one when it has a preview, otherwise the original, so every photo still shows in the grid.</summary>
        public static ArtifactVersion? DisplayVersion(ReviewPhoto photo) =>
            photo.Versions.FirstOrDefault(version => version.Kind == ArtifactKind.Cropped && version.IsCurrent && version.ThumbWidths.Count > 0)
            ?? photo.Versions.FirstOrDefault(version => version.Kind == ArtifactKind.Original && version.IsCurrent)
            ?? photo.Versions.FirstOrDefault(version => version.IsCurrent);

        public PhotoDetail PhotoDetailFor(ReviewPhoto photo)
        {
            var versions = photo.Versions
                .Where(version => version.IsCurrent || _actor.IsAdmin)
                .OrderBy(version => version.Kind)
                .ThenBy(version => version.SupersededUtc == null ? 0 : 1)
                .ThenByDescending(version => version.ImportedUtc)
                .Select(version => new VersionView
                {
                    Id = version.Id,
                    Kind = version.Kind,
                    ParentVersionId = version.ParentVersionId,
                    ParentKind = photo.Versions.FirstOrDefault(parent => parent.Id == version.ParentVersionId)?.Kind,
                    FileName = version.FileName,
                    DeliveredFileName = version.DeliveredFileName,
                    Width = version.Width,
                    Height = version.Height,
                    Bytes = version.Bytes,
                    ContentType = version.ContentType,
                    Provenance = version.Provenance,
                    ProcessingNotes = version.ProcessingNotes,
                    Tool = version.Tool,
                    CreatedUtc = version.CreatedUtc,
                    ImportedUtc = version.ImportedUtc,
                    Superseded = !version.IsCurrent,
                    ThumbWidths = version.ThumbWidths.ToList(),
                    Sha256 = version.Sha256,
                    Shared = version.ShareToken != ""
                })
                .ToList();
            return new PhotoDetail
            {
                Summary = PhotoSummaryFor(photo),
                EvidenceText = photo.EvidenceText,
                Reason = photo.Reason,
                Warnings = photo.Warnings.ToList(),
                NotesMarkdown = photo.NotesMarkdown,
                Source = photo.Source,
                Versions = versions,
                MissingKinds = PhotoReviewLabels.Kinds.Where(kind => !photo.Versions.Any(version => version.Kind == kind && version.IsCurrent)).ToList()
            };
        }

        // ---------- shot detail ----------

        public ShotDetailResponse? ShotDetail(string shotId)
        {
            var index = _ordered.FindIndex(shot => shot.Id == shotId);
            if (index < 0)
            {
                return null;
            }

            var shot = _ordered[index];
            var photos = PhotosOn(shot.Id);
            var related = _photos.Where(photo => photo.ShotId != shot.Id && photo.AlsoShotIds.Contains(shot.Id)).ToList();
            var comments = VisibleComments(CommentTarget.Shot, shot.Id);
            foreach (var photo in photos.Concat(related))
            {
                comments.AddRange(VisibleComments(CommentTarget.Photo, photo.Id));
            }

            return new ShotDetailResponse
            {
                Viewer = _viewer,
                Shot = ShotSummaryFor(shot),
                Brief = BriefFor(shot),
                References = ReferencesFor(shot.Id),
                Photos = photos.Select(PhotoDetailFor).ToList(),
                RelatedPhotos = related.Select(PhotoDetailFor).ToList(),
                Comments = comments,
                Suggestions = SuggestionViews(item => item.ShotId == shot.Id),
                PreviousShotId = index > 0 ? _ordered[index - 1].Id : "",
                NextShotId = index < _ordered.Count - 1 ? _ordered[index + 1].Id : ""
            };
        }

        public ShotBrief BriefFor(PhotoShot shot)
        {
            var setup = PhotoShootPlanner.FindSetup(_planner, shot.SetupId);
            return new ShotBrief
            {
                Summary = shot.Summary,
                Objective = shot.Objective,
                MarketingPurpose = shot.MarketingPurpose,
                FacialExpression = shot.FacialExpression,
                BodyPosition = shot.BodyPosition,
                HandPosition = shot.HandPosition,
                EyeDirection = shot.EyeDirection,
                PerformerInstructions = shot.PerformerInstructions,
                PhotographerInstructions = shot.PhotographerInstructions,
                Framing = shot.Framing,
                Orientation = PhotoShootLabels.Orientation(shot.Orientation),
                RecommendedCrop = shot.RecommendedCrop,
                NegativeSpace = PhotoShootLabels.NegativeSpace(shot.NegativeSpace),
                Background = string.IsNullOrWhiteSpace(shot.Background) ? setup?.Background ?? "" : shot.Background,
                Props = shot.Props.ToList(),
                Inspiration = shot.Inspiration,
                SetupName = setup?.Name ?? "",
                BlockName = setup?.BlockName ?? "",
                Outfit = setup?.Outfit ?? ""
            };
        }

        public List<ReferenceView> ReferencesFor(string shotId) =>
            (_planner.References ?? new List<PhotoReference>())
                .Where(reference => reference.ShotIds.Contains(shotId))
                .OrderBy(reference =>
                {
                    var role = reference.Roles?.FirstOrDefault(item => item.ShotId == shotId);
                    return role == null || role.Slot == ReferenceSlot.Unassigned ? 99 : (int)role.Slot;
                })
                .Select(reference => new ReferenceView
                {
                    Id = reference.Id,
                    Title = reference.Title,
                    Credit = reference.Credit,
                    WhatToImitate = reference.WhatToImitate,
                    WhatToChange = reference.WhatToChange,
                    Slot = PhotoShootLabels.Slot(reference.Roles?.FirstOrDefault(item => item.ShotId == shotId)?.Slot ?? ReferenceSlot.Unassigned),
                    Provenance = PhotoShootLabels.Provenance(reference.Provenance),
                    HasImage = reference.HasImage,
                    SourceUrl = SafeLink(reference.SourceUrl),
                    Version = reference.UpdatedUtc.Ticks
                })
                .ToList();

        private static string SafeLink(string? url) =>
            Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http") ? uri.ToString() : "";

        /// <summary>True when a reference image is linked to at least one planned shot, so the public may see it.</summary>
        public static bool IsPublicReference(PhotoShootDocument planner, string referenceId) =>
            (planner.References ?? new List<PhotoReference>()).Any(reference => reference.Id == referenceId && reference.HasImage && reference.ShotIds.Count > 0
                && reference.ShotIds.Any(id => planner.Shots.Any(shot => shot.Id == id)));

        // ---------- comments and suggestions ----------

        public string Tag(string visitorId) => string.IsNullOrEmpty(visitorId) || visitorId.Length < 4 ? "" : "#" + visitorId.Substring(0, 4);

        private string NameOf(string visitorId) => _visitors.TryGetValue(visitorId, out var visitor) ? visitor.DisplayName : "Someone";

        /// <summary>
        /// Visible comments for a target in thread order. A removed or withdrawn comment still appears as a
        /// placeholder while any visible reply sits beneath it, so other people's replies are never lost.
        /// </summary>
        public List<CommentView> VisibleComments(CommentTarget target, string targetId)
        {
            var all = _people.Comments.Where(comment => comment.Target == target && comment.TargetId == targetId).ToList();
            var visible = new HashSet<string>(all.Where(comment => !comment.IsRemoved && comment.DeletedUtc == null).Select(comment => comment.Id));
            var keep = new HashSet<string>(visible);
            foreach (var comment in all.Where(item => visible.Contains(item.Id)))
            {
                var parentId = comment.ParentId;
                var guard = 0;
                while (!string.IsNullOrEmpty(parentId) && guard++ < 50)
                {
                    keep.Add(parentId);
                    parentId = all.FirstOrDefault(item => item.Id == parentId)?.ParentId ?? "";
                }
            }

            return all
                .Where(comment => keep.Contains(comment.Id))
                .OrderBy(comment => comment.CreatedUtc)
                .Select(comment =>
                {
                    var hidden = !visible.Contains(comment.Id);
                    return new CommentView
                    {
                        Id = comment.Id,
                        Target = comment.Target,
                        TargetId = comment.TargetId,
                        ParentId = all.Any(item => item.Id == comment.ParentId) ? comment.ParentId : "",
                        AuthorName = hidden ? "" : NameOf(comment.VisitorId),
                        AuthorTag = hidden ? "" : Tag(comment.VisitorId),
                        AuthorId = _actor.IsAdmin ? comment.VisitorId : "",
                        ByOwner = !hidden && comment.ByOwner,
                        Text = hidden ? "" : comment.Text,
                        CreatedUtc = comment.CreatedUtc,
                        EditedUtc = hidden ? null : comment.UpdatedUtc,
                        Placeholder = hidden,
                        PlaceholderText = !hidden ? "" : comment.IsRemoved ? "This comment was removed." : "This comment was deleted by its author.",
                        Mine = !hidden && comment.VisitorId == _actor.VisitorId && !string.IsNullOrEmpty(_actor.VisitorId)
                    };
                })
                .ToList();
        }

        public List<SuggestionView> SuggestionViews(Func<PoseSuggestion, bool> filter) =>
            _people.Suggestions
                .Where(filter)
                .Select(SuggestionViewFor)
                .Where(view => view != null)
                .Select(view => view!)
                .OrderBy(view => view.State == SuggestionState.Open ? 0 : 1)
                .ThenByDescending(view => view.CreatedUtc)
                .ToList();

        public SuggestionView? SuggestionViewFor(PoseSuggestion item)
        {
            var hidden = item.IsRemoved || item.DeletedUtc != null;
            var comments = VisibleComments(CommentTarget.Suggestion, item.Id);
            if (hidden && comments.All(comment => comment.Placeholder))
            {
                return null;
            }

            var shot = string.IsNullOrEmpty(item.ShotId) ? null : _ordered.FirstOrDefault(entry => entry.Id == item.ShotId);
            return new SuggestionView
            {
                Id = item.Id,
                Kind = item.Kind,
                ShotId = item.ShotId,
                ShotNumber = shot == null ? 0 : _numbers[shot.Id],
                ShotTitle = shot?.Title ?? "",
                SuggestedStatus = hidden ? null : item.SuggestedStatus,
                Title = hidden ? "" : item.Title,
                Description = hidden ? (item.IsRemoved ? "This suggestion was removed." : "This suggestion was withdrawn by its author.") : item.Description,
                AuthorName = hidden ? "" : NameOf(item.VisitorId),
                AuthorTag = hidden ? "" : Tag(item.VisitorId),
                AuthorId = _actor.IsAdmin ? item.VisitorId : "",
                ByOwner = !hidden && item.ByOwner,
                Mine = !hidden && !string.IsNullOrEmpty(_actor.VisitorId) && item.VisitorId == _actor.VisitorId,
                State = item.State,
                StateNote = item.StateNote,
                CreatedUtc = item.CreatedUtc,
                EditedUtc = item.UpdatedUtc,
                Placeholder = hidden,
                Comments = comments
            };
        }

        // ---------- second shoot ----------

        public static QueuePriority DefaultPriority(PhotoShot shot) => shot.Priority switch
        {
            ShotPriority.MustHave => QueuePriority.High,
            ShotPriority.Bonus => QueuePriority.Low,
            _ => QueuePriority.Medium
        };

        /// <summary>
        /// The status that puts a shot on the retake list, or null when it does not need a retake. Shared by the queue
        /// and the grid so both count the same shots: no photos (not captured), or Unsuccessful and Partially successful.
        /// </summary>
        public static ShotReviewStatus? RetakeStatus(ShotReview row, int photoCount)
        {
            // Luke's verdict always wins. Without one, the labeled suggestion decides provisionally;
            // without either, a shot with no photo still needs a retake.
            var status = row.OwnerStatus ?? row.SuggestedStatus ?? (photoCount == 0 ? ShotReviewStatus.NotCaptured : (ShotReviewStatus?)null);
            return status is ShotReviewStatus.NotCaptured or ShotReviewStatus.Unsuccessful or ShotReviewStatus.PartiallySuccessful ? status : null;
        }

        /// <summary>
        /// Planned shots Luke marked Not captured, Unsuccessful or Partially successful; planned shots the
        /// selection pass found no match for (until Luke decides); and approved suggestions.
        /// </summary>
        public List<QueueItemView> Queue()
        {
            var items = new List<QueueItemView>();
            foreach (var shot in _ordered)
            {
                var row = _rows[shot.Id];
                var photos = PhotosOn(shot.Id);
                // Luke's verdict always wins. Without one, the labeled suggestion decides provisionally;
                // without either, a shot with no photo is still listed so nothing is forgotten.
                var confirmed = row.OwnerStatus != null;
                var status = RetakeStatus(row, photos.Count);
                if (status == null)
                {
                    continue;
                }

                var label = PhotoReviewLabels.Status(status.Value);
                string reason;
                if (confirmed)
                {
                    reason = label + " (Luke)";
                }
                else if (row.SuggestedStatus != null)
                {
                    reason = label + " (provisional: " + (row.SuggestedBy.Length > 0 ? row.SuggestedBy : "selection pass") + " suggestion)";
                }
                else
                {
                    reason = "No photo found yet (provisional, awaiting Luke's review)";
                }

                var key = "shot:" + shot.Id;
                var entry = _catalog.Queue.FirstOrDefault(item => item.Key == key);
                items.Add(new QueueItemView
                {
                    Key = key,
                    ShotId = shot.Id,
                    ShotNumber = _numbers[shot.Id],
                    Title = shot.Title,
                    Category = row.Category,
                    Reason = reason,
                    ReasonConfirmed = confirmed,
                    ReasonDetail = confirmed ? row.OwnerNote : row.SuggestionReason.Length > 0 ? row.SuggestionReason : row.CoverageNotes,
                    Status = status,
                    Priority = entry?.Priority ?? DefaultPriority(shot),
                    Notes = entry?.Notes ?? "",
                    Excluded = entry?.Excluded ?? false,
                    Brief = BriefFor(shot),
                    References = ReferencesFor(shot.Id),
                    Examples = photos.Concat(_photos.Where(photo => photo.AlsoShotIds.Contains(shot.Id))).Take(4).Select(PhotoSummaryFor).ToList()
                });
            }

            foreach (var suggestion in _people.Suggestions.Where(item => item.State == SuggestionState.Approved && !item.IsRemoved && item.DeletedUtc == null))
            {
                var key = "suggestion:" + suggestion.Id;
                var entry = _catalog.Queue.FirstOrDefault(item => item.Key == key);
                var shot = string.IsNullOrEmpty(suggestion.ShotId) ? null : _ordered.FirstOrDefault(item => item.Id == suggestion.ShotId);
                var view = SuggestionViewFor(suggestion);
                items.Add(new QueueItemView
                {
                    Key = key,
                    FromSuggestion = true,
                    ShotId = shot?.Id ?? "",
                    ShotNumber = shot == null ? 0 : _numbers[shot.Id],
                    Title = suggestion.Title.Length > 0 ? suggestion.Title : shot?.Title ?? "Suggested pose",
                    Category = shot == null ? (ReviewCategory?)null : _rows[shot.Id].Category,
                    Reason = "Approved " + PhotoReviewLabels.SuggestionKindLabel(suggestion.Kind).ToLowerInvariant(),
                    ReasonConfirmed = true,
                    Priority = entry?.Priority ?? QueuePriority.Medium,
                    Notes = entry?.Notes ?? "",
                    Excluded = entry?.Excluded ?? false,
                    Brief = shot == null ? new ShotBrief { Summary = suggestion.Description } : BriefFor(shot),
                    References = shot == null ? new List<ReferenceView>() : ReferencesFor(shot.Id),
                    Examples = shot == null ? new List<PhotoSummary>() : PhotosOn(shot.Id).Take(4).Select(PhotoSummaryFor).ToList(),
                    Suggestion = view
                });
            }

            return items
                .OrderBy(item => item.Excluded ? 1 : 0)
                .ThenBy(item => item.Priority)
                .ThenBy(item => item.ShotNumber == 0 ? int.MaxValue : item.ShotNumber)
                .ToList();
        }

        public SecondShootResponse SecondShoot(DateTime nowUtc) => new SecondShootResponse
        {
            Viewer = _viewer,
            Items = Queue().Where(item => _actor.IsAdmin || !item.Excluded).ToList(),
            PendingSuggestions = SuggestionViews(item => item.State == SuggestionState.Open && !item.IsRemoved && item.DeletedUtc == null),
            GeneratedUtc = nowUtc
        };

        // ---------- people (admin) ----------

        public PeopleResponse People() => new PeopleResponse
        {
            People = _people.Visitors.Select(PersonFor).OrderByDescending(person => person.LastActiveUtc).ToList(),
            Audit = _people.Audit.OrderByDescending(entry => entry.Utc).Take(200).ToList()
        };

        public PersonView PersonFor(Visitor visitor)
        {
            var votes = _people.Votes.Where(item => item.VisitorId == visitor.Id).ToList();
            var comments = _people.Comments.Where(item => item.VisitorId == visitor.Id).ToList();
            var suggestions = _people.Suggestions.Where(item => item.VisitorId == visitor.Id).ToList();
            var times = new List<DateTime> { visitor.CreatedUtc };
            times.AddRange(votes.Select(item => item.CreatedUtc));
            times.AddRange(comments.Select(item => item.CreatedUtc));
            times.AddRange(suggestions.Select(item => item.CreatedUtc));
            return new PersonView
            {
                VisitorId = visitor.Id,
                DisplayName = visitor.DisplayName,
                Tag = Tag(visitor.Id),
                FirstActiveUtc = times.Min(),
                LastActiveUtc = visitor.LastActiveUtc > times.Max() ? visitor.LastActiveUtc : times.Max(),
                Removed = visitor.RemovedUtc != null,
                RemovedUtc = visitor.RemovedUtc,
                Ratings = votes.Count(item => !item.IsRemoved),
                Comments = comments.Count(item => !item.IsRemoved && item.DeletedUtc == null),
                Suggestions = suggestions.Count(item => !item.IsRemoved && item.DeletedUtc == null),
                RemovedItems = votes.Count(item => item.IsRemoved) + comments.Count(item => item.IsRemoved) + suggestions.Count(item => item.IsRemoved)
            };
        }

        public PersonDetailResponse? PersonDetail(string visitorId)
        {
            if (!_visitors.TryGetValue(visitorId, out var visitor))
            {
                return null;
            }

            var keys = _catalog.Photos.ToDictionary(photo => photo.Id, photo => photo.PhotoKey);
            var detail = new PersonDetailResponse
            {
                Person = PersonFor(visitor),
                Votes = _people.Votes.Where(item => item.VisitorId == visitorId).Select(item => new PersonVoteView
                {
                    PhotoId = item.PhotoId,
                    PhotoKey = keys.TryGetValue(item.PhotoId, out var key) ? key : "",
                    Value = item.Value,
                    Removed = item.IsRemoved,
                    UpdatedUtc = item.UpdatedUtc ?? item.CreatedUtc
                }).ToList()
            };
            foreach (var comment in _people.Comments.Where(item => item.VisitorId == visitorId).OrderBy(item => item.CreatedUtc))
            {
                detail.Comments.Add(new CommentView
                {
                    Id = comment.Id,
                    Target = comment.Target,
                    TargetId = comment.TargetId,
                    ParentId = comment.ParentId,
                    AuthorName = visitor.DisplayName,
                    AuthorTag = Tag(visitor.Id),
                    AuthorId = visitor.Id,
                    ByOwner = comment.ByOwner,
                    Text = comment.Text,
                    CreatedUtc = comment.CreatedUtc,
                    EditedUtc = comment.UpdatedUtc
                });
                detail.CommentStates[comment.Id] = comment.IsRemoved ? "removed" : comment.DeletedUtc != null ? "deleted by author" : "";
            }

            foreach (var suggestion in _people.Suggestions.Where(item => item.VisitorId == visitorId).OrderBy(item => item.CreatedUtc))
            {
                var shot = _ordered.FirstOrDefault(item => item.Id == suggestion.ShotId);
                detail.Suggestions.Add(new SuggestionView
                {
                    Id = suggestion.Id,
                    Kind = suggestion.Kind,
                    ShotId = suggestion.ShotId,
                    ShotNumber = shot == null ? 0 : _numbers[shot.Id],
                    ShotTitle = shot?.Title ?? "",
                    SuggestedStatus = suggestion.SuggestedStatus,
                    Title = suggestion.Title,
                    Description = suggestion.Description,
                    AuthorName = visitor.DisplayName,
                    AuthorTag = Tag(visitor.Id),
                    AuthorId = visitor.Id,
                    State = suggestion.State,
                    StateNote = suggestion.StateNote,
                    CreatedUtc = suggestion.CreatedUtc,
                    EditedUtc = suggestion.UpdatedUtc
                });
                detail.SuggestionStates[suggestion.Id] = suggestion.IsRemoved ? "removed" : suggestion.DeletedUtc != null ? "withdrawn by author" : "";
            }

            return detail;
        }
    }

    /// <summary>Admin-only catalog edits. The importer owns photo metadata; these own Luke's decisions.</summary>
    public static class PhotoReviewAdminOps
    {
        public const int MaxNoteLength = 2000;

        public static ReviewResult SetShotStatus(ReviewCatalog catalog, IReadOnlyList<PlannedShotRef> planned, ReviewActor actor, string shotId, ShotReviewStatus? status, string? note, DateTime nowUtc)
        {
            if (actor == null || !actor.IsAdmin) return ReviewResult.Forbidden("Only Luke can set a shot's status.");
            if (planned.All(shot => shot.ShotId != shotId)) return ReviewResult.NotFound("Shot");
            if (status != null && !Enum.IsDefined(typeof(ShotReviewStatus), status.Value)) return ReviewResult.BadRequest("Unknown status.");
            var text = PhotoReviewParticipation.CleanText(note);
            if (text.Length > MaxNoteLength) return ReviewResult.BadRequest($"Notes can be up to {MaxNoteLength} characters.");
            PhotoReviewIngest.EnsureShots(catalog, planned);
            var row = catalog.Shots.First(item => item.ShotId == shotId);
            // Choosing "Needs review" explicitly clears the verdict.
            row.OwnerStatus = status == ShotReviewStatus.NeedsReview ? null : status;
            row.OwnerNote = text;
            row.OwnerStatusUtc = nowUtc;
            return ReviewResult.Done();
        }

        public static ReviewResult SetPhotoHidden(ReviewCatalog catalog, ReviewActor actor, string photoId, bool hidden, DateTime nowUtc)
        {
            if (actor == null || !actor.IsAdmin) return ReviewResult.Forbidden("Only Luke can hide photos.");
            var photo = catalog.Photos.FirstOrDefault(item => item.Id == photoId);
            if (photo == null) return ReviewResult.NotFound("Photo");
            if ((photo.HiddenUtc != null) == hidden) return ReviewResult.Done(changed: false);
            photo.HiddenUtc = hidden ? nowUtc : (DateTime?)null;
            photo.UpdatedUtc = nowUtc;
            return ReviewResult.Done();
        }

        /// <summary>Creates a share token for one version (when newToken is set) or revokes it (when null). An active link is kept as it is.</summary>
        public static ReviewResult SetVersionShare(ReviewCatalog catalog, ReviewActor actor, string photoId, string versionId, string? newToken, DateTime nowUtc)
        {
            // Anyone can share, but only what the public can already see: a visible, current version.
            var photo = catalog.Photos.FirstOrDefault(item => item.Id == photoId);
            if (photo == null || photo.HiddenUtc != null) return ReviewResult.NotFound("Photo");
            var version = photo.Versions.FirstOrDefault(item => item.Id == versionId);
            if (version == null || !version.IsCurrent) return ReviewResult.NotFound("Version");
            if (newToken == null)
            {
                if (version.ShareToken == "") return ReviewResult.Done(changed: false);
                version.ShareToken = "";
            }
            else
            {
                if (version.ShareToken != "") return ReviewResult.Done(changed: false);
                version.ShareToken = newToken;
            }
            photo.UpdatedUtc = nowUtc;
            return ReviewResult.Done();
        }

        public static ReviewResult UpdateQueue(ReviewCatalog catalog, ReviewActor actor, string key, QueueUpdateRequest request, Func<string, bool> keyExists, DateTime nowUtc)
        {
            if (actor == null || !actor.IsAdmin) return ReviewResult.Forbidden("Only Luke can edit the second-shoot plan.");
            if (!keyExists(key)) return ReviewResult.NotFound("Queue item");
            var notes = PhotoReviewParticipation.CleanText(request?.Notes);
            if (notes.Length > MaxNoteLength) return ReviewResult.BadRequest($"Notes can be up to {MaxNoteLength} characters.");
            if (request?.Priority != null && !Enum.IsDefined(typeof(QueuePriority), request.Priority.Value)) return ReviewResult.BadRequest("Unknown priority.");
            var entry = catalog.Queue.FirstOrDefault(item => item.Key == key);
            if (entry == null)
            {
                entry = new QueueEntry { Key = key };
                catalog.Queue.Add(entry);
            }

            entry.Priority = request?.Priority;
            entry.Notes = notes;
            entry.Excluded = request?.Excluded ?? false;
            entry.UpdatedUtc = nowUtc;
            return ReviewResult.Done();
        }
    }
}
