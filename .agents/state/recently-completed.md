# Recently completed

<!--
What the last sessions finished, and what the next one should know. Compress
the scratchpad here at the end of a session.
-->

## 2026-11-04 — #41: `Owned` → `Embedded`

- ADR-0023. `ILuiEmbeddedIndex`, `LuiEmbeddedIndexBuilder`, `LuiEmbeddedIndexBuilderExtensions`, the internal `Embedded…`
  types, `lui_meta.source = 'Embedded'`, spec/README/CHANGELOG/living docs; ADRs keep the old word with a name note.
- Guard: `Public_API_Shape.The_content_sources_are_called_Embedded_and_Linked_…` + mutation
  `the-old-word-returns-to-the-public-surface`. Pre-release databases are refused, not migrated (delete the file).
- #40 (internal settings/registrations/loop) merged as #45 (`IndexOptions`, `IOutboxRegistration`, `PeriodicRound`).

## 2026-11-04 — PR-010 (#36): the README of the first release

- README per plan §7: query options with defaults, ranking and priority, typo tolerance, §16 budgets, BenchmarkDotNet.
- BenchmarkDotNet default job, all 24 benchmarks, 34:40 min; reports in `benchmarks/results/`; the README section is
  rendered by `Readme_Benchmarks` (`LUI_UPDATE_README=1`), which also fails on drift; `tools/benchmarks.sh`. ADR-0022.
- Smoke test runs the new blocks (priority, stale dictionary); mutation `a-readme-benchmark-number-edited-by-hand`.

## 2026-11-04 — #19: a stale typo dictionary, a query that does not wait

- ADR-0021. An owned `Sync` that changes or removes marks a built dictionary `Stale` (new words are learned at once);
  `RebuildTypoDictionary()` makes it `Current`. `ILuiIndex.TypoDictionaryState`, `LuiResult.CorrectionSkipped`: a
  query does not wait for a build another thread runs. Hosting: `LuiTypoRebuilder`, `RebuildStaleTypoDictionariesEvery`
  (1 min default, `null` off). Linked `Sync()` still rebuilds.
- Measured at 300,000 words: delta sync 4.4–4.8 s → 2.6 s; the rebuild alone 2.0–2.3 s.

## 2026-11-04 — #24: the exact pin of the hosting package to the core (merged as #33)

## 2026-11-03 — PR-009: the performance budgets of §16 (issue #28)

- ADR-0020. `Corpus` (Testing) generates the measurement corpus for a seed; `PerformanceBudgets` measures every §16
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
  the three unused `Microsoft.Extensions.*` references out. ADR-0013; spec §8.3/§11/§12/§17 corrected.
- The smoke test runs every C# block of the README and checks what the quick start says it prints.
- Not done, by decision: the internal seams (next PR, 005b), instance cache and hosted warm-up (PR-008), `Suggest`.

## 2026-11-03 — PR-005: typo tolerance (issue #9)

- `TypoDictionary` (SymSpell, beginnings 3 … 10, frequency `weight + 1`, lazy, `ReaderWriterLockSlim`), correction when no
  candidate stands at a word start, up to three corrections searched and united with the input's candidates, each candidate ranked
  against the input it was found with. ADR-0012; spec §10.3a.
- Off by default. Limits pinned by tests: nothing after the tenth letter, no deletion/insertion in a word of ten letters or more.
- Measured on the whole word list: dictionary 4.2 s / 138 MB; corrected query p95 8.7 ms (target 10) at 685 k entries.
- `TypoQualityTests` pins floors (4 letters 55 % … 8 letters 88 % of the right beginning); `TypoPerformance` is gated.

## 2026-11-02 — PR-004: the multi-word index (issue #7)

- `MultiWord`: `s_compact` and a search index over it alone (owner's decision: §7.4 and §8.2 corrected, −36 % file,
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
