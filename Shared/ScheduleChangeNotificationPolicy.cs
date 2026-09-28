using System.Collections.Generic;

namespace BlazorApp.Shared
{
    public static class ScheduleChangeNotificationPolicy
    {
        public static bool ShouldNotify(string submittedBy, IEnumerable<string>? lukeAliases = null) =>
            !LukeNameMatcher.IsLuke(submittedBy, lukeAliases);
    }
}
