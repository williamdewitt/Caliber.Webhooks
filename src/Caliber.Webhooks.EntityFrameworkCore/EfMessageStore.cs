using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Caliber.Webhooks.EntityFrameworkCore;

/// <summary>
/// A durable <see cref="IMessageStore"/> backed by EF Core. The claim path is pushed into one atomic SQL
/// statement so concurrent dispatchers never double-claim: on Postgres a <c>FOR UPDATE SKIP LOCKED</c>
/// subquery lets N dispatchers claim disjoint batches with no contention; on SQLite, whose single writer
/// serializes the equivalent <c>UPDATE … RETURNING</c>, the lock is implicit. Fan-out inserts are idempotent
/// on <c>(event_id, endpoint_id)</c> via <c>ON CONFLICT DO NOTHING</c> (supported by both providers). A fresh
/// context (and connection) is taken per operation so the store is safe to call concurrently — DbContext is
/// not thread-safe and must not be shared across the workers.
/// </summary>
internal sealed class EfMessageStore : IMessageStore
{
    // SQLite: the single writer serializes this UPDATE … RETURNING, so the WHERE alone prevents double-claim.
    private const string ClaimSqliteSql =
        """
        UPDATE messages
           SET owner = @owner, lease_until = @lease_until
         WHERE id IN (
               SELECT id FROM messages
                WHERE status = @pending
                  AND next_attempt_at <= @now
                  AND (owner IS NULL OR lease_until < @now)
                ORDER BY next_attempt_at
                LIMIT @batch)
        RETURNING id, event_id, endpoint_id, event_type, payload, created_at,
                  status, attempt_count, next_attempt_at, owner, lease_until, last_error;
        """;

    // Postgres: FOR UPDATE SKIP LOCKED locks the chosen rows and skips ones a concurrent dispatcher already
    // locked, so N dispatchers claim disjoint batches with no contention. Same columns/order as the SQLite
    // variant so Map reads either identically.
    private const string ClaimPostgresSql =
        """
        UPDATE messages AS m
           SET owner = @owner, lease_until = @lease_until
          FROM (
               SELECT id FROM messages
                WHERE status = @pending
                  AND next_attempt_at <= @now
                  AND (owner IS NULL OR lease_until < @now)
                ORDER BY next_attempt_at
                FOR UPDATE SKIP LOCKED
                LIMIT @batch) AS due
         WHERE m.id = due.id
        RETURNING m.id, m.event_id, m.endpoint_id, m.event_type, m.payload, m.created_at,
                  m.status, m.attempt_count, m.next_attempt_at, m.owner, m.lease_until, m.last_error;
        """;

    private readonly IDbContextFactory<CaliberWebhooksDbContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    public EfMessageStore(IDbContextFactory<CaliberWebhooksDbContext> contextFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
    }

    public async Task<int> AddAsync(IReadOnlyCollection<WebhookMessage> messages, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);

        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        using var transaction = (DbTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var inserted = 0;
        foreach (var message in messages)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            // ON CONFLICT DO NOTHING makes fan-out idempotent on the (event_id, endpoint_id) unique
            // index; the rows-affected count is therefore the number actually inserted (skips = 0).
            command.CommandText =
                """
                INSERT INTO messages
                    (id, event_id, endpoint_id, event_type, payload, created_at,
                     status, attempt_count, next_attempt_at, owner, lease_until, last_error)
                VALUES
                    (@id, @event_id, @endpoint_id, @event_type, @payload, @created_at,
                     @status, @attempt_count, @next_attempt_at, @owner, @lease_until, @last_error)
                ON CONFLICT (event_id, endpoint_id) DO NOTHING;
                """;
            AddParameter(command, "@id", message.Id);
            AddParameter(command, "@event_id", message.EventId);
            AddParameter(command, "@endpoint_id", message.EndpointId);
            AddParameter(command, "@event_type", message.EventType);
            AddParameter(command, "@payload", message.Payload);
            AddParameter(command, "@created_at", message.CreatedAt);
            AddParameter(command, "@status", (int)message.Status);
            AddParameter(command, "@attempt_count", message.AttemptCount);
            AddParameter(command, "@next_attempt_at", message.NextAttemptAt);
            AddParameter(command, "@owner", message.Owner);
            AddParameter(command, "@lease_until", message.LeaseUntil);
            AddParameter(command, "@last_error", message.LastError);

            inserted += await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return inserted;
    }

