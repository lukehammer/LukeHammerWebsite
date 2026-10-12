using System.Net;
using BlazorApp.Shared.PhotoShoot;
using Microsoft.JSInterop;

namespace BlazorApp.Client.PhotoShoot
{
    public enum PhotoShootLoadState
    {
        Loading,
        Ready,
        NeedsSignIn,
        NoAccess,
        NoShoot,
        Error
    }

    public enum LiveConnectionState
    {
        Connected,
        Syncing,
        Reconnecting,
        Offline
    }

    /// <summary>
    /// Client-side state for the photo shoot feature: who you are, which shoot is open, the latest server copy,
    /// and the polling loop that keeps the director and photographer phones in sync.
    /// </summary>
    public sealed class PhotoShootStore : IAsyncDisposable
    {
        private const string SelectedShootKey = "photoshoot.selectedShoot";
        private const string DevRoleKey = "photoshoot.devRole";
        private static readonly TimeSpan LiveInterval = TimeSpan.FromMilliseconds(2500);
        private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(15);

        private readonly PhotoShootApiClient _api;
        private readonly IJSRuntime _js;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private CancellationTokenSource? _pollCts;
        private int _liveSubscribers;
        private int _failures;
        private bool _initialized;

        public PhotoShootStore(PhotoShootApiClient api, IJSRuntime js)
        {
            _api = api;
            _js = js;
        }

        public event Action? Changed;

        public PhotoShootApiClient Api => _api;
        public PhotoShootLoadState State { get; private set; } = PhotoShootLoadState.Loading;
        public string ErrorMessage { get; private set; } = "";
        public PhotoShootMe? Me { get; private set; }
        public PhotoShootIndex Index { get; private set; } = new();
        public PhotoShootDocument? Shoot { get; private set; }
        public string ETag { get; private set; } = "";
        public LiveConnectionState Connection { get; private set; } = LiveConnectionState.Connected;
        public DateTime? LastSyncedUtc { get; private set; }
        public string Flash { get; private set; } = "";
        public bool FlashIsError { get; private set; }

        public bool IsOwner => Me?.Access == PhotoShootAccess.Owner;
        public bool IsPhotographer => Me?.Access == PhotoShootAccess.Photographer;
        public string ShootId => Shoot?.Id ?? "";

