# 0059 — The corpora are four fixed files in the repository, not generators

- **Status:** Accepted
- **Date:** 2026-11-12
- **Builds on:** [0019](./0019-the-test-tree-three-layers-one-map.md) (the test tree),
  [0020](./0020-the-performance-budgets-measured.md) (the performance budgets),
  [0056](./0056-the-budgets-live-in-a-file.md) (the budgets live in a file),
  [0058](./0058-the-measurement-is-a-layer-of-its-own.md) (the measurement is a layer of its own)
- **Affects:**
  - `tests/TestData/corpora/` (new) — the four corpus files, gzip-compressed
  - `tests/Nordmann.LUI.Testing/CorpusFile.cs` (new), `Corpus.cs`, `TestFiles.cs`
  - `tests/Nordmann.LUI.Unit.Tests/Corpora/CorpusFileTests.cs` (new)
  - `tests/Nordmann.LUI.Performance.Tests/` — the six measurement files, `PerformanceScenario.cs`,
    `performance.scenario.json`
  - `benchmarks/Nordmann.LUI.Benchmarks/` — the fixture measures the same files as the budgets
  - `.github/workflows/performance.yml`
  - `docs/benchmarks.md`, `docs/testing.md`
- **Issues:** #123 (epic #64, part of #117)

## Context

The corpus of the measurements was **generated**. `Corpus.Generate(words, entries, kind, weights, seed)`
in `Nordmann.LUI.Testing` built it out of the word list: the first *n* words for small *n*, then compounds of two
random words up to the number of entries. Three things were wrong with that:

1. **The corpus depended on the scaling parameter.** At 200,000 entries the corpus was the *first* 200,000 words of
   the sorted list — the letters a to f, and nothing else. The measurement that used it found nothing: the input `ka`
   had 0 hits, and three of the four query lines measured an empty query. A corpus that changes with the size of the
   run is a different corpus, and a measurement on two corpora is no comparison.
2. **A prefix of a sorted list is not a sample.** "The first 200,000 words" is the alphabetically first ones — the
   worst possible sample of a German word list, and it is what the word list of the specification looks like too.
3. **The fallback was a different corpus.** Without the fetched word list the measurements fell back to the sample of
   7,071 words in the repository. A worktree has no `artifacts/`, so the number in the log was the number of a
   different corpus than the number of the record — and nothing in the report said so.

The budgets of the repository name their scenario in one file (ADR-0056): the corpus file and the prefix length. So
far no file carried that name. And the scenario of the run named a corpus *beside* the budget (ADR-0058) — two truths
about one corpus.

## Decision

**The corpus of a measurement is a file in the repository. Four files, checked in, compressed.**

- **The corpus is a file.** `tests/TestData/corpora/words.txt.gz`, `places.tsv.gz`, `movies.tsv.gz`,
  `wiki-titles.tsv.gz`. **No manifest: the file name is the identity.** A manifest is a second truth that can disagree
  with the tree.
- **Every corpus has at least 100,000 entries.** Below that it is a sample, and a sample cannot say anything about
  large amounts. The four corpora have 1.9 M, 5.8 M, 0.65 M and 5.2 M entries.
- **The header is a block of comment lines at the top of the file, ended by an empty line.** It names the source, the
  licence, the snapshot date, the number of entries, the format, the kind of the entries and the order. **A `#` inside
  an entry is data** — a film title that begins with `#` is a title, and a loader that eats it loses 316 entries of the
  film corpus alone. A convention that eats real data is a silent filter, not a convention.
- **The format of a file is machine-readable, and the reader reads it.** The `format:` member is the list of the columns of
  an entry (`text⇥weight`), the meaning of the weight stands in a `note:` member beside it. The reader compares the format
  with the columns it finds and **refuses** a file whose header names a format that does not fit its entries or a format
  nobody knows. A self-description that nobody checks is a second truth beside the file — the same rule the reader of the
  scenario file follows (ADR-0058).
- **The order of a corpus is a deterministic permutation of the file: by the SHA-256 of the text.** So a **prefix is
  always a representative sample**, never the alphabetically first *n*. One rule for every corpus, independent of how
  the source happens to be sorted.
