using BenchmarkDotNet.Attributes;

namespace Caliber.Webhooks.Benchmarks;

/// <summary>
/// The fan-out selection path, run once per published event. Tracks how the cost of choosing matching
/// endpoints scales with the registered-endpoint count (<see cref="MatchingEngine.Match"/>).
/// </summary>
[MemoryDiagnoser]
public class MatchingBenchmarks
{
    private const string EventType = "order.created";

    /// <summary>How many endpoints the event is matched against.</summary>
    [Params(10, 100, 1000)]
    public int EndpointCount { get; set; }

    private MatchingEngine _engine = null!;
    private Endpoint[] _endpoints = null!;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new MatchingEngine();
        _endpoints = new Endpoint[EndpointCount];
        for (var i = 0; i < EndpointCount; i++)
        {
            // A representative mix: subscribe-all, an explicit match, and a non-match; some disabled.
            IReadOnlyList<string>? types = (i % 3) switch
            {
                0 => null,
                1 => ["order.created", "order.paid"],
                _ => ["user.created"],
            };

            _endpoints[i] = new Endpoint
            {
                Id = Guid.NewGuid(),
                Url = "https://example.test/hook",
                Secret = "whsec_dGVzdHNlY3JldA==",
                EventTypes = types,
                Enabled = i % 7 != 0,
            };
        }
    }

    [Benchmark]
    public IReadOnlyList<Endpoint> Match() => _engine.Match(EventType, _endpoints);
}
