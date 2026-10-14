using Microsoft.JSInterop;

namespace BlazorApp.Client.PhotoShoot
{
    /// <summary>Wrapper for wwwroot/js/photoshoot.js (wake lock, visibility/online events, downloads, print).</summary>
    public sealed class PhotoShootBrowser : IAsyncDisposable
    {
        private readonly IJSRuntime _js;
        private readonly PhotoShootStore _store;
        private IJSObjectReference? _module;
        private DotNetObjectReference<PhotoShootBrowser>? _selfRef;

        public PhotoShootBrowser(IJSRuntime js, PhotoShootStore store)
        {
            _js = js;
            _store = store;
        }

        private async Task<IJSObjectReference?> ModuleAsync()
        {
            if (_module != null)
            {
                return _module;
            }

            try
            {
                _module = await _js.InvokeAsync<IJSObjectReference>("import", "./js/photoshoot.js");
            }
            catch (JSException)
            {
                _module = null;
            }

            return _module;
        }

        /// <summary>Listens for "tab visible again" and "network back" so live views refresh immediately.</summary>
        public async Task WatchAsync()
        {
            if (_selfRef != null)
            {
                return;
            }

            var module = await ModuleAsync();
            if (module == null)
            {
                return;
            }

            _selfRef = DotNetObjectReference.Create(this);
            await module.InvokeAsync<bool>("watch", _selfRef);
        }

        [JSInvokable]
        public async Task OnBrowserWake(string reason)
        {
            if (reason == "offline")
            {
                _store.SetFlash("You're offline. The shot list will refresh when the connection returns.", isError: true);
                return;
            }

            if (reason == "online")
            {
                await _store.NetworkRestoredAsync();
                return;
            }

            await _store.SyncNowAsync();
        }

        public async Task<bool> KeepAwakeAsync(bool on)
        {
            var module = await ModuleAsync();
            if (module == null)
            {
                return false;
            }

            try
            {
                return await module.InvokeAsync<bool>("keepAwake", on);
            }
            catch (JSException)
            {
                return false;
            }
        }

        public async Task DownloadTextAsync(string fileName, string text, string mimeType = "application/json")
        {
            var module = await ModuleAsync();
            if (module != null)
            {
                await module.InvokeVoidAsync("downloadText", fileName, text, mimeType);
            }
        }

        public async Task<bool> CopyTextAsync(string text)
        {
            var module = await ModuleAsync();
            if (module == null)
            {
                return false;
            }

            try
            {
                return await module.InvokeAsync<bool>("copyText", text);
            }
            catch (JSException)
            {
                return false;
            }
        }

        public async Task PrintAsync()
        {
            var module = await ModuleAsync();
            if (module != null)
            {
                await module.InvokeVoidAsync("printPage");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_module != null)
            {
                try
                {
                    if (_selfRef != null)
                    {
                        await _module.InvokeVoidAsync("unwatch", _selfRef);
                    }

                    await _module.DisposeAsync();
                }
                catch (JSDisconnectedException)
                {
                }
            }

            _selfRef?.Dispose();
        }
    }
}
