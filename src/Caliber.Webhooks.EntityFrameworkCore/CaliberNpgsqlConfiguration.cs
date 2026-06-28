using Microsoft.EntityFrameworkCore;

namespace Caliber.Webhooks.EntityFrameworkCore;

/// <summary>
/// Centralises how Caliber configures its <see cref="CaliberWebhooksDbContext"/> on Npgsql, so every entry
/// point (standalone <c>UsePostgres</c>, outbox <c>UseEntityFramework</c>, and the design-time factory)
/// applies the same settings.
/// </summary>
internal static class CaliberNpgsqlConfiguration
{
    /// <summary>
    /// Caliber tracks its own migrations in a dedicated history table, not the default
    /// <c>__EFMigrationsHistory</c>. In outbox mode Caliber shares the caller's database, and the caller's
    /// <c>DbContext</c> owns the default table — a separate history table keeps the two migration sets from
    /// colliding.
    /// </summary>
    public const string MigrationsHistoryTable = "__caliber_migrations_history";

    public static void Apply(DbContextOptionsBuilder builder, string? connectionString)
        => builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable));
}
