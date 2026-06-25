namespace Caliber.Webhooks;

/// <summary>
/// A single logical event captured at publish time, before fan-out to endpoints. One
/// <see cref="WebhookEvent"/> may produce many <see cref="WebhookMessage"/> deliveries — exactly one
/// per matching <see cref="Endpoint"/>.
/// </summary>
public sealed class WebhookEvent
{
    /// <summary>
    /// The stable source-event identifier. It is the relay's idempotency key: re-processing the same
    /// event never fans out twice.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The free-form event type — for example <c>order.shipped</c> — matched against endpoint
    /// subscriptions. There is no pre-registered catalog in v1.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// The serialized event payload, delivered verbatim to receivers and covered by the signature.
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    /// The instant the event was captured, as a UTC offset.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
