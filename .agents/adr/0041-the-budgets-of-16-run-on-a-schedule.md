# 0041 — The budgets of §16 run on a schedule: the bytes are asserted, the times are reported

- **Status:** Accepted
- **Date:** 2026-11-07
- **Builds on:** [0020](./0020-the-performance-budgets-measured.md) (the budgets, measured),
  [0032](./0032-the-allocation-of-a-query.md) (the allocation and its target),
  [0040](./0040-the-budget-table-of-the-entrance-is-a-promise.md) (the table of the entrance and its guard)
- **Affects:**
  - `.github/workflows/performance.yml` (new), `tests/Nordmann.LUI.Integration.Tests/PerformanceBudget.cs` (the rule of the
    machine), `tests/Nordmann.LUI.Integration.Tests/PerformanceBudget_Machine.cs` (new)
  - `docs/testing.md` (the gates and the map), `docs/benchmarks.md` (the lane)
- **Issue:** #65

## Context

The targets of §16 are asserted by the test `PerformanceBudgets` — and that test lives in the layer of the
measurements, which no job of the CI runs. So the seven targets of §16 are asserted **never**: they are a ritual, and
the numbers of the record come from a run on the machine of the owner.

The table of the targets holds two kinds of target at once:

- the **sizes** — bytes: the allocation of a query (p50 ≤ 60 KB, p95 ≤ 100 KB), the memory of the typo dictionary
  (≤ 200 MB), the file of a million titles (≤ 500 MB). The bytes are the bytes: they are the same on every machine, and
  the run of 2026-11-06 measured 46 / 51 KB on a machine other than the one of the record, with the same result.
- the **times** — the query, the correction, the write, the build. A time belongs to the machine it was measured on
  (Intel Xeon 2.1 GHz, 4 cores, 16 GB, Ubuntu 24.04): the specification says so, and the record repeats it.

A lane on a shared runner (4 cores, 16 GB, noisy neighbours) therefore cannot decide a time target — and a lane that
failed on a time every night would be a lane that cries wolf. A lane that skipped the times would measure nothing, and
a lane that asserted the times as they are would fail for reasons that have nothing to do with the code.

The decision that is missing is therefore not the lane, but the **rule**: what does a target mean on a machine that is not
the one of the record?

## Decision

1. **A lane of its own.** `.github/workflows/performance.yml` runs the targets of §16 on a schedule (nightly) and can be
   dispatched by hand. A file of its own, because a job cannot have a schedule of its own.
2. **Two kinds of target, two kinds of verdict.** A target whose unit is a **time** belongs to the machine it was measured
   on: on the machine of the record it is asserted, as before; on any other machine it is **reported** — the number stands
   in the log with the machine named, and it never fails the run. A target whose unit is a **size** is asserted
   everywhere: the bytes are the bytes.
3. **The default does not weaken.** Without a machine in the scenario of the run, the run is on the machine of the
   record and asserts everything, as before. The lane writes the machine `other` into the scenario file on purpose and
   says so in the summary of the run. A run loses no target it had before, and no target is quietly lowered.
4. **The corpus is checked, not hoped for.** The lane fetches the whole word list (685,829 words) — and the fetch is
   the guard: it checks the number of lines and fails loudly. A missing list makes the lane fail instead of measuring
   a sample and calling it the scenario.
5. **The machine belongs to the number.** The lane writes its machine (cores, memory, disk) into the summary of the run,
   and the numbers that go into `docs/benchmarks.md` carry the machine they were measured on — the rule of the record.

## Alternatives

| | For | Against |
|---|---|---|
| **A — no lane; the ritual stays manual** | nothing to maintain | the targets are never checked; the only day a target is checked is the day somebody remembers |
| **B — a lane that asserts every target** | one rule for everything | a shared runner fails on the times it cannot decide; a red lane every night teaches nobody to read it |
| **C — a lane that reports everything** | nothing ever fails | the sizes are reported too; a target that is missed stays missed, quietly |
| **D — the times are reported, the sizes are asserted** (this decision) | the rule follows the measure: a time has a machine, a size has none | two kinds of target in one table; the unit of a target decides its verdict |

## Consequences

- **The targets of §16 are checked every night.** The four that are sizes — the allocation of a query, the memory of the
  dictionary, the file — fail the lane when they are missed.
- **The times stay the owner's.** The lane reports them with the machine named; the run on the machine of the record
  decides them, as it always did.
- **The lane needs a runner with the disk and the memory of the scenario.** The first run says how long the build of three
  million entries takes on a runner; the timeout of the job is set from that measurement, not from an assumption.
- **A size that is missed is never lowered quietly.** It fails the lane (ADR-0020, ADR-0032).

**Verified by:** `PerformanceBudget_Machine` (six tests: a time belongs to its machine, a size belongs to no machine,
another machine reports a missed time and fails a missed size, the machine of the record fails a missed time, a run that
is not the scenario reports everything), the lane `performance.yml` and the summary of its run.

## Note (2026-11-12, issue #122)

[ADR-0058](./0058-the-measurement-is-a-layer-of-its-own.md) takes the variables of the environment away: the scenario
of a run lives in `performance.scenario.json`, and the lane writes that file before it measures.

The decision stands; the sentences above that named a variable of the environment that died, the name of a gate that
is gone (`PerformanceFact`, `TestGates`) or a path of the file of the measurements (`PerformanceBudgets.cs`,
`BudgetPerformance.cs`) are rewritten — each of them because the variable died with ADR-0058 and took the gate of the
measurements with it, or because that file moved into the layer of the measurements.

- **The machine of a run is a member of the scenario file** (`"machine": "measurement"` or `"other"`), not a variable of
  the environment. The default does not weaken: without a machine the run is on the machine of the record.
- **The gate of the whole word list is the fetch.** The lane fetches the list first, and the fetch fails loudly when the
  list is not the one of 685,829 lines.
