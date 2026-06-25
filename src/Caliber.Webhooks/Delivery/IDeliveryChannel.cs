namespace Caliber.Webhooks;

/// <summary>
/// Sends a signed delivery to an endpoint over the wire. Encapsulates the transport volatility (V6);
/// the SSRF guard and optional resilience handler slot into this pipeline from M3.
/// </summary>
internal interface IDeliveryChannel
{
    /// <summary>
    /// POSTs <paramref name="message"/> to <paramref name="endpoint"/> with the supplied signature
    /// headers, returning the attempt outcome rather than throwing for delivery failures.
    /// </summary>
    Task<DeliveryResult> SendAsync(
        Endpoint endpoint, WebhookMessage message, WebhookSignatureHeaders headers, CancellationToken ct = default);
}
