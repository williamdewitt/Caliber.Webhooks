---
title: Configuration & Defaults
description: The AddCaliberWebhooks options surface — locked v1 defaults, the default retry schedule, and eager fail-fast validation of the crash-safety invariant.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, configuration, options, defaults, retry-schedule]
related: [./delivery-semantics.md, ./storage-and-work-claiming.md, ./security.md, ../reference/public-api.md]
updated: 2026-06-22
---

# Configuration & Defaults

Every knob is configurable on `AddCaliberWebhooks(options)`. The defaults suit the zero-infra side-project on-ramp and are safe for production.

```csharp
builder.Services.AddCaliberWebhooks(options =>
{
    options.UseEntityFramework<AppDbContext>();
    options.RetrySchedule = RetrySchedule.Default;  // backoff over ~27.6h
    options.MaxAttempts   = 12;
});
```

## Locked defaults

| Option | Default | Notes |
|---|---|---|
| `MaxAttempts` | **12** | total attempts before `DeadLettered` |
| `RetrySchedule` | **`RetrySchedule.Default`** | explicit table (below) + jitter; or `.FromDelays(...)` |
| `LeaseDuration` | **60s** | fixed; no renewal in v1 |
| `HttpTimeout` | **10s** | per-attempt; **must be `< LeaseDuration`** (startup-validated) |
| `PollInterval` | **5s** | dispatcher & relay poll cadence (low-latency wakeup → roadmap) |
| `BatchSize` | **50** | messages claimed per poll |
| `MaxConcurrency` | **16** | concurrent deliveries per batch (IO-bound) |
| `MaxPayloadBytes` | **262144 (256 KB)** | outbound cap (see [Security](./security.md#resource-hardening)) |
| `AllowInsecureHttp` | **false** | dev-only opt-in (see [Security](./security.md#ssrf-guard--done-correctly-non-bypassable)) |
| `TimestampTolerance` | **5 min** | receiver replay window (see [Security](./security.md#receiver-side-replay-protection)) |

## Default retry schedule

An explicit, predictable table (operators value predictability for webhooks) with **full jitter (±20%)** per attempt. 11 inter-attempt delays produce 12 attempts:

```
5s · 30s · 2m · 5m · 10m · 30m · 1h · 2h · 4h · 8h · 12h     (≈ 27.6h span)
```

Override with `RetrySchedule.FromDelays(...)`, or extend toward a ~72h upper bound. The relationship between the schedule, `MaxAttempts`, and dead-lettering is described in [Delivery semantics → retry with backoff](./delivery-semantics.md#retry-with-backoff-and-jitter).

## Fail-fast validation

`AddCaliberWebhooks` validates options **eagerly at startup** (`IValidateOptions`). A misconfiguration fails the host build with a clear message — never silently:

- `HttpTimeout < LeaseDuration` — the crash-safety invariant (see [Storage & work-claiming → lease invariant](./storage-and-work-claiming.md#lease--crash-recovery-invariant)).
- `MaxAttempts ≥ 1`, `BatchSize ≥ 1`, `MaxConcurrency ≥ 1`.
- A non-empty retry schedule.

`TimeProvider` is injectable for testable timing (default `TimeProvider.System`).

## See also

- [Delivery semantics](./delivery-semantics.md) — what these knobs govern.
- [Security](./security.md) — payload cap, timestamp tolerance, insecure-HTTP opt-in.
- [Public API](../reference/public-api.md) — the options type at a glance.
