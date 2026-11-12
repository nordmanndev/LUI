# 0020 — The performance budgets of §16, measured: a corpus, a test per target, waivers with reasons

> **Name note (ADR-0023, 2026-11-04):** since this record was written, `Owned` is called `Embedded`: `ILuiOwnedIndex` is
> `ILuiEmbeddedIndex`, `LuiIndexBuilder` is `LuiEmbeddedIndexBuilder`, and `OwnedIndex`, `OwnedSource`, `OwnedSync` and
> `OwnedCheck` are `Embedded…`. The text below is history and stays as it was written —
> except for the sentences that the note of 2026-11-12 below rewrites and names.

- **Status:** Accepted. **Owner's decisions of 2026-11-04** on the four waivers: the targets are stated anew in §16 and
  asserted — see "The owner's decisions" at the end; the allocation with bug #30
- **Date:** 2026-11-03
- **Builds on:** [0010](./0010-the-query-budget-at-three-million-entries.md) (the query budget at 3 M, Proposed),
  [0011](./0011-the-multi-word-index.md) (the file size), [0012](./0012-typo-tolerance.md) (the correction),
  [0016](./0016-sync-check-rebuild.md) (the bulk path), [0019](./0019-the-test-tree-three-layers-one-map.md) (the test tree)
- **Affects:**
  - `tests/Nordmann.LUI.Testing/Corpus.cs` (+ `CorpusKind`), `CorpusFile.cs`, `TestDatabase.FileMegabytes()`
  - `tests/Nordmann.LUI.Performance.Tests/BudgetPerformance.cs`
  - `benchmarks/Nordmann.LUI.Benchmarks/`
  - `docs/benchmarks.md`, `docs/testing.md`, spec §16

## Context

Step 9 of the plan: the targets of §16 as tests, a benchmark project, and the scenario of three million entries. Until
now the targets were measured piecemeal — `QueryPerformance` asserted its latency only at three million and was run
with the row-by-row build of PR-003, `TypoPerformance` the dictionary, ADR-0011 the file size. The bulk path of PR-007
(`Sync`: batches in the order of the search index, one merge) had not been measured at three million at all.

## Decision

**One corpus for every measurement.** The corpus of a measurement is a file in the repository (ADR-0059):
`tests/TestData/corpora/words.txt`, `places.tsv`, `movies.tsv`, `wiki-titles.tsv`:

- **single words** — the German words of `words.txt` (1,908,798 entries);
- **titles** — the titles of `wiki-titles.tsv`, `movies.tsv` and `places.tsv`;
- **weights** — out of the corpus file: the population of a place, the votes of a film, the derived weight of a word
  (a long tail, nearly every weight distinct, the harder case for the rowid);
- **inputs** of 1 … 8 characters as users type them — a beginning, a piece out of the middle, two words of a title;
- **typos** — a beginning of 5 … 9 letters with one edit after the first letter.

The same file gives the same corpus, so the budget test and the benchmarks measure the same thing, and a run can be
repeated.

**A test per target, in the scenario of §16** (`PerformanceBudgets`, in the layer of the measurements):

- `The_single_word_index_at_three_million_entries` — the build by `Sync`, the query without and with a correction, the
  allocation, the single writes;
- `The_typo_dictionary_of_the_whole_word_list` — memory and build of the dictionary of 685,829 words;
- `The_file_of_a_million_titles` — the file size of one million titles.

Each target is **met** — then it is asserted — or **waived**: a waiver names its reason and the record that holds it,
and a waived target is still measured and printed. A target that is missed and not waived fails the test. A waiver
that is met is printed as "the waiver can go". Below the scenario (fewer entries in the scenario file) nothing is
asserted: a budget of three million is not a claim about 200,000.

**The build is the first load of a file no other process writes:** `Sync` with `BatchSize = 1_000_000` — the fewest
commits, which ADR-0016 measured to matter more than the order of the rows.

**The benchmark project** (`benchmarks/Nordmann.LUI.Benchmarks`, BenchmarkDotNet, `[MemoryDiagnoser]`) measures the code,
not the scenario: a query (single-word and multi-word, the prefix path and FTS5), a corrected query, the single writes,
the search form and the similarity measures — on 200,000 entries of the same corpus.

## What was measured

The measurement machine of `docs/benchmarks.md` (Intel Xeon 2.1 GHz, 4 cores, 15 GB, Ubuntu 24.04, .NET 10.0.14), the
whole German word list, no other load. Two runs of the single-word scenario, a third for the batch size of the build,
a fourth with the test as it is committed (the waivers in place, batches of 1,000,000): it passes.

