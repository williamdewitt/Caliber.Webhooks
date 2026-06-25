namespace Caliber.Webhooks;

/// <summary>
/// Owns the deliver-with-retry workflow (UC-3/UC-4): claim a batch of due messages, then for each
/// (bounded by configured concurrency) sign, POST, and record the outcome — mark delivered, reschedule
/// with backoff, or dead-letter once the attempt budget is spent. The stable <c>webhook-id</c> is the
/// message id and never changes across attempts.
/// </summary>
internal sealed class DeliveryManager
{
    private readonly IMessageStore _messages;
    private readonly IEndpointStore _endpoints;
    private readonly SigningEngine _signing;
    private readonly RetryEngine _retry;
    private readonly IDeliveryChannel _channel;
    private readonly CaliberWebhooksOptions _options;
    private readonly string _owner;

    public DeliveryManager(
        IMessageStore messages,
        IEndpointStore endpoints,
        SigningEngine signing,
        RetryEngine retry,
        IDeliveryChannel channel,
        CaliberWebhooksOptions options,
        string? owner = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(signing);
        ArgumentNullException.ThrowIfNull(retry);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);

        _messages = messages;
        _endpoints = endpoints;
        _signing = signing;
        _retry = retry;
        _channel = channel;
        _options = options;
        _owner = owner ?? Guid.NewGuid().ToString("n");
    }

    /// <summary>
    /// Claims and delivers one batch of due messages, returning the number claimed.
    /// </summary>
    public async Task<int> DeliverDueAsync(CancellationToken ct = default)
    {
        var claimed = await _messages
            .ClaimDueAsync(_options.BatchSize, _options.LeaseDuration, _owner, ct)
            .ConfigureAwait(false);
        if (claimed.Count == 0)
        {
            return 0;
        }

        using var throttle = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
        await Task.WhenAll(claimed.Select(message => DeliverOneAsync(message, throttle, ct))).ConfigureAwait(false);
        return claimed.Count;
    }

    private async Task DeliverOneAsync(WebhookMessage message, SemaphoreSlim throttle, CancellationToken ct)
    {
        await throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var attemptsMade = message.AttemptCount + 1;

            var endpoint = await _endpoints.GetAsync(message.EndpointId, ct).ConfigureAwait(false);
            if (endpoint is null)
            {
                await _messages.DeadLetterAsync(message.Id, attemptsMade, "Endpoint no longer exists.", ct).ConfigureAwait(false);
                return;
            }

            var result = await TryDeliverAsync(endpoint, message, ct).ConfigureAwait(false);
            if (result.Succeeded)
            {
                await _messages.MarkDeliveredAsync(message.Id, ct).ConfigureAwait(false);
                return;
            }

            var error = result.Error ?? "Delivery failed.";
            var next = _retry.Next(attemptsMade);
            if (next is null)
            {
                await _messages.DeadLetterAsync(message.Id, attemptsMade, error, ct).ConfigureAwait(false);
            }
            else
            {
                await _messages.RescheduleAsync(message.Id, attemptsMade, next.Value, error, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            throttle.Release();
        }
    }

    private async Task<DeliveryResult> TryDeliverAsync(Endpoint endpoint, WebhookMessage message, CancellationToken ct)
    {
        try
        {
            var headers = _signing.Sign(message.Id, message.Payload, endpoint.Secret);
            return await _channel.SendAsync(endpoint, message, headers, ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A single message's failure must not stop the dispatcher; it is recorded as a delivery error.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return new DeliveryResult(false, null, ex.Message);
        }
    }
}
