# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Dependency injection** ([ADR-0017](docs/adr/0017-dependency-injection-and-hosted-services.md)):
  `services.AddLui(connectionString, options?)` (or with delegates that read the container) and
  `.AddIndex(name, index => …)` for an owned or a linked index. Every index is a keyed singleton under its name, as
  `ILuiIndex<TKey, TPayload>` and as `ILuiOwnedIndex`/`ILuiLinkedIndex`; the catalog is a singleton the container
  disposes. A hosted service ensures every index when the host starts (`EnsureOnStart(false)` switches it off) and,
  with `WarmUpTypoDictionariesOnStart()`, builds the typo dictionaries of the indexes with typo tolerance in the
  background; another one applies the outbox of a linked index on its interval (`applyPendingEvery`). The package
  references `Microsoft.Extensions.DependencyInjection.Abstractions`, `.Hosting.Abstractions` and
  `.Logging.Abstractions` (10.0.14) again.

- **Sync, check, rebuild and optimize** ([ADR-0016](docs/adr/0016-sync-check-rebuild.md)). An owned index:
  `Sync(source, options?, cancellationToken)` (and an overload for an `IAsyncEnumerable` source) adds what is missing,
  writes what differs, keeps what is the same and removes the keys that were there when it began and that the source
  does not have — in batches of
  `LuiSyncOptions.BatchSize` (default 10,000), each in a transaction of its own, written in the order of the search
  index and merged once at the end. A sync cancelled while it reads the source keeps its committed batches and removes
  nothing (one cancelled during the removal at the end leaves the rest of it); run again, it skips what is done. It returns a `LuiSyncReport` (`Read`, `Added`, `Changed`, `Unchanged`, `Removed`, `Batches`, `Merged`) and
  reports the same after every batch to `LuiSyncOptions.Progress`. `Check(source)` compares and changes nothing;
  `Rebuild()` creates the triggers and the search index anew; `Optimize()` merges. Every index has `Check()` (on
  `ILuiIndex`): the definition, the triggers, FTS5's integrity check — for a linked index also the table and the
  entries against its rows. It returns a `LuiIntegrityReport` (`Problems`, `IsHealthy`, `Missing`, `Extra`,
  `Different`). 1,000,000 entries: sync 78 s (batches of 10,000) or 55 s (100,000), query p95 afterwards 3.9 ms
  (5.4 ms after `UpsertMany`).

- **The linked index** ([ADR-0015](docs/adr/0015-the-linked-index-is-loosely-coupled.md)), loosely coupled to a table
  of the application: `lui.Index(name).SingleWord().LinkTo(table, key, text, weight?, priority?).Ensure()` returns an
  `ILuiLinkedIndex<TPayload>`. LUI never changes the application's table on its own — no column, no index, no trigger;
  `Ensure()` checks the table and creates LUI's own tables (a contentless FTS5 table, the text is not stored twice).
  The application keeps it current: `Sync()`, `Refresh(key, transaction = null)`, `RefreshMany(keys, transaction = null)`,
  `Optimize()`; triggers only on request: `TriggerScript()`, `CreateTriggers()`, `DropTriggers()` — they only note the
  key of a changed row in an outbox (plain SQL, any SQLite can write the table); `ApplyPending()` brings the noted rows
  into the index, `Pending()` counts them. A payload from columns:
  `WithPayload(columns, row => …)` with `LuiRow`. `LuiLinkedTableException` when the table is not what the link says.
  `Sync()` returns a `LuiSyncResult` (indexed, without text, key out of range); `Triggers()` returns a
  `LuiTriggerState` (`None`, `Current`, `Stale`); `DropIndex(name, dropTriggers)` removes the triggers of a linked
  index only when asked. A candidate whose current text no longer matches is not a hit.
  200,000 rows: sync 3.5–3.9 s, query p95 1.11–1.17 ms, refresh p95 0.51–0.58 ms.

### Changed

