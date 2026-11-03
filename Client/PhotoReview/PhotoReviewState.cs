using BlazorApp.Shared.PhotoReview;
using Microsoft.JSInterop;

namespace BlazorApp.Client.PhotoReview
{
    /// <summary>Per-tab review state: the summary (all shots and photos), who I am, and a flash message.</summary>
    public class PhotoReviewState
    {
        // This browser's copy of the name, so a visitor who already gave it isn't asked again on this device.
        private const string RememberedNameKey = "photoreview.displayName";
        private readonly IJSRuntime _js;
        private Task<ReviewSummaryResponse>? _loading;
        private bool _nameRestored;

        public PhotoReviewState(PhotoReviewApi api, IJSRuntime js)
        {
            Api = api;
            _js = js;
        }

        /// <summary>Restores the remembered name once per session, quietly, when the server has no identity for this browser.</summary>
        private async Task RestoreRememberedNameAsync()
        {
            if (_nameRestored || Viewer.HasIdentity) return;
            _nameRestored = true;
            var name = await GetRememberedNameAsync();
            if (name.Length == 0) return;
            try
            {
                UseViewer(await Api.SetNameAsync(name));
            }
            catch (PhotoReviewApiException)
            {
                // Not fatal: the visitor can still type their name when they act.
            }
        }

        private async Task<string> GetRememberedNameAsync()
        {
            try
            {
                return await _js.InvokeAsync<string?>("localStorage.getItem", RememberedNameKey) ?? "";
            }
            catch (JSException)
            {
                return "";
            }
        }

        private async Task RememberNameAsync(string name)
        {
            try
            {
                await _js.InvokeVoidAsync("localStorage.setItem", RememberedNameKey, name);
            }
            catch (JSException)
            {
                // Storage can be blocked; the cookie still remembers the name.
            }
        }

        private async Task ForgetRememberedNameAsync()
        {
            try
            {
                await _js.InvokeVoidAsync("localStorage.removeItem", RememberedNameKey);
            }
            catch (JSException)
            {
                // Nothing to clear if storage is blocked.
            }
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
                // A refresh must not drop an identity this page already has (e.g. just after naming yourself).
                var known = Viewer;
                Viewer = Summary.Viewer;
                if (known.HasIdentity && !Viewer.HasIdentity) UseViewer(known);
                Error = "";
                await RestoreRememberedNameAsync();
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
                _nameRestored = true;
                await RememberNameAsync(Viewer.DisplayName);
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
                await ForgetRememberedNameAsync();
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
