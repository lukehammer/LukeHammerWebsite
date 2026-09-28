using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using BlazorApp.Shared;

namespace BlazorApp.Client
{
    public sealed class ScheduleSpectatorsClient
    {
        private readonly HttpClient _http;
        private Task<IReadOnlyList<string>>? _loadTask;

        public ScheduleSpectatorsClient(HttpClient http)
        {
            _http = http;
        }

        public Task<IReadOnlyList<string>> GetNamesAsync()
        {
            _loadTask ??= LoadNamesCoreAsync();
            return _loadTask;
        }

        public async Task<IReadOnlyList<string>> AppendNamesAsync(IEnumerable<string> addNames)
        {
            var payload = new { addNames = addNames };
            var response = await _http.PostAsJsonAsync("api/sports/spectators", payload);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<ScheduleSpectatorsResponse>(
                ScheduleSpectators.JsonOptions);

            var names = body?.Names ?? new List<string>();
            _loadTask = Task.FromResult<IReadOnlyList<string>>(names);
            return names;
        }

        public void InvalidateCache() => _loadTask = null;

        private async Task<IReadOnlyList<string>> LoadNamesCoreAsync()
        {
            try
            {
                var response = await _http.GetAsync("api/sports/spectators");
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<ScheduleSpectatorsResponse>(
                    ScheduleSpectators.JsonOptions);
                return body?.Names ?? ScheduleSpectators.MergeNames(new ScheduleSpectatorsData());
            }
            catch
            {
                return ScheduleSpectators.MergeNames(new ScheduleSpectatorsData());
            }
        }

        private sealed class ScheduleSpectatorsResponse
        {
            public List<string> Names { get; set; } = new();
        }
    }
}
