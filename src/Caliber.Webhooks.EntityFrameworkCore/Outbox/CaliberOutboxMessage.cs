namespace Caliber.Webhooks.EntityFrameworkCore.Outbox;

/// <summary>
/// The thin, append-only outbox row staged into the caller's <c>DbContext</c> by <c>PublishAsync</c> in
/// transactional-outbox mode. It carries one logical event; the relay fans it out into Caliber's
/// <c>messages</c> store, one delivery per matching endpoint. <see cref="Id"/> is the stable source-event
/// id and the relay's idempotency key, so re-draining a row never double-enqueues.
/// </summary>
internal sealed class CaliberOutboxMessage
{
    /// <summary>The stable source-event id; the relay's idempotency key.</summary>
    public Guid Id { get; init; }

    /// <summary>The event type, matched against endpoint subscriptions at relay time.</summary>
    public required string EventType { get; init; }

    /// <summary>The serialized event payload, delivered verbatim and covered by the signature.</summary>
    public required string Payload { get; init; }

    /// <summary>The W3C <c>traceparent</c> captured at publish time, or <see langword="null"/>.</summary>
    public string? TraceContext { get; init; }

    /// <summary>The instant the event was staged, as a UTC offset.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
