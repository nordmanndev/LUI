# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** the release of `0.1.0` — the `dev` → `main` pull request, the tag `v0.1.0` and the push to nuget.org
(ADR-0048, ADR-0053, ADR-0026).
**Branch:** `release/0.1.0`, from `dev` at `3c45920`. The version line, the CHANGELOG section and the README status are
on it; the pull request into `dev` comes first, the release pull request after that.
**Decision:** none — the release is decided (ADR-0048 the gate, ADR-0053 the version, ADR-0026 the freeze). What is
left is the owner's approval of the release pull request and of the push to nuget.org.

## What is done

- **The series is complete**: steps 1 to 8 of epic #117 are merged into `dev` (PRs #126, #127, #131, #135, #136, #138,
  #139, #141), and the residue of the German is gone (PR #143, `9d5ca7a`).
- **The route is named in the call** (PR #146, `d0435b0`): `LuiCatalog.Create` creates an embedded index,
  `LuiCatalog.CreateLinked` a linked one.
- **The surface of 0.1.0 is written down against the review of 2026-11-04** (PR #147, `87578de`).
- **The warm-up leaves nothing running** (PR #149, `715c121`): the stop and the disposal wait for a round that runs.
- **The chapter exists** (PR #150, `3c45920`): `docs/testing-strategy.md` — the layers, the techniques and their reason,
  what is deliberately not done, and what a new test brings with it.
- **The version of the release** (branch `release/0.1.0`): `VersionPrefix` `0.1.0` without a suffix, the CHANGELOG
  section `## [0.1.0] — 2026-11-13`, the README without `--prerelease` and a Status that states the freeze.

## Open

- **The release pull request `dev` → `main`** — it carries its own review (`AGENTS.md`), and the owner asks for it.
- **The tag `v0.1.0`** on the commit of `main`, and then the push to nuget.org: the owner approves the deployment of
  the environment `nuget` in the GitHub UI (`.agents/release.md`).
- **The epics #39, #64 and #72** are closed with the release — they are the epics of the first release.

## The next step

The **release pull request**, the **tag**, the **approval** — and then the backlog of the open issues: the three guards
that the work found (#128, #129, #130), the two open bugs of the measurement (#134, #137), and the three that wait for
after 0.1.0 (#31, #32, #44).
