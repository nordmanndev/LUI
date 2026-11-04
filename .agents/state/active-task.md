# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#19](https://github.com/nordmanndev/LUI/issues/19) — the typo dictionary: stale instead of rebuilt, a query that does not wait
**Branch:** `feat/typo-dictionary-lazy`
**State:** implemented, verified; pull request open.

## Decided by the owner (2026-11-04)

- #19 before 0.1.0, as proposed: a sync marks the dictionary stale, it is rebuilt in the background, a query skips the
  correction while a build runs (ADR-0021).
- Earlier the same day: §16 stated anew (ADR-0020), allocation bug #30, shadow build #31, faster first load #32; #24 the
  exact pin (merged).

## The next step

**Next:** **PR-010 — docs:** the README outline of plan §7, `docs/benchmarks.md`, the CHANGELOG, the smoke test with the README.
