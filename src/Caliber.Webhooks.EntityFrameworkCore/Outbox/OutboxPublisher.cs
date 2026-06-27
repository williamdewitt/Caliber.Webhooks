using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Caliber.Webhooks.EntityFrameworkCore.Outbox;

/// <summary>
/// The transactional-outbox <see cref="IWebhookPublisher"/>: <c>PublishAsync</c> <em>stages</em> a
/// <see cref="CaliberOutboxMessage"/> into the caller's scoped <typeparamref name="TContext"/> and
/// returns — it does <strong>not</strong> save. The caller's own <c>SaveChangesAsync</c> commits the
/// outbox row atomically with their business data; a background <see cref="RelayHost{TContext}"/> then
/// fans it out into the <c>messages</c> store. No magic <c>SaveChanges</c>: you staged it, your save commits it.
/// </summary>
/// <typeparam name="TContext">The caller's <c>DbContext</c>, carrying the <c>caliber_outbox</c> table.</typeparam>
internal sealed class OutboxPublisher<TContext> : IWebhookPublisher
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _timeProvider;

    public OutboxPublisher(TContext context, CaliberWebhooksOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        _context = context;
        _timeProvider = options.TimeProvider;
    }

    public Task PublishAsync(string eventType, object payload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        _context.Set<CaliberOutboxMessage>().Add(new CaliberOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload, payload.GetType()),
            TraceContext = Activity.Current?.Id,
            CreatedAt = _timeProvider.GetUtcNow(),
        });

        // Staged only — the caller's SaveChangesAsync commits it within their transaction.
        return Task.CompletedTask;
    }
}