    public async Task<IReadOnlyList<WebhookMessage>> ClaimDueAsync(
        int batchSize, TimeSpan lease, string owner, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentException.ThrowIfNullOrEmpty(owner);

        var now = _timeProvider.GetUtcNow();
        var leaseUntil = now + lease;

        var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);

        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        using var command = connection.CreateCommand();

        // One atomic statement selects the earliest-due, unleased batch and claims it. A message is due
        // when it is pending, its next attempt has arrived, and it is unowned or its lease has lapsed
        // (lease_until < now is strict, so a lease is still held at the exact expiry instant). On Postgres,
        // FOR UPDATE SKIP LOCKED makes concurrent dispatchers claim disjoint batches without blocking; on
        // SQLite the single writer serializes the equivalent statement, so its WHERE simply excludes rows a
        // competitor already claimed — no double-claim either way.
        command.CommandText = IsNpgsql(context) ? ClaimPostgresSql : ClaimSqliteSql;
        AddParameter(command, "@owner", owner);
        AddParameter(command, "@lease_until", leaseUntil);
        AddParameter(command, "@pending", (int)DeliveryStatus.Pending);
        AddParameter(command, "@now", now);
        AddParameter(command, "@batch", batchSize);

        var claimed = new List<WebhookMessage>();
        using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                claimed.Add(Map(reader));
            }
        }

        // RETURNING does not guarantee row order; present the batch earliest-due-first like the in-memory store.
        return claimed.OrderBy(m => m.NextAttemptAt).ToList();
    }

    public async Task MarkDeliveredAsync(Guid messageId, CancellationToken ct = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);

        await context.Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.Status, DeliveryStatus.Delivered)
                    .SetProperty(m => m.Owner, (string?)null)
                    .SetProperty(m => m.LeaseUntil, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
    }

    public async Task RescheduleAsync(
        Guid messageId, int attemptCount, DateTimeOffset nextAttemptAt, string error, CancellationToken ct = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);

        await context.Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.Status, DeliveryStatus.Pending)
                    .SetProperty(m => m.AttemptCount, attemptCount)
                    .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                    .SetProperty(m => m.LastError, error)
                    .SetProperty(m => m.Owner, (string?)null)
                    .SetProperty(m => m.LeaseUntil, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
    }

    public async Task DeadLetterAsync(Guid messageId, int attemptCount, string error, CancellationToken ct = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);

        await context.Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.Status, DeliveryStatus.DeadLettered)
                    .SetProperty(m => m.AttemptCount, attemptCount)
                    .SetProperty(m => m.LastError, error)
                    .SetProperty(m => m.Owner, (string?)null)
                    .SetProperty(m => m.LeaseUntil, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
    }

    private static bool IsNpgsql(CaliberWebhooksDbContext context) =>
        context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) ?? false;

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    // Maps a row from ClaimDueAsync's RETURNING, which is post-UPDATE: a claimed row always carries the
    // owner and lease just set, so only last_error (recorded on a failed attempt) can be null here.
    private static WebhookMessage Map(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        EventId = reader.GetGuid(1),
        EndpointId = reader.GetGuid(2),
        EventType = reader.GetString(3),
        Payload = reader.GetString(4),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
        Status = (DeliveryStatus)reader.GetInt32(6),
        AttemptCount = reader.GetInt32(7),
        NextAttemptAt = reader.GetFieldValue<DateTimeOffset>(8),
        Owner = reader.GetString(9),
        LeaseUntil = reader.GetFieldValue<DateTimeOffset>(10),
        LastError = reader.IsDBNull(11) ? null : reader.GetString(11),
    };
}
