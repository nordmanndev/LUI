# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#61](https://github.com/nordmanndev/LUI/issues/61) — the README for developers: a tutorial, the performance
up front and a map of the documentation.
**Branch:** `docs/readme-and-tutorial`, from `main` at `111eefb` (the merge of PR #60).
**Decision:** [ADR-0036](../../docs/adr/0036-the-readme-the-specification-and-the-tutorial.md),
[ADR-0022](../../docs/adr/0022-the-benchmark-results-in-the-readme.md) (restated).

## What is done

- **The README** is the entrance: 241 lines (at most 250 from now on), badges, a linked table of contents, what LUI
  is and what it is *not*, the problem, the highlights, the install, one quick start with its output, the budgets of
  §16 before the first third, one table for the two index kinds and the two content sources, the payload, `AddLui`
  in a host, reading only, the most important types, the sample and the map of the documentation. The five concept
  sections left the README — the specification has them, in full.
- **The tutorial** (`docs/tutorial/`, six files, English) is the way: from an empty database to a working autocomplete,
  in five steps. Every C# example compiles; 22 of them were compiled and run in the review.
- **The map of the documentation**: `docs/README.md` (what lies where, who reads what), `docs/plan/README.md` (the
  plans as the history of the repository) and a **topic** column over the 36 ADRs in `docs/adr/README.md`. The three
  orphaned documents are linked from the README.
- **The guard follows the raw data**: the BenchmarkDotNet tables live in `docs/benchmarks.md`, `Readme_Benchmarks`
  renders into that file and fails when it and the committed reports differ. The review proved that it still bites:
  a number changed by hand in the record and in the report each turn the guard red.
- **The paper**: ADR-0022 was restated, ADR-0036 was added, the CHANGELOG and `docs/testing.md` follow.

## Open

- The pull request **#62** is open against `main` (head `7bd1053`), the CI is green, and the review of the pull
  request is written (`/workspace/verify-61-readme.md`, `VERDICT: PASS`). **The merge is the owner's.**
- Three points for the owner: the link checker of the map is not yet a guard in `tools/`; the tutorial has no guard
  in the tree (its examples are checked by hand in the review); `tools/package-smoke-test.sh` fails in this sandbox
  because `Nordmann.LUI.Hosting` does not pin `Nordmann.LUI` exactly at `[0.1.0]` — on `main` as well.

## The next step

After #61: **#38** (the budget table of the README guarded against §16), then the **release** (PR-012, `0.1.0`) — which
the owner has to ask for; after that #35, #31, #32, #44, #59 (only on demand) and the `compactLevel` follow-up of #48.
