using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caliber.Webhooks.Tests;

public sealed class CaliberWebhooksServiceCollectionExtensionsTests
{
    private static void AssertRejected(Action<CaliberWebhooksOptions> configure)
    {
        var act = () => new ServiceCollection().AddCaliberWebhooks(configure);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddCaliberWebhooks_resolves_the_public_surface()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCaliberWebhooks()
            .BuildServiceProvider();

        provider.GetService<IWebhookPublisher>().Should().NotBeNull();
        provider.GetService<IWebhookEndpoints>().Should().NotBeNull();
        provider.GetServices<IHostedService>().Should().ContainSingle(service => service is DispatcherHost);
    }

    [Fact]
    public void AddCaliberWebhooks_uses_default_options_when_unconfigured()
    {
        using var provider = new ServiceCollection().AddLogging().AddCaliberWebhooks().BuildServiceProvider();

        provider.GetRequiredService<CaliberWebhooksOptions>().MaxAttempts.Should().Be(12);
    }

    [Fact]
    public void AddCaliberWebhooks_applies_configuration()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCaliberWebhooks(o => o.BatchSize = 7)
            .BuildServiceProvider();

        provider.GetRequiredService<CaliberWebhooksOptions>().BatchSize.Should().Be(7);
    }

    [Fact]
    public void Rejects_zero_max_attempts() => AssertRejected(o => o.MaxAttempts = 0);

    [Fact]
    public void Rejects_zero_batch_size() => AssertRejected(o => o.BatchSize = 0);

    [Fact]
    public void Rejects_zero_concurrency() => AssertRejected(o => o.MaxConcurrency = 0);

    [Fact]
    public void Rejects_zero_payload_cap() => AssertRejected(o => o.MaxPayloadBytes = 0);

    [Fact]
    public void Rejects_non_positive_poll_interval() => AssertRejected(o => o.PollInterval = TimeSpan.Zero);

    [Fact]
    public void Rejects_non_positive_lease() => AssertRejected(o => o.LeaseDuration = TimeSpan.Zero);

    [Fact]
    public void Rejects_non_positive_http_timeout() => AssertRejected(o => o.HttpTimeout = TimeSpan.Zero);

    [Fact]
    public void Rejects_negative_timestamp_tolerance() => AssertRejected(o => o.TimestampTolerance = TimeSpan.FromSeconds(-1));

    [Fact]
    public void Rejects_http_timeout_not_below_lease() => AssertRejected(o => o.HttpTimeout = o.LeaseDuration);
}
