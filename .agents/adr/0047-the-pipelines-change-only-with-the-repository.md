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

- **`global.json` names the SDK exactly** (`10.0.114`, `rollForward: latestPatch`): the feature band `10.0.1xx`, its
  latest patch. The version is `major.minor.patch` — `setup-dotnet` reads it, and a `10.0.x` is turned into the channel
  `10.0`, which pins nothing (review of pull request 83). `rollForward: disable` would fix the patch as well; the
  guard accepts it, and `latestPatch` is the choice because a patch of the SDK carries security fixes. The pipelines install that band (`setup-dotnet` with `global-json-file`), and `dotnet` itself refuses an SDK
  of another band — on the runner and on the machine of a developer alike. No workflow names a `dotnet-version` of its
  own.
- **The images are named** — `ubuntu-24.04`, `windows-2025`.
- **The actions are pinned by their commit**, with the version as a comment: `uses: actions/checkout@<40 hex> # v4.4.0`.
- **Dependabot moves the pins** — the actions, the packages of `Directory.Packages.props` and the SDK of `global.json`,
  weekly, each as a pull request that runs every gate. A package of the two shipped packages still needs its ADR before
  the merge (AGENTS.md): Dependabot proposes, it does not decide.
- **Every job has a timeout, and every test run a hang timeout with a full dump**
  (`--blame-hang-timeout … --blame-hang-dump-type full --results-directory artifacts/test-results`): a hang is stopped —
  after ten minutes in the CI and the release, after sixty in the lane of the budgets, whose scenario takes minutes —
  and **every job that tests uploads `artifacts/test-results`**, so that the dump is in the artifacts of the run. The
  type of the dump is named on purpose: its default differs between machines — the review of pull request 83 measured
  a run that announced its dump and wrote none, and this session measured the same command writing 319 MB. Named, it
  is written on both. A full dump is about 300 MB; it is uploaded only where a test ran, and lives 14 days.
- **One setup action** (`.github/actions/setup`): the SDK of `global.json`, the cache of the packages (keyed on
  `global.json`, the props files and the projects), the restore.
- **No work twice:** `Format` is a job of its own and reports in a minute; the parity runs only `NormalizationParity`;
  the smoke test runs beside the build.