- A candidate of a linked index whose current text no longer matches does not count against the candidate budget any
  more: the read goes on past it, up to another budget of such rows (review of PR #16, F1).
- `Highlight` is not part of 0.1 (owner's decision): the front end marks the match; it comes with a later version (#20).
- `ILuiIndex<TKey, TPayload>` has `Check()`. Inside, the catalog is taken apart (review DevEx D4, D6): no change to
  the public API besides.
- **The builder: `Owned<long>` is the default** (ADR-0015). `lui.Index(name)` returns a `LuiIndexKindBuilder`;
  `SingleWord()`/`MultiWord()` return a `LuiIndexBuilder<long, LuiNoPayload>` — an owned index, or with `LinkTo(…)`
  a linked one (the names were `LuiIndexBuilder` and `LuiOwnedIndexBuilder<TKey, TPayload>`, review of PR #16, N1);
  `Owned<TKey>()` and `LuiIndexSourceBuilder` are gone, `WithKey<string>()` chooses a text key.
  `lui.Index("tags").SingleWord().Ensure()` is a whole index.
- `DropIndex` decides by the stored source and refuses a source it does not know.

- **The API shape before the first release** ([ADR-0013](docs/adr/0013-the-api-shape-before-the-first-release.md)) —
  a break on purpose, while it costs nothing:
  - A staged builder: `lui.Index(name).SingleWord().WithPayload(…).TypoTolerance().Ensure()`. An index
    without a kind does not compile; the key type is checked at `WithKey<TKey>()`; a payload is optional
    (`LuiNoPayload` until `WithPayload`). `TypoTolerance(int maxEdits)` is `TypoTolerance(bool enabled = true)`.
    `Drop()` is `LuiCatalog.DropIndex(name)`.
  - Reading and writing are two interfaces: `ILuiIndex<TKey, TPayload>` (`Query`, `Search`, `Count`, the typo
    dictionary) and `ILuiOwnedIndex<TKey, TPayload>` (the writes). `Query(text, options)` is
    `Search(text, options = null)`.
  - `LuiEntry<TKey, TPayload>(Key, Text) { Weight, Priority, Payload }` is what an upsert stores; the short form
    `Upsert(key, text, weight, priority, transaction)` carries no payload.
  - `LuiHit`, `LuiResult` and `LuiTimings` have named `init` properties instead of positions; `LuiHit.Highlight`
    and `Highlight` are gone until the highlighting lands.
  - `LuiCatalogOptions` is a record with `init` properties (`CacheSizeMegabytes`, `MemoryMapMegabytes`,
    `BusyTimeout`, `WriteAheadLog`, `TimeProvider`), checked when set and bindable from configuration;
    `LuiCatalog.Open(connectionString, options)` takes it as a value. `LuiQueryOptions` checks its values when they
    are set, so `Take(0)` throws at the call.
  - Every public type lives in `Nordmann.LUI` and starts with `Lui`/`ILui`: `Priority` → `LuiPriority`,
    `IndexKind` → `LuiIndexKind`, `RankingOrder` → `LuiRankingOrder`, `PriorityRanking` → `LuiPriorityRanking`,
    `ITextSimilarity` → `ILuiSimilarity`, `Similarity` → `LuiSimilarity`, `IPayloadSerializer<T>` →
    `ILuiPayloadSerializer<T>`, `JsonPayloadSerializer<T>` → `LuiJsonPayloadSerializer<T>`.
  - `Normalizer`, `NormalizedText` and `NormalizationOptions` are internal.
  - The package no longer depends on `Microsoft.Extensions.DependencyInjection.Abstractions`,
    `Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Logging.Abstractions`: no code used them.

### Added

- `ILuiOwnedIndex.UpsertMany(entries, transaction, cancellationToken)` — many entries in one transaction, all or
  nothing in LUI's own; 100,000 entries in 3.8 s instead of 16.9 s one by one (4.4×).
  The typo dictionary learns the words at once, also inside the application's transaction: a rollback leaves
  them there until `RebuildTypoDictionary()` (documented and pinned by a test).
- A quick start in the README, run by the package smoke test.

- **Typo tolerance** ([ADR-0012](docs/adr/0012-typo-tolerance.md)): `TypoTolerance()` on the builder
  (off unless asked for), `LuiQuery.TypoTolerance(bool)` and `LuiQueryOptions.TypoTolerance`.
  An input without a word start is corrected by one edit with a SymSpell dictionary of the word
  beginnings (3 … 10 letters) of the index; the three best corrections are searched and united with
  the candidates of the input. `LuiResult.CorrectedQuery` says what was searched, `LuiTimings.Correction`
  what it cost, `ILuiIndex.WarmUpTypoDictionary()` builds the dictionary ahead of the first typo,
  `RebuildTypoDictionary()` builds it anew (both take a `CancellationToken`); the dictionary is shared by all
  instances of an index and its build never holds up a write. The dictionary of the whole
  German word list takes 4.2 s and 138 MB.

- **The multi-word index** (`MultiWord`, `Owned`): `catalog.Index(name).MultiWord().Ensure()`
  for names and titles ([ADR-0011](docs/adr/0011-the-multi-word-index.md)). The input is split
  into words that are ANDed and also searched compactly, so `kuchen apfel` and `apfelkuch` both
  find „Apfel Kuchen"; the word start is decided on the compact form; the default similarity is
  N-gram Dice. A multi-word index has a compact column `s_compact`, and its search index holds
  that column alone — the specification (§7.4, §8.2) is corrected accordingly. One and two letters
  are read on the compact text (`a b` = `ab` = `a-b`); at most the eight longest words go into the
  expression.
- `LuiSimilarity.NgramDice` no longer allocates: a query that compared 500 candidates allocated
  3.6 MB at the 95th percentile, now 263 KB; the number is the one of `F23.SorensenDice(2)`.

- **The single-word index** (`SingleWord`, `Owned`) — the first index that
  answers a query ([ADR-0006](docs/adr/0006-the-single-word-index.md),
  [ADR-0008](docs/adr/0008-the-query-path.md)):
  - `LuiCatalog.Open(connectionString, options)` with `LuiCatalogOptions`
    (`CacheSizeMegabytes`, `MemoryMapMegabytes`, `BusyTimeout`, `TimeProvider`), and
    `lui_meta`. `Dispose()` waits for the operations that are running.
  - `catalog.Index(name).SingleWord().WithKey<TKey>().WithPayload(…)
    .Similarity(…).GermanTransliteration(…).Ensure()` and `DropIndex(name)`; the
    result is an `ILuiOwnedIndex<TKey, TPayload>`.
  - Writes: `Upsert`, `UpdateWeight`, `UpdatePriority`, `Remove`, `Count` —
    also inside the application's `SqliteTransaction`
    ([ADR-0009](docs/adr/0009-writes-use-prepared-statements.md)).
  - Queries: the prefix path for one and two characters, FTS5 trigram
    candidates from three, ranked by priority → word start → similarity →
    weight (`LuiRankingOrder`, `LuiPriorityRanking`, `DistinctText`, `Take`,
    `Candidates`, `TrackTimings`), fluent (`LuiQuery`) or with
    `Search(text, LuiQueryOptions)`. The answer is a `LuiResult` of `LuiHit`s.
  - `LuiPriority` (`System` … `Normal`) in the rowid: a system tag is a
    candidate for every input that matches it.
  - `LuiCatalogOptions.WriteAheadLog`: `Open` switches the database to
    WAL by default; this keeps the application's journal mode.
  - `LuiSimilarity.JaroWinkler`, `NgramDice`, `DamerauLevenshtein`,
    `Levenshtein` and `ILuiSimilarity` for your own.
  - Payloads: `ILuiPayloadSerializer<T>` and `LuiJsonPayloadSerializer<T>` over a
    source-generated `JsonTypeInfo<T>`.
  - `LuiDefinitionMismatchException` and `LuiEnvironmentException`.
- **The catalog checks the SQLite it runs on** — the version and the folding
  of the tokenizer against the measured table
  ([ADR-0007](docs/adr/0007-the-catalog-checks-the-sqlite-it-runs-on.md)).

- The (internal) `Normalizer` — the C# side of the normalisation
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
