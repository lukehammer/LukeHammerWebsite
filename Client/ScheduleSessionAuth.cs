using BlazorApp.Shared;
using Microsoft.JSInterop;

namespace BlazorApp.Client
{
    public static class ScheduleSessionAuth
    {
        public static async Task<string?> TryRestoreSignedInNameAsync(IJSRuntime js)
        {
            try
            {
                var stored = await js.InvokeAsync<string>("scheduleAuth.getName");
                if (string.IsNullOrWhiteSpace(stored))
                {
                    return null;
                }

                if (!ScheduleSubmitters.TryResolveSubmitterForWrite(stored, out var normalized, out _))
                {
                    await ClearSignedInNameAsync(js);
                    return null;
                }

                if (string.Equals(stored.Trim(), "Luke", StringComparison.OrdinalIgnoreCase))
                {
                    await ClearSignedInNameAsync(js);
                    return null;
                }

                if (string.Equals(stored.Trim(), ScheduleSubmitters.LukeAuthCode, StringComparison.Ordinal))
                {
                    return ScheduleSubmitters.LukeAuthCode;
                }

                return normalized;
            }
            catch (JSException)
            {
                return null;
            }
        }

        public static async Task PersistSignedInNameAsync(IJSRuntime js, string normalizedFirstName)
        {
            try
            {
                await js.InvokeVoidAsync("scheduleAuth.setName", normalizedFirstName);
            }
            catch (JSException)
            {
                // sessionStorage unavailable
            }
        }

        public static async Task ClearSignedInNameAsync(IJSRuntime js)
        {
            try
            {
                await js.InvokeVoidAsync("scheduleAuth.clearName");
            }
            catch (JSException)
            {
                // sessionStorage unavailable
            }
        }
    }
}