| Target of §16 | Scenario | Measured | Limit | Verdict |
|---|---|---:|---:|---|
| query p50 | 3 M entries, inputs of 1–8 characters | 1.61 / 1.71 / 1.67 / 1.34 ms | ≤ 2 ms | **met** |
| query p95 | the same | 9.64 / 10.25 / 9.93 / 10.22 ms | ≤ 5 ms | **waived** → ADR-0010 |
| allocation p95 | per query | 298 KB | ≤ 250 KB | **waived** → below |
| query with correction p95 | 3 M entries, a beginning with one typo | 28.5 / 29.0 / — / 27.9 ms | ≤ 10 ms | **waived** → ADR-0010, ADR-0012 |
| single write p95 | `Upsert` / `UpdateWeight` / `Remove`, own transaction | 0.57 / 0.51 / — / 0.57 ms | ≤ 1 ms | **met** |
| build | 3 M entries, `Owned`, `Sync` | **172.5 / 174.2 s** with batches of 1,000,000; 180.3 / 189.8 s with 100,000 | ≤ 180 s | **met** |
| typo dictionary | 685,829 distinct words | 138 MB, 4.1–4.8 s | ≤ 200 MB, ≤ 10 s | **met** |
| file size | 1 M titles, `MultiWord` | 457 MB | ≤ 300 MB | **waived** → ADR-0011 |

**Against the row-by-row build of PR-003** (ADR-0010: p50 3.57 ms, p95 26.5 ms, build 334 s, a write at p95 2.6 ms) the
bulk path and its merge took the median under its target, the p95 from 26.5 to 10 ms, the build from 334 to 172 s, and
the single write under its millisecond.

**The query p95 is the length of the input.** p95 per length at 3 M: 1 character 1.2 ms, 2 → 1.5, 3 → 2.0, 4 → 3.1,
5 → 6.9, 6 → 10.1, 7 → 13.3, 8 → 17.5 ms. Up to four characters the budget holds; beyond it is FTS5's own cost of a
longer trigram expression over three million entries — the trigram-dense corpus of ADR-0010.

**The allocation follows the candidates read.** A query that spends the budget of 500 candidates allocates p50 124 KB,
p95 306 KB; one that does not, p50 62 KB, p95 173 KB. At three million entries 60 % of the inputs spend it (at 200,000
the p95 was 244–249 KB). It is not a leak of the scale but the budget: 500 texts read, normalised and ranked.

**The corrected query** reads up to three corrections more, each one FTS5 statement of the cost above: p50 6.9 ms, p95
29 ms (ADR-0012: 8.7 ms at 685,000 entries).

**The benchmarks** (BenchmarkDotNet, `ShortRun`, 200,000 entries; mean, allocated):

| Benchmark | Mean | Allocated |
|---|---:|---:|
| query, single-word: `b` · `ka` · `haus` · `apfelkuc` | 398 · 22 · 688 · 92 µs | 63 · 25 · 143 · 27 KB |
| query, multi-word: the same inputs | 520 · 539 · 1,002 · 158 µs | 86 · 87 · 185 · 25 KB |
| corrected query: `kartofel` · `bahmhof` · `fusbal` · `strasee` | 97 · 257 · 481 · 117 µs | 27 · 100 · 80 · 27 KB |
| write: `UpdateWeight` · the same upsert · an upsert with another text | 279 · 79 · 226 µs | 0.4 · 0.8 · 0.8 KB |
| search form · Jaro-Winkler · N-gram Dice, per text | 48–131 · 90–158 · 119–262 ns | 144–320 · 200–232 · 0 B |

## Consequences

- **Four targets are waived, not met, and not lowered.** Each waiver is a sentence in `PerformanceBudgets.Waivers` and a
  row here; the test measures them on every run and says when one is met.
- **Open for the owner** — none of them is decided here:
  1. **Query p95 and the corrected query** — ADR-0010 (Proposed) has the options: measure on a real stock, restate the
     budget per input length (it holds up to four characters), or a word-start structure for longer inputs.
  2. **The allocation** — the lever is `Candidates` (500): a smaller budget allocates less and ranks fewer; or the
     target becomes a budget per candidate.
  3. **The file size** — ADR-0011: titles of 36 characters, the table holds the text and two generated forms.
- The build meets its target only with the largest batch. With the default of 10,000 it does not (ADR-0016: 78 s for a
  million); a first load is told to use the large batch (`LuiSyncOptions.BatchSize`).
- The budget test runs for about ten minutes; it is not part of CI — a budget on a shared runner is a coin toss.
  It lives in the layer of the measurements (ADR-0058) and is run on purpose, on the measurement machine, and its
  output goes here.

