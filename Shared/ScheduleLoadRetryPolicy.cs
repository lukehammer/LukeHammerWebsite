namespace BlazorApp.Shared
{
    /// <summary>
    /// Backoff when the schedule API is unreachable: 5s, 10s, 30s, then double each time.
    /// </summary>
    public static class ScheduleLoadRetryPolicy
    {
        public const string ApiUnavailableMessage =
            "Could not load schedule. Make sure the API is running.";

        public const string TimedOutMessage =
            "Schedule request timed out. Check that the API is running at the configured address.";

        public static bool IsRetriableError(string error)
        {
            if (error == null)
            {
                return false;
            }

            return error == ApiUnavailableMessage || error == TimedOutMessage;
        }

        /// <param name="consecutiveFailures">Number of failed load attempts so far (1 = first failure).</param>
        public static int GetSecondsBeforeRetry(int consecutiveFailures)
        {
            if (consecutiveFailures <= 1)
            {
                return 5;
            }

            if (consecutiveFailures == 2)
            {
                return 10;
            }

            if (consecutiveFailures == 3)
            {
                return 30;
            }

            var delaySeconds = 30;
            for (var i = 4; i <= consecutiveFailures; i++)
            {
                delaySeconds *= 2;
            }

            return delaySeconds;
        }

        public static string FormatRetryStatus(string errorMessage, int secondsRemaining)
        {
            var unit = secondsRemaining == 1 ? "second" : "seconds";
            return errorMessage + " Retrying in " + secondsRemaining + " " + unit + "...";
        }
    }
}
