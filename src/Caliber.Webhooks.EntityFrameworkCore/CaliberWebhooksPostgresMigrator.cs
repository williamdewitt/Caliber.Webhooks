using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Caliber.Webhooks.EntityFrameworkCore;

/// <summary>
/// Applies Caliber's shipped Postgres migrations on startup, creating or upgrading the <c>messages</c> and
/// <c>endpoints</c> schema before the dispatcher and relay begin. Plain <see cref="IHostedService"/> so
/// <see cref="StartAsync"/> completes — and the schema is ready — before the next hosted service starts.
/// Unlike the SQLite ensure-created path, production Postgres provisions via reviewed migrations.
/// </summary>
internal sealed class CaliberWebhooksPostgresMigrator : IHostedService
{
    private readonly IDbContextFactory<CaliberWebhooksDbContext> _factory;

    public CaliberWebhooksPostgresMigrator(IDbContextFactory<CaliberWebhooksDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
