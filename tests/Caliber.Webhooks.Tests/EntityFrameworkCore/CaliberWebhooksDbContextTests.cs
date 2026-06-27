using AwesomeAssertions;
using Caliber.Webhooks.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Caliber.Webhooks.Tests;

public sealed class CaliberWebhooksDbContextTests
{
    [Fact]
    public async Task EnsureCreated_builds_a_usable_messages_and_endpoints_schema_on_a_sqlite_file()
    {
        var dbPath = NewDbPath();
        try
        {
            await using var db = NewContext(dbPath);

            var created = await db.Database.EnsureCreatedAsync();

            created.Should().BeTrue();
            File.Exists(dbPath).Should().BeTrue();

            // A query against each table proves both that the table exists and that the entity mapping
            // is valid — either would throw if the schema were missing or the model misconfigured.
            (await db.Messages.CountAsync()).Should().Be(0);
            (await db.Endpoints.CountAsync()).Should().Be(0);
        }
        finally
        {
            DeleteIfExists(dbPath);
        }
    }

    [Fact]
    public async Task Schema_maps_the_domain_to_the_durable_snake_case_contract()
    {
        var dbPath = NewDbPath();
        try
        {
            await using var db = NewContext(dbPath);
            await db.Database.EnsureCreatedAsync();

            // The column names are the durable, cross-provider schema contract the SQLite and Postgres
            // stores (and the host's migrations) depend on — pin them here so a rename can't pass silently.
            (await ColumnsAsync(db, "messages")).Should().BeEquivalentTo(
                "id", "event_id", "endpoint_id", "event_type", "payload", "created_at",
                "status", "attempt_count", "next_attempt_at", "owner", "lease_until", "last_error");

            (await ColumnsAsync(db, "endpoints")).Should().BeEquivalentTo(
                "id", "tenant_key", "url", "secret", "subscribed_event_types", "enabled", "description");

            // Fan-out is idempotent on a UNIQUE (event_id, endpoint_id) index — the contract the relay
            // and publish-retry paths rely on to never double-enqueue.
            var messageIndexes = await db.Database
                .SqlQueryRaw<string>(
                    "SELECT sql AS Value FROM sqlite_master WHERE type = 'index' AND tbl_name = 'messages' AND sql IS NOT NULL")
                .ToListAsync();

            messageIndexes.Should().Contain(sql =>
                sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("event_id", StringComparison.Ordinal)
                && sql.Contains("endpoint_id", StringComparison.Ordinal));
        }
        finally
        {
            DeleteIfExists(dbPath);
        }
    }

    // Pooling=False so the file handle is released on context dispose and the temp file can be deleted.
    private static CaliberWebhooksDbContext NewContext(string dbPath) =>
        new(new DbContextOptionsBuilder<CaliberWebhooksDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=False")
            .Options);

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"caliber-webhooks-efcore-{Guid.NewGuid():N}.db");

    private static async Task<List<string>> ColumnsAsync(CaliberWebhooksDbContext db, string table)
    {
        // pragma_table_info exposes the columns as rows; the table names are test constants (no injection).
        var sql = string.Equals(table, "messages", StringComparison.Ordinal)
            ? "SELECT name AS Value FROM pragma_table_info('messages')"
            : "SELECT name AS Value FROM pragma_table_info('endpoints')";
        return await db.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
