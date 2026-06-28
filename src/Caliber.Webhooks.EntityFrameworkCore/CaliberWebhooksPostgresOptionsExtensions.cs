using Caliber.Webhooks.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caliber.Webhooks;

/// <summary>
/// Extension methods on <see cref="CaliberWebhooksOptions"/> that activate the durable Postgres storage tier.
/// </summary>
public static class CaliberWebhooksPostgresOptionsExtensions
{
    /// <summary>
    /// Switches Caliber.Webhooks to a durable PostgreSQL store — the production tier. Concurrent dispatchers
    /// claim disjoint batches with <c>FOR UPDATE SKIP LOCKED</c>, so multiple instances never double-send.
    /// The <c>messages</c>/<c>endpoints</c> schema is provisioned by Caliber's shipped, reviewed migrations,
    /// applied on startup (no ensure-created in production).
    /// </summary>
    /// <param name="options">The Caliber.Webhooks options being configured.</param>
    /// <param name="connectionString">
    /// An Npgsql connection string, e.g. <c>"Host=localhost;Database=app;Username=app;Password=…"</c>.
    /// </param>
    /// <returns><paramref name="options"/> for chaining.</returns>
    public static CaliberWebhooksOptions UsePostgres(
        this CaliberWebhooksOptions options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        options.StoreConfigurator = services =>
        {
            services.AddDbContextFactory<CaliberWebhooksDbContext>(
                builder => CaliberNpgsqlConfiguration.Apply(builder, connectionString));

            services.AddSingleton<IMessageStore>(sp => new EfMessageStore(
                sp.GetRequiredService<IDbContextFactory<CaliberWebhooksDbContext>>(), options.TimeProvider));
            services.AddSingleton<IEndpointStore>(sp => new EfEndpointStore(
                sp.GetRequiredService<IDbContextFactory<CaliberWebhooksDbContext>>()));

            // Applied before DispatcherHost so the migrated schema exists before polling starts.
            services.AddHostedService<CaliberWebhooksPostgresMigrator>();
        };

        return options;
    }
}
