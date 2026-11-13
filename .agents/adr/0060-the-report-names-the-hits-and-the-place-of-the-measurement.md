# 0060 — The report of a measurement names the hits and the place of the measurement

- **Status:** Accepted
- **Date:** 2026-11-13
- **Builds on:** [0022](./0022-the-benchmark-results-in-the-readme.md) (the numbers of the
  documentation come from the benchmarks), [0041](./0041-the-budgets-of-16-run-on-a-schedule.md) (the budgets of ADR-0020 run
  on a schedule), [0058](./0058-the-measurement-is-a-layer-of-its-own.md) (the measurement is a layer of its own),
  [0059](./0059-the-corpora-are-four-fixed-files-in-the-repository.md) (the corpora are files)
- **Affects:**
  - `benchmarks/Nordmann.LUI.Benchmarks/` — the settings of a query, the fixture with its two sources, the writer of the
    reports
  - `tests/Nordmann.LUI.Testing/BenchmarkHits.cs` (new) — the cell of a line that measures no query
  - `tests/Nordmann.LUI.Architecture.Tests/ReadmeBenchmarksTests.cs` — the renderer and its guards
  - `.github/workflows/performance.yml` — the schedule of the lane
  - `tools/benchmarks.sh`, `docs/benchmarks.md`, `docs/testing.md`, `README.md`
- **Issues:** #124 (epic #117)

## Context

The reports of the benchmarks were committed, and they said less than they looked like they said.

1. **A line without the number of hits can measure nothing and look healthy.** The report of a query named its input and
   its time, and not how many entries the query found. The input `ka` found **no** entry in the corpus of the day — and
   the report said nothing about it: a query that returns nothing and a query that returns a thousand entries stood in the
   same table, distinguished by a time that was fast in both cases.
2. **A number without its place is a rumour.** The reports carried no date, no number of measurements and no journal mode.
   The machine stood in the header (that BenchmarkDotNet writes), but the date of the run, the number of iterations and the
   journal mode of the database did not — and the journal mode is a property of the file the measurement ran on: a write
   line in `DELETE` is a different measurement from a write line in `WAL`.
3. **The settings of a query were not measured.** The budget of the candidates, the order of the ranking, the measure of
   the similarity and the correction on and off change what a query does — and therefore what it costs. The report named
   none of them; a reader had to take the numbers on faith.
4. **The linked index was measured somewhere else.** The benchmark measured the index whose content lives in the index
   itself; the index whose content lives in a table of the application (ADR-0015) was measured in the measurement layer,
   in its own file, with its own scenario. Two reports, two places, no comparison.
5. **The lane ran every night.** A number that changes every night is a number nobody reads twice; the schedule of
   ADR-0020 measures the targets of the budgets — and the benchmarks were not run by any workflow at all.

## Decision

**A report of a measurement names what it measured and where.**

1. **Every line names the number of hits its query found** — the candidates that were ranked, not the entries the answer
   carries (the answer is capped at twenty). A line that measures **no** query — a write, a text — says so with a dash.
   **Zero is a measurement; a dash is not.** The difference between the two cells is the difference between a line that
   found nothing and a line that asked nothing.
2. **Every report carries the record of its run:** the date, the number of measurements per case, the machine and the
   journal mode of the database.
3. **The settings of a query are axes** — the budget of the candidates, the order of the ranking, the measure of the
   similarity and the correction on and off. A setting that is not measured is a setting nobody knows.
4. **The linked fixture joins the embedded one.** One fixture, one report, both sources; the source is an axis of the
   report like every other.
5. **The lane runs weekly** (`17 3 * * 1`), not nightly. The schedule runs on the default branch: the number of the week
   is the stable line of `main`, and `dev` is measured by hand.
6. **The CI runs on the pull requests that target `dev`.** The series branches from `dev` and targets `dev`; without
   `dev` in the trigger of the workflow, not one pull request of the series is checked at all.

## Consequences

- **The reports are the output of a run.** They are written by `tools/benchmarks.sh`, which runs the benchmarks, keeps
  BenchmarkDotNet's reports and brings the README in step. A report that was written by hand is a report that does not
  come from a run.
- **The hits are measured by the same query the benchmark measured.** The settings of a case live in one place
  (`QuerySettings`), and the report and the benchmark build the query from it — a second truth about a query would be a
  second truth about its hits.
- **The axes have short names.** BenchmarkDotNet cuts a long parameter value in the table of its report, and a cut value
  cannot be read back: `words` and `titles` stand for the two corpus files, `Start/Sim` for the order of the ranking. The
  mapping lives in the code, next to the setting it names.
- **The report keeps one table.** BenchmarkDotNet writes the table of the cases and the summary of them; the committed
  report keeps the summary, with the hits.
- **The guard of the README grows.** The renderer must show a zero as a zero, and every report must carry its record —
  two guards over the committed reports, beside the one that keeps the README in step with them.
- **ADR-0020 asks for exactly this, and it is not changed by it.** The section says that the measured value stands in the
  protocol, next to the machine it was measured on — the reports carry the machine now, and with it the date, the number
  of measurements and the journal mode. The targets of ADR-0020 are re-decided in the ADR-0020 round, against the measured corpora
  (issue #123); the section itself falls with the German specification (issue #125).
