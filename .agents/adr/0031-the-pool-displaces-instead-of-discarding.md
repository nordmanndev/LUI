# 0031 — The pool of the buffers displaces instead of discarding

- **Status:** Accepted
- **Date:** 2026-11-05
- **Builds on:** [0032](./0032-the-allocation-of-a-query.md) (the allocation of a query, the pool of the buffers),
  [0020](./0020-the-performance-budgets-measured.md) (the budgets of ADR-0020, measured)
- **Affects:**
  - `src/Nordmann.LUI/Querying/ScoredBuffer.cs` (the rule of the pool, the doc comment)
  - `tests/Nordmann.LUI.Integration.Tests/AllocationPoolTests.cs` (new)
  - ADR-0020, `docs/testing.md`, CHANGELOG, ADR-0032
- **Issue:** #30 (the review of PR #53, finding I1)

## Context

ADR-0032 gave every thread a pool of the buffers of the candidates: a query that reads 500 candidates needs three arrays
of 500 entries — 60 KB — and they are the same size every time. The pool had twelve places, and one query (with the
typo correction) needs nine of them at the same time.

The review of PR #53 found the flaw: **a service does not have one budget**. The length of the buffers follows the budget
of the candidates, and the budget is a query option. A second budget needs new buffers, a third one more — and the
pool of twelve filled up with the buffers of the budgets of the past.

What the pool did then was **worse than no pool at all**: the buffer that was asked for was not in the pool, so it was
allocated new — and the buffer that was in the pool was thrown away, because there was no place left for it. Every
query allocated everything anew, and the memory of the pool was dead weight.

Measured (200,000 entries, the typo path, ten candidate budgets one after another in one process):

| The query with the typo path | the base `8c3c5be` (no pool) | PR #53 as it was |
|---|---:|---:|
| the budget of 1,000 at the end of the sequence | 83.43 KB | **294.93 KB** |

3.53× worse than the base — a pool that had become a liability.

## Decision

**The pool keeps twelve buffers. When there is no place left, the smallest buffer goes and the new one takes its
place.**

The smallest is the one that goes, because the big buffers are the expensive ones: the union of the corrections holds
`limit * 4` entries — 80 KB at a budget of 500. Keeping them and dropping a small one keeps the memory of the thread
useful; the price is that the dropped one is allocated again, and the smallest buffer of a query is the one of the hits
(800 B at the default of 20 hits).

The rules of the pool are therefore three:

1. a buffer is only lent to a query that wants **exactly** that length,
2. the arrays are cleared before they are returned,
3. when there is no place left, **the smallest buffer is replaced** by the new one.

## What was measured

The same probe (200,000 entries, the typo path, the median of 31 rounds), the base, the branch as it was and the branch
after the correction:

| The query with the typo path | the base `8c3c5be` | PR #53 before | after the correction |
|---|---:|---:|---:|
| one budget (1,000), a fresh process | 83.43 KB | 19.63 KB | **19.63 KB** |
| ten budgets (100 … 1,000) in one process | 83.43 KB | 294.93 KB | **59.68 KB** |
| the budgets 500, 600, 1,000, then 700 (the guard) | — | 95.4 KB | **39.66 KB** |

The correction is worth what it claims: the query after a change of the budget allocates **59.68 KB instead of
294.93 KB** — and less than the base that has no pool at all. The remaining 59.68 KB are the two buffers that the
displacement had to give up (the candidates and what remains of the union); they are the price of a pool of twelve
places for the budgets of a whole service.

**The guard:** `tests/Nordmann.LUI.Integration.Tests/AllocationPoolTests.cs` —
`The_allocation_of_a_query_stays_low_after_the_candidate_budget_changed` — measures the query after the budget has
changed three times and asserts **64 KB**. The value was 95.4 KB before the correction and is 39.8 KB after it (the
probe of the same sequence measures 39.66 KB over 31 rounds).

## What was tried and rejected

- **The number of the hits travels with the buffer of the hits.** The buffer of the hits had the length of the number
  of the hits, and that number changes with every query — so every number was a new length in the pool. The obvious
  repair is a buffer of the fixed length of the maximum of the hits, with the number travelling beside it. It was
  implemented (`Ranker.Rank` returns the buffer **and** the number, the reader of the hits takes both) and measured:
  **39.8 KB against 39.8 KB** — no difference at all. Three production files and a unit test would have carried a
  change that buys nothing, so it was taken back out. The length of the buffer of the hits is therefore still the
  number of the hits, and that buffer is allocated per query — it is the smallest one of a query (800 B at 20 hits).
- **A buffer that is lent to a query that wants fewer entries than it has.** Measured and rejected: a buffer per
  format makes the pool worse than no pool (295 KB against 83 KB), because every different length evicts the others.

## The guard must not be flaky

The first version of the guard measured the query on the thread of the test. `ScoredBuffer` is `[ThreadStatic]`, and
xUnit lends its threads to one test after another — so the pool that the test before had left behind decided the
number, and the guard was green in one run and red in the next. **A guard that has never been seen red proves
nothing**, and one that is red at random teaches nothing either.

The scenario therefore runs on a thread of its own (`OnOwnThread`), where the pool starts empty. The measurement is
then stable: three runs, three times **39.8 KB**.

## Consequences

- **The public API does not change.** The rule of the pool is internal; the freeze of ADR-0026 is not touched.
- **The pool is no longer a trap.** A service that changes its budget of the candidates — or that serves several
  budgets — no longer pays for every query with a new allocation of the big buffers.
- **Twelve places are still twelve.** A budget that changes every time would displace the buffers again and again; the
  price of that is one big buffer per query, not four. More places would cost more memory per thread — the decision
  about the number of places belongs to a measurement of a real workload, not to this record.
- **The doc comment of the pool no longer promises what does not exist.** It said that the first query of a thread
  allocates and the others do not; that is true only while the budgets stay the same. It now says what is true: a query
  that finds its buffers allocates nothing but its own texts.
