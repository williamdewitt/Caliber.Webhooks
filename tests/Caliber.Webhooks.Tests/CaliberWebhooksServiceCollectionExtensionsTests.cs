using AwesomeAssertions;
using Caliber.Webhooks.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caliber.Webhooks.Tests;

public sealed class CaliberWebhooksServiceCollectionExtensionsTests
{
    // Asserts the misconfiguration is rejected AND that the operator-facing message is the expected
    // one (prefix + specific detail) — the message text is part of the fail-fast contract.
    private static void AssertRejected(Action<CaliberWebhooksOptions> configure, string expectedDetail)
    {
        var act = () => new ServiceCollection().AddCaliberWebhooks(configure);

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().StartWith("Caliber.Webhooks is misconfigured: ");
        message.Should().Contain(expectedDetail);
    }

    // The minimum valid configuration is accepted — pins the comparison boundaries so `< 1` can't
    // drift to `<= 1` (etc.) unnoticed.
    private static void AssertAccepted(Action<CaliberWebhooksOptions> configure)
    {
        var act = () => new ServiceCollection().AddCaliberWebhooks(configure);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddCaliberWebhooks_rejects_a_null_service_collection()
    {
        var act = () => ((IServiceCollection)null!).AddCaliberWebhooks(static _ => { });
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("services");
    }

    [Fact]
    public void AddCaliberWebhooks_rejects_a_null_configure_delegate()
    {
        var act = () => new ServiceCollection().AddCaliberWebhooks(null!);
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("configure");
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
    public void Rejects_null_retry_schedule()
        => AssertRejected(o => o.RetrySchedule = null!, "RetrySchedule must not be null.");

    [Fact]
    public void Rejects_null_time_provider()
        => AssertRejected(o => o.TimeProvider = null!, "TimeProvider must not be null.");

    [Fact]
    public void Rejects_zero_max_attempts()
        => AssertRejected(o => o.MaxAttempts = 0, "MaxAttempts must be at least 1.");

    [Fact]
    public void Rejects_zero_batch_size()
        => AssertRejected(o => o.BatchSize = 0, "BatchSize must be at least 1.");

    [Fact]
    public void Rejects_zero_concurrency()
        => AssertRejected(o => o.MaxConcurrency = 0, "MaxConcurrency must be at least 1.");

    [Fact]
    public void Rejects_zero_payload_cap()
        => AssertRejected(o => o.MaxPayloadBytes = 0, "MaxPayloadBytes must be at least 1.");

    [Fact]
    public void Rejects_non_positive_poll_interval()
        => AssertRejected(o => o.PollInterval = TimeSpan.Zero, "PollInterval must be greater than zero.");

    [Fact]
    public void Rejects_non_positive_lease()
        => AssertRejected(o => o.LeaseDuration = TimeSpan.Zero, "LeaseDuration must be greater than zero.");

    [Fact]
    public void Rejects_non_positive_http_timeout()
        => AssertRejected(o => o.HttpTimeout = TimeSpan.Zero, "HttpTimeout must be greater than zero.");

    [Fact]
    public void Rejects_negative_timestamp_tolerance()
        => AssertRejected(o => o.TimestampTolerance = TimeSpan.FromSeconds(-1), "TimestampTolerance must not be negative.");

    [Fact]
    public void Rejects_http_timeout_not_below_lease()
        => AssertRejected(o => o.HttpTimeout = o.LeaseDuration, "HttpTimeout must be less than LeaseDuration");

    [Fact]
    public void Accepts_the_minimum_valid_counts()
        => AssertAccepted(o =>
        {
            o.MaxAttempts = 1;
            o.BatchSize = 1;
            o.MaxConcurrency = 1;
            o.MaxPayloadBytes = 1;
        });

    [Fact]
    public void Accepts_the_smallest_positive_poll_interval()
        => AssertAccepted(o => o.PollInterval = TimeSpan.FromTicks(1));

    [Fact]
    public void Accepts_a_zero_timestamp_tolerance()
        => AssertAccepted(o => o.TimestampTolerance = TimeSpan.Zero);

    [Fact]
    public void Accepts_an_http_timeout_just_below_the_lease()
        => AssertAccepted(o =>
        {
            o.LeaseDuration = TimeSpan.FromSeconds(2);
            o.HttpTimeout = TimeSpan.FromSeconds(1);
        });

    // ── UseSqlite wiring ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UseSqlite_registers_EfMessageStore_and_EfEndpointStore()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCaliberWebhooks(o => o.UseSqlite("Data Source=:memory:"))
            .BuildServiceProvider();

        provider.GetRequiredService<IMessageStore>().Should().BeOfType<EfMessageStore>();
        provider.GetRequiredService<IEndpointStore>().Should().BeOfType<EfEndpointStore>();
    }

    [Fact]
    public void UseSqlite_registers_IDbContextFactory()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCaliberWebhooks(o => o.UseSqlite("Data Source=:memory:"))
            .BuildServiceProvider();

        provider.GetService<IDbContextFactory<CaliberWebhooksDbContext>>().Should().NotBeNull();
    }

    [Fact]
    public void UseSqlite_registers_the_schema_initializer_as_a_hosted_service()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCaliberWebhooks(o => o.UseSqlite("Data Source=:memory:"))
            .BuildServiceProvider();

        provider.GetServices<IHostedService>()
            .Should().Contain(s => s is CaliberWebhooksSqliteInitializer);
    }

    [Fact]
    public void UseSqlite_rejects_a_null_connection_string()
    {
        var act = () => new CaliberWebhooksOptions().UseSqlite(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UseSqlite_rejects_a_whitespace_connection_string()
    {
        var act = () => new CaliberWebhooksOptions().UseSqlite("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UseSqlite_rejects_a_null_options_instance()
    {
        var act = () => ((CaliberWebhooksOptions)null!).UseSqlite("Data Source=caliber.db");
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("options");
    }
}
