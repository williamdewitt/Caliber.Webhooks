namespace Caliber.Webhooks;

/// <summary>
/// Atomic, verb-based access to the <c>messages</c> resource. Hides the store-backend volatility (V1)
/// — the in-memory, SQLite, and Postgres providers all implement this one contract.
/// </summary>
internal interface IMessageStore
{
    /// <summary>
    /// Inserts delivery jobs, idempotently on the <c>(EventId, EndpointId)</c> pair so a relay or
    /// publish retry never double-enqueues.
    /// </summary>
    /// <returns>The number of rows actually inserted (duplicates are skipped).</returns>
    Task<int> AddAsync(IReadOnlyCollection<WebhookMessage> messages, CancellationToken ct = default);

    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due, unleased messages for <paramref name="owner"/>,
    /// leasing each for <paramref name="lease"/>. A message is claimable when it is pending, its next
    /// attempt is due, and it is unowned or its lease has lapsed (crash recovery is intrinsic here).
    /// </summary>
    /// <returns>Snapshots of the claimed messages.</returns>
    Task<IReadOnlyList<WebhookMessage>> ClaimDueAsync(
        int batchSize, TimeSpan lease, string owner, CancellationToken ct = default);

    /// <summary>Marks a message delivered (terminal) and releases its lease.</summary>
    Task MarkDeliveredAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>Returns a failed message to pending, records the attempt and error, and reschedules it.</summary>
    Task RescheduleAsync(
        Guid messageId, int attemptCount, DateTimeOffset nextAttemptAt, string error, CancellationToken ct = default);

    /// <summary>Moves a message to the terminal dead-letter state, recording the final attempt and error.</summary>
    Task DeadLetterAsync(Guid messageId, int attemptCount, string error, CancellationToken ct = default);
}
