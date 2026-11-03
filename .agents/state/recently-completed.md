# Recently completed

<!--
What the last sessions finished, and what the next one should know. Compress
the scratchpad here at the end of a session.
-->

## 2026-11-03 — PR-005b: seams for Linked (issue #13)

- ADR-0014: the kind as rules (one place, an architecture test forbids kind comparisons elsewhere), the content source as
  `ContentSource`/`OwnedSource`, the query as a pipeline of stages, one direction between the internal namespaces.
- Proven unchanged: `sqlite_master` byte-identical for both kinds against `main`; tests unchanged in their assertions;
  query latency equal (five alternating runs); multi-word alloc p50 51 KB vs 82 KB on `main`, cause not established.

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
