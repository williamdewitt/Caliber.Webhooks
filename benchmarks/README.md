# Caliber.Webhooks — Benchmarks

A [BenchmarkDotNet](https://benchmarkdotnet.org/) harness over the hot code paths — the methods that
run per delivery, per published event, and per dispatcher poll. Every benchmark carries
`[MemoryDiagnoser]`, so each run reports **allocation** alongside throughput; keeping these paths
lean (and allocation-free where they should be) is an explicit goal, not an accident.

This is a developer harness — it is **not shipped** and **not a merge gate** (wall-clock timings on
shared CI runners are too noisy to assert on). It is part of the solution, so the normal CI build
compiles it and guards it against rot. Capture fresh numbers from the **Benchmarks** GitHub Action
(`workflow_dispatch`) or locally.

## What's covered

| Benchmark | Hot path | Why it matters |
|---|---|---|
| `SigningBenchmarks` | `SigningEngine.ComputeSignature` | HMAC-SHA256 runs once per delivery attempt; swept across payload sizes (256 B / 4 KB / 64 KB). |
| `MatchingBenchmarks` | `MatchingEngine.Match` | Endpoint fan-out runs once per published event; swept across 10 / 100 / 1000 endpoints. |
| `RetryBenchmarks` | `RetryEngine.Next` | The retry decision runs per failed attempt — expected allocation-free; this keeps it honest. |
| `MessageStoreClaimBenchmarks` | `InMemoryMessageStore.ClaimDueAsync` | The dispatcher's claim query (`Where/OrderBy/Take` + copy) every poll; swept across 100 / 1000 / 10000 backlog. Surfaces the cost the durable stores (M2) push down into SQL. |

## Running

The harness multi-targets `net8.0;net10.0`, so pick a framework with `-f` (the same code on both
runtimes makes a nice side-by-side when you run each):

```bash
# All benchmarks on .NET 10
dotnet run -c Release -f net10.0 --project benchmarks/Caliber.Webhooks.Benchmarks -- --filter '*'

# One group
dotnet run -c Release -f net10.0 --project benchmarks/Caliber.Webhooks.Benchmarks -- --filter '*Signing*'

# Same code on .NET 8, to compare runtimes
dotnet run -c Release -f net8.0  --project benchmarks/Caliber.Webhooks.Benchmarks -- --filter '*Signing*'

# Fast smoke (validates the harness, not real numbers)
dotnet run -c Release -f net10.0 --project benchmarks/Caliber.Webhooks.Benchmarks -- --filter '*' --job Dry
```

Reports are written to `BenchmarkDotNet.Artifacts/` (markdown, HTML, and CSV).
