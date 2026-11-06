# 0039 — The entrance shows the measurement, the bulk path, the kinds and the sources

- **Status:** Accepted
- **Date:** 2026-11-06
- **Builds on:** [0022](./0022-the-benchmark-results-in-the-readme.md) (the benchmark tables and their guard),
  [0020](./0020-the-performance-budgets-measured.md) (the budgets, measured),
  [0036](./0036-the-readme-the-specification-and-the-tutorial.md) (the entrance, the contract, the way),
  [0029](./0029-the-sample-fills-its-own-bulk-indexes-on-demand.md) (the bulk indexes of the sample)
- **Affects:**
  - `README.md` (the sections "Loading many entries", "The measurements of one run", "The kinds of an index",
    "Embedded or linked", "The sample application"), `docs/benchmarks.md` (the record of the run),
    `docs/images/sample.gif` (new)
  - `tests/Nordmann.LUI.Architecture.Tests/Readme_Benchmarks.cs` (the target of the guard),
    `tools/benchmarks.sh`, `tools/mutation-check.sh`
  - `.agents/adr/0022-the-benchmark-results-in-the-readme.md` (restated), `.agents/adr/0036-the-readme-the-specification-and-the-tutorial.md`
    (decision 1 and decision 4 superseded)
- **Issue:** #61

## Context

The owner read the README of the release and found five gaps.

| | Befund |
|---|---|
| **Bulk loading** | nowhere a word about it — and it is the way an index of a million entries is built. The tutorial has a page about it, the README does not. |
| **The measurement** | the raw tables of the BenchmarkDotNet run stood in `docs/benchmarks.md`, and the reports of the run were from 2026-11-05. The entrance showed a budget table and a link — the owner wants **the result itself**, and a **fresh** one. |
| **The kinds and the sources** | one table with four rows, half a sentence each. "Embedded vs. linked" was a row, not a decision. |
| **The sample** | two screenshots of a page that moves. |
| **The API** | eight types; thirty-nine are public ([0038](./0038-the-public-api-has-a-page-of-its-own.md)). |

Two of the gaps are decisions about where a thing lives, and they touch two records:
[0022](./0022-the-benchmark-results-in-the-readme.md) put the raw tables into the record, and
[0036](./0036-the-readme-the-specification-and-the-tutorial.md) set a limit of 250 lines for the entrance.

## Decision

1. **The measurement of the run belongs in the entrance.** The BenchmarkDotNet tables are rendered into the
   `README.md` again, between the markers `<!-- benchmarks:begin -->` and `<!-- benchmarks:end -->`; the guard
   [0022](./0022-the-benchmark-results-in-the-readme.md) follows the tables and fails when the file and the committed
   reports differ. The record, `docs/benchmarks.md`, keeps the method, the corpus and the budgets — it is the place
   for the numbers of a run, and the README now shows them itself.
2. **A fresh run.** The reports in `benchmarks/results/` were from 2026-11-05; the benchmark was run again on a quiet
   machine, and the tables of the entrance come from that run. A number without a run behind it is a rumour
   (`AGENTS.md`).
3. **The entrance may grow.** The limit of 250 lines from [0036](./0036-the-readme-the-specification-and-the-tutorial.md)
   is superseded: the entrance of the release carries the tables of the measurement, the example of the bulk path, the
   matrix of the kinds and the decision guide of the sources. The rule that stays is the one that mattered: **the
   entrance may not say what the specification says** — the concept sections stay in the contract.
4. **The bulk path gets an example.** `Sync` with a `BatchSize`, the report it returns and the difference to
   `UpsertMany` — in the entrance, with the output it prints.
5. **The kinds become a matrix and the sources become a decision.** The matrix of `SingleWord` and `MultiWord` with
   the typo tolerance on and off, so that the four combinations can be read off directly; and the guide "embedded or
   linked" as three questions with an answer, before the table that compares them.
6. **The sample moves.** A GIF of the page — the autocompletes, the correction of a typo, the fill of a bulk index on
   demand — next to the two screenshots.

## Alternatives

| | For | Against |
|---|---|---|
| **A — the tables stay in the record** | the entrance stays short | the owner asked for the result in the entrance; and a link to a file from 2026 is a promise of freshness that the file does not keep |
| **B — the tables in the entrance, the budgets in the record** | one place for the numbers | the budgets are the promise a reader checks — they belong where the reader starts |
| **C — the measurement as a picture** | short and pretty | a picture of a number is a picture: nobody can check it against a report |
| **D — the entrance grows, and the measurement is in it** (this decision) | the reader sees the run, the machine and the tables without leaving the page | the entrance is longer than a sitting — the owner's call, and it is the call of the person who reads it |

## Consequences

- **The public API does not change.** Nothing in this decision touches a type, a member or a signature.
- **The guard follows the tables** ([0022](./0022-the-benchmark-results-in-the-readme.md)): `Readme_Benchmarks` renders
  into `README.md` and fails when the file and the committed reports differ. The mutation of a number by hand still
  turns the build red.
- **The tables of the entrance and the tables of the record are the same run.** Both are rendered from the reports in
  `benchmarks/results/`; the record keeps the machine, the corpus and the method beside them.
- **The bulk example is executed.** The block is one of the C# blocks of the README, and the package smoke test runs
  it and checks what it prints.
- **The matrix is measured, not asserted.** Every example in it was run against the library before it was written down.

**Verified by:** `tools/benchmarks.sh` (the run, the reports, the rendering, the guard), the package smoke test (the
blocks of the README), `Readme_Benchmarks` (the tables against the reports), and the matrix examples, run against the
library.
