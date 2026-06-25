using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

public sealed class CaliberWebhooksOptionsTests
{
    [Fact]
    public void Defaults_match_the_locked_values()
    {
        var options = new CaliberWebhooksOptions();

        options.MaxAttempts.Should().Be(12);
        options.RetrySchedule.Should().BeSameAs(RetrySchedule.Default);
        options.LeaseDuration.Should().Be(TimeSpan.FromSeconds(60));
        options.HttpTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.BatchSize.Should().Be(50);
        options.MaxConcurrency.Should().Be(16);
        options.MaxPayloadBytes.Should().Be(262144);
        options.AllowInsecureHttp.Should().BeFalse();
        options.TimestampTolerance.Should().Be(TimeSpan.FromMinutes(5));
        options.TimeProvider.Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void Http_timeout_default_is_below_the_lease_duration()
    {
        // This is the lease/crash-recovery invariant the registration validates.
        var options = new CaliberWebhooksOptions();

        options.HttpTimeout.Should().BeLessThan(options.LeaseDuration);
    }
}
