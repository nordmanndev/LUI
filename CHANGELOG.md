# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **The single-word index** (`SingleWord`, `Owned`) — the first index that
  answers a query ([ADR-0006](docs/adr/0006-the-single-word-index.md),
  [ADR-0008](docs/adr/0008-the-query-path.md)):
  - `LuiCatalog.Open(connectionString, options)` with `LuiCatalogOptions`
    (`CacheSize`, `MemoryMap`, `BusyTimeout`, `UseTimeProvider`), and
    `lui_meta`. `Dispose()` waits for the operations that are running.
  - `catalog.Index<TKey, TPayload>(name).SingleWord().Owned().Payload(…)
    .Similarity(…).GermanTransliteration(…).Ensure()` and `Drop()`; the
    result is an `ILuiIndex<TKey, TPayload>`.
  - Writes: `Upsert`, `UpdateWeight`, `UpdatePriority`, `Remove`, `Count` —
    also inside the application's `SqliteTransaction`
    ([ADR-0009](docs/adr/0009-writes-use-prepared-statements.md)).
  - Queries: the prefix path for one and two characters, FTS5 trigram
    candidates from three, ranked by priority → word start → similarity →
    weight (`RankingOrder`, `PriorityRanking`, `DistinctText`, `Take`,
    `Candidates`, `TrackTimings`), fluent (`LuiQuery`) or with
    `LuiQueryOptions`. The answer is a `LuiResult` of `LuiHit`s.
  - `Priority` (`System` … `Normal`) in the rowid: a system tag is a
    candidate for every input that matches it.
  - `Similarity.JaroWinkler`, `NgramDice`, `DamerauLevenshtein`,
    `Levenshtein` and `ITextSimilarity` for your own.
  - Payloads: `IPayloadSerializer<T>` and `JsonPayloadSerializer<T>` over a
    source-generated `JsonTypeInfo<T>`.
  - `LuiDefinitionMismatchException` and `LuiEnvironmentException`.
- **The catalog checks the SQLite it runs on** — the version and the folding
  of the tokenizer against the measured table
  ([ADR-0007](docs/adr/0007-the-catalog-checks-the-sqlite-it-runs-on.md)).
  `LuiResult.CorrectedQuery` and `LuiHit.Highlight` are reserved and `null`
  until the typo tolerance and the highlighting land.

- `Nordmann.LUI.Normalization.Normalizer` — the C# side of the normalisation
  rule: the search form, the compact form and the map back to the original
  text, with the German transliteration on or off.
- The measured fold table that keeps the C# side and SQLite's tokenizer in
  step — see [`docs/normalization.md`](./docs/normalization.md) and
  [ADR-0005](docs/adr/0005-measured-normalisation.md).
- `Normalizer.TryWriteSearchForm` / `TryWriteCompactForm` — the same two
  forms without an allocation, for the path that normalises every candidate.

## [0.1.0] — not published

The first release. It ships the package `Nordmann.LUI` with the API of the
specification ([`docs/spec/LUI-Spezifikation.md`](./docs/spec/LUI-Spezifikation.md)):

- `SingleWord` and `MultiWord` indices, `Owned` and `Linked` content sources
- FTS5 trigram search with priority-first candidates, word-start ranking and
  similarity scoring
- typo tolerance over a prefix dictionary
- `Sync`, `Check`, `Rebuild`, `Optimize`
- dependency injection (`AddLui`, keyed services)
- a sample application that exposes the whole API in the browser

[Unreleased]: https://github.com/nordmanndev/LUI/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/nordmanndev/LUI/releases/tag/v0.1.0
