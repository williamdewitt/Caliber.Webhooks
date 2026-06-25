namespace Caliber.Webhooks;

/// <summary>
/// An ordered set of inter-attempt delays that spaces out webhook delivery retries. The schedule
/// supplies the <em>base</em> delays; per-attempt jitter is applied by the retry engine.
/// </summary>
public sealed class RetrySchedule
{
    private static readonly TimeSpan[] DefaultDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(8),
        TimeSpan.FromHours(12),
    ];

    private readonly TimeSpan[] _delays;

    private RetrySchedule(TimeSpan[] delays) => _delays = delays;

    /// <summary>
    /// The default schedule — <c>5s, 30s, 2m, 5m, 10m, 30m, 1h, 2h, 4h, 8h, 12h</c>: eleven delays
    /// giving twelve attempts over roughly a day before dead-lettering. Operators value a
    /// predictable table, so this is an explicit list rather than a formula.
    /// </summary>
    public static RetrySchedule Default { get; } = new((TimeSpan[])DefaultDelays.Clone());

    /// <summary>
    /// The ordered inter-attempt delays. The delay before attempt <c>n + 1</c> (one-based) is
    /// <c>Delays[n - 1]</c>; once the delays are exhausted the message is dead-lettered.
    /// </summary>
    public IReadOnlyList<TimeSpan> Delays => _delays;

    /// <summary>
    /// Builds a schedule from an explicit, ordered set of inter-attempt delays.
    /// </summary>
    /// <param name="delays">One or more non-negative delays, in attempt order.</param>
    /// <returns>A schedule over a private copy of <paramref name="delays"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="delays"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="delays"/> is empty or contains a negative delay.</exception>
    public static RetrySchedule FromDelays(params TimeSpan[] delays)
    {
        ArgumentNullException.ThrowIfNull(delays);
        if (delays.Length == 0)
        {
            throw new ArgumentException("A retry schedule needs at least one delay.", nameof(delays));
        }

        foreach (var delay in delays)
        {
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentException("Retry delays must be non-negative.", nameof(delays));
            }
        }

        return new RetrySchedule((TimeSpan[])delays.Clone());
    }
}
