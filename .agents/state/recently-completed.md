# Recently completed

<!--
What the last sessions finished, and what the next one should know. Compress
the scratchpad here at the end of a session.
-->

## 2026-11-13 — The specification is deleted, and every section of it has a home (#125, step 8 of #117)

- **The specification is gone**: `.agents/spec/LUI-Spezifikation.md` (1429 lines, German) is deleted. It is **not**
  translated — it was unlinked in step 3, and translating a document with a known end date is work without a purpose.
- **The references are rehung, not dropped** (ADR-0061, the mapping table): every `§x` became the ADR or the page that
  carries the same rule. The sweep was one mapping and one pass over 188 files. A reference that names the document it
  belongs to was left alone: `docs/normalization.md` has a section 7 of its own, and the plan has sections of its own.
- **The records stay history, the pointer moves**: 31 ADRs that were written against the specification carry a dated note
  that says where the rule lives now — the precedent is ADR-0036, which carries three such notes.
- **The contract of the library is the set of the ADRs** (ADR-0002, ADR-0061). `AGENTS.md` says so now, and the papers
  of the work begin with the decisions and not with a specification.
- **A guard holds the deletion**: `SpecificationIsGoneTests` — the file is gone, the tree around the papers names it
  nowhere, and every section reference names a document that exists.
- **The stale dependabot pull request #108 is closed**: it bumped `xunit.v3` alone and would have failed the restore
  (NU1107) on today's tree, because `xunit.v3.extensibility.core` came in with PR #136 and pins `xunit.v3.common`.
  The family moves together instead — PR #140, merged as `8c83bfa`.

## 2026-11-13 — The reports name the hits and the place of the measurement (#124, PR #139 merged as `46f7c36`)

- Every line of a committed report names the number of hits its query found, every report carries the record of its run,
  the settings of a query are axes, and the lane runs weekly. The correction benchmark finds **no** hits in every line of
  the report — a measurement that is committed as it stands, and an open point of the pull request.
- The mutation of the report followed the report the writer writes: the report carries no bold, and a mutation that
  nothing reacts to proves nothing (`tools/mutation-check.sh`).

## 2026-11-06 — The read path (#56, PR #57 merged as `73ef344`)

- The catalog is the entry point (ADR-0033): `Create(name, builder)` writes, `Index(…)` opens without writing; a
  `Mode=ReadOnly` file refuses every write with `LuiReadOnlyException`; the container holds the catalog and nothing else
  (ADR-0034). The typed `Index<TKey, TPayload>` of that PR was replaced the same day by #58 (ADR-0035).
- The owner's discussion of the document type (a base class `LuiDocument<TKey, TPayload>`) ended in variant C: the
  base class does not compile as planned (`static virtual` only on interfaces, no inference of `TKey` from `TDoc`), and
  the types belong where the payload is used. Variant D (a typed view) is #59.

## 2026-11-05 — The sample fills its own bulk indexes (ADR-0029), third round

- The owner's second review: "the ux is still aweful and clunky of building the index and the query settings are overlapping
  still. Put each index into a tab and make it more clean and aligned." — and a report: the index showed no data and no
  suggestions after a tab switch.
- **The page**: two columns, the settings in the right one (they lie over nothing), the index panel back in the left one. The
  panel of the indexes is a **tab bar with one tab per index** and the count it holds — one card instead of six. The button that
  fills an index stands **in the tab of that index**, under the input, where the index is used.
- **The report of the owner could not be reproduced** (a fill, a tab switch, a query — all of it worked). What was found and
  fixed instead are the fragilities that produce exactly those symptoms: one index that cannot be read emptied the **whole**
  panel of the indexes (the route described every index in one go), a handler of the bus that threw stopped **all** the other
  handlers, a scenario that could not be built left an input that never answered. Now: the route describes every index on its
  own, the bus catches what a handler throws, the panel keeps what it had, the input says what is wrong.
