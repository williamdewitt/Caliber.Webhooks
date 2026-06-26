using System.Text.Json;

namespace Caliber.Webhooks;

/// <summary>
/// Owns the publish workflow (UC-1) and, in M1's standalone mode, the inline fan-out (UC-2): it
/// serializes the payload, matches enabled endpoints, and writes one delivery job per match. The
/// outbox-drain relay arrives in M2 behind this same manager.
/// </summary>
internal sealed class IngestionManager : IWebhookPublisher
{
    private readonly IEndpointStore _endpoints;
    private readonly IMessageStore _messages;
    private readonly MatchingEngine _matching;
    private readonly TimeProvider _timeProvider;

    public IngestionManager(
        IEndpointStore endpoints, IMessageStore messages, MatchingEngine matching, CaliberWebhooksOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(matching);
        ArgumentNullException.ThrowIfNull(options);

        _endpoints = endpoints;
        _messages = messages;
        _matching = matching;
        _timeProvider = options.TimeProvider;
    }

    public async Task PublishAsync(string eventType, object payload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        var enabled = await _endpoints.ListEnabledAsync(ct).ConfigureAwait(false);
        var matched = _matching.Match(eventType, enabled);
        // Stryker disable once all : equivalent — fast-path return; with no matches the fan-out loop below produces nothing anyway.
        if (matched.Count == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var eventId = Guid.NewGuid();
        var serialized = JsonSerializer.Serialize(payload, payload.GetType());

        var jobs = new List<WebhookMessage>(matched.Count);
        foreach (var endpoint in matched)
        {
            jobs.Add(new WebhookMessage
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                EndpointId = endpoint.Id,
                EventType = eventType,
                Payload = serialized,
                CreatedAt = now,
                NextAttemptAt = now,
            });
        }

        await _messages.AddAsync(jobs, ct).ConfigureAwait(false);
    }
}
