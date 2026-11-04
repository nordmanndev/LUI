# Active task

<!--
What the next session is doing. One task, in the words of the issue that
describes it. Link the issue; do not repeat the whole plan here.
-->

**Issue:** [#36](https://github.com/nordmanndev/LUI/issues/36) — PR-010: the README, the CHANGELOG, the benchmarks (BenchmarkDotNet results in the README)
**Branch:** `docs/readme-benchmarks`
**State:** implemented, verified, pushed; the pull request waits for the owner's word.

## Decided by the owner (2026-11-04)

- The BenchmarkDotNet results belong in the README (ADR-0022): committed reports in `benchmarks/results/`, the README
  section rendered and guarded by `Readme_Benchmarks`, a new run with `tools/benchmarks.sh`.
- Earlier: #19 merged (ADR-0021), follow-up #35; open #20, #30, #31, #32.

## The next step

**Next:** PR-011 — the sample application (`samples/Lui.Sample/`), then PR-012 — the release (ask before publishing).