- **`ci.yml` can be called** (`workflow_call`): the release runs the gates of every pull request instead of a gate of
  its own (issue #75).

**The guard is a test**, as every rule of this repository: `Workflow_Rules` reads the workflows (`*.yml` and `*.yaml`)
and the setup action and fails for an action used by its tag, an image called `latest`, a job without a timeout, a
`dotnet test` without the hang timeout and the type of its dump, a job that tests and uploads nothing, a
`dotnet-version` beside `global.json`, a setup action that reads another file, a version of `global.json` that is not
exact, and a roll-forward across bands. Every rule was seen red; three are mutated in `tools/mutation-check.sh`
(`an-action-pinned-by-its-tag`, `a-job-without-a-timeout`, `the-sdk-version-is-a-channel`).

**What the guard cannot see:** it checks the *shape* of a pin — forty hex characters and a version — not that the
commit belongs to the version of the comment. A wrong commit fails loudly (GitHub does not know it); a wrong comment
beside a right commit would pass. Dependabot writes both together, and a pin written by hand is checked against the
tag of the action (`git ls-remote`) when it is written.

## Consequences

- A pin is a decision that has to be moved — Dependabot does it, and a pull request of Dependabot is read like any
  other. Without it, the pins would mean "never again".
- A developer needs an SDK of the band `10.0.1xx` (at least `10.0.114`); `dotnet` says so itself, with the version —
  instead of a build that fails on an analyzer that only the CI has. A developer with only a later band installs the
  `10.0.1xx` SDK beside it (SDKs live side by side), or the band moves for everyone, through `global.json`.
- **What the first run taught** (the run of pull request #83): with `rollForward: latestFeature` the pin was no pin.
  `setup-dotnet` turned it into the channel `10.0`, found the SDK `10.0.403` that the image carries, and `dotnet` rolled
  forward to it — the CI built with `10.0.403` while `global.json` said `10.0.114`. `latestPatch` keeps the band; the
  guard refuses a roll-forward across bands (`The_sdk_is_the_one_of_global_json`).
- The guard reads YAML line by line, the way these workflows are written (jobs two spaces deep, steps as list items). A
  workflow written in another shape is a change the guard will notice — and that the guard is changed for, on purpose.
- Branch protection is not available for this repository (a private repository of the free plan): no check is
  required by GitHub. The job `The guards bite` keeps the one row of the mutation check anyway — it is what a reader of
  the run looks for.

## Alternatives

| The alternative | Why not |
|---|---|
| Keep `10.0.x` and `-latest` | The build of tomorrow differs from the build of today with no commit in between — the opposite of a reproducible build. |
| Leave the dump type to its default | Measured both ways in the review of pull request 83: on one machine the dump was announced and not written. A default that only the help text knows is not a default. |
| Pin by tag (`@v4.4.0`) | A tag can be moved; a commit cannot. The version stays readable as the comment. |
| `rollForward: latestFeature` (the first version of this decision) | Measured in the first run: the runner image carries a later band (`10.0.403`), and `dotnet` rolls forward to it — the pin pins nothing. A developer with a later band is the price of a pin that holds. |
| A YAML package for the guard | A dependency of the tests for five rules that read lines. |
| Renovate instead of Dependabot | Dependabot is built in and knows the three ecosystems here (actions, NuGet with central versions, the SDK). |

## Note (2026-11-09, issue #91)

The repository is public since 2026-11-09, and branch protection is now available to it (the note above on
branch protection was written for the private repository).

Every `curl` of a pipeline retries a transient error: `curl --retry 5 --retry-delay 10`. curl counts 408, 429, 500,
502, 503 and 504 as transient. On failure it writes the status and the address that answered
(`-w '%{http_code} %{url_effective}'`). The download of actionlint met a 504 three times in a row on pull request 90
(07:25, 07:32, 08:06), and the job died before actionlint ran. The log did not say which host sent it: after `-L` it
can be github.com or the host of the release assets. That is what the `-w` is for.

Two guards hold the rule, and they read **commands**, not lines. A command goes on over a trailing backslash, and `curl`
may stand in `$(…)`, after a pipe or in quotes (review of pull request 92):
- `Every_download_of_a_pipeline_retries_a_transient_error` asks for `--retry`.
- `Every_download_of_a_pipeline_says_who_answered` asks for `%{http_code}` and `%{url_effective}`.

They hold `curl` and no other way of downloading: today `curl` is the only one in the pipelines. The mutations
`a-download-without-a-retry`, `a-download-in-a-substitution-without-a-retry` and `a-download-that-does-not-say-who-answered`
show that they bite.

## Note (2026-11-09, the open updates of Dependabot)

The pins moved for the first time, through the pull requests of Dependabot (#84, #85, #97), taken in one pull request:
- **The SDK band moves for everyone:** `global.json` names `10.0.403`, the newest SDK (owner's decision, 2026-11-09). A
  developer now needs an SDK of the band `10.0.4xx`, at least `10.0.403`; the consequence above that names `10.0.1xx`
  is the history of the first pin. The runner image carries `10.0.403` already.
- **The actions on their current majors:** `checkout` 7.0.1, `setup-node` 7.1.0, `upload-artifact` 7.0.2. All three run
  on Node 24 and need a runner of at least 2.327.1, which the hosted images are. The warning of the runs ("Node.js 20
  is deprecated … forced to run on Node.js 24") is gone with them.
- **Dependabot reads the composite action too.** `directory: /` reads `.github/workflows` only, so the pins of
  `setup-dotnet` and `cache` in `.github/actions/setup` stood still. The configuration names
  `/.github/actions/*` beside `/`.
- **The mutations name no pin.** Two mutations named the commit of `checkout` and the version of the SDK. After the
  first update they would have changed nothing, and the check would have failed for a reason that is not a rule. Their
  patterns now match any commit and any version.
