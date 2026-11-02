# Recently completed

<!--
What the last sessions finished, and what the next one should know. Compress
the scratchpad here at the end of a session.
-->

## 2026-11-02 — PR-003: the single-word index (issue #5)

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
