namespace Caliber.Webhooks;

/// <summary>
/// A single delivery job: one event addressed to one endpoint. The <see cref="Id"/> is the stable
/// Standard Webhooks <c>webhook-id</c>, preserved across retries and replay so receivers can dedupe.
/// </summary>
/// <remarks>
/// Identity and payload fields are set once at creation; the remaining fields capture mutable
/// delivery state advanced by the dispatcher as attempts are made.
/// </remarks>
public sealed class WebhookMessage
{
    /// <summary>
    /// The stable per-delivery identifier — the Standard Webhooks <c>webhook-id</c>. It is unchanged
    /// across retries and replay, and is what a receiver dedupes on.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The <see cref="WebhookEvent.Id"/> this delivery fanned out from. The pair
    /// (<see cref="EventId"/>, <see cref="EndpointId"/>) is unique, which makes fan-out idempotent.
    /// </summary>
    public required Guid EventId { get; init; }

    /// <summary>
    /// The <see cref="Endpoint.Id"/> this delivery targets.
    /// </summary>
    public required Guid EndpointId { get; init; }

    /// <summary>
    /// The event type carried by this delivery.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// The serialized payload delivered to the endpoint and covered by the signature.
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    /// The instant this delivery job was created, as a UTC offset.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// The current lifecycle state. New messages start <see cref="DeliveryStatus.Pending"/>.
    /// </summary>
    public DeliveryStatus Status { get; set; }

    /// <summary>
    /// The number of delivery attempts made so far.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// The earliest instant the next attempt may run, as a UTC offset.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>
    /// The claim token of the dispatcher instance currently leasing this message, or
    /// <see langword="null"/> when the message is unclaimed.
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>
    /// The instant the current claim lease expires, after which the message becomes reclaimable, or
    /// <see langword="null"/> when the message is unclaimed.
    /// </summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    /// <summary>
    /// The error detail from the most recent failed attempt, or <see langword="null"/> when no
    /// attempt has failed.
    /// </summary>
    public string? LastError { get; set; }
}
