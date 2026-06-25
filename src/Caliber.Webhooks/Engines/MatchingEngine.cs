namespace Caliber.Webhooks;

/// <summary>
/// Selects the endpoints an event should fan out to. Stateless across calls; encapsulates the
/// matching/fan-out volatility (V5). v1 implements exact event-type matching with subscribe-all when
/// an endpoint declares no types. Wildcards and payload filters are out of scope.
/// </summary>
internal sealed class MatchingEngine
{
    private readonly StringComparer _eventTypeComparer;

    /// <summary>
    /// Creates an engine that matches event types using the supplied comparer (default
    /// case-sensitive <see cref="StringComparer.Ordinal"/>).
    /// </summary>
    /// <param name="eventTypeComparer">The comparer used to match event-type strings.</param>
    public MatchingEngine(StringComparer? eventTypeComparer = null)
        => _eventTypeComparer = eventTypeComparer ?? StringComparer.Ordinal;

    /// <summary>
    /// Returns the enabled endpoints among <paramref name="endpoints"/> that subscribe to
    /// <paramref name="eventType"/>.
    /// </summary>
    /// <param name="eventType">The event type being published.</param>
    /// <param name="endpoints">The candidate endpoints.</param>
    /// <returns>The matching endpoints, preserving the supplied order.</returns>
    public IReadOnlyList<Endpoint> Match(string eventType, IEnumerable<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(endpoints);

        var matches = new List<Endpoint>();
        foreach (var endpoint in endpoints)
        {
            if (Matches(endpoint, eventType))
            {
                matches.Add(endpoint);
            }
        }

        return matches;
    }

    private bool Matches(Endpoint endpoint, string eventType)
    {
        if (!endpoint.Enabled)
        {
            return false;
        }

        var types = endpoint.EventTypes;
        if (types is null || types.Count == 0)
        {
            return true; // No declared types means subscribe to all.
        }

        return types.Contains(eventType, _eventTypeComparer);
    }
}
