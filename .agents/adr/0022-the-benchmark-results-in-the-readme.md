# 0022 — The BenchmarkDotNet results: the raw data in the record, the budgets in the README

> **Restated (2026-11-06, issue #61, [ADR-0039](./0039-the-entrance-shows-the-measurement.md)):** the raw tables of a
> BenchmarkDotNet run live in the **README** again — the owner read the entrance and wants the measurement itself, not a
> link to it. The guard renders and checks the section between `<!-- benchmarks:begin -->` and `<!-- benchmarks:end -->`
> in `README.md`; [`docs/benchmarks.md`](../../docs/benchmarks.md) keeps the machine, the corpus, the method and the budgets, and points at the
> tables of the entrance. Only the **place** of the tables changes; every other rule of this record — the committed
> reports, one run on one machine, the default job, the guard that renders — stands. The text below is the decision as
> it stood after [ADR-0036](./0036-the-readme-the-specification-and-the-tutorial.md) and stays as it was written.
>
> **Restated (2026-11-06, issue #61, [ADR-0036](./0036-the-readme-the-specification-and-the-tutorial.md)):** the raw
> tables of a BenchmarkDotNet run live in [`docs/benchmarks.md`](../../docs/benchmarks.md) since the README became the entrance
> of the documentation; the README shows the budgets of the specification and links to the record. The guard renders and
> checks the section in the record. The decision below is written as it stands now.

> **Note (ADR-0057, 2026-11-12, issue #121):** the German wording of the record — the owner's request — was translated
> into English. The decision stands as it was written; the text below is history.

> **Note (ADR-0061, 2026-11-13, issue #125):** the specification this record was written against is deleted.
> Every section of it has a home — the ADR or the page that carries the same rule; the mapping is in
> [ADR-0061](./0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md). The sentences below
> that name the specification are history; the rule they carry lives in the ADR named beside them.

- **Status:** Accepted (owner's request of 2026-11-04: "I want to see the results from BenchmarkDotNet in the README")
- **Date:** 2026-11-04 (restated 2026-11-06)
- **Issue:** #36 (PR-010 of the plan), #61 (the move of the tables)
- **Builds on:** [0020](./0020-the-performance-budgets-measured.md) (the corpus, the budget test, the benchmark project), [0036](./0036-the-readme-the-specification-and-the-tutorial.md) (where a document belongs)
- **Affects:**
  - `benchmarks/results/` (new): BenchmarkDotNet's `*-report-github.md` of every benchmark class
  - `tests/Nordmann.LUI.Architecture.Tests/Readme_Benchmarks.cs` (new), `tools/benchmarks.sh` (new)
  - `README.md` (the section "Performance"), `docs/benchmarks.md` (the rendered tables), `docs/testing.md`, `tools/mutation-check.sh`

## Context

The README of plan §7 has a section "Performance: the ADR-0020 numbers, measured, with the machine". The owner wants the
results of BenchmarkDotNet there as well. A table of numbers copied into a README by hand has two problems:

- It ages silently. The code changes, the numbers stay, and nothing says which run they came from.
- It can be edited. A number nobody measured looks exactly like one somebody did. (What this record closes is the
  README side; the reports themselves are trusted input — see the consequences.)

AGENTS.md: "a performance claim without a measurement is a rumour". The README is the first page a user reads, so the
rule matters most there.

The README grew, however, into an architecture document: 571 lines, with the raw tables of a BenchmarkDotNet run
(80 lines) in the middle of it. A README is an entrance — it cannot be the record of every run and still be an
entrance (ADR-0036).

## Decision

1. **BenchmarkDotNet's own reports are committed.**
   - `benchmarks/results/` holds the `*-report-github.md` of each benchmark class, as BenchmarkDotNet wrote them,
     unedited. The machine and the runtime are part of each report.
   - The other exports (HTML, CSV, logs) are not committed; they stay ignored (`BenchmarkDotNet.Artifacts/`).
2. **The rendered tables live in the record, `docs/benchmarks.md`, and a test renders them.**
   - The file marks the section with `<!-- benchmarks:begin -->` and `<!-- benchmarks:end -->`.
   - `Readme_Benchmarks` renders, from the reports:
     - the machine, once;
     - a table per benchmark class: the method, the parameters that vary, `Mean`, `Error` and `Allocated`;
     - the parameters that are the same in every row, in the caption.
   - The test fails when the section differs from that.
   - `LUI_UPDATE_README=1` writes the section instead of failing. The rendering lives in one place, so the generator and
     the guard cannot disagree. `tools/benchmarks.sh` runs the benchmarks, copies the reports and does that.
3. **The README shows the budgets, the record shows the run.**
   - The README shows the **budget table** of [ADR-0020](./0020-the-performance-budgets-measured.md): the target, the measured
     value, the limit — nine rows, the promise a reader checks — with the machine named and a link to the record.
   - The record holds the raw tables of the BenchmarkDotNet run: 200,000 entries of the same corpus, the code and not
     the scenario of ADR-0020, the machine, the corpus and the method.
   - The two are told apart in both files: the budget test measures the scenario of three million entries, the
     benchmark project measures the code on 200,000.
4. **The reports come from one run.** The tables are set side by side, so a test asserts that every report names the
   same machine and runtime. A report of one class from a newer run beside an old one fails.
5. **Every benchmark class has a report and a section.** A benchmark class without a committed report fails the test,
   and so does a report without a class. A new benchmark therefore cannot be added without a run.
6. **`tools/benchmarks.sh` is the one way to a new run.** It:
   - runs every benchmark with BenchmarkDotNet's **default job**;
   - copies the reports;
   - renders the tables of the record;
   - runs the guard.

   It refuses to run without the full word list: with only the repository's small sample, the benchmarks would measure
   another corpus than the record names. A run takes about 40 minutes on the measurement machine.
7. **The default job, not `ShortRun`.** The tables show `Error`, the half-width of the 99.9 % confidence interval.
   With the three iterations of `ShortRun` that interval is wider than many of the means (ADR-0020: 254 µs error on a
   97 µs mean). The default job runs until the interval is narrow.

## Alternatives

| | For | Against |
|---|---|---|
| **A — the raw tables stay in the README** (the decision of 2026-11-04) | the numbers are where the reader starts; the owner asked for it | 80 lines of raw data in an entrance; the README cannot be read in one sitting; every number of a run has to be maintained in the place where the promise is made |
| **B — the raw data in the record, the budgets in the README** (this decision) | the entrance stays an entrance, the promise stays visible, the raw data keeps its machine, corpus and method beside it | two places to look for a number — the budgets in the README, the run in the record; the link between them has to be there |
| **C — no tables at all, only the budgets** | the README is small; the record is the only place with numbers | the code and the scenario are told apart nowhere; the comparison of two runs has no home |
| **D — the raw data in the repository history only** | nothing to maintain | a number without a file behind it is a rumour (AGENTS.md) |

## Consequences

- A change that makes the code faster or slower does not turn the build red. Only a record that disagrees with its
  reports does. Re-running the benchmarks is a decision, made with `tools/benchmarks.sh` on a quiet machine, and the
  diff of `benchmarks/results/` shows what changed.
- The numbers of the record are those of one machine, the one named in the section. CI does not run the benchmarks: a
  shared runner measures its neighbours.
- `tools/mutation-check.sh` has two mutations: a number in the record edited by hand, and a number in a committed
  report edited by hand. `Readme_Benchmarks` catches both, because each breaks the agreement of the two.
- **The guard proves consistency, not authenticity** (review of pull request 37, M2). The reports are trusted input: a
  number changed in a report *and* the section rendered anew from it passes, and so would a report written by hand.
  Nothing in a repository can make a file's content trustworthy. The guards for that are code review and the diff of
  `benchmarks/results/` — a report changes only with `tools/benchmarks.sh`, all four at once, and with a new environment
  line or a new run date in the commit that brings it.
- The name of the environment variable (`LUI_UPDATE_README`) stays, although the section it writes is no longer in the
  README: the tools of the repository address it in one place, and a rename would only make the diff of the scripts
  larger.

**Verified by:** `Readme_Benchmarks` (three tests), the mutations
`a-benchmark-number-in-the-record-edited-by-hand` and `a-benchmark-report-edited-by-hand`, and the run recorded in
`docs/benchmarks.md`.