- **The weight belongs to the file.** The places carry their population, the films their votes; the words and the
  Wikipedia titles carry the derived weight of their text. Without a weight column the weight is
  `1 + floor(9999 · (1 − u)³)`, with `u` the first four bytes of the SHA-256 of the text — computed in **exact integer
  arithmetic** (`1 + floor(9999 · t³ / 2⁹⁶)`, with `t = 2³² − v` and `v` the first four bytes as a whole number), so
  that the builder that wrote the file and the loader that reads it compute the same number. The rule in floating point
  would be one off at the border.
- **The context is metadata and is not indexed.** It is what lets a corpus carry the country, the region and the
  district of an entry beside it.
- **The budget names the corpus.** A measurement loads the corpus of the budget whose target it measures, and takes
  the prefix of that budget — cut to the prefix the run asks for. **The scenario file names no corpus any more**: it
  carries the prefix length, the batch, the minimum weight, the optimization, the machine and the database. The corpus
  is not named twice.
- **The generator is gone.** `Corpus.Generate` and the fallback to the 7,071-word sample are gone. A corpus that is
  generated is a different corpus on every run.

## The corpora

| File | Entries | Format | Kind | Source | Licence | Snapshot |
|---|---:|---|---|---|---|---|
| `words.txt` | 1,908,798 | `text⇥weight` | single | [`kkrypt0nn/wordlists`](https://github.com/kkrypt0nn/wordlists), `wordlists/languages/german.txt` | none stated by the repository for the lists it gathers itself — the owner accepted that | 2026-11-12 |
| `places.tsv` | 5,779,011 | `text⇥weight⇥context` | multi | [`download.geonames.org/export/dump/`](https://download.geonames.org/export/dump/) — `allCountries.txt`, `countryInfo.txt`, `admin1CodesASCII.txt`, `admin2Codes.txt` | CC BY 4.0 | 2026-11-12 |
| `movies.tsv` | 652,001 | `text⇥weight` | multi | [`datasets.imdbws.com`](https://datasets.imdbws.com/#help) — `title.basics.tsv`, `title.ratings.tsv` | the non-commercial IMDb datasets | 2026-11-12 |
| `wiki-titles.tsv` | 5,167,623 | `text⇥weight` | multi | [`dumps.wikimedia.org`](https://dumps.wikimedia.org/dewiki/latest/dewiki-latest-all-titles.gz) | CC BY-SA 4.0 + GFDL | 2026-11-12 |

The counts are the counts of the files: the header of a corpus names its own number of entries, and the loader reads
the number from the header. The builder (`tools/` — the script that writes the files) counts the entries while it
streams them, so the number in the header is the number of the file and not a number carried along from elsewhere.

**What is not in the repository, and why.** The books of the Amazon scrape (1,601 titles), the German names (27,273)
and the Cologne first names (9,620): below the floor. The street addresses: 5,858,056 of them were measured, but a
street address is a *lookup*, not a string somebody types into an autocomplete — a corpus is made of search strings.
The list of all `.de` domains: DENIC hands out no zone files to third parties. The raw rows of the names dataset
(6,040,701 German rows, one per person): personal data of identifiable people (C-413/23). **The line is the name
level:** *Müller is rank 1 in Germany* says nothing about a person and is published; *Sven Liebscher, M, DE* is a person
and is not.

## Consequences

- **The budgets of the scenario of three million entries are not asserted — they are reported.** The words corpus has
  1,908,798 entries, so the prefix of three million does not exist. The numbers of the budgets were measured on the
  old, generated corpus; **the counts of the budgets of §16 are re-decided against the measured corpora** — that is the
  work of the next round, and until then the promise of the file of the budgets is not met by a run on these corpora.
  The measurement says so in its report: a budget below its scenario is reported, not missed.
- **The distribution of the weights of Anhang A1 is not measured any more.** The weights come from the corpus, and
  none of the four corpora has half of its entries at the weight 0. The words corpus carries the derived weight — a
  long tail of distinct weights, which is the harder case for the rowid (ADR-0010).
- **The query over the multi-word index is not measured any more.** The corpus of the titles exists, but no budget
  names it, and a measurement that asserts nothing is a measurement that cannot fail. The §16 round decides which
  promise measures the titles.
- **The word list has two lives.** The big list (1,908,798) is the corpus of the benchmark and of the measurements.
  The smaller CC0 list (685,829 words, `enz/german-wordlist`) stays the input of the unit and the integration layer:
  the normalisation parity test (§19) runs on its checked-in stride of 97 (7,071 words), and the full list is fetched
  by `tools/fetch-wordlist.sh`.
- **The lane needs no fetch.** The corpora are in the repository; the lane writes the scenario of the run and measures
  the files that are checked in.
- **The four files are 162 MB in the repository** — 432 MB of raw corpora, gzip-compressed. The two numbers are
  measured, not estimated: `ls -l tests/TestData/corpora/` sums to **162,238,188 bytes**, and the four files unpacked
  (`zcat … | wc -c`) to **432,304,016 bytes**. The places corpus is the largest of them (82 MB of the 162 MB) because it
  carries the country, the region and the district of every one of its 5.8 million entries. A public repository has to
  be able to answer *may this be redistributed?* (the licences are in the headers and in `docs/benchmarks.md`) and
  *may this be published at all?* (the data-protection boundary is above).
- **Two of the four files are above GitHub's recommended maximum of 50 MB** (the places 78.3 MB, the Wikipedia titles
  54.6 MB). They are stored as they are, **without Git LFS**, so that a clone needs no LFS client and a `git clone` is
  enough to measure. The cost is that every clone of the repository carries 162 MB — a price the owner decides, not this
  ADR.

## The criterion that is not met literally

Issue #123 asks: *`Corpus.Generate` and the fallback to the 7 071-word sample with `LUI_WORDLIST` are gone; `grep -r
LUI_WORDLIST` over the repository finds nothing.*

**The first half is met in full:** `Corpus.Generate` is gone, and the fallback to the sample of 7,071 words is gone
with it — the measurements take the corpus of their budget out of the repository, and there is no branch in the code
that would run a measurement on anything else.

**The second half is not met literally, and the exception is named here instead of being argued away.** The issue
names the same name in its own text: *the script and the path override `LUI_WORDLIST` therefore stay*. The script
that fetches the word list of the parity prints the name, the path override of the parity reads it, the sample reads
it for its own word list, and the records of the repository name it — a record that forgets what it decided cannot be
read as a record. **The names stand in these places and nowhere else:**

| Place | What it is |
|---|---|
| `tools/fetch-wordlist.sh` | the script that fetches the word list of the parity (the issue says it stays) |
| `tests/TestData/wordlist/README.md` | the note of the test data about the input of the parity |
| `tests/Nordmann.LUI.Testing/TestFiles.cs` | the path override of the parity (the issue says it stays) |
| `samples/Lui.Sample/Data/GermanWordList.cs` | the word list of the sample, which is its own thing (ADR-0029) |
| `docs/testing.md` | the table of the gates of the tree |
| `.agents/adr/0029-the-sample-fills-its-own-bulk-indexes-on-demand.md` | the record of the decision of the sample |
| `.agents/adr/0051-xunit-v3-run-by-vstest.md` | the record of the gate of the parity |
| `.agents/plan/2026-11-11-the-public-surface-and-the-measurement.md` | the paper of the work (D7) |
| `.agents/state/recently-completed.md` | the state of the work |

**No code of the measurements, no workflow, no script of the measurement and no document of the corpora names it.**
The criterion is lifted for the nine places above and kept for everything else. It is lifted explicitly, not argued
away: this section is the lift.

## Alternatives

- **A generated corpus with a fixed seed.** Rejected: that is the state before this decision. The corpus depends on
  the size of the run, and the prefix of a sorted word list is not a sample — the input `ka` had 0 hits in it.
- **The corpora outside the repository, fetched like the word list.** Rejected: then the corpus of a run is a thing
  that is fetched, and the file a budget names may not be the file of the run. Checked in, it is one truth.
- **A manifest beside the files.** Rejected: the file name is the identity; a manifest is a second truth that can
  disagree with the tree.
- **One corpus file with the format of all three.** Rejected: the three formats say three different things. A file
  that carries a weight it does not have is a file with a column of zeroes.

## The number of this decision

The paper of 2026-11-11 names this decision ADR 0059 (D7). The number was given to the decision of the corpora
(issue #123) before it was built, and the numbers are given when the decision is built.
