namespace Caliber.Webhooks;

/// <summary>
/// A non-durable, single-process <see cref="IMessageStore"/> for tests and local development. All
/// operations are serialized by one lock, which is sufficient — and correct — for the in-memory tier.
/// </summary>
internal sealed class InMemoryMessageStore : IMessageStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, WebhookMessage> _byId = [];
    private readonly HashSet<(Guid EventId, Guid EndpointId)> _fanOutKeys = [];
    private readonly TimeProvider _timeProvider;

    public InMemoryMessageStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public Task<int> AddAsync(IReadOnlyCollection<WebhookMessage> messages, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var added = 0;
        lock (_gate)
        {
            foreach (var message in messages)
            {
                if (_fanOutKeys.Add((message.EventId, message.EndpointId)))
                {
                    _byId[message.Id] = message;
                    added++;
                }
            }
        }

        return Task.FromResult(added);
    }

    public Task<IReadOnlyList<WebhookMessage>> ClaimDueAsync(
        int batchSize, TimeSpan lease, string owner, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentException.ThrowIfNullOrEmpty(owner);

        var now = _timeProvider.GetUtcNow();
        var leaseUntil = now + lease;
        var claimed = new List<WebhookMessage>();

        lock (_gate)
        {
            var due = _byId.Values
                .Where(m => m.Status == DeliveryStatus.Pending
                    && m.NextAttemptAt <= now
                    && (m.Owner is null || m.LeaseUntil < now))
                .OrderBy(m => m.NextAttemptAt)
                .Take(batchSize)
                .ToList();

            foreach (var message in due)
            {
                message.Owner = owner;
                message.LeaseUntil = leaseUntil;
                claimed.Add(Copy(message));
            }
        }

        return Task.FromResult<IReadOnlyList<WebhookMessage>>(claimed);
    }

    public Task MarkDeliveredAsync(Guid messageId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(messageId, out var message))
            {
                message.Status = DeliveryStatus.Delivered;
                ReleaseLease(message);
            }
        }

        return Task.CompletedTask;
    }

    public Task RescheduleAsync(
        Guid messageId, int attemptCount, DateTimeOffset nextAttemptAt, string error, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(messageId, out var message))
            {
                message.Status = DeliveryStatus.Pending;
                message.AttemptCount = attemptCount;
                message.NextAttemptAt = nextAttemptAt;
                message.LastError = error;
                ReleaseLease(message);
            }
        }

        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(Guid messageId, int attemptCount, string error, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(messageId, out var message))
            {
                message.Status = DeliveryStatus.DeadLettered;
                message.AttemptCount = attemptCount;
                message.LastError = error;
                ReleaseLease(message);
            }
        }

        return Task.CompletedTask;
    }

    private static void ReleaseLease(WebhookMessage message)
    {
        message.Owner = null;
        message.LeaseUntil = null;
    }

    private static WebhookMessage Copy(WebhookMessage m) => new()
    {
        Id = m.Id,
        EventId = m.EventId,
        EndpointId = m.EndpointId,
        EventType = m.EventType,
        Payload = m.Payload,
        CreatedAt = m.CreatedAt,
        Status = m.Status,
        AttemptCount = m.AttemptCount,
        NextAttemptAt = m.NextAttemptAt,
        Owner = m.Owner,
        LeaseUntil = m.LeaseUntil,
        LastError = m.LastError,
    };
}
