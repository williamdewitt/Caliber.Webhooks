---
title: Security
description: Caliber.Webhooks' v1 security scope — Standard Webhooks HMAC-SHA256 signing, a non-bypassable SSRF guard with connect-time anti-rebinding, payload/response caps, TLS, secret-at-rest encryption, and receiver-side replay protection.
status: pre-release
audience: [human, ai]
tags: [caliber-webhooks, security, signing, ssrf, hmac, standard-webhooks, replay-protection]
related: [./endpoints-and-matching.md, ./delivery-semantics.md, ./configuration.md, ../design/decisions.md]
updated: 2026-06-22
---

# Security

Signing is the product thesis, and SSRF is a real liability when customers supply URLs — both must be **correct in v1**, not gestured at. A *bypassable* SSRF guard would be worse than an honest "not yet," so v1 does SSRF properly; deeper hardening (rotation, ed25519, mTLS) is on the roadmap.

## Signing (Standard Webhooks)

Caliber.Webhooks signs every delivery with **HMAC-SHA256** per the [Standard Webhooks](https://www.standardwebhooks.com/) spec — the same scheme major API providers use, so receivers interoperate instantly.

- Headers: `webhook-id`, `webhook-timestamp`, `webhook-signature`.
- Signature is computed over `{id}.{timestamp}.{payload}` using the endpoint's `whsec_…` secret (the `SigningEngine`).

The `SigningEngine` contract abstracts the algorithm, so **ed25519** asymmetric signatures and **multi-secret rotation** drop in behind it without a breaking change (both roadmap).

## SSRF guard — done correctly (non-bypassable)

Because customers supply endpoint URLs, the delivery client (`SsrfGuardEngine`) defends against server-side request forgery at the level that actually holds:

- **HTTPS-only** by default — `http://`, `file://`, and other schemes are rejected. An explicit `AllowInsecureHttp` opt-in exists for loopback dev only.
- **IP filtering** of *every resolved address* — RFC1918 private, loopback, link-local (`169.254/16`, `fe80::/10`), unique-local (`fc00::/7`), cloud-metadata (`169.254.169.254`, `fd00:ec2::254`), broadcast/multicast/reserved, and `0.0.0.0/8`.
- **Connect-time validation (anti-DNS-rebinding).** The check runs at socket-connect (`SocketsHttpHandler.ConnectCallback`), so the *validated* IP is the *connected* IP. A TOCTOU rebind that re-resolves to an internal address after a passing pre-flight check is defeated. *(A pre-flight DNS check alone is bypassable; the library does not ship the bypassable version.)*
- **No auto-redirect.** Redirects are disabled on the delivery client — a 3xx to an internal URL would be an SSRF bypass. A 3xx is recorded as a delivery outcome instead.
- **Policy seam** for host allow/deny overrides (e.g. whitelisting a known internal test endpoint), with safe defaults out of the box.

## Resource hardening

- **Outbound payload cap** — configurable (default ~256 KB); oversized payloads are rejected at publish with a clear error.
- **Response-body read cap** — at most a few KB of the receiver's response is read (for `last_error`), so a malicious receiver cannot stream unbounded data into the dispatcher.
- **TLS verification on** — standard certificate validation; there is no convenience off-switch.
- **Per-attempt timeout** kept strictly `< lease` — both a delivery-correctness invariant and resource hardening (see [Storage & work-claiming → lease invariant](./storage-and-work-claiming.md#lease--crash-recovery-invariant)).

## Secret-at-rest

Signing needs the raw secret, so hashing is impossible — the choice is plaintext vs. encrypted. The v1 default:

- **Encrypt at rest using the host's `IDataProtectionProvider` when one is registered** — automatic and zero-config in any ASP.NET Core app (the common case).
- **Otherwise store as-is with a one-time startup warning** recommending `AddDataProtection()` with persisted, shared keys.
- Opt out via `SecretProtection.None`; a custom `ISecretProtector` seam is the roadmap extension.

> **Caveat:** multi-instance deployments must share/persist the Data Protection key ring so every instance can decrypt (standard ASP.NET guidance). The design is encryption-by-default where it's free, and **honest** — it warns rather than silently claiming protection it isn't providing — where it isn't.

## Receiver-side replay protection

The verify helper (`VerificationEngine`, behind `WebhookVerifier`) recomputes the signature, compares it in **constant time**, and **rejects a stale `webhook-timestamp`** outside a tolerance window (default 5 minutes):

```csharp
var result = WebhookVerifier.Verify(request.Headers, rawBody, secret);
```

## Deferred to the roadmap

Secret rotation (multiple active secrets per endpoint during overlap windows), ed25519 asymmetric signatures, mTLS / client certificates, per-endpoint rate-limiting & circuit-breaking (the optional `Caliber.Webhooks.Resilience` package), and org-wide IP allowlists beyond the policy seam. Their seams (`SigningEngine`, the endpoint `secret`, the SSRF policy hook) already accommodate them without breaking changes.

> **The bar:** HMAC + a *non-bypassable* SSRF guard + payload/response caps + no-redirect is the table-stakes security a production-grade claim must back. The rest is real but additive.

## See also

- [Endpoints & matching](./endpoints-and-matching.md) — where endpoint secrets live.
- [Configuration](./configuration.md) — payload cap, timestamp tolerance, `AllowInsecureHttp`.
- [Decisions](../design/decisions.md) — decision #4 (security scope) in full.
