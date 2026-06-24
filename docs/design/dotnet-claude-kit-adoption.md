---
title: Adopting dotnet-claude-kit into the Caliber.Webhooks flow
description: How the dotnet-claude-kit plugin (agents, commands, skills, Roslyn MCP tools, hooks) is wired into this repo and mapped to Caliber.Webhooks' milestones and self-building loop — and, honestly, which of its opinions we decline because they collide with bedded decisions.
status: living
audience: [human, ai]
tags: [caliber-webhooks, tooling, claude-code, dotnet-claude-kit, workflow, process]
related: [./development-loop.md, ./decisions.md, ./roadmap.md, ../concepts/architecture.md]
updated: 2026-06-24
---

# Adopting dotnet-claude-kit into the Caliber.Webhooks flow

> **Maintainer/process doc** (how we *build* the library), distinct from the product docs. Companion to [the self-building loop](./development-loop.md): the loop governs how a change *merges*; this governs which assistant capabilities we *reach for* to produce it.

## What is wired in, and how it travels

[`dotnet-claude-kit`](https://github.com/codewithmukesh/dotnet-claude-kit) (v0.7.0: 11 agents, 16 commands, 47 skills, 15 Roslyn MCP tools, 7 hooks, 10 rules) is enabled at **repo scope** in [`.claude/settings.json`](../../.claude/settings.json). That single committed file does three things at once:

1. **Travels with the repo** — any clone or contributor gets the kit, not just the maintainer's machine.
2. **Templates the portfolio** — the same `.claude/settings.json` is the copy-paste starting point for the other Caliber projects.
3. **Reaches the CI agent** — the `@claude` agent-in-the-loop ([`claude.yml`](../../.github/workflows/claude.yml)) runs in the checked-out repo, so it inherits the kit's agents, commands, and skills for issue-driven work. *(The Roslyn MCP server additionally needs a .NET runtime in that job to expose its 15 tools — a deliberate follow-up, see [Open items](#open-items).)*

There is nothing to "turn on" per session: the kit is also enabled in the maintainer's user settings, so its hooks and tools are already live locally. This doc exists so the capability is **used deliberately** and its opinions are **reconciled**, not absorbed by accident.

## The mapping — capability → moment in our flow

| When you're… | Reach for | Risk band → gate |
|---|---|---|
| Designing a new volatility area / decomposition | `dotnet-architect` agent + **`idesign` skill** | core → CODEOWNERS |
| Building the outbox relay, fan-out, `messages` store (#1, #2, #6) | `ef-core-specialist` agent + `ef-core` skill; `/migrate` for schema | core → CODEOWNERS |
| Working the **signing / SSRF guard / secret-at-rest** surface (#4) | `security-auditor` agent + `/security-scan`; `security-testing` skill | **critical** → human + linked design |
| Delivery HTTP, retry table + jitter, timeouts (#5) | `httpclient-factory` + `resilience` skills | core |
| Dead-letter + per-attempt OTel history (#3) | `opentelemetry` skill; `performance-analyst` for backoff cost | core |
| Writing tests (xUnit v3) | `test-engineer` agent + `/tdd`; `testing` skill | low |
| **Before opening any PR** | `/verify` (7-phase), then `/code-review` (Roslyn) | — local gate |
| Build is broken | `/build-fix` + `build-error-resolver` agent | — |
| Cleanup pass before PR | `/de-sloppify` + `refactor-cleaner` agent | — |
| Navigating / auditing code, **always** | Roslyn MCP: `get_public_api`, `detect_antipatterns`, `detect_circular_dependencies`, `find_references`, `find_dead_code`, `get_diagnostics` | — |
| Periodic quality audit | `/health-check` (A–F letter grades) | — |

**How this lines up with the loop.** The self-building loop's spine is one `risk:*` classification driving gate + model + effort. The kit slots in cleanly: `/verify` + `/code-review` are the **local** pre-PR equivalent of what `classify.yml` + CI enforce **remotely**; the agent-in-the-loop picks the model from the same routing table; and `get_public_api` guards the `PublicAPI.*.txt` surface that the loop classifies `critical`.

## Adopt / decline — the honest reconciliation

The kit is opinionated. "Adopt as much as fits" means adopting what aligns and **explicitly declining** what collides with decisions already bedded in [`decisions.md`](./decisions.md) (#0–#6). This mirrors the portfolio's *honest-positioning* and *bed-decisions-down* ethos.

### Adopt
- **All 15 Roslyn MCP tools** — pure navigation/quality leverage, zero opinion conflict.
- **All 10 specialist agents** — `security-auditor` and `ef-core-specialist` map directly onto our highest-value surfaces.
- **Workflow commands** — `/verify`, `/code-review`, `/security-scan`, `/tdd`, `/health-check`, `/build-fix`, `/migrate`, `/de-sloppify`, `/plan`.
- **House-aligned skills** — `idesign` (our method), `ef-core`, `opentelemetry`, `modern-csharp`, `testing`, `resilience`, `httpclient-factory`, `messaging`, `error-handling`, `security-scan`.
- **Both always-on hooks** — `pre-bash-guard` (blocks `push --force`, `reset --hard`, unscoped `rm -rf`) and `post-edit-format` (`dotnet format` on edit, matching our `.editorconfig` + warnings-as-errors).

### Decline (and why)
| Kit default | We use instead | Why |
|---|---|---|
| `packages.md`: "`dotnet add` without `--version`" | **Central Package Management** (`Directory.Packages.props`), pinned | Deterministic, reproducible builds are a house standard. |
| `001-vsa-default` / `architecture-advisor` (VSA / Clean Arch) | **iDesign** volatility-based decomposition (kit's `idesign` skill) | Bedded in [`decisions.md`](./decisions.md) #0 + [`../concepts/architecture.md`](../concepts/architecture.md). |
| `testing.md`: "no InMemory DB" | **in-memory + SQLite** providers as first-class | Zero-infra-by-default is a portfolio ethos; it's a *feature*. |
| `002-result-over-exceptions` as a blanket rule | Case-by-case (exceptions + fail-fast options validation, #5) | Not a decision we've made; no blanket mandate. |
| Kit's `ci-cd` / `autonomous-loops` skills as the source of truth | Our own [self-building loop](./development-loop.md) | The loop is already built and is the authority; kit skills are reference only. |

We also keep our own **memory / OKF docs** as the system of record rather than the kit's `instinct-system` / `convention-learner` / `split-memory` skills — those would duplicate (and could drift from) the handoff docs and agent memory.

## Open items

- **Roslyn MCP in CI.** Skills/agents/commands reach the `@claude` agent for free via repo settings; the `cwm-roslyn-navigator` MCP server needs a `dotnet` setup step in `claude.yml` to expose its 15 tools there. Wire it once confirmed the action picks up the repo's plugin settings on a live `@claude` run.

## See also
- [The self-building development loop](./development-loop.md) — how a change merges.
- [Decisions](./decisions.md) · [Roadmap](./roadmap.md) · [Architecture](../concepts/architecture.md) — the bedded decisions this doc reconciles against.
