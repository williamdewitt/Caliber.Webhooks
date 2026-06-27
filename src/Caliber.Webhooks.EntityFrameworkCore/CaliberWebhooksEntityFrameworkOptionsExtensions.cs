using Caliber.Webhooks.EntityFrameworkCore;
using Caliber.Webhooks.EntityFrameworkCore.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caliber.Webhooks;

/// <summary>
/// Extension methods on <see cref="CaliberWebhooksOptions"/> that activate the EF Core transactional-outbox tier.
/// </summary>
public static class CaliberWebhooksEntityFrameworkOptionsExtensions
{
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>
    /// Runs Caliber.Webhooks in <strong>transactional-outbox</strong> mode against the caller's
    /// <typeparamref name="TContext"/>. <c>PublishAsync</c> stages an outbox row into that context (commits
    /// with your <c>SaveChangesAsync</c>); a background relay fans it out into Caliber's <c>messages</c> store,
    /// which lives in the same database. Map the outbox onto your model with
    /// <see cref="CaliberOutboxModelBuilderExtensions.AddCaliberOutbox(ModelBuilder)"/>:
    /// <code>
    /// builder.Services.AddCaliberWebhooks(o => o.UseEntityFramework&lt;AppDbContext&gt;());
    /// // in AppDbContext: protected override void OnModelCreating(ModelBuilder b) => b.AddCaliberOutbox();
    /// </code>
    /// Caliber's <c>messages</c>/<c>endpoints</c> schema is created automatically when absent; you migrate the
    /// thin <c>caliber_outbox</c> table with your own <c>dotnet ef</c> workflow.
    /// </summary>
    /// <typeparam name="TContext">The caller's registered <c>DbContext</c>.</typeparam>
    /// <param name="options">The Caliber.Webhooks options being configured.</param>
    /// <returns><paramref name="options"/> for chaining.</returns>
    public static CaliberWebhooksOptions UseEntityFramework<TContext>(this CaliberWebhooksOptions options)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(options);

        options.StoreConfigurator = services =>
        {
            // Caliber's own messages/endpoints schema shares TContext's database. The provider and connection
            // are read once from the caller's registered context when the factory options are first built.
            services.AddDbContextFactory<CaliberWebhooksDbContext>((sp, builder) =>
            {
                using var scope = sp.CreateScope();
                var caller = scope.ServiceProvider.GetRequiredService<TContext>();
                var provider = caller.Database.ProviderName;
                var connectionString = caller.Database.GetConnectionString();

                if (string.Equals(provider, SqliteProvider, StringComparison.Ordinal))
                {
                    builder.UseSqlite(connectionString);
                }
                else
                {
                    throw new NotSupportedException(
                        $"Caliber.Webhooks outbox mode supports SQLite in v1; the '{provider}' provider arrives " +
                        "with the durable Postgres store (issue #38).");
                }
            });

            services.AddSingleton<IMessageStore>(sp => new EfMessageStore(
                sp.GetRequiredService<IDbContextFactory<CaliberWebhooksDbContext>>(), options.TimeProvider));
            services.AddSingleton<IEndpointStore>(sp => new EfEndpointStore(
                sp.GetRequiredService<IDbContextFactory<CaliberWebhooksDbContext>>()));

            // Scoped, so it stages into the same TContext instance the caller saves; registered before the
            // core's TryAddSingleton fallback so this outbox publisher wins.
            services.AddScoped<IWebhookPublisher, OutboxPublisher<TContext>>();

            services.AddSingleton<RelayProcessor>();
            services.AddHostedService<CaliberWebhooksSharedSchemaInitializer>();
            services.AddHostedService<RelayHost<TContext>>();
        };

        return options;
    }
}