- Tests: `SampleIndexPanelTests` (one broken index does not empty the panel) and the client test of the bus.

## 2026-11-05 — The sample fills its own bulk indexes (ADR-0029), second round

- The owner's review of the pull request, answered: the **settings are short** (the explanation of a setting is behind its "?"
  and is its tooltip as well; the panel fits a screen, 707 px, without a scroll of its own, and stands beside the autocomplete),
  the **word list is the full one and the sample downloads it** (685,829 words, CC0 — the same source and the same place as
  `tools/fetch-wordlist.sh`; the curated 7,071-word subset is what the tests use, and a test never downloads 5.5 MB), the
  **bulk indexes are created on demand** (not registered with the host: the index of LUI comes into being when the panel asks
  for it or when the button is pressed), and the **largest fill is three million entries** (the scale of ADR-0020).
- Measured: the fill of the whole German word list took **2 m 15 s** (685,829 entries, 69 batches, 158 MB of database), a
  million names about the same; the query against a million names stays in the milliseconds.
- New: `GermanWordList` downloads the list, `WordSource` prepares it, `BulkSampleIndex` creates the index of LUI on demand,
  `FillJobs.Limit` is three million, the fill has a note ("downloading the German word list …"). The client: the "?" of a
  setting, `data-index` and `fill-status` on the fill form, the limit of three million in the number field.

## 2026-11-05 — The sample fills its own bulk indexes (ADR-0029)

- ADR-0029. Three indexes beside the curated ones — `words` (the German word list), `people` (Bogus's German full names),
  `hashtags` (a place and a surname, joined by a hyphen) — filled by a button in the index panel: a number of entries, a
  background fill, the progress of the sync polled by the page. The source of a bulk index is a function of the number and is
  prefix-stable, so a fill with fewer entries removes the rest and a fill with more adds only what is missing.
- The word list is looked up in `LUI_WORDLIST`, then `artifacts/wordlist/`, then the curated subset of the tests; the panel names
  the source. Bogus 35.6.5 is a dependency of the sample alone.
- New: `Data/IBulkSource`, `BulkWeights`, `RepositoryRoot`, `GermanWordList`, `WordSource`, `PeopleSource`, `HashtagSource`;
  `Indexes/IBulkSampleIndex`, `BulkSampleIndex`; `Api/FillRequest`, `FillStatus`, `FillJob`, `FillJobs`; the client modules
  `scenarios/words.js`, `people.js`, `hashtags.js`, `panels/fill-form.js`. Changed: `IndexInfo` (source, capacity), the fill
  routes, `SampleApp`, `SampleSeed` (the bulk indexes are not seeded), `Directory.Packages.props`.
- Tests: `SampleBulkTests` (the fill, the progress, exactly the number of entries, the numbers that are refused) and the client
  test of the progress text. The map of the tests (`docs/testing.md`) knows the new file.
- The page: the index panel stands in the column of the layout instead of across it, and the settings scroll themselves when
  the window is short. The settings are longer than the column they stand in, so they lay over the index panel otherwise — a
  bug of ADR-0027's layout, visible for the first time with six cards.

## 2026-11-04 — #42: the API polish before the freeze

- ADR-0024. `AddLinkedIndex`; `LuiPriorityMode`/`PriorityMode`; `LuiTimings.CandidateRead`; `entries`; `LuiPayloadRow` (a kept
  row throws); abstract `LuiException`; `LuiLinkedSyncReport`; `ILuiIndex` only reads (`Check`, `WarmUp…`, `Rebuild…` on the write
  interfaces); no `ILuiSimilarity.Name`; README/spec on `AddLui` opening the database at the start.
- Guards: exceptions, `LuiQuery` ↔ `LuiQueryOptions`, the read interface; four mutations, each caught by test.
- #41 (`Owned` → `Embedded`, ADR-0023) merged as #46.

## 2026-11-04 — #41: `Owned` → `Embedded`

- ADR-0023. `ILuiEmbeddedIndex`, `LuiEmbeddedIndexBuilder`, `LuiEmbeddedIndexBuilderExtensions`, the internal `Embedded…`
  types, `lui_meta.source = 'Embedded'`, spec/README/CHANGELOG/living docs; ADRs keep the old word with a name note.
- Guard: `Public_API_Shape.The_content_sources_are_called_Embedded_and_Linked_…` + mutation
  `the-old-word-returns-to-the-public-surface`. Pre-release databases are refused, not migrated (delete the file).
- #40 (internal settings/registrations/loop) merged as #45 (`IndexOptions`, `IOutboxRegistration`, `PeriodicRound`).

## 2026-11-04 — PR-010 (#36): the README of the first release

- README per plan §7: query options with defaults, ranking and priority, typo tolerance, ADR-0020 budgets, BenchmarkDotNet.
- BenchmarkDotNet default job, all 24 benchmarks, 34:40 min; reports in `benchmarks/results/`; the README section is
  rendered by `Readme_Benchmarks` (`LUI_UPDATE_README=1`), which also fails on drift; `tools/benchmarks.sh`. ADR-0022.
- Smoke test runs the new blocks (priority, stale dictionary); mutation `a-readme-benchmark-number-edited-by-hand`.

## 2026-11-04 — PR-011 (#51): the sample

- ADR-0027, ADR-0028. `samples/Lui.Sample`: three autocompletes with flexdatalist 3.1.1 (vendored unmodified, SHA-256 in `vendor/README.md`):
  recipes (embedded, JSON payload, grouped), tags (linked, column payload, priority System, picker, add-new through triggers + outbox),
  names (typo tolerance, a dictionary of its own). Every `LuiQueryOptions` setting is a control and a parameter; the panels show the
  whole `LuiResult`, the hits as ranked, the requests, and the indexes with their maintenance.
- The client is ES modules by concern with the direction of the imports as a table (`Sample_Client`), one fetch boundary
  (`net/`: abort on every keystroke, the envelope on a bus, the items from a presenter), an escaped `_highlight` twin for every
  displayed property (flexdatalist writes them with `innerHTML`), a strict CSP, no build, no dependency.
- Found by checking flexdatalist's source: no abort, a marker that is the same function as its client filter (so none with
  `searchDisabled`), a cache keyed without `params`, an unescaped `innerHTML`.
