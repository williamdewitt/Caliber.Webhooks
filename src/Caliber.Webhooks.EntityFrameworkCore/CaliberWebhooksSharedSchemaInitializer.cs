using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;

namespace Caliber.Webhooks.EntityFrameworkCore;

/// <summary>
/// Provisions Caliber's <c>messages</c>/<c>endpoints</c> tables into the caller's database (outbox mode),
/// where the database already exists and holds the caller's own tables plus <c>caliber_outbox</c>. Plain
/// <see cref="IHostedService"/> so <see cref="StartAsync"/> completes — and the schema is ready — before the
/// dispatcher and relay start. On Postgres it applies Caliber's shipped migrations (tracked in a dedicated
/// history table, so they don't collide with the caller's); on SQLite it create-tables-if-missing, which is
/// idempotent and a no-op on every start after the first.
/// </summary>
internal sealed class CaliberWebhooksSharedSchemaInitializer : IHostedService
{
    private readonly IDbContextFactory<CaliberWebhooksDbContext> _factory;

    public CaliberWebhooksSharedSchemaInitializer(IDbContextFactory<CaliberWebhooksDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);

        if (context.Database.IsNpgsql())
        {
            // Production Postgres provisions via reviewed migrations, into Caliber's own history table.
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (await TablesExistAsync(context, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // SQLite (zero-infra): CreateTables emits CREATE TABLE for every entity in this context's model
        // (messages + endpoints) against the existing database, leaving the caller's tables and
        // caliber_outbox untouched.
        var creator = context.Database.GetService<IRelationalDatabaseCreator>();
        await creator.CreateTablesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Probes for Caliber's schema by querying the messages table; a provider-specific "no such table"
    // surfaces as a DbException, which means the tables are not provisioned yet.
    private static async Task<bool> TablesExistAsync(CaliberWebhooksDbContext context, CancellationToken ct)
    {
        try
        {
            await context.Messages.AnyAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbException)
        {
            return false;
        }
    }
}
