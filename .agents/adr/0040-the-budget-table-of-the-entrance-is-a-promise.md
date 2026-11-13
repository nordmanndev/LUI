# 0040 — The budget table of the entrance is the promise of ADR-0020, and a test holds it up

> **Note (ADR-0061, 2026-11-13, issue #125):** the specification this record was written against is deleted.
> Every section of it has a home — the ADR or the page that carries the same rule; the mapping is in
> [ADR-0061](./0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md). The sentences below
> that name the specification are history; the rule they carry lives in the ADR named beside them.

- **Status:** Accepted
- **Date:** 2026-11-06
- **Builds on:** [0020](./0020-the-performance-budgets-measured.md) (the budgets, measured),
  [0022](./0022-the-benchmark-results-in-the-readme.md) (the tables of a run and their guard),
  [0032](./0032-the-allocation-of-a-query.md) (the allocation and its target),
  [0039](./0039-the-entrance-shows-the-measurement.md) (the entrance shows the measurement)
- **Affects:**
  - `tests/Nordmann.LUI.Architecture.Tests/Readme_Budgets.cs` (new), `tools/mutation-check.sh` (a mutation),
    `docs/testing.md`
  - `README.md` (the budget table), `docs/benchmarks.md` (the status table and the target table)
  - ADR-0020 (the contract that is read)
- **Issue:** #38

## Context

The budget table of the README is the promise a reader checks: nine rows, the target, the measured value, the limit. It
is **copied by hand** — the budget test `PerformanceBudgets` takes about ten minutes and is not part of the CI, so
nothing checks either column:

- the **limits** can drift from the target table of ADR-0020;
- the **measured values** can drift from the run they came from.

The drift had already happened. ADR-0032 lowered the target of the allocation from p50 ≤ 250 KB / p95 ≤ 350 KB to
p50 ≤ 60 KB / p95 ≤ 100 KB and the budget test was changed with it — the specification's table followed, the README
and the record did not. The entrance still promised a limit that the code no longer holds, and the numbers beside it
were measured before the allocation was lowered.

A promise that nobody checks is a wish.

## Decision

1. **The contract is the source.** A new architecture test, `Readme_Budgets`, reads the target table of ADR-0020 out of the
   specification and compares its limits with the **three** tables that show them: the budget table of the README, the
   target table of the record and the status table of the record (the summary at the top). The limits are compared
   **in order** — the sequence of "≤ number unit" of the contract against the sequence of the table — so a swapped
   limit is caught as well as a changed one. The rows of a table follow the order of the contract; the minute is
   folded into the second, because "≤ 4 min" and "≤ 240 s" are the same promise.
2. **What is compared, and what is not.** The **limits** are the promise, and they are checked. The **measured values**
   are the numbers of a run: they belong to a machine and a day, and a test cannot know them. They stand in the record
   with the machine they were measured on, and the entrance says where its numbers come from.
3. **The drift that the guard found is fixed.** The allocation row of the README and of the record carries the limits
   of ADR-0020 (≤ 60 / ≤ 100 KB) and the measured values of the run of 2026-11-06 (46 / 51 KB — measured in bytes, so the
   machine does not matter). The status table of the record stood apart from the contract twice: it promised the old
   limits, and it listed the allocation before the correction. Its rows follow the order of the contract now.
4. **The mutation check proves the teeth.** Two mutations change one limit — "≤ 5 ms" becomes "≤ 6 ms" in the README,
   "≤ 20 ms" becomes "≤ 21 ms" in the status table of the record — and `Readme_Budgets` has to fail. The message names
   both lists: what the specification says and what the table says.
5. **The specification stays the contract.** The limits are read from it, so it is a dependency of the build. When the
   owner deletes the specification at the end of the release, the limits have to live somewhere else — where is a
   decision of the owner, and this record is where it stands.

## Alternatives

| | For | Against |
|---|---|---|
| **A — no guard; the table is written by hand** | nothing to maintain | the drift is invisible: the entrance promises a limit the code no longer holds, and nobody notices until a reader does |
| **B — the measured values are checked too** | the whole table is a promise | a measured value belongs to a machine and a day; a test that knows them has to hard-code them — a hand-written list with a slower heartbeat |
| **C — the table is generated from a committed file** | the table cannot be edited by hand | a fourth place for the limits: the file, the specification, the README and the record |
| **D — the contract is read, the tables are checked against it** (this decision) | one source, the specification; the two tables are checked against it | the build depends on the specification — until the owner decides where the limits live without it |

## Consequences

- **The public API does not change.** Nothing in this decision touches a type, a member or a signature.
- **The entrance and the record follow the contract.** A limit that changes in ADR-0020 fails the build until both tables
  follow it.
- **The allocation row is honest now.** It carries the target of ADR-0032 and the measured values of a run of the
  current code — and the entrance says which machine the other numbers come from.
- **The dependency on the specification is visible.** It is the contract of the repository; when it goes, the limits
  have to move with it.

**Verified by:** `Readme_Budgets` (three tests), the mutations
`a-budget-limit-in-the-readme-edited-by-hand` and `a-budget-limit-in-the-record-edited-by-hand`, and the three tables of
the README and the record.