- **`Highlight` struck (owner, 2026-11-04, ADR-0028, #20 closed):** the page marks nothing; a correction is "Did you mean …?" from
  `CorrectedQuery` (one line above the list, choosing it searches the word). A first version with a measured fold table and a span map
  was built, matched the tokenizer for 334 letters of 16 Latin alphabets, and was dropped. Spec ADR-0028 "falls away".
- Tests: 35 `SampleApiTests` (real Kestrel), 16 `Sample_Client`, 32 node tests (`npm test`, a CI lane), five mutations;
  browser assertions by hand (Chromium). The sample's `appsettings.json` is gone (a referencing test project copied it and changed its log level).

## 2026-11-04 — #43: the freeze

- ADR-0026. From `v0.1.0` on the public API grows only additively; a break needs a minor version, a migration note and an ADR.
  The rejected points of the review of 2026-11-04 are listed by name with their reasons (A.2, A.4, B.1, B.2, C.4/C.5, D.2a, D.3).
  Note in ADR-0018; README (Status) and CHANGELOG name the promise. A snapshot guard of the public API was offered and not taken.

## 2026-11-04 — #48: the typo dictionary only grows

- ADR-0025 (amends 0021). No `Stale`, no marks counter, no `LuiSyncReport.TypoDictionaryStale`, no `LuiTypoRebuilder`, no
  `RebuildStaleTypoDictionariesEvery`; the build uses SymSpell's `SuggestionStage` + `CommitStaged` (warm-up 2.8 → 2.3 s,
  rebuild 2.1 → 1.6 s at 300,000 entries). Every index has its own dictionary — pinned by a unit and an integration test and
  a mutation. Linked `Sync()` still rebuilds (explicit refill). `compactLevel` untouched (follow-up).

