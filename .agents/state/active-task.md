# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#28](https://github.com/nordmanndev/LUI/issues/28) — performance budgets (PR-009)
**Branch:** `feat/performance-budgets`
**State:** pull request #29 open; the owner's decisions worked in.

## Decided by the owner (2026-11-04)

- §16 stated anew (ADR-0010 option 2, ADR-0020): query p95 per length, correction ≤ 35 ms, allocation p50 ≤ 250 /
  p95 ≤ 350 KB (too much — bug #30), file ≤ 500 MB for 1 M titles of 36 characters, the build ≤ 4 min (a faster first load: #32). Batch size stays 10,000; the shadow
  build after 0.1.0 (#31); #24 an exact pin; #19 before 0.1.0.

## The next step

**Next:** #24 (the exact pin of Hosting → core), #19 (the typo dictionary), then **PR-010 — docs:** the README outline of plan §7, `docs/benchmarks.md`, the CHANGELOG, the smoke test with the README.
