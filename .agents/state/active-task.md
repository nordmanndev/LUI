# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#61](https://github.com/nordmanndev/LUI/issues/61) — the README for developers: a tutorial, the performance
up front and a map of the documentation. The owner reviewed the pull request and named seven gaps; this round closes them.
**Branch:** `docs/readme-and-tutorial`, from `main` at `111eefb` (the merge of PR #60).
**Decision:** [ADR-0039](../../.agents/adr/0039-the-entrance-shows-the-measurement.md),
[ADR-0038](../../.agents/adr/0038-the-public-api-has-a-page-of-its-own.md),
[ADR-0037](../../.agents/adr/0037-the-papers-of-the-work-live-under-agents.md),
[ADR-0036](../../.agents/adr/0036-the-readme-the-specification-and-the-tutorial.md) (decisions 1 and 4 superseded),
[ADR-0022](../../.agents/adr/0022-the-benchmark-results-in-the-readme.md) (restated).

## What is done

- **The seven gaps of the review are closed.**
  1. **Bulk loading** has an example in the README: `Sync` with a `BatchSize`, the report it returns, the difference
     to `UpsertMany` — a C# block that the package smoke test executes.
  2. **The public API is documented**: `docs/api.md` describes every public type of the two packages — 38 in the core,
     one in the hosting — with a sentence for each. A new test, `Public_API_Documentation`, fails when a type is missing
     on the page or when the page (or the README) names a type the code does not have.
  3. **"Embedded or linked" is a decision guide**: three questions with an answer, before the table that compares them.
  4. **The kinds are a matrix**: `SingleWord` and `MultiWord`, with the typo tolerance on and off — the four
     combinations can be read off directly. Every example was run against the library before it was written down.
  5. **The sample moves**: `docs/images/sample.gif` — the autocompletes, the correction of a typo, the fill of a bulk
     index on demand.
  6. **The measurement is fresh and in the entrance**: the BenchmarkDotNet run of 2026-11-06 (Debian 12, Intel Xeon
     6982P-C) is rendered into the README between the markers; `Readme_Benchmarks` follows the tables.
  7. **The papers of the work live under `.agents/`**: the ADRs, the plans and the specification moved there with an
     index of their own, and neither the README nor `docs/` links them any more.
- **The tutorial** (`docs/tutorial/`, six files, English) is the way: from an empty database to a working autocomplete,
  in five steps. Every C# example compiles; 22 of them were compiled and run in the review.
- **The map of the documentation**: `docs/README.md` (what lies where, who reads what) and a **topic** column over the
  ADRs in `.agents/adr/README.md`.

## Open

- The pull request **#62** is open against `main`; **the merge is the owner's.**
- Two points for the owner: the link checker of the map is not yet a guard in `tools/`; the tutorial has no guard in the
  tree (its examples are checked by hand in the review). `tools/package-smoke-test.sh` fails in this sandbox because
  `Nordmann.LUI.Hosting` does not pin `Nordmann.LUI` exactly at `[0.1.0]` — on `main` as well.

## The next step

After #61: **#38** (the budget table of the README guarded against §16), then the **release** (PR-012, `0.1.0`) — which
the owner has to ask for; after that #35, #31, #32, #44, #59 (only on demand) and the `compactLevel` follow-up of #48.