## 2026-11-04 — #19: a stale typo dictionary, a query that does not wait

- ADR-0021. An owned `Sync` that changes or removes marks a built dictionary `Stale` (new words are learned at once);
  `RebuildTypoDictionary()` makes it `Current`. `ILuiIndex.TypoDictionaryState`, `LuiResult.CorrectionSkipped`: a
  query does not wait for a build another thread runs. Hosting: `LuiTypoRebuilder`, `RebuildStaleTypoDictionariesEvery`
  (1 min default, `null` off). Linked `Sync()` still rebuilds.
- Measured at 300,000 words: delta sync 4.4–4.8 s → 2.6 s; the rebuild alone 2.0–2.3 s.

## 2026-11-04 — #24: the exact pin of the hosting package to the core (merged as #33)

## 2026-11-03 — PR-009: the performance budgets of ADR-0020 (issue #28)

- ADR-0020. `Corpus` (Testing) generates the measurement corpus for a seed; `PerformanceBudgets` measures every ADR-0020
  target in its scenario and asserts it, or prints its waiver; `benchmarks/Nordmann.LUI.Benchmarks` has real benchmarks.
- 3 M entries: query p50 1.6–1.7 ms ✓, p95 9.6–10.3 ms (≤ 3.1 ms up to four characters) waived → ADR-0010; allocation
  p95 298 KB waived (it follows the 500 candidates); corrected p95 29 ms waived; single write p95 0.5 ms ✓; build
  172.5 s with batches of 1,000,000 ✓ (190 s with 100,000); typo dictionary 138 MB / 4.4 s ✓; 1 M titles 457 MB waived.

## 2026-11-03 — fix: hosting lifecycle, Truncated edge (issue #26)

- A code review of `main` after #23: the hosted services now resolve the catalog first (disposed before it; Dispose
  cancels the warm-up); an outdated row after the budget no longer sets `Truncated`; the counters of `CandidateReader`
  are untangled. Docs: the F1 edge is 501 (not 1,001), the test package `Microsoft.Extensions.Hosting` is named, the
  order of hosted services is documented (+ a test with a migration registered before `AddLui`).

## 2026-11-03 — #22 → PR #25: the test tree (branch `feat/test-landscape`)

- ADR-0019. The one test project becomes three — `Nordmann.LUI.Unit.Tests` (no SQLite at all),
  `Nordmann.LUI.Integration.Tests`, `Nordmann.LUI.Architecture.Tests` — plus `Nordmann.LUI.Testing`, a
  library with the shared setup. The unit lane: 134 tests in 1.0 s; the whole suite 495 → 509 tests,
  37 s → 34 s.
- `docs/testing.md` is the map of the tests, and `Test_Landscape` is its guard: the map and the tree
  must agree, in both directions. The unit of the map is the *file* — parsing class names out of the
  source found the words "at", "that" and "holds" in the comments.
- The silent green is gone: the full-word-list parity reported *Passed* in 1 ms when the list was
  missing. Now it is skipped with the reason, and it fails under `LUI_REQUIRE_FULL_WORDLIST=1`.
- The gaps closed: `LuiJsonPayloadSerializer` (public, untested), the lazy similarity of ADR-0008
  (a promise about work — provable only by counting the calls of the measure), the ranking orders.
  The measurement asserted nothing below 3 M entries; the allocation budget holds at every size now.
- `Alba` was a dead pin; the guard checks version → reference now, with the three
  `Microsoft.Extensions.*` pins of ADR-0013 as the only named exception.
- CI runs the three lanes, the parity over the whole word list and the mutation check. The script
  itself grew a rule: a filter that matches no test is a broken script, not a caught mutation.
## 2026-11-03 — PR-008: dependency injection and hosted services (issue #21)