        public async Task EnsureInitializedAsync(string? devRoleFromQuery = null)
        {
            await _initLock.WaitAsync();
            try
            {
                if (!string.IsNullOrWhiteSpace(devRoleFromQuery))
                {
                    await SessionSetAsync(DevRoleKey, devRoleFromQuery);
                    _initialized = false;
                }

                if (_initialized && State == PhotoShootLoadState.Ready)
                {
                    return;
                }

                _api.DevRole = await SessionGetAsync(DevRoleKey) ?? "";
                await InitializeCoreAsync();
                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task InitializeCoreAsync()
        {
            State = PhotoShootLoadState.Loading;
            Notify();
            try
            {
                Me = await _api.GetMeAsync();
                Index = await _api.ListShootsAsync();
                var preferred = await LocalGetAsync(SelectedShootKey);
                var target = Index.Shoots.FirstOrDefault(s => s.Id == preferred)?.Id
                    ?? Index.DefaultShootId;
                if (string.IsNullOrEmpty(target))
                {
                    Shoot = null;
                    State = PhotoShootLoadState.NoShoot;
                }
                else
                {
                    await LoadShootAsync(target);
                    State = PhotoShootLoadState.Ready;
                }

                ErrorMessage = "";
            }
            catch (PhotoShootApiException ex) when (ex.Status == HttpStatusCode.Unauthorized)
            {
                State = PhotoShootLoadState.NeedsSignIn;
            }
            catch (PhotoShootApiException ex) when (ex.Status == HttpStatusCode.Forbidden)
            {
                State = PhotoShootLoadState.NoAccess;
                ErrorMessage = ex.Message;
            }
            catch (PhotoShootApiException ex)
            {
                State = PhotoShootLoadState.Error;
                ErrorMessage = ex.Message;
            }

            Notify();
        }

        public async Task ReloadAsync()
        {
            _initialized = false;
            await EnsureInitializedAsync();
        }

        public async Task LoadShootAsync(string shootId)
        {
            var response = await _api.GetShootAsync(shootId);
            Accept(response);
            await LocalSetAsync(SelectedShootKey, shootId);
            State = PhotoShootLoadState.Ready;
            Notify();
        }

        public async Task RefreshIndexAsync()
        {
            Index = await _api.ListShootsAsync();
            Notify();
        }

        /// <summary>Applies a server response (from a mutation or poll) as the new authoritative state.</summary>
        public void Accept(LiveShootResponse response)
        {
            if (response.Changed && response.Shoot != null)
            {
                Shoot = PhotoShootEditor.Normalize(response.Shoot);
                ETag = response.ETag;
            }

            LastSyncedUtc = DateTime.UtcNow;
            Notify();
        }

        /// <summary>Runs a mutation, applies the returned document, and records a friendly status message.</summary>
        public async Task<bool> RunAsync(Func<string, Task<LiveShootResponse>> call, string? successMessage = null)
        {
            if (Shoot == null)
            {
                return false;
            }

            try
            {
                Connection = Connection == LiveConnectionState.Connected ? LiveConnectionState.Syncing : Connection;
                Notify();
                var response = await call(Shoot.Id);
                Accept(response);
                _failures = 0;
                Connection = LiveConnectionState.Connected;
                SetFlash(successMessage ?? response.Message, isError: false);
                return true;
            }
            catch (PhotoShootApiException ex)
            {
                if (ex.IsNetworkProblem)
                {
                    Connection = LiveConnectionState.Reconnecting;
                }
                else
                {
                    Connection = LiveConnectionState.Connected;
                }

                SetFlash(ex.Message, isError: true);
                return false;
            }
        }

        public Task<bool> SessionAsync(SessionAction action, string shotId = "", bool flag = false, string text = "") =>
            RunAsync(id => _api.SessionAsync(id, new SessionCommand { Action = action, ShotId = shotId, Flag = flag, Text = text }));

        public void SetFlash(string? message, bool isError)
        {
            Flash = message ?? "";
            FlashIsError = isError;
            Notify();
        }

        public void ClearFlash()
        {
            if (Flash.Length > 0)
            {
                Flash = "";
                Notify();
            }
        }

        // ---------- live sync ----------

        /// <summary>Call from live pages (director/photographer/dashboard). Polling stops when nobody needs it.</summary>
        public void StartLive()
        {
            _liveSubscribers++;
            if (_pollCts == null)
            {
                _pollCts = new CancellationTokenSource();
                _ = PollLoopAsync(_pollCts.Token);
            }
        }

        public void StopLive()
        {
            _liveSubscribers = Math.Max(0, _liveSubscribers - 1);
            if (_liveSubscribers == 0)
            {
                _pollCts?.Cancel();
                _pollCts = null;
            }
        }

        /// <summary>Immediate poll (manual refresh button, tab became visible, network came back).</summary>
        public async Task SyncNowAsync()
        {
            await PollOnceAsync(manual: true);
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var delay = _failures == 0
                    ? LiveInterval
                    : TimeSpan.FromMilliseconds(Math.Min(MaxBackoff.TotalMilliseconds, LiveInterval.TotalMilliseconds * Math.Pow(2, Math.Min(_failures, 4))));
                if (_liveSubscribers == 0)
                {
                    delay = IdleInterval;
                }

                try
                {
                    await Task.Delay(delay, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                await PollOnceAsync(manual: false);
            }
        }

        private bool _polling;

        private async Task PollOnceAsync(bool manual)
        {
            if (_polling || Shoot == null)
            {
                return;
            }

            _polling = true;
            try
            {
                if (manual)
                {
                    Connection = LiveConnectionState.Syncing;
                    Notify();
                }

                var response = await _api.GetShootAsync(Shoot.Id, manual ? null : ETag);
                var wasDown = _failures > 0;
                _failures = 0;
                Connection = LiveConnectionState.Connected;
                Accept(response);
                if (wasDown)
                {
                    SetFlash("Back online — showing the latest shot.", isError: false);
                }
            }
            catch (PhotoShootApiException ex) when (ex.IsAuthProblem)
            {
                _failures++;
                Connection = LiveConnectionState.Offline;
                SetFlash("Your sign-in expired. Reload the page to sign in again.", isError: true);
            }
            catch (PhotoShootApiException)
            {
                _failures++;
                Connection = _failures >= 6 ? LiveConnectionState.Offline : LiveConnectionState.Reconnecting;
                Notify();
            }
            finally
            {
                _polling = false;
            }
        }

        private void Notify() => Changed?.Invoke();

        // ---------- browser storage ----------

        private async Task<string?> LocalGetAsync(string key)
        {
            try { return await _js.InvokeAsync<string?>("localStorage.getItem", key); }
            catch (JSException) { return null; }
        }

        private async Task LocalSetAsync(string key, string value)
        {
            try { await _js.InvokeVoidAsync("localStorage.setItem", key, value); }
            catch (JSException) { }
        }

        private async Task<string?> SessionGetAsync(string key)
        {
            try { return await _js.InvokeAsync<string?>("sessionStorage.getItem", key); }
            catch (JSException) { return null; }
        }

        private async Task SessionSetAsync(string key, string value)
        {
            try { await _js.InvokeVoidAsync("sessionStorage.setItem", key, value); }
            catch (JSException) { }
        }

        public ValueTask DisposeAsync()
        {
            _pollCts?.Cancel();
            return ValueTask.CompletedTask;
        }
    }
}
