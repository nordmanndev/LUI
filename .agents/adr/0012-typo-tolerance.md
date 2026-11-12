# 0012 — Typo tolerance: SymSpell over the word beginnings, corrected when nothing stands at a word start

> **Note (ADR-0055, 2026-11-12):** `DistinctText` is gone — the uniqueness of a text belongs to the data, and the
> candidates of the input and of its corrections are united without dropping a duplicate. The sentence about it below
> is history. See [0055](./0055-the-uniqueness-of-a-text-belongs-to-the-data.md).

> **Name note (ADR-0023, 2026-11-04):** since this record was written, `Owned` is called `Embedded`: `ILuiOwnedIndex` is
> `ILuiEmbeddedIndex`, `LuiIndexBuilder` is `LuiEmbeddedIndexBuilder`, and `OwnedIndex`, `OwnedSource`, `OwnedSync` and
> `OwnedCheck` are `Embedded…`. The text below is history and stays as it was written.

> **Name note (ADR-0024, 2026-11-04):** since this record was written, `LuiRow` is `LuiPayloadRow`, `LuiSyncResult` is
> `LuiLinkedSyncReport`, `LuiPriorityRanking` is `LuiPriorityMode` (`Priorities` → `PriorityMode`), `LuiTimings.Candidates`
> is `CandidateRead`, the linked overload of `AddIndex` is `AddLinkedIndex`, and `Check()`, `WarmUpTypoDictionary` and
> `RebuildTypoDictionary` are on the write interfaces, not on `ILuiIndex`. The text below is history.

- **Status:** Accepted
- **Date:** 2026-11-03
- **Supersedes:** —
- **Affects:** `src/Nordmann.LUI/Typos/TypoDictionary.cs`, `src/Nordmann.LUI/Querying/QueryRunner.cs`,
  `src/Nordmann.LUI/Querying/Ranker.cs`, `src/Nordmann.LUI/Indexing/OwnedIndex.cs`,
  `src/Nordmann.LUI/LuiIndexBuilder.cs`, `src/Nordmann.LUI/LuiQuery.cs`

## Context

Step 5 of the plan. §10 specifies the mechanism and Anhang A4 measures why: SymSpell over whole words corrects a
word that is not finished hardly at all (11 %); over the **beginnings** of the words it reaches 40 %, most of what is
possible without a typo. ADR-0004 took the `symspell` dependency for this. What the specification leaves open is the
places where the mechanism meets real input.

## Decision

**The dictionary** (`TypoDictionary`, one per index) holds every beginning of three to ten code points of every word of
the normalised texts — a word is a run between separators — with the weights as frequency. SymSpell, one edit, prefix
length 7 (its default), count threshold 1. Two details of the specification, made exact:

- The frequency of a beginning is the sum of **`weight + 1`** of the texts it begins: SymSpell drops an entry whose
  count is 0, and half of a tag stock has the weight 0 (Anhang A1). A word with the weight 0 is still a word.
- `Upsert` adds only the beginnings that are not in the dictionary yet (a lookup at distance 0 per beginning), so
  writing the same text again does not inflate a frequency. `Remove` and `UpdateWeight` leave the dictionary as it is;
  `RebuildTypoDictionary()` builds it anew.

**It is built lazily** — on the first input that needs a correction, from `SELECT text, weight FROM lui_<name>`, not at
`Ensure()`. The whole word list (685,739 words) takes **4.2 s and 138 MB** (targets of §16: ≤ 10 s and ≤ 200 MB). An
application that cannot afford the first-use delay calls `WarmUpTypoDictionary()` at start (it builds only if nothing is
built; `RebuildTypoDictionary()` builds anew). Both take a `CancellationToken`.

**One dictionary per index of a catalog** (review of pull request 10, S1). `Ensure()` returns a new `OwnedIndex` each time;
the dictionary belongs to the catalog and the index name, so two instances — an importer beside a search endpoint — share
the memory and see each other's writes. `Drop()` forgets it: the next index of that name starts empty.

**When it corrects:** when **no candidate stands at a word start** — not when there are no hits (§10.2). The test:
`apflekuchen` has a hit in the middle of `Teigapflekuchen`, and still `Apfelkuchen` is found. Not for one- or two-letter
inputs (the prefix path), and not for a word of fewer than three letters.

**How it corrects** (§10.3). Every word of three or more letters is looked up (closest, distance 1). A word that is
a beginning of a word of the index comes back unchanged. The k-th correction of the input (k = 1 … 3) takes the k-th
suggestion of every word that has one — `apflekuchen sahne` becomes `apfelkuchen sahne`. Each correction is searched like
the input itself (same expression, same prefix rules), and **the candidates are united with those of the input**: the
candidates that were found mid-word stay, the corrected ones join. A document found with more than one input keeps the
version that stands at a word start. The cap to `Candidates` keeps the candidates with a word start first, in rowid order, and
fills up with those from the middle of words (review M2: ten hits in the middle of words pushed the corrected candidate out
of a budget of five, and the result said it had searched the correction). `DistinctText` is applied per input and then across
the inputs, the entry at a word start first (M3: a shared set of seen texts dropped the corrected version of a document that the
input had found mid-word). The ranking then measures every candidate against **the input it was found with**
(`Scored.Variant`) — word start and similarity. `CorrectedQuery` is the first correction of which a candidate remains after the cap, in
search form (`apfelkuchen`, `mueller`); `null` when nothing was corrected.

