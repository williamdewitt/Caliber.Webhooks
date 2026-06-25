namespace Caliber.Webhooks;

/// <summary>
/// Publishes events for reliable, at-least-once delivery to subscribed endpoints. Resolve this from
/// dependency injection in your domain code.
/// </summary>
public interface IWebhookPublisher
{
    /// <summary>
    /// Publishes an event. The payload is serialized to JSON, fanned out to every matching endpoint,
    /// and delivered in the background — the call never makes the outbound HTTP request itself.
    /// </summary>
    /// <param name="eventType">The event type, matched against endpoint subscriptions.</param>
    /// <param name="payload">The event payload, serialized to JSON.</param>
    /// <param name="ct">A token to cancel the enqueue.</param>
    Task PublishAsync(string eventType, object payload, CancellationToken ct = default);
}
