# 0047 — The pipelines change only with the repository

- **Status:** Accepted
- **Date:** 2026-11-08
- **Builds on:** [0002](./0002-library-not-an-application.md) (the build wiring is part of the contract),
  [0019](./0019-the-test-tree-three-layers-one-map.md) (the lanes of the tests)
- **Affects:**
  - `global.json` (new: the SDK)
  - `.github/actions/setup/action.yml` (new: the setup of every job)
  - `.github/workflows/ci.yml`, `performance.yml`, `publish.yml`
  - `.github/dependabot.yml` (new)
  - `tests/Nordmann.LUI.Architecture.Tests/Workflow_Rules.cs` (new: the guard), `tools/mutation-check.sh` (two mutations)
  - `docs/testing.md` (the lanes)
- **Issue:** #74 (epic #72)

## Context

The review of the tests (2026-11-08, epic #72) read the pipelines and found a build that could change while the
repository stood still:

- the SDK was `10.0.x` in the CI and whatever the developer had installed — a new SDK brings new analyzer warnings, and
  the warnings are errors here;
- the images were `ubuntu-latest` and `windows-latest`;
- the actions were used by their major tag (`@v4`) — a tag can be moved by whoever owns the action;
- only one job had a timeout: a test that hangs keeps a runner for six hours, the default of GitHub, and leaves nothing
  to read;
- the same four steps (checkout, SDK, cache, restore) stood five times, without a cache of the packages.

And it found work done twice: the job of the parity ran the whole integration layer again, the smoke test waited for a
build it did not use, and the format was checked after every test.

## Decision

**A build changes only through a change of the repository.**

- **`global.json` names the SDK** (`10.0.114`, `rollForward: latestFeature`). The pipelines install exactly that
  version (`setup-dotnet` with `global-json-file`); a developer with a later 10.0 SDK still builds. No workflow names a
  `dotnet-version` of its own.
- **The images are named** — `ubuntu-24.04`, `windows-2025`.
- **The actions are pinned by their commit**, with the version as a comment: `uses: actions/checkout@<40 hex> # v4.4.0`.
- **Dependabot moves the pins** — the actions, the packages of `Directory.Packages.props` and the SDK of `global.json`,
  weekly, each as a pull request that runs every gate. A package of the two shipped packages still needs its ADR before
  the merge (AGENTS.md): Dependabot proposes, it does not decide.
- **Every job has a timeout, and every test run a hang timeout** (`--blame-hang-timeout`): a hang is stopped after ten
  minutes, and its dump is in the artifacts of the run.
- **One setup action** (`.github/actions/setup`): the SDK of `global.json`, the cache of the packages (keyed on
  `global.json`, the props files and the projects), the restore.
- **No work twice:** `Format` is a job of its own and reports in a minute; the parity runs only `NormalizationParity`;
  the smoke test runs beside the build.
- **`ci.yml` can be called** (`workflow_call`): the release runs the gates of every pull request instead of a gate of
  its own (issue #75).

**The guard is a test**, as every rule of this repository: `Workflow_Rules` reads the workflows and the setup action and
fails for an action used by its tag, an image called `latest`, a job without a timeout, a `dotnet test` without a hang
timeout, and a `dotnet-version` beside `global.json`. Two of its rules are mutated in `tools/mutation-check.sh`
(`an-action-pinned-by-its-tag`, `a-job-without-a-timeout`); all five were seen red.

## Consequences

- A pin is a decision that has to be moved — Dependabot does it, and a pull request of Dependabot is read like any
  other. Without it, the pins would mean "never again".
- A developer without a 10.0 SDK of at least `10.0.114` is told so by `dotnet` itself, with the version — instead of a
  build that fails on an analyzer that only the CI has.
- The guard reads YAML line by line, the way these workflows are written (jobs two spaces deep, steps as list items). A
  workflow written in another shape is a change the guard will notice — and that the guard is changed for, on purpose.
- Branch protection is not available for this repository (a private repository of the free plan): no check is
  required by GitHub. The job `The guards bite` keeps the one row of the mutation check anyway — it is what a reader of
  the run looks for.

## Alternatives

| The alternative | Why not |
|---|---|
| Keep `10.0.x` and `-latest` | The build of tomorrow differs from the build of today with no commit in between — the opposite of a reproducible build. |
| Pin by tag (`@v4.4.0`) | A tag can be moved; a commit cannot. The version stays readable as the comment. |
| `rollForward: latestPatch` | A developer with a 10.0 SDK of a later feature band could not build at all. The pipelines are pinned by `setup-dotnet` either way. |
| A YAML package for the guard | A dependency of the tests for five rules that read lines. |
| Renovate instead of Dependabot | Dependabot is built in and knows the three ecosystems here (actions, NuGet with central versions, the SDK). |
