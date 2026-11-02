# Benchmarks

> The measured numbers behind the performance targets of the specification
> (§16). Every number here comes from a run on a stated machine; a number
> without a machine is a rumour.

## Status

**The single-word index is measured; the budget of §16 is not met at three
million entries.** The numbers are below, with the machine and the corpus; the
reason and the options are in
[ADR-0010](./adr/0010-the-query-budget-at-three-million-entries.md) (Proposed).
`MultiWord`, typo correction and `Sync` are measured with the pull requests that
build them (PR-004 … PR-009); the benchmark project
(`benchmarks/Nordmann.LUI.Benchmarks/`) arrives with PR-009.

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

---

## Results: the single-word index (PR-003)

Run on **2026-11-02**. Machine: Intel Xeon @ 2.80 GHz, **4 cores**, 15 GB RAM,
Ubuntu 24.04, .NET 10.0.14 (SDK 10.0.114), SQLite 3.53.3
(`Microsoft.Data.Sqlite` 10.0.14), the database on a local disk and in the
page cache. This is a cloud VM; the reference machine of Anhang A is a
2.1 GHz Xeon with the same core count, so the two are comparable.

**Corpus.** The 685,829 words of the German word list (CC0,
`tools/fetch-wordlist.sh`), then compounds of two random words — the first
capitalised — up to the number of entries. Weights: a long tail
(`1 + 10⁶·u³`, `u` uniform), seed 42. The compound corpus is **dense in
trigrams**: 3 M entries from 685 k words means 2.3 M compounds.

**Queries.** 2,000 inputs per run, seed 7: two thirds the *beginning* of a
random entry, one third a piece from its *middle*, 1–8 characters (about 250
per length), warmed with 500 queries first. Default options: 20 hits, 500
candidates, `WordStartThenSimilarity`, Jaro-Winkler. One query is
`index.Query(input).Execute()`, measured in-process on one thread.

**Reproduce.**

```bash
tools/fetch-wordlist.sh
LUI_PERF=1 LUI_PERF_ENTRIES=3000000 \
  dotnet test tests/Nordmann.LUI.Tests -c Release --filter QueryPerformance \
  --logger "console;verbosity=detailed"
# LUI_PERF_DB=<path> keeps the database, so that the next run skips the build
```

### The query

| Entries | p50 | p95 | max | allocation p50 / p95 |
|---:|---:|---:|---:|---:|
| 200,000 | **0.86 ms** | **2.2 ms** | 4.0 ms | 58 KB / 235 KB |
| 1,000,000 | 2.02 ms | 7.4 ms | 25 ms | 63 KB / 267 KB |
| 3,000,000 | 3.57 ms | 26.5 ms | 120 ms | 79 KB / 290 KB |
| *target (§16), 3,000,000* | *≤ 2 ms* | *≤ 5 ms* | | *≤ 250 KB* |

**After the join was moved to the primary key** (`doc_id = f.rowid & 4294967295`
instead of a seek on `s_rowid`, [ADR-0006](./adr/0006-the-single-word-index.md)),
same index, same queries: 200,000 entries p50 **0.82 ms**, p95 **1.8 ms**;
1,000,000 entries p50 **1.39 ms**, p95 **6.1 ms**. The 3,000,000 row above was
measured before and has not been repeated.

By length of the input (p50 / p95, ms), before that change:

| Chars | 200 k | 1 M | 3 M |
|---:|---:|---:|---:|
| 1 | 0.96 / 1.5 | 1.15 / 1.7 | 1.41 / 2.1 |
| 2 | 0.79 / 1.4 | 1.01 / 1.9 | 1.34 / 2.2 |
| 3 | 1.44 / 2.4 | 1.99 / 3.0 | 2.34 / 3.5 |
| 4 | 1.40 / 2.4 | 2.44 / 4.4 | 3.29 / 7.7 |
| 5 | 0.73 / 2.4 | 2.72 / 6.4 | 4.85 / 14.2 |
| 6 | 0.61 / 2.0 | 2.52 / 9.0 | 7.83 / 25.8 |
| 7 | 0.68 / 2.2 | 3.09 / 10.6 | 10.62 / 33.6 |
| 8 | 0.81 / 1.9 | 3.75 / 10.9 | 14.42 / 48.8 |

**Reading.** The prefix path (1–2 characters) is a range scan and stays at
≈ 1.4 ms / 2.2 ms even at 3 M. From three characters the time is FTS5's: the
slowest queries spend 99.5 % of their time in the candidate statement (ranking
0.4 ms, payload 0.3 ms). A phrase made of frequent trigrams with few matches
walks the posting lists to the end — see ADR-0010 for the mechanism and the
measured alternatives (no join, `AND` of trigrams: no change).

**The target is missed at 3 M** — p50 by a factor of 1.8, p95 by a factor of 5 —
**and held at 200 k.** At 1 M the median is on the line and the p95 is 1.5×
over. The allocation target holds at the median at every size.

