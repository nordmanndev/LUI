# 0048 — The release: the gates of every pull request, the owner's approval, a key of one hour

- **Status:** Accepted
- **Date:** 2026-11-08
- **Builds on:** [0018](./0018-two-packages-core-and-hosting.md) (two packages, one version),
  [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze from the tag on),
  [0047](./0047-the-pipelines-change-only-with-the-repository.md) (the pipelines change only with the repository)
- **Affects:**
  - `.github/workflows/publish.yml` (rewritten)
  - `tests/Nordmann.LUI.Architecture.Tests/Workflow_Rules.cs` (the guard of the release), `tools/mutation-check.sh`
  - `.agents/release.md` (new: what the owner does once, and how a release is made), `docs/testing.md`
- **Issue:** #75 (epic #72) — the owner's decision of 2026-11-08: the repository becomes public before the first release

## Context

The release workflow had a gate of its own, and it was **weaker** than the gate of a pull request: no Windows, no
mutation check, no client of the sample — and nothing checked that the tagged commit was on `main`. A release could
publish what no pull request would have let through. It pushed with a long-lived `NUGET_API_KEY` that lived in the
secrets of the repository, and nobody approved the push. A `workflow_dispatch` without a tag could only fail.

nuget.org offers **Trusted Publishing**: the workflow asks GitHub for an OIDC token, nuget.org checks it against a
policy (owner, repository, workflow file, environment) and issues an API key that lives **one hour**. Nothing is stored.

GitHub **environments** with a required reviewer are free for public repositories; for a private one they need a paid
plan, and a required reviewer in a private repository needs GitHub Enterprise. The repository was private on the free
plan. The owner decided that it becomes public before the first release — which also lets SourceLink show the source to
the users of the package.

## Decision

**The release is three jobs, and the push waits for both the gates and the owner.**

1. **`tag`** — the version of the tag (`v0.1.0`) is the `VersionPrefix` of `Directory.Build.props`, and the tagged commit
   is on `main` (`git merge-base --is-ancestor`). A release is made from `main`, never from a branch nobody reviewed.
2. **`gate`** — `uses: ./.github/workflows/ci.yml`: **exactly** the gates of every pull request — the format, the three
   layers, the client of the sample, the parity over the whole word list, the mutation check, the smoke test, Windows.
   There is no second list of gates that could drift.
3. **`publish`** — `needs: [tag, gate]`, in the environment **`nuget`** whose required reviewer is the owner. It packs
   and smoke-tests both packages at the version of the tag, asks nuget.org for a key (`NuGet/login`, `id-token: write`)
   right before the push — the key lives one hour, one token buys one key — and pushes the `.nupkg` and the `.snupkg`.
   The packages of the release stay as an artifact for 90 days.

**No key is stored**: the secret `NUGET_API_KEY` is deleted once the first release went out. The user name of nuget.org
is a variable of the repository (`NUGET_USER`), not a secret — it is public on nuget.org anyway.

**The trigger is the tag** — no `workflow_dispatch`. A run by hand without a tag could only fail at the version check.

**The guard is a test**: `Workflow_Rules.The_release_runs_the_gates_of_every_pull_request_and_stores_no_key` fails when the
release does not call `ci.yml`, when the push does not need that call, runs outside the environment `nuget`, asks for no
OIDC token, uses no `NuGet/login`, or reads `secrets.NUGET_API_KEY`. All of it seen red; the mutation
`the-release-skips-the-gate` keeps it biting.

**The release workflow is checked before the release**: it runs only on a tag, so a mistake in it would be found when
the version is due. The job *Workflows · actionlint* of `ci.yml` reads every workflow on every pull request — the release
of actionlint, checked against the checksum of that release.

## Consequences

- **A release runs every gate again** — about five minutes before the owner is asked. That is the price of one list of
  gates; a release that skips them is the problem this decision removes.
- **The owner approves in the run** (Actions → the run → *Review deployments*). The approval is a second decision after
  the tag: the tag says "this version", the approval says "now".
- **What the owner does once** — make the repository public, create the environment, add the policy on nuget.org, set
  the variable — is written down in `.agents/release.md`. Until then the job `publish` waits for an environment that has
  no reviewer, or fails at the login: it never pushes by accident.
- A **job that calls a workflow** has no runner and no timeout of its own; the guard of the timeouts knows that (the jobs
  of `ci.yml` have theirs).
- Once the repository is public, **branch protection** becomes available as well: a required status check on `main`
  (the gates of `ci.yml`) is the next lever — a step of its own, not of this decision.

## Alternatives

| The alternative | Why not |
|---|---|
| Keep the gate of its own in `publish.yml` | It drifted from the gate of the pull requests once already — that is how Windows and the mutation check were missing. |
| Keep the stored API key | A key that lives for months and can push every package of the account; Trusted Publishing issues one that lives an hour and can only be had by this workflow of this repository. |
| Private repository, the tag as the only approval | Possible, and written down as the fallback: without the environment, the policy on nuget.org leaves the environment empty. The owner chose the public repository — the approval, and SourceLink that works for the users. |
| `workflow_dispatch` with the version as an input | A second way to start a release, which would have to repeat every check of the tag. The tag is the one way. |

## Note (2026-11-11, a prerelease)

The tag check reads `VersionSuffix` as well, and so does the smoke test when it gets no version: the tag of a
prerelease is `v<VersionPrefix>-<VersionSuffix>` ([ADR-0053](./0053-the-first-release-is-a-prerelease.md)).
