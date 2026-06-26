using BenchmarkDotNet.Attributes;

namespace Caliber.Webhooks.Benchmarks;

/// <summary>
/// The HMAC-SHA256 signing path, run once per delivery attempt. Tracks throughput and allocation as
/// the payload grows (<see cref="SigningEngine.ComputeSignature"/>).
/// </summary>
[MemoryDiagnoser]
public class SigningBenchmarks
{
    /// <summary>Payload size in bytes — small webhook, a few KB, and a 64 KB body near the cap.</summary>
    [Params(256, 4096, 65536)]
    public int PayloadBytes { get; set; }

    private string _id = null!;
    private string _timestamp = null!;
    private string _payload = null!;
    private string _secret = null!;

    [GlobalSetup]
    public void Setup()
    {
        _id = Guid.NewGuid().ToString();
        _timestamp = "1700000000";
        _payload = new string('x', PayloadBytes);
        _secret = WebhookSecret.Generate();
    }

    [Benchmark]
    public string ComputeSignature()
        => SigningEngine.ComputeSignature(_id, _timestamp, _payload, _secret);
}
