using Microsoft.EntityFrameworkCore;

namespace Caliber.Webhooks.EntityFrameworkCore.Outbox;

/// <summary>
/// Drains one batch of committed outbox rows and fans each out into the <c>messages</c> store. The fan-out
/// insert is idempotent on <c>(event_id, endpoint_id)</c> (via <see cref="IMessageStore.AddAsync"/>), so a
/// crash between the insert and the outbox delete is safe: the retry re-fans-out, the inserts no-op, and the
/// rows are deleted on the next pass. Stateless across batches; <see cref="RelayHost{TContext}"/> drives it.
/// </summary>
internal sealed class RelayProcessor
{
    private readonly IMessageStore _messages;
    private readonly IEndpointStore _endpoints;
    private readonly MatchingEngine _matching;
    private readonly TimeProvider _timeProvider;
    private readonly int _batchSize;

    public RelayProcessor(
        IMessageStore messages, IEndpointStore endpoints, MatchingEngine matching, CaliberWebhooksOptions options)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(matching);
        ArgumentNullException.ThrowIfNull(options);

        _messages = messages;
        _endpoints = endpoints;
        _matching = matching;
        _timeProvider = options.TimeProvider;
        _batchSize = options.BatchSize;
    }

    /// <summary>
    /// Relays up to one batch from <paramref name="context"/>'s outbox into <c>messages</c>, then deletes the
    /// drained rows. Returns the number of outbox rows processed (0 when the outbox is empty).
    /// </summary>
    public async Task<int> RelayBatchAsync(DbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var outbox = context.Set<CaliberOutboxMessage>();
        var batch = await outbox
            .OrderBy(o => o.CreatedAt)
            .Take(_batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (batch.Count == 0)
        {
            return 0;
        }

        var enabled = await _endpoints.ListEnabledAsync(ct).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        foreach (var row in batch)
        {
            var matched = _matching.Match(row.EventType, enabled);
            if (matched.Count == 0)
            {
                continue; // No subscriber — the row still drains so it cannot be relayed twice.
            }

            var jobs = new List<WebhookMessage>(matched.Count);
            foreach (var endpoint in matched)
            {
                jobs.Add(new WebhookMessage
                {
                    Id = Guid.NewGuid(),
                    EventId = row.Id, // the outbox id is the fan-out idempotency key
                    EndpointId = endpoint.Id,
                    EventType = row.EventType,
                    Payload = row.Payload,
                    CreatedAt = now,
                    NextAttemptAt = now,
                });
            }

            // Idempotent on (event_id, endpoint_id): runs before the outbox delete, so a crash here is safe.
            await _messages.AddAsync(jobs, ct).ConfigureAwait(false);
        }

        outbox.RemoveRange(batch);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return batch.Count;
    }
}
