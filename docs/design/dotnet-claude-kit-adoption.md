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
3. **Does *not* reach the CI agent on its own.** `claude-code-action` does **not** read this file's plugin config — so the `@claude` agent-in-the-loop ([`claude.yml`](../../.github/workflows/claude.yml)) gets the kit only because the workflow names it **explicitly** via the action's `plugins` inputs. CI now installs the kit's **agents, commands, and skills** that way (a cheap marketplace clone + install); the **Roslyn MCP tools remain deferred** there — they need a .NET runtime + a per-run whole-solution index ([issue #29](https://github.com/williamdewitt/Caliber.Webhooks/issues/29)). This was assumed to be free; it is not. See [Roslyn MCP in CI — verified finding](#roslyn-mcp-and-the-whole-kit-in-ci--verified-finding).

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

## Roslyn MCP (and the whole kit) in CI — verified finding

> **Investigated 2026-06-24 (issue #29), from inside a live `@claude` CI run.** The earlier assumption that "skills/agents/commands reach the `@claude` agent for free via repo settings" is **wrong**. They do not. Here is what actually happens, and the wiring that fixes it.

### AC1 — does the in-CI agent inherit the repo-scoped `.claude/settings.json` plugins? **No.**

Two independent lines of evidence, both conclusive:

1. **Code inspection of `anthropics/claude-code-action@v1`.** The action never reads the repo's `.claude/settings.json` plugin config:
   - `base-action/src/setup-claude-code-settings.ts` writes **`~/.claude/settings.json`** (home scope) by merging only the action's `settings` **input** (`INPUT_SETTINGS`) over what's already there, then force-sets `enableAllProjectMcpServers: true`. Our `claude.yml` passes **no** `settings` input, so nothing from the repo's `extraKnownMarketplaces` / `enabledPlugins` is ever loaded here.
   - `base-action/src/install-plugins.ts` is the **only** thing that runs `claude plugin marketplace add` / `claude plugin install`, and it is driven **exclusively** by the action's `plugins` + `plugin_marketplaces` **inputs** (`INPUT_PLUGINS` / `INPUT_PLUGIN_MARKETPLACES`). With both unset, it logs "No marketplaces specified" / "No plugins specified" and installs nothing.
2. **Empirical, from this very `@claude` run.** The job's `~/.claude/settings.json` contained exactly `{"enableAllProjectMcpServers": true}` — the no-input default the action writes — with **none** of the repo's marketplace/plugin keys merged in. Correspondingly, **no** kit capability was live in the session: none of the kit's agents (`security-auditor`, `ef-core-specialist`, …), none of its 47 skills (`idesign`, `ef-core`, …), and **none** of the 15 `cwm-roslyn-navigator` MCP tools were present.

**Why.** The repo's `.claude/settings.json` is still on disk in the checkout, and `enabledPlugins` only *enables* a plugin from an **already-installed** marketplace — it does not itself clone/fetch one. In a fresh CI checkout the marketplace was never added, so there is nothing to enable. `enableAllProjectMcpServers` only auto-approves project `.mcp.json` servers, **not** plugin-provided MCP servers. **Conclusion: the kit must be wired explicitly via the action's inputs.** (It would help every contributor too, but in CI it is mandatory.)

### Recommended wiring (proposed `claude.yml` change)

This is `.github/workflows/**` → **`risk:core`**, CODEOWNERS-reviewed, never auto-merges. The CI agent (GitHub App token) **cannot** push workflow edits — the `workflows` permission is withheld — so this diff is recorded here for a maintainer to apply by hand.

1. **A .NET setup step**, pinned to `global.json` (the Roslyn MCP server is a .NET process and needs a runtime), added after `Checkout` and before `Run Claude`:

   ```yaml
         # The Roslyn MCP server (cwm-roslyn-navigator, from dotnet-claude-kit) is a
         # .NET process; it needs a runtime to start. Pin to global.json so CI uses the
         # same SDK the library builds against.
         - name: Setup .NET (for the Roslyn MCP server)
           uses: actions/setup-dotnet@v5
           with:
             global-json-file: global.json
   ```

2. **Explicit plugin install** on the `Run Claude` step (the fix for AC1) — the action installs only what these inputs name:

   ```yaml
         - name: Run Claude
           uses: anthropics/claude-code-action@v1
           with:
             claude_code_oauth_token: ${{ secrets.CLAUDE_CODE_OAUTH_TOKEN }}
             anthropic_api_key: ${{ secrets.ANTHROPIC_API_KEY }}
             # The in-CI agent does NOT inherit the repo's .claude/settings.json plugin
             # config — the action installs ONLY plugins named here. Wire the kit
             # explicitly so its Roslyn MCP tools + agents/skills/commands are live in CI.
             plugin_marketplaces: |
               https://github.com/codewithmukesh/dotnet-claude-kit.git
             plugins: |
               dotnet-claude-kit@dotnet-claude-kit
             claude_args: |
               --model ${{ steps.route.outputs.model }}
               --max-turns 30
   ```

   The trusted-actor guard, `risk:* → model` routing, `--max-turns 30` cap, and the `AGENT_PAT` auto-PR step are **unchanged** — only the `Setup .NET` step and the two plugin inputs are added.

3. **Provisioning `cwm-roslyn-navigator` itself — confirm before applying.** The plugin declares its MCP server as `cwm-roslyn-navigator --solution ${workspaceFolder}`, i.e. it expects that command on `PATH`. Whether the plugin **bundles** the binary or expects a host-installed **.NET global tool** could not be confirmed from inside the CI sandbox (outbound network to the plugin repo is blocked there). Before this lands, a maintainer should check the plugin's `.mcp.json` / install docs and, if it is a global tool, add a restore step, e.g.:

   ```yaml
         - name: Install the Roslyn navigator tool   # only if the plugin does NOT bundle it
           run: dotnet tool install --global cwm-roslyn-navigator   # confirm the exact package id
   ```

   Acceptance ("confirm `get_project_graph` is callable in a CI run") can only be closed by a maintainer-applied workflow change plus one observed run, since the agent cannot self-modify `claude.yml`.

### Cold-start cost — and the recommended gate

Turning this on adds, to **every** `@claude` run, the sum of: the plugin marketplace clone + plugin install; `setup-dotnet` (cached, ~5–15 s warm); any `dotnet tool` restore; and — the real cost — the Roslyn MCP **indexing the whole solution** (a full MSBuild/Roslyn workspace load) on first tool call. That last item grows with solution size and is paid *per run*, even for a one-line docs PR that never needs navigation. This cuts against the loop's **"cost control is gate control"** principle.

**Recommendation: gate the kit/Roslyn wiring behind a label** (e.g. only enrich when the work is `risk:core` / `risk:critical`, or behind an explicit `agent:deep` label), rather than paying the index cost on trivial/low work. Mechanically, the existing `Route risk band → model` step already inspects the labels — have it also emit an `enable_roslyn` output and pass the plugin inputs conditionally:

```yaml
        plugin_marketplaces: ${{ steps.route.outputs.enable_roslyn == 'true' && 'https://github.com/codewithmukesh/dotnet-claude-kit.git' || '' }}
        plugins:             ${{ steps.route.outputs.enable_roslyn == 'true' && 'dotnet-claude-kit@dotnet-claude-kit' || '' }}
```

(and guard the `Setup .NET` step with the same `if:`). Today the repo is an M0 shell, so the index cost is small — but the gate keeps it from scaling into every trivial run as the solution grows.

### Status / deferral

This was split MLP-style — wire the cheap half now, defer the expensive half:

- **Landed (maintainer-applied to `claude.yml`).** The `plugin_marketplaces` + `plugins` inputs are on the `Run Claude` step, so the in-CI `@claude` agent now gets the kit's **agents, commands, and skills** on every run — a cheap marketplace clone + install, **no indexing**. This is unconditional (not label-gated): plain-markdown capabilities cost ~nothing, so there's no reason to withhold them from trivial/low runs. The trusted-actor guard, `risk:* → model` routing, `--max-turns 30`, and the `AGENT_PAT` auto-PR step are unchanged.
- **Still deferred — Roslyn MCP server in CI ([issue #29](https://github.com/williamdewitt/Caliber.Webhooks/issues/29) stays open).** The `setup-dotnet` step, provisioning `cwm-roslyn-navigator`, and the live `get_project_graph` confirmation are *not* wired. That path pays the per-run whole-solution index cold-start, so when it lands it should be **label-gated** to `risk:core` / `critical` work — have the `Route` step emit an `enable_roslyn` output and apply the .NET-dependent bits (and the plugin's MCP server) conditionally (snippets above). On today's M0 shell the index would be near-zero value, which is why it waits.

AC1 is **answered** (not inherited; wire explicitly), and the agents/commands/skills criterion is now met in CI. The Roslyn-MCP criteria remain a deliberate, label-gated future step.

## See also
- [The self-building development loop](./development-loop.md) — how a change merges.
- [Decisions](./decisions.md) · [Roadmap](./roadmap.md) · [Architecture](../concepts/architecture.md) — the bedded decisions this doc reconciles against.
