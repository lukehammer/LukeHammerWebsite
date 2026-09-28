using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest;

public class ScheduleLoadRetryPolicyTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 30)]
    [InlineData(4, 60)]
    [InlineData(5, 120)]
    [InlineData(6, 240)]
    public void GetSecondsBeforeRetry_follows_backoff_sequence(int failures, int expectedSeconds)
    {
        ScheduleLoadRetryPolicy.GetSecondsBeforeRetry(failures).Should().Be(expectedSeconds);
    }

    [Theory]
    [InlineData(ScheduleLoadRetryPolicy.ApiUnavailableMessage, true)]
    [InlineData(ScheduleLoadRetryPolicy.TimedOutMessage, true)]
    [InlineData("Schedule data is invalid", false)]
    [InlineData(null, false)]
    public void IsRetriableError_matches_api_and_timeout_only(string? error, bool retriable)
    {
        ScheduleLoadRetryPolicy.IsRetriableError(error).Should().Be(retriable);
    }
}
