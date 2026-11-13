# 0053 — The first release is a prerelease: 0.1.0-alpha

> **Contract note (ADR-0056, 2026-11-12):** this record was written against a contract outside the repository — a
> private one, that no reader of LUI can know and does not have to. The repository states its own rules since: the
> rules that contract carried for LUI stand in [`AGENTS.md`](../../AGENTS.md) and in the documents of
> [`docs/`](../../docs/README.md). The text below is history and stays as it was written.
> **Note (ADR-0057, 2026-11-12, issue #121):** this record named an external contract that a reader of a public
> library cannot know. The two sentences that named it are restated in the terms of this repository. The decision
> stands as it was written; the text below is history.
>
> **Note (2026-11-13, the release):** the release came, and the line `VersionSuffix` is gone — the version of the
> repository is `0.1.0`, the tag is `v0.1.0`, and the freeze of
> [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) begins with it. The consequences below that
> name the prerelease are the ones of the alpha; the README shows the install line without `--prerelease` now.

- **Status:** Accepted
- **Date:** 2026-11-11
- **Builds on:** [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze begins with the tag),
  [0048](./0048-the-release-is-the-pull-request-gate-the-owner-and-a-key-of-one-hour.md) (the release pipeline),
  [0018](./0018-two-packages-core-and-hosting.md) (two packages, one version)
- **Affects:** `Directory.Build.props` (`VersionSuffix`), `.github/workflows/publish.yml` (the tag check),
  `tools/package-smoke-test.sh` (the version without an argument), `RepositoryLayoutTests`, `CHANGELOG.md`,
  `README.md`, `.agents/release.md`
- **Issue:** #115. The owner's decision of 2026-11-11.

## Context

The epics before the first release are done: #39 (the public API), #64 (production readiness), and #72 (the tests and
the pipelines), except its step #81. #81 and the alignment with the rules of this repository come together in one
later pull request, once those rules are rebuilt.

ADR-0026 makes the promise of a stable API with the tag `v0.1.0`, and ADR-0026 (7) says the first release stays `0.1.0`.
The owner wants the packages on nuget.org to be tried out first, without that promise yet.

The pipeline could not publish such a release:
- the tag was compared with `VersionPrefix` alone, so `v0.1.0-alpha` failed against `0.1.0`;
- the smoke test packed `0.1.0` when it was given no version.

## Decision

**The first release is `0.1.0-alpha`, a prerelease of `0.1.0`.**

- **The repository names the version.** `Directory.Build.props` holds `VersionPrefix` `0.1.0` and `VersionSuffix`
  `alpha`; MSBuild makes them `0.1.0-alpha`, for the packages and for the informational version of the assemblies.
- **The tag check reads both.** `publish.yml` compares the tag with `VersionPrefix`, plus `-VersionSuffix` when there is
  one. The smoke test does the same when it is given no version, so the job of the pull requests packs the version of
  the repository.
- **A prerelease does not start the freeze.** The freeze of ADR-0026 begins with the tag `v0.1.0`, without a suffix.
  Until then the public API may still change, as before the tag: each change with an ADR and an entry in the CHANGELOG.
  A user of the alpha can be broken by the next alpha or by `0.1.0`, and the README says so.
- **The next steps are the same change of one line each.**
  - Another prerelease: `VersionSuffix` `alpha.2`.
  - The release `0.1.0`: the line `VersionSuffix` goes. Then the tag `v0.1.0` is the one the freeze begins with.

## What was measured

- **The tag check**, the block of `publish.yml` run as it stands:

  | Tag | `Directory.Build.props` | Result |
  |---|---|---|
  | `v0.1.0-alpha` | `0.1.0` + `alpha` | accepted |
  | `v0.1.0` | `0.1.0` + `alpha` | refused: "The tag (0.1.0) and Directory.Build.props (0.1.0-alpha) disagree." |
  | `v0.1.0-beta` | `0.1.0` + `alpha` | refused |
  | `v0.1.0` | `0.1.0`, no suffix | accepted |
  | `v0.1.0-alpha` | `0.1.0`, no suffix | refused |

- **The smoke test without an argument:**
  - it packs `Nordmann.LUI.0.1.0-alpha.nupkg` and `Nordmann.LUI.Hosting.0.1.0-alpha.nupkg`;
  - the hosting package pins `id="Nordmann.LUI" version="[0.1.0-alpha]"`;
  - the installed package loads as `Nordmann.LUI 0.1.0-alpha+<commit>`.

## Consequences

- `dotnet add package Nordmann.LUI` without `--prerelease` finds no version until `0.1.0` exists. The README shows the
  line with `--prerelease`.
- The version lives in two properties of one file. `RepositoryLayoutTests` keeps both out of the projects, as it kept
  `VersionPrefix` out before (ADR-0018).
- The CHANGELOG has a section `[0.1.0-alpha]`. The changes on the way to it stand in that section. `0.1.0` gets its own
  section, which lists what changed after the alpha.

## Alternatives

- **The suffix only in the tag, the props unchanged.** The pipeline would pack the version of the tag. But a build of the
  repository would call itself `0.1.0` while nuget.org has `0.1.0-alpha`, and the smoke test of the pull requests would
  test another version than the release. Rejected: the repository names the version, the tag repeats it (ADR-0048).
- **`0.1.0` right away.** Rejected by the owner: the freeze would begin before anyone outside this repository has used
  the API.
- **`0.0.x` versions for the trial.** Rejected: SemVer has prereleases for exactly this, and the version the trial leads
  to is `0.1.0`.
