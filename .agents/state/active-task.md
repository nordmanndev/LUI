# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#21](https://github.com/nordmanndev/LUI/issues/21) — dependency injection, hosted services, stale candidates (PR-008)
**Branch:** `feat/dependency-injection`
**State:** merged into `main` (ADR-0017, ADR-0018 — two packages: the core and the hosting).

## Open

- `Highlight` → #20 (a later version, owner's decision: the front end marks the match).
- The typo dictionary is rebuilt in full at the end of a sync with changes → #19 (a lazy or background rebuild; the
  hosted services of PR-008 are the place).
- Owner's decisions still open: the default `LuiSyncOptions.BatchSize`, a shadow build for `Linked.Sync()`, ADR-0010.

## Open in parallel

- **#22 → PR #25, `feat/test-landscape`**: the test tree in three layers
  (`Nordmann.LUI.Unit.Tests`, `Nordmann.LUI.Integration.Tests`, `Nordmann.LUI.Architecture.Tests`) plus the
  shared `Nordmann.LUI.Testing`; `docs/testing.md` is the map of the tests and `Test_Landscape` is its guard
  (ADR-0019). Waiting for the review.

## The next step

**PR-009 — performance budgets:** BenchmarkDotNet project, 3 M corpus generator, the §16 targets as tests (with ADR-0010).