- ADR-0017. `AddLui(…)`, `.AddIndex(name, index => …)` (owned or linked by the lambda's return type), keyed singletons
  under the name (read and write interface, one instance); `LuiStartup` ensures on start (+ optional warm-up of the
  indexes with typo tolerance), `LuiPendingApplier` runs `ApplyPending` on `applyPendingEvery`, logs and goes on.
- Owner's decision (ADR-0018): the DI is a second package, `Nordmann.LUI.Hosting`, one version with the core; only it
  references the three abstractions (+5 transitive). It sees no internals; `ILuiIndex.TypoTolerance` is new for the
  warm-up, the options are a registered singleton for the `TimeProvider`, the name is checked on start.
- F1: an outdated linked candidate does not count against the budget; read on up to a second budget, then `Truncated`.
- `Highlight` moved to #20 by the owner; the full typo rebuild after a sync is #19.

## 2026-11-03 — PR-007: sync, check, rebuild, optimize (issue #17)

- ADR-0016. The catalog is a facade: `Storage.Database` (connections, writer, lifetime), `Indexes.IndexStore`
  (lui_meta, DDL), `Typos.TypoDictionaries`; the `LuiCatalog` exception of `Internal_Layering` is gone, two new mutations.
- Owned `Sync` in batches (default 10,000), sorted by rowid, `automerge` off, one `optimize` at the end; removal by a
  snapshot of the keys at the start, crossed off — a key added meanwhile stays. `Check(source)`, `Check()` (on
  `ILuiIndex`, also Linked), `Rebuild()`, `Optimize()`. 30 tests in `SyncCheckRebuildTests`; review of PR #18 worked in (S1–S4 docs, S3 and N6 in code, N3–N5, N7).
- Measured at 1 M: sync 77.8 s (batch 10 k) / 54.9 s (100 k), UpsertMany 52.5 s; query p95 after sync 3.9 ms vs 5.4 ms.
  The number of commits costs more than the order; the merge decides the query.
- Open for the owner: the default batch size; a shadow build for `Linked.Sync()` (S2; 26.7 s lock per million rows).

## 2026-11-03 — PR-006: the linked index, loosely coupled (issue #15)

- Owner's decisions: no column/index/implicit trigger on the application's table; the application keeps the index current
  (`Sync`, `Refresh`/`RefreshMany` with an optional transaction), triggers only on request (`TriggerScript`,
  `CreateTriggers`, `DropTriggers`); `Owned<long>` as the builder's default, `WithKey<string>()`, `LinkTo(…)`.
- ADR-0015. Contentless FTS5 + `lui_<name>` without text; 30 integration tests; `SchemaGoldenTests` pins the Owned schema.
- Measured: sync of 200 k rows 3.5–3.9 s, query p95 1.11–1.17 ms (owned 1.25–1.33), refresh p95 0.51–0.58 ms.
- Done from the review of PR #14: S3, N2, N3, N6. From the reviews of PR #16: M1 as an outbox (`ApplyPending`,
  `Pending`), M2 key range, M3 `DropIndex(dropTriggers)` + `Triggers()`, S1 stale candidates (F1: they use up the
  budget until applied — reading on is for PR-008), S3, S4, the builder names `LuiIndexKindBuilder` /
  `LuiIndexBuilder<TKey, TPayload>`.

## 2026-11-03 — PR-005b: seams for Linked (issue #13)

- ADR-0014: the kind as rules (one place, an architecture test forbids kind comparisons elsewhere), the content source as
  `ContentSource`/`OwnedSource`, the query as a pipeline of stages, one direction between the internal namespaces.
- Proven unchanged: `sqlite_master` byte-identical for both kinds against `main`; tests unchanged in their assertions;
  query latency and allocation equal to `main` (five alternating runs per kind, same word list — a first comparison in a
  worktree without `artifacts/` measured the sample list and showed a difference that does not exist).