**Off by default.** `Builder.TypoTolerance(maxEdits)` takes 0 (off) or 1; a query overrides it with
`.TypoTolerance(bool)` or `LuiQueryOptions.TypoTolerance`. The dictionary costs memory the application did not ask for, and
Anhang B's rule „a typo does not lead nowhere" is for an application that decided on it. The setting is a property of the
instance, like the similarity; it is not part of the stored definition.

**Locking** (§15) — and what a build does not do (review M1). SymSpell is not made for reading while writing: lookups share a
`ReaderWriterLockSlim`, an addition has it alone. The **build does not hold it**: it builds a dictionary of its own, outside
the lock, behind a build gate (one build at a time; a cancelled build leaves everything as it was). The writes that arrive
meanwhile are queued; when the new dictionary is ready the queue is replayed on it and the dictionary is swapped in, in one
step under the lock — so neither the first build nor a rebuild loses a word, and a `Upsert` never waits for seconds (before
the fix it waited 5.6 s with 150,000 entries, inside the caller's SQLite transaction). The sum-versus-new trade-off (S3): the
build sums the weights, `Upsert` adds only beginnings that are new; the frequencies are exact after a rebuild.

## Limits, written down

- **The dictionary stops at ten letters.** A word of more than ten letters is looked up by its first ten, and the rest is kept.
  A typo after the tenth letter is **not** corrected, and in a word of ten letters or more a letter left out or too many is
  not found (the beginning of eleven letters it would match is not in the dictionary); a letter that is wrong or two letters
  swapped within the first ten is. Both are pinned by tests. The specification's range of 3 … 10 stays.
- **A deletion in the last letters of a long beginning needs the right length**: `apelkuc` finds `apfelkuc`, `apelkuchen`
  does not find `apfelkuchen`.
- **A compound typed together is corrected only if such a word exists.** The dictionary holds the beginnings of the
  *words* of the titles. „Apfel Kuchen" has the words `apfel` and `kuchen`; `apflekuchen` has no word near it, `apfle kuchen`
  does (pinned by a test). A multi-word index finds `apfelkuchen` in „Apfel Kuchen" because it reads the titles compactly
  (ADR-0011); the dictionary does not.
- **The three corrections are the nearest ones of the typed length first, then the most frequent.** Frequency alone would
  always prefer the shortest beginning — every beginning of a word shares its frequency, and a shorter one is never rarer —
  so `apfle kuchen` would be corrected to `apfe kuchen`. Two letters swapped or one wrong keep the length; a letter
  left out or too many come after them. A beginning with many neighbours (`stra`) may still not offer the one that was meant.
- **A correction is a candidate search.** Up to three more FTS statements: the typo is paid for in time (below).

## Measurements

Whole German word list as a `SingleWord` index (685,739 words, weights random), 4 cores, built row by row; typos of the four kinds
after the first letter, 400 cases per length, Hit@20 = the meant word among the best 20
(`TypoPerformance`, `LUI_PERF=1`):

| Beginning typed | right | with one typo | Anhang A4 (3 M entries) right / typo |
|---:|---:|---:|---:|
| 4 letters | 30 % | 8 % | 13 % / 9 % |
| 5 letters | 53 % | 28 % | 42 % / 29 % |
| 6 letters | 68 % | 46 % | 64 % / 55 % |
| 7 letters | 86 % | 70 % | 75 % / 69 % |

Absolute figures like the specification's; the ratios (26 / 52 / 69 / 82 %, against 69 / 69 / 86 / 92 %) are lower because the cases here
are drawn uniformly from the list and Anhang A draws them by popularity — a popular word is found with and without a typo.
The floors in the test suite (`TypoQualityTests`, a stock of 7,071 words, where 64 / 82 / 93 / 96 / 98 % of the right beginning are found for 4 … 8 letters) keep a margin of
about ten points: 55 / 70 / 80 / 85 / 88 %.

**Cost.** Dictionary: 4.2 s, 138 MB. A corrected query (1,000 inputs with a typo, 5 … 9 letters): **p50 2.7 ms, p95 8.7 ms**, 91 / 408 KB
(§16: p95 ≤ 10 ms — held here at 685 k entries, built row by row; at 3 M entries it is untested, and ADR-0010 says what to expect). 864 of
1,000 inputs were corrected. The measurement before the review read p50 4.0 ms and p95 12.8 ms (twice); after it 2.7 ms and 8.7 ms (twice). It was not isolated which change did it — candidates are the lock-free `IsBuilt` (a read lock per corrected query) and the rewritten union; the earlier figures are not a different machine, they were taken on the same one.

## What a rollback and a write past the library do

`Upsert` adds the word beginnings at once, inside the caller's transaction. After a rollback they stay in the dictionary, like
those of a removed entry: a correction towards them finds nothing. A write past the library — another process, direct SQL, a
transaction that commits after the dictionary was built — reaches it with `RebuildTypoDictionary()`.

## Consequences

- `LuiResult.CorrectedQuery` is no longer reserved. `LuiTimings` has a `Correction` span.
- `ILuiIndex.WarmUpTypoDictionary(ct)` and `RebuildTypoDictionary(ct)` are part of the contract; the builder takes `TypoTolerance(int)` and
  `TypoTolerance(bool)`. `LuiTimings` has `Correction` as its last field.
- `OwnedIndex` and `TypoDictionary` own a lock that is not disposed (CA1001 is suppressed with the reason): it allocates a kernel
  event only when a thread has to wait, and an index lives as long as its catalog.