**Verified by:** `tests/Nordmann.LUI.Performance.Tests/BudgetPerformance.cs` (the table above is its output),
`benchmarks/Nordmann.LUI.Benchmarks` (`dotnet run -c Release --project benchmarks/Nordmann.LUI.Benchmarks -- --filter '*'`),
and the test map (`Test_Landscape`), which lists the budget test.

## The owner's decisions (2026-11-04)

The four waivers are gone; the targets are stated anew in §16 and `PerformanceBudgets` asserts them. For the long inputs,
the correction and the file this is a **loosening** of the target, not its meeting — written down as such:

| Target of §16 | Was | Now | Measured |
|---|---|---|---:|
| query p95 | ≤ 5 ms | ≤ 5 ms for 1–4 characters, ≤ 20 ms for 5–8 characters (ADR-0010, option 2) | ≤ 3.1 ms · 17.5 ms at eight |
| query with correction p95 | ≤ 10 ms | ≤ 35 ms | 27.9–29.0 ms |
| allocation | ≤ 250 KB | p50 ≤ 250 KB, p95 ≤ 350 KB | 83 · 298 KB |
| file of 1 M titles | ≤ 300 MB | ≤ 500 MB for titles of 36 characters on average | 457 MB |
| build of 3 M | ≤ 3 min | ≤ 4 min (a faster first load: #32) | 172.5–205.9 s on two machines |

- **The allocation is a bug** (#30). The owner finds 250–300 KB per query too much; the restated target keeps the test
  honest until the causes are measured and the allocation is lowered. The candidate budget stays 500 — a smaller one
  would cost the quality of the ranking.
- **A real stock** measures the scenario again when there is one (ADR-0010, option 1).
- The waiver mechanism of `PerformanceBudgets` stays: a target missed in the future is fixed or waived with a reason,
  never lowered quietly.

**The build is waived again — open for the owner.** Measured with the targets as decided, on a container started anew
the next day, with the same code: the build of three million entries took **194.9 s and 205.9 s** (the day before
172.5 s and 174.2 s); the queries were as fast or faster (p50 1.45–1.48 ms, p95 of 5–8 characters 13.4–14.4 ms), the
corrected query p95 30.5–32.5 ms, the single write p95 0.72–0.78 ms. The build writes a file of 925 MB and follows the
speed of the disk; the 180 s of §16 are met on one machine and missed on the next. **Decided by the owner on the same
day:** the target is **≤ 4 min** for the first load of three million entries, and a faster first load is issue #32,
after 0.1.0. `PerformanceBudgets` asserts it; no target of §16 is waived any more.


## Note (2026-11-12, issue #123, ADR-0059)

The corpus of a measurement is a file in the repository (ADR-0059); the generator and the distribution of the weights
of the scenario are gone with it.

The decision stands; the sentences above that this note rewrites and names are rewritten — each of them because the
generator died with ADR-0059 and the corpus file took its place:

- **the list of affected files above** — `tests/Nordmann.LUI.Testing/Corpus.cs` (+ `CorpusKind`, `WeightDistribution`) —
  is rewritten to `tests/Nordmann.LUI.Testing/Corpus.cs` (+ `CorpusKind`), `CorpusFile.cs`: the file
  `WeightDistribution.cs` is gone, and the reader of the corpus files is new.
- **the sentence "**One corpus for every measurement.** `Corpus.Generate(words, entries, kind, weights, seed)` in
  `Nordmann.LUI.Testing`:"** and **the three bullets under it** — "**single words** — the German word list first
  (685,829 words), then compounds of two of them;", "**titles** — two to four words of the list;" and "**weights** — a
  long tail (nearly every weight distinct, the harder case for the rowid) or sparse (half of them 0, as in Anhang
  A1);" — are rewritten to the corpora of the repository and to the weights out of the corpus file.
- **the sentence "The same seed gives the same corpus, so the budget test and the benchmarks measure the same thing,
  and a run can be repeated."** is rewritten to "The same file gives the same corpus, so the budget test and the
  benchmarks measure the same thing, and a run can be repeated."

The bullets of the inputs and of the typos are untouched: the corpus still knows what users type and what they mistype.

## Note (2026-11-12, issue #122)

The budget test lives in the layer of the measurements since ADR-0058, and the scenario of a run lives in
`performance.scenario.json`.

The decision stands; the sentences above that named a variable of the environment that died, the name of a gate that
is gone (`PerformanceFact`, `TestGates`) or a path of the file of the measurements (`PerformanceBudgets.cs`,
`BudgetPerformance.cs`) are rewritten — each of them because the variable died with ADR-0058 and took the gate of the
measurements with it, or because that file moved into the layer of the measurements. The path in the list of affected
files above now names the file where it lives; the name of the class stays, as ADR-0049 says.
