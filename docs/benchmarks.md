# Benchmarks

> The measured numbers behind the performance targets of the repository. Every
> number here comes from a run on a stated machine; a number without a machine
> is a rumour.
>
> The **targets** live in one file —
> [`tests/Nordmann.LUI.Testing/Budgets/budgets.json`](../tests/Nordmann.LUI.Testing/Budgets/budgets.json)
> (ADR-0056). The tables below show them beside the numbers that were measured
> against them; a test holds the two together.

## Status

**Every target of §16 is measured in its scenario and met** (PR-009,
ADR-0020) — three of them as draft 2 set them, five as the owner
stated them anew after the measurement (2026-11-04, ADR-0010, ADR-0020). The table is the output of
`BudgetPerformance`, which asserts every row. The allocation was lowered to its target (ADR-0032) and measured again
on 2026-11-06: 46 / 51 KB. The benchmark project (`benchmarks/Nordmann.LUI.Benchmarks/`) measures the code on the same
corpus.

| Target | Measured at the scenario | Limit |
|---|---:|---:|
| query p50 | 1.34–1.71 ms | ≤ 2 ms |
| query p95, 1–4 characters | ≤ 3.1 ms | ≤ 5 ms |
| query p95, 5–8 characters | 13.4–17.5 ms | ≤ 20 ms |
| query with correction p95 | 27.9–32.5 ms | ≤ 35 ms |
| allocation p50 / p95 | 46 / 51 KB | ≤ 60 / ≤ 100 KB |
| single write p95 | 0.51–0.78 ms | ≤ 1 ms |
| build of 3 M | 172.5–174.2 s on one container, 194.9–205.9 s on the next (`Sync`, batches of 1,000,000) | ≤ 240 s (#32) |
| typo dictionary of 685,739 words | 138 MB, 4.1–4.8 s (the machine of the record), 4.3 s (the run of 2026-11-12) | ≤ 200 MB, ≤ 10 s |
| typo dictionary of two errors, 685,739 words | 240 MB (2026-11-12) | ≤ 240 MB |
| file of 1 M titles | 457 MB | ≤ 500 MB |

**The numbers below were measured on the corpus that was generated.** Since ADR-0059 the corpus is a file in the
repository, and the corpora have their own sizes: the words corpus has 1,908,798 entries, so the **scenario of three
million entries does not exist** in it. A budget below its scenario is **reported, not asserted** — the numbers of the
table are the ones of the runs of 2026-11-04 to 2026-11-12, and **the counts of the budgets of §16 are re-decided
against the measured corpora in the next round**. Nothing here is quietly lowered: the file of the budgets still names
3,000,000 entries, and the run says that it did not reach them.

The dictionary of two errors is the one the **distance `TwoErrors`** builds (ADR-0054): the promise of 200 MB belongs to
the dictionary of one error, and the two-error dictionary measured **240 MB** — 40 MB more than the target of §16 allows
(≤ 200 MB). That is a **miss of the old promise** — it is written down as one, in the ADR of the typo distance and in
[`docs/typo-dictionary.md`](typo-dictionary.md), and no limit is lowered quietly here: the file states 240 MB, the number
that was measured.

## The targets

These are the goals of the file of the budgets, in the order of the file. They are written down here as well, so that
the measurement has something to contradict.

| Measurement | Scenario | Target |
|---|---|---|
| Query without correction | 3 M entries, inputs of 1–8 characters | p50 ≤ 2 ms; p95 ≤ 5 ms for 1–4 characters, ≤ 20 ms for 5–8 |
| Query with typo correction | 3 M entries, a beginning with one typo | p95 ≤ 35 ms |
| Allocation per query | 3 M entries, inputs of 1–8 characters, the candidate budget of 500 | p50 ≤ 60 KB, p95 ≤ 100 KB |
| `Upsert` / `UpdateWeight` / `Remove` | one at a time, own transaction | p95 ≤ 1 ms |
| Build-up | 3 M entries, `Embedded`, a first load by `Sync` | ≤ 4 min |
| Typo dictionary | 685,739 distinct words | ≤ 200 MB, build ≤ 10 s |
| Typo dictionary, two errors | 685,739 distinct words | ≤ 240 MB |
| File size | 1 M titles of 36 characters on average, `MultiWord` | ≤ 500 MB including the content table |

The query p95, the correction, the allocation, the file size and the build were stated anew by the owner on 2026-11-04
after they were measured (ADR-0010, ADR-0020): a **loosening**, written down as such. The targets of draft 2 were p95
≤ 5 ms for every length, the correction ≤ 10 ms, ≤ 250 KB, ≤ 300 MB, ≤ 3 min.

## The rules for a number in this file

1. **The machine is named.** CPU, cores, RAM, OS, .NET version, SQLite version.
2. **The corpus is named — and it names itself.** The corpus is a file in the
   repository (ADR-0059); its header names the source, the licence, the snapshot
   date, the number of entries, the format and the kind. A corpus that is
   generated is not named: it is a different one on every run.
3. **Old and new are measured alternately**, and the **median** is taken. One
   machine, one load budget — a single run tells you more about the weather
   than about the code.
4. **Hits and misses are counted separately.** A p95 over a corpus where
   nothing matches measures a different thing than a p95 over a corpus where
   everything matches.
5. **A target that is missed gets written down as missed**, with the reason.
   The target is not quietly lowered.

## The corpora

The corpus of a measurement is a **file in the repository** (ADR-0059), not something that is generated at the
start of a run. The file name is the identity; there is no manifest. Four files, gzip-compressed, in
[`tests/TestData/corpora/`](../tests/TestData/corpora):

| File | Entries | Format | Kind | Source | Licence | Snapshot |
|---|---:|---|---|---|---|---|
| [`words.txt.gz`](../tests/TestData/corpora/words.txt.gz) | 1,908,798 | `text⇥weight` | single words | [`kkrypt0nn/wordlists`](https://github.com/kkrypt0nn/wordlists), `wordlists/languages/german.txt` | none stated by the repository for the lists it gathers itself — the owner accepted that | 2026-11-12 |
| [`places.tsv.gz`](../tests/TestData/corpora/places.tsv.gz) | 5,779,011 | `text⇥weight⇥context` | titles | [`download.geonames.org/export/dump/`](https://download.geonames.org/export/dump/) — `allCountries.txt`, `countryInfo.txt`, `admin1CodesASCII.txt`, `admin2Codes.txt` | **CC BY 4.0** | 2026-11-12 |
| [`movies.tsv.gz`](../tests/TestData/corpora/movies.tsv.gz) | 652,001 | `text⇥weight` | titles | [`datasets.imdbws.com`](https://datasets.imdbws.com/#help) — `title.basics.tsv`, `title.ratings.tsv` | the **non-commercial** IMDb datasets | 2026-11-12 |
| [`wiki-titles.tsv.gz`](../tests/TestData/corpora/wiki-titles.tsv.gz) | 5,167,623 | `text⇥weight` | titles | [`dumps.wikimedia.org`](https://dumps.wikimedia.org/dewiki/latest/dewiki-latest-all-titles.gz) | **CC BY-SA 4.0** + GFDL | 2026-11-12 |

Three rules hold for every corpus:

- **The header is a block of comment lines at the top of the file, ended by an empty line.** It names the source, the
  licence, the snapshot date, the number of entries, the format and the kind. **A `#` inside an entry is data**: a film
  title that begins with `#` is a title. The **format** is machine-readable — the list of the columns of an entry, the
  meaning of the weight stands in a `note:` — and **the reader checks it**: a file whose header names a format that does
  not fit its entries, or a format nobody knows, is refused.
- **The order of the entries is the SHA-256 of their text.** A **prefix is therefore always a representative sample**,
  never the alphabetically first *n* — that was the bug of the old generator, whose prefix of 200,000 entries held
  nothing but the letters a to f.
- **The weight belongs to the file.** The places carry their population, the films their votes; the words and the
  Wikipedia titles carry the derived weight of their text, `1 + floor(9999 · (1 − u)³)` with `u` the first four bytes
  of the SHA-256, computed in exact integer arithmetic.

The **budgets** name the corpus of each target
([`tests/Nordmann.LUI.Testing/Budgets/budgets.json`](../tests/Nordmann.LUI.Testing/Budgets/budgets.json),
ADR-0056), and a measurement takes the corpus of the budget whose target it measures. The scenario of the run
(`tests/Nordmann.LUI.Performance.Tests/performance.scenario.json`) carries the prefix length, the batch, the minimum
weight, the optimization, the machine and the database — and no corpus: the corpus is not named twice.

**What is not in the repository, and why.** The books of the Amazon scrape (1,601 titles), the German names (27,273)
and the Cologne first names (9,620): below the floor of 100,000 entries. The street addresses (5,858,056 measured): a
street address is a *lookup*, not a string somebody types into an autocomplete — a corpus is made of search strings. The
list of all `.de` domains: DENIC hands out no zone files to third parties. The raw rows of the names dataset
(6,040,701 German rows, one per person): personal data of identifiable people (C-413/23). **The line is the name
level:** *Müller is rank 1 in Germany* says nothing about a person and is published; *Sven Liebscher, M, DE* is a person
and is not.

**Rebuild them.** [`tools/make-corpora.py`](../tools/make-corpora.py) writes the four files: it fetches the sources
if they are missing, sorts the entries by the hash of their text, and writes the header of each file. The count in the
header is the count of the file — the builder counts the entries while it streams them.

```bash
python3 tools/make-corpora.py /tmp/corpora-src tests/TestData/corpora --snapshot 2026-11-12
```

## The measurement machine

The reference measurements of the specification (Anhang A) were taken on
2026-11-01 on an Intel Xeon 2.1 GHz, 4 cores, 16 GB RAM, Ubuntu 24.04,
.NET 10.0.14, SQLite 3.53.3, with the database in the page cache.

The numbers this repository publishes come from the machine the CI ran on —
which is a smaller one. The difference is stated, not hidden.

## The lane

The targets of §16 run on a schedule of their own
(`.github/workflows/performance.yml`, ADR-0041): every night, and by hand when
somebody wants to see it. The lane writes the scenario of the run into
`tests/Nordmann.LUI.Performance.Tests/performance.scenario.json` and then runs the budgets of the
measurement layer (ADR-0058). It fetches nothing: the corpora are files in the repository (ADR-0059), so the lane
measures the files that are checked in. The word list of the parity is a different thing — the lane of the CI fetches
it, and the fetch is the guard that fails loudly when the list is not the one of 685,829 lines.

The lane runs on a shared runner — not on the machine of the record. That changes what a target means there:

| Kind of target | On the machine of the record | On the lane |
|---|---|---|
| a **size** — bytes: the allocation, the memory of the dictionary, the file | asserted | **asserted** — the bytes are the bytes |
| a **time** — the query, the write, the build | asserted | **reported** — a time belongs to the machine it was measured on |

The machine of a run is a member of the scenario file: `"machine": "measurement"` is the machine of the record and
asserts everything, as before, `"machine": "other"` is another one. The lane writes `"other"` on purpose, and the
summary of its run says which machine it was. A size that is missed fails the lane; a time that is missed in the
lane stands in the log with the machine named.

The numbers of the lane are **not** the numbers of this file: a runner is smaller and louder than the machine of the
record. What the lane decides is whether the sizes still hold.

**The first run** (2026-11-09, `7ea9e77`, a hosted `ubuntu-24.04` with 4 cores, 15 GB and 85 GB of free disk; issue #99):
every target that applies was met, the times as reported numbers. The query p50 was 0.87 ms, the p95 for 5–8
characters 9.04 ms, the corrected p95 20.64 ms. The allocation p50 was 45.6 KB and p95 51.0 KB. The build of 3 M
entries took 148.5 s, and the typo dictionary of the whole list took 1.95 s and 138 MB. The tests took 8.3 minutes;
the lane is sized by that: a timeout of 30 minutes, a hang timeout of 20.

The one red was the lane's, not the library's. The target of one million titles built three million, because
The `entries` of 3,000,000 of the single-word index also enlarged the titles scenario. Its file of 1,309 MB is
≈ 436 MB per million, under the 500 MB of the target. A run now never builds more than the scenario of its target.

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
# the corpus is in the repository (ADR-0059): there is nothing to fetch
# the scenario of the run is a file: set "prefixLength" to 3000000 in it, then
dotnet test tests/Nordmann.LUI.Performance.Tests -c Release --filter QueryPerformance \
  --logger "console;verbosity=detailed"
# the member "database" keeps the database, so that the next run skips the build
```

### The query

| Entries | p50 | p95 | max | allocation p50 / p95 |
|---:|---:|---:|---:|---:|
| 200,000 | **0.86 ms** | **2.2 ms** | 4.0 ms | 58 KB / 235 KB |
| 1,000,000 | 2.02 ms | 7.4 ms | 25 ms | 63 KB / 267 KB |
| 3,000,000 | 3.57 ms | 26.5 ms | 120 ms | 79 KB / 290 KB |
| *target (§16), 3,000,000* | *≤ 2 ms* | *≤ 5 ms* | | *≤ 250 KB* |

**After the join was moved to the primary key** (`doc_id = f.rowid & 4294967295`
instead of a seek on `s_rowid`, ADR-0006),
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

### The linked index

A `Linked` index over a table of the application (ADR-0015):
a contentless FTS5 table and LUI's table without text; the text comes from the application's table by its primary
key. Against an `Embedded` index of the same entries, single-word, the whole word list:

| | 200,000 rows | 1,000,000 rows |
|---|---:|---:|
| `Sync` of the table | 3.5–3.9 s | 26.7 s |
| `UpsertMany` of the same entries into `Embedded` | 8.2 s | 50.8 s |
| query p50 / p95, linked | 0.52 / 1.11–1.17 ms | 1.10 / 3.43 ms |
| query p50 / p95, embedded | 0.59–0.61 / 1.25–1.33 ms | 1.41 / 4.72 ms |
| `Refresh` of one row, p50 / p95 | 0.31–0.33 / 0.51–0.58 ms | 0.94 / 1.67 ms |
| size of the index (spike) | 36 MB (embedded: 45 MB) | — |

`Sync` writes the search index sorted and merges it; `UpsertMany` builds it row by row and does not merge — the
difference is the bulk path of ADR-0010, not the source. Many single refreshes leave deletion marks: p95 1.78 ms
after 3,000 changes at 200,000 rows, 0.82 ms after `Optimize()`.

```text
dotnet test tests/Nordmann.LUI.Performance.Tests -c Release --filter LinkedPerformance --logger "console;verbosity=detailed"
```

The spike of the layout (`LinkedLayoutExperiment`) is gone from the tree (issue #76); its numbers are in ADR-0015, its
code in the history — `git show 0cbc02f:tests/Nordmann.LUI.Integration.Tests/LinkedLayoutExperiment.cs`.

### The batch

`UpsertMany` (ADR-0013) against a loop of single `Upsert`s,
each in a transaction of its own, 100,000 single-word entries:

| | Time | Rate |
|---|---:|---:|
| 100,000 × `Upsert` | 16.9 s | 5,913/s |
| one `UpsertMany` | **3.8 s** | 26,052/s — **4.4×** |

```text
dotnet test tests/Nordmann.LUI.Performance.Tests -c Release --filter BatchPerformance --logger "console;verbosity=detailed"
```

### The build

200,000 entries in 13.8 s (14,500/s); 1,000,000 in 87 s (11,500/s);
**3,000,000 in 334 s (9,000/s) — the target is 3 minutes, missed.** The file is
913 MB at 3 M. The build is `Upsert` inside one transaction of the application
on a connection with a 512 MB cache. What moved it:

| Per entry | |
|---|---:|
| `Upsert` through a fresh command (first version) | 184 µs |
| `Upsert` through a prepared statement (this version, ADR-0009) | 59 µs at 50 k |
| the same without the three triggers (content table, generated columns, four indexes) | 11 µs |

The rest is the search index, whose segments are merged as it grows. The bulk
path — rowid-ordered loading, one merge at the end — belongs to `Sync` and
`Rebuild` (PR-007) and is measured below.

### The sync, the check and the rebuild (PR-007)

ADR-0016. Single-word, the whole word list, 1,000,000 entries in random order,
cache 512 MB. First the experiment that decided the bulk path (`EmbeddedBulkExperiment`, batches of 10,000 unless said —
a spike, gone from the tree since issue #76: `git show 0cbc02f:tests/Nordmann.LUI.Integration.Tests/EmbeddedBulkExperiment.cs`):

| Load | Time | query p95 before / after the merge |
|---|---:|---:|
| row order | 73.4 s | 5.73 / 2.91 ms |
| each batch sorted by its rowid | 64.2 s | 4.47 / 3.04 ms |
| sorted, `automerge` off | 61.9 s | 5.83 / 3.19 ms |
| sorted, `automerge` off, batches of 100,000 | **42.9 s** | — / 1.00 ms p50 |
| content table without triggers, then FTS5 `rebuild` | 48.3 s | 5.22 / 3.24 ms |

The merge decides the query; the number of commits decides the load more than the order does. Then the
implementation (`SyncPerformance`):

| | batch 10,000 | batch 100,000 |
|---|---:|---:|
| `Sync` into an empty index | 77.8 s | 54.9 s |
| the same `Sync` again (nothing changes) | 15.2 s | 13.2 s |
| a `Sync` that changes half, cancelled halfway · run again | 44.3 · 54.7 s | 27.5 · 35.2 s |
| a `Sync` of 100 changes and 100 removals | 15.8 s | 14.5 s |
| `Check(source)` · `Check()` · `Rebuild()` | 10.0 · 2.4 · 16.2 s | 10.4 · 1.7 · 16.2 s |
| `UpsertMany` of the same entries, one transaction | 52.5 s | 53.8 s |
| query p50 / p95 after `Sync` · after `UpsertMany` | 1.22 / 3.86 · 1.68 / 5.39 ms | 1.11 / 3.36 · 1.73 / 5.55 ms |

The §16 build target (3 M in ≤ 3 min) is measured with the benchmarks (PR-009).

**A delta sync beside a built typo dictionary** (ADR-0021,
`SyncPerformance.A_delta_sync_with_a_built_typo_dictionary`, 300,000 words of the German list, two runs): a sync of 100
changes and 100 removals took **4.4 / 4.8 s** while it rebuilt the dictionary at its end, and takes **2.6 / 2.6 s** now
that it marks it stale; the rebuild on its own is 2.0 / 2.3 s.

```text
dotnet test tests/Nordmann.LUI.Performance.Tests --filter SyncPerformance --logger "console;verbosity=detailed"
dotnet test tests/Nordmann.LUI.Performance.Tests --filter SyncPerformance --logger "console;verbosity=detailed"
dotnet test tests/Nordmann.LUI.Performance.Tests --filter A_delta_sync_with_a_built_typo_dictionary --logger "console;verbosity=detailed"
```

### Opening a catalog

`LuiCatalog.Open` probes the folding of the loaded SQLite once per process
(ADR-0007): **110 ms**
cold (JIT included), **37 ms** warm, 2,466 code points. Every further `Open` in
the process skips it.

---

## Results: the multi-word index (PR-004)

Same machine, SQLite and method as above. **Corpus:** titles of two to four words, each from the German word
list (average 36 characters — a recipe title is nearer 25), weights as above. **Inputs** (2,000): two thirds the
beginning of a title or a piece of it, 1–8 characters; one third the first two words of a title, 3–5 letters of
each, in either order. Row-by-row build through the triggers.

```bash
# the corpus of the budget is the titles (wiki-titles.tsv, ADR-0059);
# the scenario file names the prefix length, then
dotnet test tests/Nordmann.LUI.Performance.Tests -c Release --filter BudgetPerformance \
  --logger "console;verbosity=detailed"
```

**The query over the multi-word index is not measured any more.** The corpus of the titles exists
([`wiki-titles.tsv.gz`](../tests/TestData/corpora/wiki-titles.tsv.gz)), but no budget names it, and a measurement that
asserts nothing is a measurement that cannot fail. The file of the titles is still measured — by the file size of the
budget `file-size-1m` — and the promise that measures the query over it is decided in the §16 round.

| Entries | index | build | file | query p50 / p95 | alloc p50 / p95 | write p50 / p95 |
|---:|---|---:|---:|---:|---:|---:|
| 200,000 | `s_compact` only, `s_norm` virtual (this PR) | 30 s | 91 MB | 1.26 / 3.4 ms | 47 / 263 KB | 0.24 / 1.5 ms |
| 1,000,000 | `s_compact` only, `s_norm` virtual (this PR) | 157 s | 445 MB | 3.16 / 14.1 ms | 83 / 273 KB | 0.32 / 5.7 ms |
| 200,000 | two columns (first version) | 33 s | 126 MB | 1.64 / 4.9 ms | 46 / 263 KB | 0.31 / 1.9 ms |
| 1,000,000 | two columns (first version) | 180 s | 619 MB | 4.16 / 21.0 ms | 83 / 273 KB | 0.35 / 4.8 ms |
| *target (§16)* | | *3 M ≤ 3 min* | *1 M ≤ 300 MB* | *3 M ≤ 2 / 5 ms* | *≤ 250 KB* | *≤ 1 ms p95* |

- **Allocation.** With `F23`'s Dice the 95th percentile was **3,651 KB** (a dictionary per comparison, 500
  comparisons); with the allocation-free Dice of this PR it is 263 KB (ADR-0011).
- **Missed:** the query at 1 M (p50 3.2 ms, p95 14.1 ms — the two-word inputs of frequent trigrams, `Rent Schwe` 54 ms
  for 38 candidates; the mechanism of ADR-0010), the file size (1.5× the target; long titles), the 3 M build
  (extrapolated: about eight minutes). Held: the query at 200 k (p50 1.3 ms, p95 3.4 ms), the allocation, the write p95
  at 200 k (1.3 ms against 1 ms: just over).
- **One indexed column instead of two** is built (ADR-0011): −36 % file, −40 % build, −30 % query latency in the
  500 k experiment (`MultiWordLayoutExperiment`, gone from the tree since issue #76 — `git show 0cbc02f:tests/Nordmann.LUI.Integration.Tests/MultiWordLayoutExperiment.cs`), −28 % file, −13 % build, −24 % to −33 % latency in the table above (two indexed columns → one, `s_norm` virtual).

---

## Results: the typo tolerance (PR-005)

Same machine, SQLite and method as above. **Corpus:** the whole German word list as a `SingleWord` index — 685,739 words, weights
random, built row by row. **Cases:** 400 words per length; the beginning of the word typed right, and typed with one typo of the four
kinds (a letter left out, one too many, one wrong, two swapped) after the first letter. Hit@20 = the meant word is among the best 20.

```bash
dotnet test tests/Nordmann.LUI.Performance.Tests -c Release --filter TypoPerformance --logger "console;verbosity=detailed"
```

| | Result | Target (§16) |
|---|---:|---:|
| Dictionary build (first correction) | **4.2 s** | ≤ 10 s |
| Dictionary memory | **138 MB** | ≤ 200 MB |
| Corrected query p50 / p95 | 2.7 / **8.7 ms** | p95 ≤ 10 ms |
| Corrected query allocation p50 / p95 | 91 / 408 KB | ≤ 250 KB (p50 holds) |

| Beginning | Hit@20 right | Hit@20 with a typo | Anhang A4: right / typo |
|---:|---:|---:|---:|
| 4 letters | 30 % | 8 % | 13 % / 9 % |
| 5 letters | 53 % | 28 % | 42 % / 29 % |
| 6 letters | 68 % | 46 % | 64 % / 55 % |
| 7 letters | 86 % | 70 % | 75 % / 69 % |

- **Held:** the dictionary (time and memory), the p95 of a corrected query (8.7 ms against 10 ms, at 685 k entries built row by row; untested
  at 3 M), the allocation at the median, the absolute quality (like Anhang A4).
- **Missed, written down:** the allocation at p95 (408 KB: up to three more candidate lists and their unions) and the ratios of Anhang A4
  (69 / 69 / 86 / 92 %) which are not reached (25 / 53 / 69 / 82 %) because the cases are drawn uniformly here and by popularity there; the
  absolute figures agree. Details in ADR-0012.

---

## Results: the budgets of §16 (PR-009)

ADR-0020. The measurement machine above; the whole German word list;
the corpus of `Corpus` (`Nordmann.LUI.Testing`, seed 42): the 685,829 words, then compounds of two of them up to three
million, weights with a long tail; 2,000 inputs of 1–8 characters, 1,000 beginnings of 5–9 letters with one typo. No
other load; two runs, a third for the batch size of the build.

```text
dotnet test tests/Nordmann.LUI.Performance.Tests --filter BudgetPerformance --logger "console;verbosity=detailed"
```

| Target | Run 1 | Run 2 | Run 3 (batches of 1,000,000) | Run 4 (as committed) | Limit |
|---|---:|---:|---:|---:|---:|
| build of 3 M by `Sync` | 180.3 s (100,000) | 189.8 s (100,000) | **172.5 s** | **174.2 s** | ≤ 180 s |
| query p50 | 1.71 ms | 1.61 ms | 1.67 ms | 1.34 ms | ≤ 2 ms |
| query p95 | 10.25 ms | 9.64 ms | 9.93 ms | 10.22 ms | ≤ 5 ms |
| allocation p50 / p95 | 83 / 298 KB | 83 / 298 KB | — | — / 298 KB | ≤ 250 KB |
| corrected query p50 / p95 | 6.92 / 28.49 ms | 6.93 / 28.98 ms | — | — / 27.89 ms | p95 ≤ 10 ms |
| single write p95 | 0.57 ms | 0.51 ms | — | 0.57 ms | ≤ 1 ms |
| file of the 3 M entries | 925 MB | 925 MB | 925 MB | — |
| typo dictionary of 3 M entries (built for the corrected queries) | 29.8 s | 26.3 s | — | — |

Runs 5 and 6, the next day on a container started anew, with the targets as the owner decided them and the same code:
build 194.9 / 205.9 s, query p50 1.48 / 1.45 ms, p95 of 1–4 characters 1.88 / 1.89 ms, of 5–8 characters 14.36 / 13.35 ms,
allocation p50 / p95 82.8 / 297.5 KB, corrected query p95 32.49 / 30.54 ms, single write p95 0.72 / 0.78 ms; the file of
1 M titles 457 MB, the typo dictionary 138 MB in 4.88 s.

The query p95 by the length of the input (run 2): 1 character 1.21 ms · 2 → 1.45 · 3 → 1.97 · 4 → 3.11 · 5 → 6.90 ·
6 → 10.06 · 7 → 13.34 · 8 → 17.48 ms. The allocation by the candidates: a query that spends the budget of 500
(1,204 of 2,000) p50 124 KB, p95 306 KB; the rest p50 62 KB, p95 173 KB.

The two scenarios of their own:

| Target | Measured | Limit |
|---|---:|---:|
| typo dictionary of 685,739 distinct words | 138 MB, 4.1–4.8 s | ≤ 200 MB, ≤ 10 s |
| file of 1,000,000 titles, `MultiWord`, built by `Sync` in 88.1 s | 457 MB | ≤ 300 MB |

### The benchmarks

BenchmarkDotNet 0.15.8, `ShortRun` (3 iterations — the error column is wide; the means are what to compare), 200,000
entries of the same corpus.

```text
dotnet run -c Release --project benchmarks/Nordmann.LUI.Benchmarks -- --filter '*' --job short
```

| Benchmark | Input | Mean | Allocated |
|---|---|---:|---:|
| query, single-word | `b` · `ka` · `haus` · `apfelkuc` | 398 · 22 · 688 · 92 µs | 63 · 25 · 143 · 27 KB |
| query, multi-word | `b` · `ka` · `haus` · `apfelkuc` | 520 · 539 · 1,002 · 158 µs | 86 · 87 · 185 · 25 KB |
| corrected query | `kartofel` · `bahmhof` · `fusbal` · `strasee` | 97 · 257 · 481 · 117 µs | 27 · 100 · 80 · 27 KB |
| `UpdateWeight` · the same upsert · an upsert with another text | — | 279 · 79 · 226 µs | 0.4 · 0.8 · 0.8 KB |
| search form | `Apfelkuchen` · `Größenänderungsmaßnahme` · a title of 35 characters | 48 · 121 · 131 ns | 144 · 320 · 240 B |
| Jaro-Winkler against `apfelku` | the same | 90 · 152 · 158 ns | 208 · 200 · 232 B |
| N-gram Dice against `apfelku` | the same | 119 · 172 · 262 ns | 0 B |

### The run behind the tables (PR-010)

Run on **2026-11-04**, on `main` after #19 (`20bbbde`), with `tools/benchmarks.sh`'s command: BenchmarkDotNet 0.15.8,
the **default job** (not `ShortRun`), every benchmark, 34 min 40 s. Intel Xeon 2.10 GHz, 4 cores, 15 GB, Ubuntu
24.04.5, .NET 10.0.14 (SDK 10.0.114), the full German word list, nothing else running. The reports are committed in
[`benchmarks/results/`](../benchmarks/results/) and the tables below are rendered from them by `ReadmeBenchmarksTests`
(ADR-0022).

Against the `ShortRun` above (mean, µs; the error of the default job is 2–8 % of the mean, that of `ShortRun` was up to
260 % of it):

| Benchmark | `ShortRun` (PR-009) | default job (PR-010) |
|---|---:|---:|
| query, single-word: `b` · `ka` · `haus` · `apfelkuc` | 398 · 22 · 688 · 92 | 470 · 24 · 687 · 113 |
| query, multi-word: the same | 520 · 539 · 1,002 · 158 | 595 · 634 · 1,071 · 192 |
| corrected query: `kartofel` · `bahmhof` · `fusbal` · `strasee` | 97 · 257 · 481 · 117 | 114 · 275 · 477 · 124 |
| `UpdateWeight` · the same upsert · an upsert with another text | 279 · 79 · 226 | 343 · 93 · 228 |
| search form: `Apfelkuchen` · `Größenänderungsmaßnahme` · the title of 35 characters (ns) | 48 · 121 · 131 | 57 · 135 · 130 |
| Jaro-Winkler against `apfelku`: the same (ns) | 90 · 152 · 158 | 106 · 185 · 155 |
| N-gram Dice against `apfelku`: the same (ns) | 119 · 172 · 262 | 106 · 202 · 287 |

*The texts in the order of PR-009 — BenchmarkDotNet's report sorts them otherwise (`Apfelkuchen`, the title, then
`Größe…`); the allocations, 144 · 320 · 240 B in both runs, pin the rows to their texts (review of pull request 37,
M1).*

The allocations are the same to the byte. The means of all 24 benchmarks differ by **−11 % to +23 %** — ten of them by
less than 10 %, five are lower (`haus` in single words, `fusbal`, the search form and Jaro-Winkler of the title,
N-gram Dice of `Apfelkuchen`). It is not a change the two runs can name in either direction: `ShortRun`'s three
iterations gave an interval of up to 260 % of the mean, so none of its means is known to better than the spread
between them, and the queries without a correction take no path that #19 changed. The default job is the one the
tables show, because its interval (2–8 %) is narrow enough to compare two rows of one run — and a later run against
this one.

## The tables of the run

The tables of the BenchmarkDotNet run are **generated** from BenchmarkDotNet's own reports in
[`benchmarks/results/`](../benchmarks/results/) — and they stand in the [README](../README.md), between the markers
`<!-- benchmarks:begin -->` and `<!-- benchmarks:end -->`, where a test (`ReadmeBenchmarksTests`) fails when they differ. A
number there always has a run behind it. Run them again with `tools/benchmarks.sh`.

<!-- benchmarks:begin -->
<!-- the tables of the run are rendered into the README of the release (ADR-0039) -->
<!-- benchmarks:end -->

**Reading.** These are 200,000 entries on one thread, in the page cache — the code, not the scenario of §16. A query
takes 24 µs (two letters, the prefix path of a single-word index) to 1.1 ms (`haus` in titles: many candidates to
rank); its allocation follows the candidates it reads (#30). A correction costs one more search per correction tried.
A single write is a commit of its own; load a stock with `UpsertMany` or `Sync` instead.

---

```text
tools/fetch-wordlist.sh   # once: the full word list
tools/benchmarks.sh       # runs everything, copies the reports, renders the tables, runs the guard
```

