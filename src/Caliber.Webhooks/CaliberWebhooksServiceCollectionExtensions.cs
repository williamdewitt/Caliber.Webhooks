using Caliber.Webhooks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers that wire Caliber.Webhooks into an <see cref="IServiceCollection"/>.
/// </summary>
public static class CaliberWebhooksServiceCollectionExtensions
{
    /// <summary>
    /// Registers Caliber.Webhooks with default options and the in-memory store.
    /// </summary>
    public static IServiceCollection AddCaliberWebhooks(this IServiceCollection services)
        => services.AddCaliberWebhooks(static _ => { });

    /// <summary>
    /// Registers Caliber.Webhooks, applying <paramref name="configure"/> to the options. The
    /// configured options are validated eagerly, so a misconfiguration throws here rather than
    /// surfacing later as a runtime failure.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configured options are invalid.</exception>
    public static IServiceCollection AddCaliberWebhooks(
        this IServiceCollection services, Action<CaliberWebhooksOptions> configure)
    {
        // Stryker disable once all : equivalent — a null service collection is guarded identically by the downstream AddSingleton calls.
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new CaliberWebhooksOptions();
        configure(options);
        Validate(options);

        services.AddSingleton(options);

        if (options.StoreConfigurator is { } configureStores)
        {
            configureStores(services);
        }
        else
        {
            services.AddSingleton<IMessageStore>(_ => new InMemoryMessageStore(options.TimeProvider));
            services.AddSingleton<IEndpointStore, InMemoryEndpointStore>();
        }

        services.AddSingleton(_ => new MatchingEngine());
        services.AddSingleton(_ => new SigningEngine(options.TimeProvider));
        services.AddSingleton(_ => new RetryEngine(options));

        services.AddHttpClient(HttpDeliveryChannel.HttpClientName);
        services.AddSingleton<IDeliveryChannel, HttpDeliveryChannel>();

        services.AddSingleton<DeliveryManager>();
        services.AddSingleton<IWebhookPublisher, IngestionManager>();
        services.AddSingleton<IWebhookEndpoints, SubscriptionManager>();

        services.AddHostedService<DispatcherHost>();
        return services;
    }

    private static void Validate(CaliberWebhooksOptions options)
    {
        if (options.RetrySchedule is null)
        {
            throw Invalid("RetrySchedule must not be null.");
        }

        if (options.TimeProvider is null)
        {
            throw Invalid("TimeProvider must not be null.");
        }

        if (options.MaxAttempts < 1)
        {
            throw Invalid("MaxAttempts must be at least 1.");
        }

        if (options.BatchSize < 1)
        {
            throw Invalid("BatchSize must be at least 1.");
        }

        if (options.MaxConcurrency < 1)
        {
            throw Invalid("MaxConcurrency must be at least 1.");
        }

        if (options.MaxPayloadBytes < 1)
        {
            throw Invalid("MaxPayloadBytes must be at least 1.");
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            throw Invalid("PollInterval must be greater than zero.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            throw Invalid("LeaseDuration must be greater than zero.");
        }

        if (options.HttpTimeout <= TimeSpan.Zero)
        {
            throw Invalid("HttpTimeout must be greater than zero.");
        }

        if (options.TimestampTolerance < TimeSpan.Zero)
        {
            throw Invalid("TimestampTolerance must not be negative.");
        }

        if (options.HttpTimeout >= options.LeaseDuration)
        {
            throw Invalid("HttpTimeout must be less than LeaseDuration so a delivery completes before its lease lapses.");
        }
    }

    private static InvalidOperationException Invalid(string detail)
        => new("Caliber.Webhooks is misconfigured: " + detail);
}
