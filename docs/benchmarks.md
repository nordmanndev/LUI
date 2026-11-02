# Benchmarks

> The measured numbers behind the performance targets of the specification
> (§16). Every number here comes from a run on a stated machine; a number
> without a machine is a rumour.

## Status

**No measurements yet.** The benchmark project exists
(`benchmarks/Nordmann.LUI.Benchmarks/`); the measurements arrive with the
feature they measure (PR-009).

## The targets

These are the goals from §16. They are written down here first, so that the
measurement has something to contradict.

| Measurement | Scenario | Target |
|---|---|---|
| Query without correction | 3 M entries, inputs of 1–8 characters | p50 ≤ 2 ms, p95 ≤ 5 ms |
| Query with typo correction | the same | p95 ≤ 10 ms |
| Allocation per query | the same | ≤ 250 KB |
| `Upsert` / `UpdateWeight` / `Remove` | one at a time, own transaction | p95 ≤ 1 ms |
| Build-up | 3 M entries, `Owned` | ≤ 3 min |
| Typo dictionary | 700 000 distinct words | ≤ 200 MB, build ≤ 10 s |
| File size | 1 M entries, `MultiWord` | ≤ 300 MB including the content table |

## The rules for a number in this file

1. **The machine is named.** CPU, cores, RAM, OS, .NET version, SQLite version.
2. **The corpus is named.** How it was generated, how many entries, what
   distribution.
3. **Old and new are measured alternately**, and the **median** is taken. One
   machine, one load budget — a single run tells you more about the weather
   than about the code.
4. **Hits and misses are counted separately.** A p95 over a corpus where
   nothing matches measures a different thing than a p95 over a corpus where
   everything matches.
5. **A target that is missed gets written down as missed**, with the reason.
   The target is not quietly lowered.

## The measurement machine

The reference measurements of the specification (Anhang A) were taken on
2026-11-01 on an Intel Xeon 2.1 GHz, 4 cores, 16 GB RAM, Ubuntu 24.04,
.NET 10.0.14, SQLite 3.53.3, with the database in the page cache.

The numbers this repository publishes come from the machine the CI ran on —
which is a smaller one. The difference is stated, not hidden.
