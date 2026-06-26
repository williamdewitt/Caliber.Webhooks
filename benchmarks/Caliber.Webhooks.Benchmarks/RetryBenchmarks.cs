using BenchmarkDotNet.Attributes;

namespace Caliber.Webhooks.Benchmarks;

/// <summary>
/// The retry-scheduling decision, run once per failed attempt. Expected to be allocation-free — this
/// benchmark exists to keep it that way (<see cref="RetryEngine.Next"/>).
/// </summary>
[MemoryDiagnoser]
public class RetryBenchmarks
{
    private RetryEngine _engine = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = new CaliberWebhooksOptions();
        // Deterministic jitter so the benchmark measures the engine, not the RNG.
        _engine = new RetryEngine(options, () => 0.5);
    }

    [Benchmark]
    public DateTimeOffset? Next() => _engine.Next(attemptsMade: 3);
}
