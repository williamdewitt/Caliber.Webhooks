using Caliber.Webhooks.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caliber.Webhooks;

/// <summary>
/// Extension methods on <see cref="CaliberWebhooksOptions"/> that activate the SQLite durable-storage tier.
/// </summary>
public static class CaliberWebhooksSqliteOptionsExtensions
{
    /// <summary>
    /// Switches Caliber.Webhooks from the default in-memory store to a durable SQLite store. The
    /// <c>messages</c> and <c>endpoints</c> schema are created automatically on startup via
    /// <c>EnsureCreatedAsync</c> — no separate migration step needed.
    /// </summary>
    /// <param name="options">The Caliber.Webhooks options being configured.</param>
    /// <param name="connectionString">
    /// A SQLite connection string, e.g. <c>"Data Source=caliber.db"</c>. The database file is created
    /// if it does not exist.
    /// </param>
    /// <returns><paramref name="options"/> for further chaining.</returns>
    public static CaliberWebhooksOptions UseSqlite(
        this CaliberWebhooksOptions options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        options.StoreConfigurator = services =>
        {
            services.AddDbContextFactory<CaliberWebhooksDbContext>(
                o => o.UseSqlite(connectionString));

            services.AddSingleton<IMessageStore>(sp => new EfMessageStore(
                sp.GetRequiredService<IDbContextFactory<CaliberWebhooksDbContext>>(),
                options.TimeProvider));

            services.AddSingleton<IEndpointStore>(sp => new EfEndpointStore(
                sp.GetRequiredService<IDbContextFactory<CaliberWebhooksDbContext>>()));

            // Registered before DispatcherHost so EnsureCreatedAsync completes before polling starts.
            services.AddHostedService<CaliberWebhooksSqliteInitializer>();
        };

        return options;
    }
}
