using Caliber.Webhooks.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Caliber.Webhooks.IntegrationTests;

/// <summary>
/// A shared real-Postgres backdrop for the cross-instance work-claiming proofs. The whole suite is gated
/// behind <c>CALIBER_INTEGRATION=1</c> (set only by the dedicated CI integration job) so the fast PR path —
/// which runs the solution's tests — never starts a Docker container; each test calls
/// <see cref="Xunit.Assert.SkipUnless(bool, string)"/> on <see cref="Available"/>. Locally the suite skips
/// cleanly when Docker is unreachable.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string SkipReason =
        "Postgres integration test — set CALIBER_INTEGRATION=1 with Docker running to execute.";

    private PostgreSqlContainer? _container;

    /// <summary>True when the opt-in flag is set and a Postgres container started successfully.</summary>
    public bool Available { get; private set; }

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        // Opt-in only: keeps Docker off the fast PR path, and keeps local `dotnet test` green without Docker.
        if (!string.Equals(Environment.GetEnvironmentVariable("CALIBER_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Provision messages/endpoints via Caliber's shipped migration — the same path production uses, so
        // the suite also proves the migration applies cleanly against a real server.
        await using var context = NewMessagesContext();
        await context.Database.MigrateAsync();

        Available = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// A factory that hands out a fresh context (and connection) per call. DbContext is not thread-safe, and
    /// every dispatcher must own its connection for <c>FOR UPDATE SKIP LOCKED</c> to partition the backlog.
    /// </summary>
    internal IDbContextFactory<CaliberWebhooksDbContext> MessagesFactory() => new NpgsqlContextFactory(ConnectionString);

    internal CaliberWebhooksDbContext NewMessagesContext() => MessagesFactory().CreateDbContext();

    /// <summary>Clears Caliber's tables so each test asserts against only the rows it seeded.</summary>
    public async Task ResetAsync()
    {
        await using var context = NewMessagesContext();
        await context.Messages.ExecuteDeleteAsync();
        await context.Endpoints.ExecuteDeleteAsync();
    }

    private sealed class NpgsqlContextFactory(string connectionString) : IDbContextFactory<CaliberWebhooksDbContext>
    {
        private readonly DbContextOptions<CaliberWebhooksDbContext> _options = Build(connectionString);

        public CaliberWebhooksDbContext CreateDbContext() => new(_options);

        // Reuses the production Npgsql configuration so the test exercises the same provider settings
        // (including the dedicated migrations-history table) as UsePostgres / the outbox path.
        private static DbContextOptions<CaliberWebhooksDbContext> Build(string connectionString)
        {
            var builder = new DbContextOptionsBuilder<CaliberWebhooksDbContext>();
            CaliberNpgsqlConfiguration.Apply(builder, connectionString);
            return builder.Options;
        }
    }
}
