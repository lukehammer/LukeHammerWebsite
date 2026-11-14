using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace BlazorApp.Client.PhotoShoot
{
    /// <summary>Runs the browser-side marker QR and photo analysis (wwwroot/js/photoshoot-upload.js).</summary>
    public sealed class PhotoShootMedia : IAsyncDisposable
    {
        private readonly IJSRuntime _js;
        private IJSObjectReference? _module;

        public PhotoShootMedia(IJSRuntime js)
        {
            _js = js;
        }

        private async Task<IJSObjectReference> ModuleAsync() =>
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/photoshoot-upload.js");

        /// <summary>Draws the marker QR into the element. Returns false if the element is not on the page.</summary>
        public async Task<bool> RenderQrAsync(string elementId, string text)
        {
            var module = await ModuleAsync();
            return await module.InvokeAsync<bool>("renderQr", elementId, text);
        }

        /// <summary>Measures one photo in the browser. Returns the numbers the sort needs.</summary>
        public async Task<MediaAnalysis> AnalyseAsync(byte[] bytes, DateTime fallbackUtc)
        {
            var module = await ModuleAsync();
            return await module.InvokeAsync<MediaAnalysis>("analyse", bytes, fallbackUtc.ToString("o"));
        }

        public async ValueTask DisposeAsync()
        {
            if (_module != null)
            {
                await _module.DisposeAsync();
            }
        }
    }

    /// <summary>Output of analyse() in photoshoot-upload.js.</summary>
    public sealed class MediaAnalysis
    {
        public double Sharpness { get; set; }
        public double MeanLuma { get; set; }
        public double ClippedHighlights { get; set; }
        public double ClippedShadows { get; set; }
        public string? QrText { get; set; }
        public string? CapturedIso { get; set; }
    }
}