## 2026-11-03 — PR-005a: the API shape before 0.1.0 (issue #11)

- From the owner's DevEx/architecture review: staged builder (`Index(name).SingleWord().Owned<long>().WithPayload(…)`),
  `ILuiIndex` (read) / `ILuiOwnedIndex` (write), `LuiEntry` + `UpsertMany` (4.4× faster than a loop), named result
  properties, `Lui` prefix and one public namespace, `Normalizer` internal (owner's decision), options checked when set,
  the three unused `Microsoft.Extensions.*` references out. ADR-0013; ADR-0008/ADR-0030/docs/api.md/ADR-0018 corrected.
- The smoke test runs every C# block of the README and checks what the quick start says it prints.
- Not done, by decision: the internal seams (next PR, 005b), instance cache and hosted warm-up (PR-008), `Suggest`.

## 2026-11-03 — PR-005: typo tolerance (issue #9)

- `TypoDictionary` (SymSpell, beginnings 3 … 10, frequency `weight + 1`, lazy, `ReaderWriterLockSlim`), correction when no
  candidate stands at a word start, up to three corrections searched and united with the input's candidates, each candidate ranked
  against the input it was found with. ADR-0012; ADR-0012.
- Off by default. Limits pinned by tests: nothing after the tenth letter, no deletion/insertion in a word of ten letters or more.
- Measured on the whole word list: dictionary 4.2 s / 138 MB; corrected query p95 8.7 ms (target 10) at 685 k entries.
- `TypoQualityTests` pins floors (4 letters 55 % … 8 letters 88 % of the right beginning); `TypoPerformance` is gated.

## 2026-11-02 — PR-004: the multi-word index (issue #7)

- `MultiWord`: `s_compact` and a search index over it alone (owner's decision: ADR-0011 and ADR-0008 corrected, −36 % file,
  −40 % build, −30 % latency), words of fewer than three characters only in the ranking, the prefix path decided on
  letters.
- `Similarity.NgramDice` is our own and allocates nothing (3.6 MB → 263 KB per query at p95), tested equal to
  `F23.SorensenDice(2)`.
- ADR-0011. Missed targets are written down (1 M query, file size).
- Gated experiments: `FtsLayoutExperiment`, `MultiWordLayoutExperiment` (`LUI_PERF=1`); `QueryPerformance` takes
  `LUI_PERF_KIND=multi`.

## 2026-11-02 — PR-003 + review (issue #5, PR #6, merged)

- The single-word index, ADR-0006 … ADR-0010; the review of the pull request was worked in (snapshot read, `#`
  rule, code points, value-type payload, no-op updates, …).
- `Nordmann.LUI` now answers queries: `LuiCatalog.Open`, `Index<TKey,TPayload>(…)
  .SingleWord().Owned().Ensure()`, `Upsert`/`UpdateWeight`/`UpdatePriority`/
  `Remove`, `Query(…)` with ranking, priority, `DistinctText`, payload for the
  hits only.
- Decisions: ADR-0006 (schema and rowid), ADR-0007 (the catalog probes the
  SQLite), ADR-0008 (query path), ADR-0009 (prepared write statements).
- The measurement that moved a decision: a fresh command per `Upsert` cost
  184 µs, a prepared one 59 µs — the write path keeps prepared statements on
  a writer connection (ADR-0009).
- A known limitation is pinned by a test: the prefix path (one or two
  characters) folds ASCII only (ADR-0006).
- `artifacts/wordlist/german-words.txt` (fetched with
  `tools/fetch-wordlist.sh`, git-ignored) makes the parity test run over all
  685,829 words and gives the performance test a real corpus.

## 2026-11-02 — PR-002: the normaliser (issue #3, PR #4)

- Merged. See `docs/normalization.md` and ADR-0005.

## 2026-11-02 — Repository foundation (PR-001)

- The repository was empty; it now holds the solution, the props, the CI, the
  tests, the documentation and four ADRs.
