namespace Caliber.Webhooks;

/// <summary>
/// Decides when — or whether — a failed delivery should be retried. Stateless and deterministic
/// given the configured clock and jitter source; encapsulates the retry/backoff volatility (V4).
/// </summary>
internal sealed class RetryEngine
{
    private const double JitterFraction = 0.2;

    private readonly RetrySchedule _schedule;
    private readonly int _maxAttempts;
    private readonly TimeProvider _timeProvider;
    private readonly Func<double> _sampleUnitInterval;

    /// <summary>
    /// Creates a retry engine from the configured options.
    /// </summary>
    /// <param name="options">The schedule, attempt cap, and clock to use.</param>
    /// <param name="sampleUnitInterval">
    /// A jitter source returning a value in <c>[0, 1)</c>. Defaults to a shared thread-safe RNG;
    /// tests inject a deterministic sample.
    /// </param>
    public RetryEngine(CaliberWebhooksOptions options, Func<double>? sampleUnitInterval = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _schedule = options.RetrySchedule;
        _maxAttempts = options.MaxAttempts;
        _timeProvider = options.TimeProvider;
        _sampleUnitInterval = sampleUnitInterval ?? Random.Shared.NextDouble;
    }

    /// <summary>
    /// Decides the next attempt time after <paramref name="attemptsMade"/> failed attempts.
    /// </summary>
    /// <param name="attemptsMade">The number of attempts already made; at least <c>1</c>.</param>
    /// <returns>
    /// The UTC instant of the next attempt, or <see langword="null"/> when the attempt budget or the
    /// schedule is exhausted and the message should be dead-lettered.
    /// </returns>
    public DateTimeOffset? Next(int attemptsMade)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptsMade, 1);

        if (attemptsMade >= _maxAttempts)
        {
            return null;
        }

        var delayIndex = attemptsMade - 1;
        if (delayIndex >= _schedule.Delays.Count)
        {
            return null;
        }

        return _timeProvider.GetUtcNow() + ApplyJitter(_schedule.Delays[delayIndex]);
    }

    private TimeSpan ApplyJitter(TimeSpan baseDelay)
    {
        // Sample in [0,1) maps to a multiplier in [1 - JitterFraction, 1 + JitterFraction).
        var factor = 1.0 + (((_sampleUnitInterval() * 2.0) - 1.0) * JitterFraction);
        return baseDelay * factor;
    }
}
