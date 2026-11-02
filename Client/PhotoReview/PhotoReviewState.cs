using BlazorApp.Shared.PhotoReview;

namespace BlazorApp.Client.PhotoReview
{
    /// <summary>Per-tab review state: the summary (all shots and photos), who I am, and a flash message.</summary>
    public class PhotoReviewState
    {
        private Task<ReviewSummaryResponse>? _loading;

        public PhotoReviewState(PhotoReviewApi api)
        {
            Api = api;
        }

        public PhotoReviewApi Api { get; }
        public ReviewSummaryResponse? Summary { get; private set; }
        public ReviewViewer Viewer { get; private set; } = new ReviewViewer();
        public string Error { get; private set; } = "";
        public string Flash { get; private set; } = "";
        public bool FlashIsError { get; private set; }
        public bool NamePromptOpen { get; private set; }
        public event Action? Changed;

        public bool IsAdmin => Viewer.IsAdmin;
        public bool CanTakePart => Viewer.HasIdentity && !Viewer.Suspended;

        public async Task<ReviewSummaryResponse?> EnsureSummaryAsync(bool force = false)
        {
            if (Summary != null && !force)
            {
                return Summary;
            }

            try
            {
                _loading = force || _loading == null || _loading.IsFaulted ? Api.SummaryAsync() : _loading;
                Summary = await _loading;
                Viewer = Summary.Viewer;
                Error = "";
            }
            catch (PhotoReviewApiException ex)
            {
                Error = ex.Message;
                _loading = null;
            }

            Changed?.Invoke();
            return Summary;
        }

        /// <summary>First call binds the Microsoft admin (if this is Luke's first sign-in) and refreshes the viewer.</summary>
        public async Task RefreshViewerAsync()
        {
            try
            {
                Viewer = await Api.MeAsync();
                Changed?.Invoke();
            }
            catch (PhotoReviewApiException)
            {
                // Not fatal: the summary also carries the viewer.
            }
        }

        public void UseViewer(ReviewViewer viewer)
        {
            Viewer = viewer;
            if (Summary != null) Summary.Viewer = viewer;
            Changed?.Invoke();
        }

        public async Task<bool> SetNameAsync(string name)
        {
            try
            {
                UseViewer(await Api.SetNameAsync(name));
                NamePromptOpen = false;
                ShowFlash($"Thanks, {Viewer.DisplayName}! You can rate, comment and suggest poses now.", false);
                return true;
            }
            catch (PhotoReviewApiException ex)
            {
                ShowFlash(ex.Message, true);
                return false;
            }
        }

        public async Task ForgetAsync()
        {
            try
            {
                UseViewer(await Api.ForgetAsync());
                ShowFlash("This browser no longer remembers your name. Your earlier ratings and comments stay.", false);
            }
            catch (PhotoReviewApiException ex)
            {
                ShowFlash(ex.Message, true);
            }
        }

        /// <summary>Called before any contribution. Opens the name prompt when there is no identity yet.</summary>
        public bool RequireIdentity()
        {
            if (Viewer.Suspended)
            {
                ShowFlash(PhotoReviewParticipation.SuspendedMessage, true);
                return false;
            }

            if (Viewer.HasIdentity)
            {
                return true;
            }

            NamePromptOpen = true;
            Changed?.Invoke();
            return false;
        }

        public void OpenNamePrompt()
        {
            NamePromptOpen = true;
            Changed?.Invoke();
        }

        public void CloseNamePrompt()
        {
            NamePromptOpen = false;
            Changed?.Invoke();
        }

        public void ShowFlash(string message, bool isError)
        {
            Flash = message;
            FlashIsError = isError;
            Changed?.Invoke();
        }

        public void ClearFlash()
        {
            Flash = "";
            Changed?.Invoke();
        }

        /// <summary>Runs a mutation and reports errors in the flash. Returns null on failure.</summary>
        public async Task<MutationResult?> RunAsync(Func<Task<MutationResult>> action, string? success = null)
        {
            try
            {
                var result = await action();
                if (!string.IsNullOrEmpty(success)) ShowFlash(success, false);
                return result;
            }
            catch (PhotoReviewApiException ex)
            {
                if (ex.Status == System.Net.HttpStatusCode.Unauthorized && !Viewer.HasIdentity)
                {
                    NamePromptOpen = true;
                }

                ShowFlash(ex.Message, true);
                return null;
            }
        }

        public ShotSummary? Shot(string shotId) => Summary?.Shots.FirstOrDefault(shot => shot.ShotId == shotId);

        public PhotoSummary? Photo(string photoId) => Summary?.Photos.FirstOrDefault(photo => photo.Id == photoId);
    }
}
