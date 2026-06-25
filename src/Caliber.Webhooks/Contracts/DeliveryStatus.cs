namespace Caliber.Webhooks;

/// <summary>
/// The lifecycle state of a <see cref="WebhookMessage"/> as it moves through the delivery loop.
/// </summary>
public enum DeliveryStatus
{
    /// <summary>
    /// The message is awaiting its first delivery or a scheduled retry. This is the initial state
    /// and the state a message returns to between failed attempts that have not yet been exhausted.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The receiver acknowledged the delivery with a success response. This state is terminal.
    /// </summary>
    Delivered = 1,

    /// <summary>
    /// Every attempt was exhausted without success. This state is terminal until an explicit replay
    /// re-arms the message; the final failure is recorded on <see cref="WebhookMessage.LastError"/>.
    /// </summary>
    DeadLettered = 2,
}