**What was checked afterwards** (ADR-0010, "Follow-up measurement"): the
plan, the indexes and the ordering are the specification's. The same texts
loaded in rowid order are 3× faster to query (warm) and 8× faster to build than
the row-by-row index measured above; whether that carries over to the cold p95
is shown in ADR-0010: **bulk-built and merged, the 3 M index answers at p50
1.3–1.8 ms and p95 13.5 ms, and builds in about 100 s** (the targets: ≤ 2 ms,
≤ 5 ms, ≤ 3 min). SQLite settings (memory map, cache, page size, FTS5 `pgsz`,
journal mode) change nothing.

### The write

One call, own transaction (the writer connection of the catalog), over the
index above; 500 calls, a third each `UpdateWeight`, `Upsert` of an existing
key and `UpdatePriority`:

| Entries | p50 | p95 | target p95 |
|---:|---:|---:|---:|
| 200,000 | 0.18 ms | **1.0 ms** | ≤ 1 ms |
| 1,000,000 | 0.26 ms | 5.7 ms | ≤ 1 ms |
| 3,000,000 | 0.28 ms | 2.6 ms | ≤ 1 ms |

The median is a fifth of a millisecond at every size; the p95 is the FTS5
segment merges that a write now and then triggers. **Missed at 1 M and 3 M.**

### The build

200,000 entries in 13.8 s (14,500/s); 1,000,000 in 87 s (11,500/s);
**3,000,000 in 334 s (9,000/s) — the target is 3 minutes, missed.** The file is
913 MB at 3 M. The build is `Upsert` inside one transaction of the application
on a connection with a 512 MB cache. What moved it:

| Per entry | |
|---|---:|
| `Upsert` through a fresh command (first version) | 184 µs |
| `Upsert` through a prepared statement (this version, [ADR-0009](./adr/0009-writes-use-prepared-statements.md)) | 59 µs at 50 k |
| the same without the three triggers (content table, generated columns, four indexes) | 11 µs |

The rest is the search index, whose segments are merged as it grows. The bulk
path — rowid-ordered loading, one merge at the end — belongs to `Sync` and
`Rebuild` (PR-007) and is measured there.

### Opening a catalog

`LuiCatalog.Open` probes the folding of the loaded SQLite once per process
([ADR-0007](./adr/0007-the-catalog-checks-the-sqlite-it-runs-on.md)): **110 ms**
cold (JIT included), **37 ms** warm, 2,466 code points. Every further `Open` in
the process skips it.

---

## Results: the multi-word index (PR-004)

Same machine, SQLite and method as above. **Corpus:** titles of two to four words, each from the German word
list (average 36 characters — a recipe title is nearer 25), weights as above. **Inputs** (2,000): two thirds the
beginning of a title or a piece of it, 1–8 characters; one third the first two words of a title, 3–5 letters of
each, in either order. Row-by-row build through the triggers.

```bash
LUI_PERF=1 LUI_PERF_KIND=multi LUI_PERF_ENTRIES=1000000 \
  dotnet test tests/Nordmann.LUI.Tests -c Release --filter QueryPerformance \
  --logger "console;verbosity=detailed"
```

| Entries | index | build | file | query p50 / p95 | alloc p50 / p95 | write p50 / p95 |
|---:|---|---:|---:|---:|---:|---:|
| 200,000 | `s_compact` only, `s_norm` virtual (this PR) | 30 s | 91 MB | 1.26 / 3.4 ms | 47 / 263 KB | 0.24 / 1.5 ms |
| 1,000,000 | `s_compact` only, `s_norm` virtual (this PR) | 157 s | 445 MB | 3.16 / 14.1 ms | 83 / 273 KB | 0.32 / 5.7 ms |
| 200,000 | two columns (first version) | 33 s | 126 MB | 1.64 / 4.9 ms | 46 / 263 KB | 0.31 / 1.9 ms |
| 1,000,000 | two columns (first version) | 180 s | 619 MB | 4.16 / 21.0 ms | 83 / 273 KB | 0.35 / 4.8 ms |
| *target (§16)* | | *3 M ≤ 3 min* | *1 M ≤ 300 MB* | *3 M ≤ 2 / 5 ms* | *≤ 250 KB* | *≤ 1 ms p95* |

- **Allocation.** With `F23`'s Dice the 95th percentile was **3,651 KB** (a dictionary per comparison, 500
  comparisons); with the allocation-free Dice of this PR it is 263 KB ([ADR-0011](./adr/0011-the-multi-word-index.md)).
- **Missed:** the query at 1 M (p50 3.2 ms, p95 14.1 ms — the two-word inputs of frequent trigrams, `Rent Schwe` 54 ms
  for 38 candidates; the mechanism of ADR-0010), the file size (1.5× the target; long titles), the 3 M build
  (extrapolated: about eight minutes). Held: the query at 200 k (p50 1.3 ms, p95 3.4 ms), the allocation, the write p95
  at 200 k (1.3 ms against 1 ms: just over).
- **One indexed column instead of two** is built (ADR-0011): −36 % file, −40 % build, −30 % query latency in the
  500 k experiment (`MultiWordLayoutExperiment`), −28 % file, −13 % build, −24 % to −33 % latency in the table above (two indexed columns → one, `s_norm` virtual).

