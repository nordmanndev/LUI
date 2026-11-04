# 0022 — The BenchmarkDotNet results in the README: committed reports, a rendered section, a guard

- **Status:** Accepted (owner's request of 2026-11-04: "ich will die Ergebnisse aus BenchmarkDotNet in der README sehen")
- **Date:** 2026-11-04
- **Issue:** #36 (PR-010 of the plan)
- **Builds on:** [0020](./0020-the-performance-budgets-measured.md) (the corpus, the budget test, the benchmark project)
- **Affects:**
  - `benchmarks/results/` (new): BenchmarkDotNet's `*-report-github.md` of every benchmark class
  - `tests/Nordmann.LUI.Architecture.Tests/Readme_Benchmarks.cs` (new), `tools/benchmarks.sh` (new)
  - `README.md` (the section "Performance"), `docs/benchmarks.md`, `docs/testing.md`, `tools/mutation-check.sh`

## Context

The README of plan §7 has a section "Performance: the §16 numbers, measured, with the machine". The owner wants the
results of BenchmarkDotNet there as well. A table of numbers copied into a README by hand has two problems:

- It ages silently. The code changes, the numbers stay, and nothing says which run they came from.
- It can be edited. A number nobody measured looks exactly like one somebody did.

AGENTS.md: "a performance claim without a measurement is a rumour". The README is the first page a user reads, so the
rule matters most there.

## Decision

1. **BenchmarkDotNet's own reports are committed.**
   - `benchmarks/results/` holds the `*-report-github.md` of each benchmark class, as BenchmarkDotNet wrote them,
     unedited. The machine and the runtime are part of each report.
   - The other exports (HTML, CSV, logs) are not committed; they stay ignored (`BenchmarkDotNet.Artifacts/`).
2. **The README section is rendered from them, by a test.**
   - The README marks the section with `<!-- benchmarks:begin -->` and `<!-- benchmarks:end -->`.
   - `Readme_Benchmarks` renders, from the reports:
     - the machine, once;
     - a table per benchmark class: the method, the parameters that vary, `Mean`, `Error` and `Allocated`;
     - the parameters that are the same in every row, in the caption.
   - The test fails when the section differs from that.
   - `LUI_UPDATE_README=1` writes the section instead of failing. The rendering lives in one place, so the generator and
     the guard cannot disagree.
3. **The reports come from one run.** The README sets the benchmarks side by side, so a test asserts that every report
   names the same machine and runtime. A report of one class from a newer run beside an old one fails.
4. **Every benchmark class has a report and a section.** A benchmark class without a committed report fails the test,
   and so does a report without a class. A new benchmark therefore cannot be added without a run.
5. **`tools/benchmarks.sh` is the one way to a new run.** It:
   - runs every benchmark with BenchmarkDotNet's **default job**;
   - copies the reports;
   - renders the README;
   - runs the guard.

   It refuses to run without the full word list: with only the repository's small sample, the benchmarks would measure
   another corpus than the README names. A run takes about 40 minutes on the measurement machine.
6. **The default job, not `ShortRun`.** The README shows `Error`, the half-width of the 99.9 % confidence interval.
   With the three iterations of `ShortRun` that interval is wider than many of the means (ADR-0020: 254 µs error on a
   97 µs mean). The default job runs until the interval is narrow.
7. **The §16 budget table stays beside it.** BenchmarkDotNet measures the code on 200,000 entries; the budget test
   measures the scenario of three million. The README shows both and says which is which. The budget table remains the
   output of `PerformanceBudgets` (ADR-0020), copied with its machine, because that test runs for ten minutes and is not
   part of CI.

## Consequences

- A change that makes the code faster or slower does not turn the build red. Only a README that disagrees with its
  reports does. Re-running the benchmarks is a decision, made with `tools/benchmarks.sh` on a quiet machine, and the
  diff of `benchmarks/results/` shows what changed.
- The numbers in the README are those of one machine, the one named in the section. CI does not run the benchmarks: a
  shared runner measures its neighbours.
- `tools/mutation-check.sh` has a mutation that edits a number in the README by hand; `Readme_Benchmarks` catches it.

**Verified by:** `Readme_Benchmarks` (three tests), the mutation `a-readme-benchmark-number-edited-by-hand`, and the
run recorded in `docs/benchmarks.md`.
