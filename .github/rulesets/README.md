# Branch ruleset (`main`)

`main.json` is the branch-protection ruleset for `main`, kept in-repo so it is
reviewable and applied in one command. It **requires GitHub Pro or a public
repository** — the API returns `403` otherwise — so it is not applied while this
repo is private on a free plan. (Code scanning and native auto-merge are gated the
same way; see `../../docs/design/development-loop.md`.)

## What it enforces

- A pull request is required to update `main` (no direct pushes); branch deletion
  and non-fast-forward pushes are blocked.
- **Code-owner review** on protected paths (`CODEOWNERS`: `src/Caliber.Webhooks/**`,
  public-API files, migrations). Non-owned paths (docs, CI, tests) need **0**
  approvals, so `risk:trivial` PRs can auto-merge on green.
- The CI status check **`build · test · pack`** must pass (and be up to date with
  `main`) before merge.
- **Bypass:** the repository admin role (`actor_id: 5`) may bypass — the emergency
  valve for a solo maintainer (GitHub forbids approving your own PR). The in-CI
  agent runs as a non-admin actor, so it is **never** a bypass actor.

## Apply it (once the repo is public or on Pro)

```bash
gh api -X POST repos/williamdewitt/Caliber.Webhooks/rulesets --input .github/rulesets/main.json
# update later with the ruleset id:
# gh api -X PUT repos/williamdewitt/Caliber.Webhooks/rulesets/<id> --input .github/rulesets/main.json
```

Or import it in the UI: **Settings → Rules → Rulesets → New ruleset → Import**.

After applying, also enable native auto-merge (also gated on public/Pro):

```bash
gh api -X PATCH repos/williamdewitt/Caliber.Webhooks -F allow_auto_merge=true
```

> Verify the required check context still reads exactly `build · test · pack`
> (the `ci.yml` job name) after the first PR runs; rename here if the job is renamed.
