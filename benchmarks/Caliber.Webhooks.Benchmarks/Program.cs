using BenchmarkDotNet.Running;
using Caliber.Webhooks.Benchmarks;

// Dispatch to a benchmark by name, e.g. `-- --filter '*Signing*'`, or run them all with `-- --filter '*'`.
BenchmarkSwitcher.FromAssembly(typeof(SigningBenchmarks).Assembly).Run(args);
