namespace BlazorApp.Client;

public sealed class ScheduleLoadRetryPageState
{
    public string? RetryMessage { get; private set; }

    public int? RetrySecondsRemaining { get; private set; }

    public int RetryTotalSeconds { get; private set; }

    public bool IsRetryWaiting =>
        RetrySecondsRemaining is > 0 && !string.IsNullOrWhiteSpace(RetryMessage);

    public void ApplyWaiting(ScheduleLoadRetryUpdate update)
    {
        RetryMessage = update.ErrorMessage;
        RetrySecondsRemaining = update.SecondsRemaining;
        RetryTotalSeconds = update.TotalDelaySeconds;
    }

    public void Clear()
    {
        RetryMessage = null;
        RetrySecondsRemaining = null;
        RetryTotalSeconds = 0;
    }
}
