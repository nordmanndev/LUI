# 0065 — The first load keeps its path, and its budget is three minutes again

- **Status:** Accepted (owner, 2026-11-15)
- **Date:** 2026-11-15
- **Supersedes:** —
- **Affects:** `tests/Nordmann.LUI.Testing/Budgets/budgets.json`, `tests/Nordmann.LUI.Performance.Tests/BudgetPerformance.cs`, `.github/workflows/performance.yml`, `docs/benchmarks.md`, `README.md`

## Context

The first load of a file no other process writes is `Sync` with the batch of [ADR-0020](./0020-the-performance-budgets-measured.md)
— `BatchSize = 1_000_000`, the fewest commits. ADR-0020 gave it the target of **3 minutes**: 172.5 / 174.2 s for three
million entries on two containers — met. On 2026-11-04 the same code took 194.9 / 205.9 s on a container started anew; the
build writes a file of nearly a gigabyte and follows the disk, so the target was loosened to **4 minutes** in the same
breath, and issue #32 was opened: *a faster first load*.

The issue named four levers: the **checkpoint interval of the write-ahead log** while the sync loads, the **content table
in key order**, **`PRAGMA synchronous`** during the first load, and an empty index in one transaction with the content
first and a rebuild after it. The fourth was measured and rejected in [ADR-0016](./0016-sync-check-rebuild.md) — it stays
the repair (`Rebuild()`), not the load.

Two facts of the repository stood in the way of an honest measurement, and both had to be fixed before a number could
mean anything:

- **The lane measured the build with the wrong batch.** The scenario of the run carried the batch of 10,000, while the
  promise of ADR-0020 names the batch of 1,000,000 — the batch is worth about 20 % (ADR-0016). The number the lane
  published was therefore not the number of the promise.
- **The scenario of three million entries does not exist.** Since ADR-0059 the corpora are files in the repository, and
  the words corpus holds **1,908,798** entries. A budget below its scenario is reported, not asserted, and the number at
  three million entries is the one of 2026-11-04.

## Decision

1. **The first load keeps its path.** `Sync` with the batch of 1,000,000, the pragmas where SQLite puts them. **No lever
   goes into the library.**
2. **The checkpoint interval of the write-ahead log is rejected.** It was measured on two machines, against the whole
   corpus of the repository: baseline 120.0 s, with a log of 100,000 pages 122.0 s, baseline again 120.0 s on the machine
   of the record; baseline 172.9 s, the lever 180.8 s, baseline again 165.1 s on the machine of the report. It brings no
   gain, and it doubles the log of the file (229 MB → 424 MB). The pragma stays where SQLite puts it.
3. **The two levers that were not measured are moot.** The content table in key order and `PRAGMA synchronous` during the
   first load: the target holds again, so no lever is needed.
4. **The budget `build-3m` goes back to three minutes (180 s)** — the promise of ADR-0020, unchanged from the original
   target. The four minutes of 2026-11-04 answered a miss of the three minutes at a size the corpus of the repository does
   not have; against the corpus that exists, the build takes **120.4 s** (the machine of the record) and **165.1–172.9 s**
   (the machine of the report).

The owner decided this on 2026-11-15.

## Consequences

- The file of the budgets ([ADR-0056](./0056-the-budgets-live-in-a-file.md)) says 180 s again, and the tables of the
  `README.md` and of `docs/benchmarks.md` follow it — the guard `ReadmeBudgetsTests` holds the three together.
- The budget stays **reported, not asserted** until the corpus reaches three million entries: the words corpus has
  1,908,798. The number at three million entries is the one of 2026-11-04 (172.5 / 174.2 s on one container, 194.9 /
  205.9 s on the next), and it is written down as it is — a target that is missed is written down as missed (rule 5 of
  `docs/benchmarks.md`). The corpus that has three million entries, and the limits measured against it, are the task of
  issue #176.
- The measurement of the lever stays in the tree as a spike (`The_first_load_with_a_longer_write_ahead_log` in
  `BudgetPerformance`): its numbers go into the record, its code goes with the decision. It is not a budget, it asserts
  nothing, and it takes no part in the lane of the schedule.
- The public API, the schema and the dependency set do not move.
