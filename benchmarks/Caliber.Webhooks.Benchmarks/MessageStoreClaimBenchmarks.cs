using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Caliber.Webhooks.Benchmarks;

/// <summary>
/// The dispatcher's claim path, run every poll cycle. The in-memory store scans, orders, and copies
/// under a lock; this benchmark surfaces how that <c>Where/OrderBy/Take</c> cost — and its allocation —
/// scales with the backlog, motivating the SQL push-down planned for the durable stores (M2).
/// </summary>
[MemoryDiagnoser]
[Config(typeof(PerOperationConfig))]
public class MessageStoreClaimBenchmarks
{
    private const int BatchSize = 50;

    /// <summary>How many pending, already-due messages sit in the store when a batch is claimed.</summary>
    [Params(100, 1000, 10000)]
    public int StoreSize { get; set; }

    private InMemoryMessageStore _store = null!;

    [IterationSetup]
    public void IterationSetup()
    {
        // ClaimDueAsync mutates state, so each measured call runs against a freshly built store.
        _store = new InMemoryMessageStore(TimeProvider.System);
        _store.AddAsync(BuildPending(StoreSize)).GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task<int> ClaimDue()
    {
        var claimed = await _store.ClaimDueAsync(BatchSize, TimeSpan.FromSeconds(60), owner: "bench");
        return claimed.Count;
    }

    private static WebhookMessage[] BuildPending(int count)
    {
        var now = DateTimeOffset.UtcNow;
        var due = now.AddSeconds(-1);
        var messages = new WebhookMessage[count];
        for (var i = 0; i < count; i++)
        {
            messages[i] = new WebhookMessage
            {
                Id = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                EndpointId = Guid.NewGuid(),
                EventType = "order.created",
                Payload = "{}",
                CreatedAt = now,
                Status = DeliveryStatus.Pending,
                NextAttemptAt = due,
            };
        }

        return messages;
    }

    private sealed class PerOperationConfig : ManualConfig
    {
        // One invocation per iteration; [IterationSetup] rebuilds the store between them.
        public PerOperationConfig()
            => AddJob(Job.Default.WithInvocationCount(1).WithUnrollFactor(1));
    }
}
