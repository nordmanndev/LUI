# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **The read path opens an index without writing** ([ADR-0033](.agents/adr/0033-the-read-path-opens-an-index-without-writing.md),
  #56). A second way into an index: the definition is read out of `lui_meta`, the index is rebuilt from it, and the
  file is not touched — no table, no definition, no write lock. It is the way into a database that is written
  elsewhere: a replica, a file that is read-only, a service that may not write.
  - `LuiCatalog.Index(name)` opens an embedded index, `LuiCatalog.LinkedIndex(name)` and
    `LuiCatalog.LinkedIndex(name, map)` a linked one — the columns of the payload are in the definition, the map is the
    one of the application. None of them names a type ([ADR-0035](.agents/adr/0035-the-index-knows-no-types.md), below).
  - The existence of the index, the version of its schema, the version of its folding and its content source are
    checked when it is opened, and a mismatch says what is wrong: the new `LuiIndexNotFoundException` (with the name of
    the index) and `LuiDefinitionMismatchException`.
  - The similarity is the default of the kind of the index and the typo tolerance is off; a query asks for a
    correction (`LuiQueryOptions.TypoTolerance`).
  - The index it hands out is a view on the file: it writes when the file allows it.
  - **The file decides what may happen — not the index**: a catalog that was opened with `Mode=ReadOnly` writes
    nothing — no WAL, no `lui_meta` — and refuses every write with the new `LuiReadOnlyException` (`Create`,
    `DropIndex`, `Upsert`, `Sync`). The same application code therefore runs in the service that writes and in the
    service that only reads.

- **The sample fills its own bulk indexes** ([ADR-0029](.agents/adr/0029-the-sample-fills-its-own-bulk-indexes-on-demand.md)). Three
  indexes beside the curated ones — `words` (the German word list, one word per entry), `people` (the full names of Bogus's
  German locale) and `hashtags` (a place and a surname, joined by a hyphen) — are created and loaded by a button in the tab of
  the index: you say how many entries it should hold, and the fill runs in the background while the page shows its progress.
  The indexes do not exist before the button is pressed, and the sample downloads the German word list itself (685,829 words,
  CC0) when it is not on the machine — the same source and the same place as `tools/fetch-wordlist.sh`; the panel says which
  list it found. Bogus is a dependency of the sample alone — the two packages keep theirs. Every index has a tab of its own in
  the panel of the indexes, the settings of the query keep their own column and never lie over the page, and one thing that
  breaks says so instead of taking the rest of the page down with it. No change to the public API.

- **The sample** ([ADR-0027](.agents/adr/0027-the-sample-flexdatalist-modular-client-one-fetch-boundary.md), #51). `samples/Lui.Sample` is an
  ASP.NET Core application with three autocompletes built with flexdatalist — recipes (embedded, JSON payload), tags (linked to
  the application's table, column payload, priority System, add-new through triggers and the outbox) and names (typo
  tolerance, a dictionary of its own) — every query setting as a control, the whole `LuiResult`, the hits as ranked, the
  write side and the maintenance. The client is plain ES modules with one fetch boundary that aborts a request on every
  keystroke and escapes everything flexdatalist writes with `innerHTML`; a corrected input is shown as "Did you mean …?".
  No change to the public API.

- **The README of the first release** ([ADR-0022](.agents/adr/0022-the-benchmark-results-in-the-readme.md), #36): the
  query options with their defaults, the ranking and the priority, the typo tolerance, the §16 budgets and the results
  of BenchmarkDotNet. The benchmark tables are rendered from BenchmarkDotNet's reports in `benchmarks/results/` by the
  architecture test `Readme_Benchmarks`, which fails when the README and the reports differ; `tools/benchmarks.sh`
  makes a new run.

- **A typo dictionary that only grows, a query that does not wait** ([ADR-0021](.agents/adr/0021-a-stale-typo-dictionary-and-a-query-that-does-not-wait.md),
  [ADR-0025](.agents/adr/0025-the-typo-dictionary-only-grows.md), #19, #48). A built typo dictionary learns the words of every
  write at once and is never built anew by itself: `Sync` of an embedded index does not touch it, a removed word lingers
  until `RebuildTypoDictionary()` (the index is the filter, so a correction towards it falls away), and the build uses
  SymSpell's bulk path (the warm-up of a 300,000-entry index: 2.8 → 2.3 s). Every index has a dictionary of its own — a search among tags is
  never corrected towards names. New API:
  - `LuiTypoDictionaryState` (`NotBuilt`, `Building`, `Current`) and `ILuiIndex.TypoDictionaryState` — an application's own
    implementation of `ILuiIndex` (a test double) adds the property;
  - `LuiResult.CorrectionSkipped`: a query that would correct while another thread builds the dictionary no longer
    waits for it, and one whose own build fails no longer throws; it answers with what its input found and sets the
    flag. The build a query starts no longer takes the query's token: a cancelled request leaves the dictionary built.
  The sync of a linked index still builds its dictionary anew at its end (a full refill, called by the application).
  `WarmUpTypoDictionary` and `RebuildTypoDictionary` now end their wait for a build another thread runs when their token
  is cancelled.

- **The performance budgets of §16, measured** ([ADR-0020](.agents/adr/0020-the-performance-budgets-measured.md)): a test per
  target in its scenario (`PerformanceBudgets`, gated), a generated corpus for the measurements, and the benchmark
  project (`benchmarks/Nordmann.LUI.Benchmarks`, BenchmarkDotNet). At three million entries the query median (1.6 ms),
  the single write (p95 0.5–0.8 ms) and the typo dictionary are met. Four targets of §16 are
  stated anew by the owner after the measurement: the query p95 per input length (≤ 5 ms for 1–4 characters, ≤ 20 ms
  for 5–8), the corrected query (p95 ≤ 35 ms), the allocation (p50 ≤ 250 KB, p95 ≤ 350 KB — too much, bug #30) and the
  file of a million titles (≤ 500 MB) and the build (≤ 4 min — it follows the disk, 172–206 s on two machines; a faster
  first load is #32). Every target is asserted.

- **Dependency injection, in a second package `Nordmann.LUI.Hosting`** ([ADR-0017](.agents/adr/0017-dependency-injection-and-hosted-services.md),
  [ADR-0018](.agents/adr/0018-two-packages-core-and-hosting.md),
  [ADR-0034](.agents/adr/0034-the-hosting-package-carries-only-the-dependency-injection.md)), released with `Nordmann.LUI` under the same version:
  `services.AddLui(connectionString, options?)` (or with delegates that read the container) puts the **catalog** into the
  container — and nothing else ([ADR-0033](.agents/adr/0033-the-read-path-opens-an-index-without-writing.md)): an index is
  not registered and not injected, the application asks the catalog for what it needs — `catalog.Create(name, …)` in its
  migration, `catalog.Index(…)` in the runtime path. The catalog is a singleton the container disposes. The package
  references `Microsoft.Extensions.DependencyInjection.Abstractions` (10.0.14) and nothing else: the abstractions of the
  host are gone (ADR-0034); the core
  `Nordmann.LUI` does not carry them either — a console application carries none of them.
- `ILuiIndex.TypoTolerance`: whether a query of the instance corrects typos unless it says otherwise. An application's
  own implementation of `ILuiIndex` (a test double) has to add it.

- **Sync, check, rebuild and optimize** ([ADR-0016](.agents/adr/0016-sync-check-rebuild.md)). An embedded index:
  `Sync(entries, options?, cancellationToken)` (and an overload for an `IAsyncEnumerable` source) adds what is missing,
  writes what differs, keeps what is the same and removes the keys that were there when it began and that the source
  does not have — in batches of
  `LuiSyncOptions.BatchSize` (default 10,000), each in a transaction of its own, written in the order of the search
  index and merged once at the end. A sync cancelled while it reads the source keeps its committed batches and removes
  nothing (one cancelled during the removal at the end leaves the rest of it); run again, it skips what is done. It returns a `LuiSyncReport` (`Read`, `Added`, `Changed`, `Unchanged`, `Removed`, `Batches`, `Merged`) and
  reports the same after every batch to `LuiSyncOptions.Progress`. `Check(entries)` compares and changes nothing;
  `Rebuild()` creates the triggers and the search index anew; `Optimize()` merges. Every index has `Check()` (on
  `ILuiEmbeddedIndex` and `ILuiLinkedIndex`): the definition, the triggers, FTS5's integrity check — for a linked index also the table and the
  entries against its rows. It returns a `LuiIntegrityReport` (`Problems`, `IsHealthy`, `Missing`, `Extra`,
  `Different`). 1,000,000 entries: sync 78 s (batches of 10,000) or 55 s (100,000), query p95 afterwards 3.9 ms
  (5.4 ms after `UpsertMany`).

- **The linked index** ([ADR-0015](.agents/adr/0015-the-linked-index-is-loosely-coupled.md)), loosely coupled to a table
  of the application: `lui.Create(name, index => index.SingleWord().LinkTo(table, key, text, weight?, priority?))`
  returns an `ILuiLinkedIndex<TPayload>`. LUI never changes the application's table on its own — no column, no index, no trigger;
  `Create()` checks the table and creates LUI's own tables (a contentless FTS5 table, the text is not stored twice).
  The application keeps it current: `Sync()`, `Refresh(key, transaction = null)`, `RefreshMany(keys, transaction = null)`,
  `Optimize()`; triggers only on request: `TriggerScript()`, `CreateTriggers()`, `DropTriggers()` — they only note the
  key of a changed row in an outbox (plain SQL, any SQLite can write the table); `ApplyPending()` brings the noted rows
  into the index, `Pending()` counts them. A payload from columns:
  `WithPayload(columns, row => …)` with `LuiPayloadRow`. `LuiLinkedTableException` when the table is not what the link says.
  `Sync()` returns a `LuiLinkedSyncReport` (indexed, without text, key out of range); `Triggers()` returns a
  `LuiTriggerState` (`None`, `Current`, `Stale`); `DropIndex(name, dropTriggers)` removes the triggers of a linked
  index only when asked. A candidate whose current text no longer matches is not a hit.
  200,000 rows: sync 3.5–3.9 s, query p95 1.11–1.17 ms, refresh p95 0.51–0.58 ms.

- `ILuiEmbeddedIndex.UpsertMany(entries, transaction, cancellationToken)` — many entries in one transaction, all or
  nothing in LUI's own; 100,000 entries in 3.8 s instead of 16.9 s one by one (4.4×).
  The typo dictionary learns the words at once, also inside the application's transaction: a rollback leaves
  them there until `RebuildTypoDictionary()` (documented and pinned by a test).
- A quick start in the README, run by the package smoke test.

- **Typo tolerance** ([ADR-0012](.agents/adr/0012-typo-tolerance.md)): `TypoTolerance()` on the builder
  (off unless asked for), `LuiQuery.TypoTolerance(bool)` and `LuiQueryOptions.TypoTolerance`.
  An input without a word start is corrected by one edit with a SymSpell dictionary of the word
  beginnings (3 … 10 letters) of the index; the three best corrections are searched and united with
  the candidates of the input. `LuiResult.CorrectedQuery` says what was searched, `LuiTimings.Correction`
  what it cost, `WarmUpTypoDictionary()` builds the dictionary ahead of the first typo,
  `RebuildTypoDictionary()` builds it anew (both are on `ILuiEmbeddedIndex` and `ILuiLinkedIndex`, and take a `CancellationToken`); the dictionary is shared by all
  instances of an index and its build never holds up a write. The dictionary of the whole
  German word list takes 4.2 s and 138 MB.

- **The multi-word index** (`MultiWord`, `Embedded`): `catalog.Create(name, index => index.MultiWord())`
  for names and titles ([ADR-0011](.agents/adr/0011-the-multi-word-index.md)). The input is split
  into words that are ANDed and also searched compactly, so `kuchen apfel` and `apfelkuch` both
  find „Apfel Kuchen"; the word start is decided on the compact form; the default similarity is
  N-gram Dice. A multi-word index has a compact column `s_compact`, and its search index holds
  that column alone — the specification (§7.4, §8.2) is corrected accordingly. One and two letters
  are read on the compact text (`a b` = `ab` = `a-b`); at most the eight longest words go into the
  expression.
- `LuiSimilarity.NgramDice` no longer allocates: a query that compared 500 candidates allocated
  3.6 MB at the 95th percentile, now 263 KB; the number is the one of `F23.SorensenDice(2)`.

- **The single-word index** (`SingleWord`, `Embedded`) — the first index that
  answers a query ([ADR-0006](.agents/adr/0006-the-single-word-index.md),
  [ADR-0008](.agents/adr/0008-the-query-path.md)):
  - `LuiCatalog.Open(connectionString, options)` with `LuiCatalogOptions`
    (`CacheSizeMegabytes`, `MemoryMapMegabytes`, `BusyTimeout`, `TimeProvider`), and
    `lui_meta`. `Dispose()` waits for the operations that are running.
  - `catalog.Create(name, index => index.SingleWord().WithKey(…)
    .Similarity(…).GermanTransliteration(…))` and `DropIndex(name)`; the
    result is an `ILuiEmbeddedIndex` (no type parameter, ADR-0035).
  - Writes: `Upsert`, `UpdateWeight`, `UpdatePriority`, `Remove`, `Count` —
    also inside the application's `SqliteTransaction`
    ([ADR-0009](.agents/adr/0009-writes-use-prepared-statements.md)).
  - Queries: the prefix path for one and two characters, FTS5 trigram
    candidates from three, ranked by priority → word start → similarity →
    weight (`LuiRankingOrder`, `LuiPriorityMode`, `DistinctText`, `Take`,
    `Candidates`, `TrackTimings`), fluent (`LuiQuery`) or with
    `Search(text, LuiQueryOptions)`. The answer is a `LuiResult` of `LuiHit`s.
  - `LuiPriority` (`System` … `Normal`) in the rowid: a system tag is a
    candidate for every input that matches it.
  - `LuiCatalogOptions.WriteAheadLog`: `Open` switches the database to
    WAL by default; this keeps the application's journal mode.
  - `LuiSimilarity.JaroWinkler`, `NgramDice`, `DamerauLevenshtein`,
    `Levenshtein` and `ILuiSimilarity` for your own.
  - Payloads: one `ILuiPayloadSerializer` per catalog, `LuiPayloadSerializer.Json`
    over the source-generated `JsonSerializerContext` of the application, read
    with `hit.GetPayload<T>()` (ADR-0035).
  - `LuiDefinitionMismatchException` and `LuiEnvironmentException`.
- **The catalog checks the SQLite it runs on** — the version and the folding
  of the tokenizer against the measured table
  ([ADR-0007](.agents/adr/0007-the-catalog-checks-the-sqlite-it-runs-on.md)).

- The (internal) `Normalizer` — the C# side of the normalisation
  rule: the search form, the compact form and the map back to the original
  text, with the German transliteration on or off.
- The measured fold table that keeps the C# side and SQLite's tokenizer in
  step — see [`docs/normalization.md`](./docs/normalization.md) and
  [ADR-0005](.agents/adr/0005-measured-normalisation.md).
- `Normalizer.TryWriteSearchForm` / `TryWriteCompactForm` — the same two
  forms without an allocation, for the path that normalises every candidate.

### Removed

- **`Highlight` is struck from the specification** ([ADR-0028](.agents/adr/0028-lui-has-no-highlight-a-correction-is-shown-as-a-question.md), #20
  closed): LUI delivers no found positions (`LuiHit.Highlight`, `.Highlight(…)`, a map from the search form to the original). It never
  existed in the code; spec §9.3 now says "entfällt". An application shows a correction with `LuiResult.CorrectedQuery`.

- **The freeze** ([ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md), #43). From the tag `v0.1.0` on, the
  public API of `Nordmann.LUI` and `Nordmann.LUI.Hosting` grows only additively. A breaking change — a removed or renamed
  member, a changed signature (a new optional parameter counts), a changed meaning of a documented behaviour — needs a new
  minor version, a migration note in this file and an ADR. A new interface member comes as a default member; a new enum value
  is a line here ("adds `X` to `Y`"). The record also lists the points of the architecture review of 2026-11-04 that were
  decided against, by name, with their reasons.

### Fixed

- **The distance of two long texts no longer depends on what the pool handed out before.** The table of the
  Damerau-Levenshtein distance does not fit on the stack for texts of more than about 45 characters, so it comes from
  the pool — and the pool hands out arrays that still hold what the last tenant wrote in them. The cell at the corner
  of the table was never written by the measure, and the transposition reads it when neither text has seen the
  character yet: the number was then the rubbish of the pool, and on a machine where the pool held a large number in
  that cell the distance overflowed (the CI found it: the similarity of two long texts came out as 2,404,636 instead of
  a number between zero and one). The corner is zero now, like in the library the measure replaces. The guard
  `DamerauEquivalenceTests.The_measure_survives_a_table_that_the_pool_handed_out_before` fills a rented table with
  rubbish before the comparison (#30).

- **The pool of the buffers no longer fills up** ([ADR-0031](.agents/adr/0031-the-pool-displaces-instead-of-discarding.md), #30).
  The length of the buffers follows the budget of the candidates, and the pool had twelve places for one budget: after
  the budget had changed, every query allocated the big buffers anew — 294.9 KB at a budget of 1,000, against 83.4 KB
  of the base that has no pool at all. Now the smallest buffer makes room for the new one. The same query allocates
  59.7 KB; the guard `AllocationPoolTests` measures it after the budget has changed (39.8 KB, limit 64 KB).

- `Nordmann.LUI.Hosting` pins `Nordmann.LUI` exactly (`[x.y.z]`), not as a lower bound: a newer core referenced beside
  an older hosting package is named at restore (`NU1608`, an error with `TreatWarningsAsErrors`) instead of surfacing at
  run time (#24, ADR-0018).

- A host disposed without a stop no longer waits for the warm-up of the typo dictionaries: the hosted services are
  disposed before the catalog and cancel it; a round of `ApplyPending` that meets a disposed catalog ends quietly
  instead of logging an error (#26). The hosted services are gone with #56 — the application warms the dictionaries
  itself now.
- `Truncated` is no longer set for a linked index when the only row after the budget is an outdated one (#26).

### Changed

- **The budget table of the README is the promise of §16, and a test holds it up**
  ([ADR-0040](.agents/adr/0040-the-budget-table-of-the-entrance-is-a-promise.md), #38). The limits of the table were
  copied by hand, and they had drifted: the allocation still promised the old target of ≤ 250 / ≤ 350 KB, while the
  specification and the budget test have held ≤ 60 / ≤ 100 KB since ADR-0032.
  - A new architecture test (`Readme_Budgets`) reads the target table of §16 out of the specification and compares it
    with the budget table of the README, with the target table of the record and with its status table — in order, so
    a swapped limit is caught as well as a changed one.
  - The status table of the record stood apart from the contract twice: it promised the old limits, and it listed the
    allocation before the correction. Its rows follow the order of the contract now.
  - The allocation row of both tables carries the limits of §16 and the measured values of the run of 2026-11-06
    (46 / 51 KB — bytes, so the machine does not matter).
  - The mutation check has two mutations for it: a limit of the README and a limit of the record edited by hand turn
    the build red.
  - **No change to the public API.**

- **The entrance shows the measurement, the bulk path, the kinds and the sources**
  ([ADR-0039](.agents/adr/0039-the-entrance-shows-the-measurement.md),
  [ADR-0038](.agents/adr/0038-the-public-api-has-a-page-of-its-own.md),
  [ADR-0037](.agents/adr/0037-the-papers-of-the-work-live-under-agents.md), #61). The review of the README of the release:
  - **The measurement is in the entrance.** The tables of a **fresh** BenchmarkDotNet run are rendered into the README
    between two markers, and the guard follows them: `Readme_Benchmarks` fails when the file and the committed reports
    differ ([ADR-0022](.agents/adr/0022-the-benchmark-results-in-the-readme.md)). The record,
    [`docs/benchmarks.md`](docs/benchmarks.md), keeps the method, the corpus and the budgets.
  - **The bulk path has an example.** `Sync` with a `BatchSize`, the report it returns and the difference to
    `UpsertMany` — a C# block that the package smoke test executes, with the output it prints.
  - **The kinds are a matrix and the sources are a decision.** `SingleWord` and `MultiWord` with the typo tolerance on
    and off, so that the four combinations can be read off directly; and "embedded or linked" as three questions with an
    answer, before the table that compares them. Every example of the matrix was run against the library before it was
    written down.
  - **The sample moves.** A GIF of the page: the autocompletes, the correction of a typo and the fill of a bulk index on
    demand.
  - **The public API has a page of its own.** [`docs/api.md`](docs/api.md) describes every public type of the two
    packages — thirty-eight in the core, one in the hosting — with a sentence for each. A new test
    (`Public_API_Documentation`) fails when a public type is missing on the page, or when the page or the README names a
    type the code does not have.
  - **No change to the public API.**

- **The papers of the work live under `.agents/`**
  ([ADR-0037](.agents/adr/0037-the-papers-of-the-work-live-under-agents.md)). The ADRs, the delivery plans and the
  specification are not the documentation of the package: they move to [`.agents/`](.agents/README.md), with an index of
  their own, and neither the README nor the documentation under [`docs/`](docs/README.md) links them any more. The
  documentation of the library is the map, the tutorial, the API, the benchmarks, the normalisation, the test map, the
  alignment and the domain. The paths of every link in the repository follow.

- **The documentation has an entrance, a contract and a way** ([ADR-0036](.agents/adr/0036-the-readme-the-specification-and-the-tutorial.md),
  [ADR-0022](.agents/adr/0022-the-benchmark-results-in-the-readme.md), #61). The README is rewritten: 571 lines with two
  truths for one rule become 241 lines — what LUI is, what it is **not**, the problem, the highlights, a linked table
  of contents, the install, one quick start with its output, the budgets of §16, one table for the two index kinds and
  the two content sources, the payload, `AddLui` in a host, reading only, the most important types, the sample and the
  map of the documentation.
  - The five concept sections (the kinds of index, the sources of the content, the query, the ranking and the
    priority, the typo tolerance) leave the README: the [specification](.agents/spec/LUI-Spezifikation.md) has them, in
    full, and it is the contract.
  - The raw tables of a BenchmarkDotNet run move to [`docs/benchmarks.md`](docs/benchmarks.md) — with the machine, the
    corpus and the method. The guard follows them: `Readme_Benchmarks` renders the section there and fails when the
    file and the committed reports differ; the README keeps the budgets (target, measured value, limit) and links to
    the record. *(They moved back into the README with ADR-0039, above.)*
  - A new [tutorial](docs/tutorial/README.md) takes the reader from an empty database to a working autocomplete, in
    five steps, with code that compiles.
  - Every C# block of the README is still executed by the package smoke test, against the packed `.nupkg`.
  - **No change to the public API.**

- **The index knows no types** ([ADR-0035](.agents/adr/0035-the-index-knows-no-types.md), #58). The file stores a key as
  text and a payload as bytes, so the index has no type parameters any more: `ILuiIndex`, `ILuiEmbeddedIndex`,
  `ILuiLinkedIndex`, `LuiEntry`, `LuiHit`, `LuiResult`, `LuiQuery` and the builders. The serialiser of the payloads
  belongs to the catalog (`LuiCatalogOptions.PayloadSerializer`), and the type of a payload stands where it is used —
  `hit.GetPayload<T>()`. Nothing is deserialised during a query: a query whose payloads are not read allocates 7.6 %
  less (35.5 KB instead of 38.4 KB, 200,000 entries, 20 hits); one without a payload 1.8 % more (34.3 KB instead of
  33.7 KB, the larger hit).
  - **New**: `LuiKey` (a number or a text, made from `long` and `string` without a cast, read back with one; `1` is not
    `"1"`), `LuiKeyKind`, `ILuiIndex.KeyKind`, `ILuiPayloadSerializer` (one per catalog, no type parameter),
    `LuiPayloadSerializer.Json(context)`, `LuiCatalogOptions.PayloadSerializer`, `LuiHit.GetPayload<T>()`,
    `LuiHit.TryGetPayload<T>(out T)`, `LuiHit.HasPayload`, `LuiCatalog.LinkedIndex(name)`,
    `LuiCatalog.LinkedIndex(name, map)`, `LuiPayloadException` (the index, the key and the type that cannot be read).
  - **Gone**: `LuiNoPayload`, `ILuiPayloadSerializer<T>`, `LuiJsonPayloadSerializer<T>`, `LuiEmbeddedIndexBuilderExtensions`,
    the payload type in the definition (a definition that still has it is read, and it is ignored).
  - **Migration** (from `main` before #58):
    - `LuiCatalog.Open(cs)` with a payload — now: `LuiCatalog.Open(cs, new LuiCatalogOptions { PayloadSerializer =
      LuiPayloadSerializer.Json(AppJsonContext.Default) })`; the context lists every payload type
      (`[JsonSerializable(typeof(TagChip))]`).
    - `index => index.SingleWord().WithPayload(ctx.TagChip)` — now: `index => index.SingleWord()`; every entry of an
      embedded index may carry a payload.
    - `index => index.SingleWord().WithKey<string>()` — now: `index => index.SingleWord().WithKey(LuiKeyKind.Text)`.
    - `catalog.Index<long, TagChip>(name, ctx.TagChip)`, `catalog.Index<long, LuiNoPayload>(name)` — now:
      `catalog.Index(name)`.
    - `catalog.Index<TagChip>(name, map)`, `catalog.Index(name)` of a linked index — now:
      `catalog.LinkedIndex(name, map)`, `catalog.LinkedIndex(name)`.
    - `ILuiEmbeddedIndex<long, TagChip>`, `LuiEntry<long, TagChip>`, `LuiHit<long, TagChip>`, `LuiResult<…>` — now:
      `ILuiEmbeddedIndex`, `LuiEntry`, `LuiHit`, `LuiResult`.
    - `hit.Payload` — now: `hit.GetPayload<TagChip>()` (or `TryGetPayload`); `hit.Payload is null` — now:
      `!hit.HasPayload`.
    - `hit.Key` as a `long` — now: `(long)hit.Key` or `hit.Key.ToInt64()`; `index.Remove(42)` stays as it is.
    - A serialiser of the application: `ILuiPayloadSerializer<T>` with `Serialize(T)` / `T Deserialize(bytes)` — now:
      `ILuiPayloadSerializer` with `Serialize(object)` / `T Deserialize<T>(bytes)`, given to the catalog.
    - A value-type payload that is its default (`0`) was not stored; it is now — only `null` means "no payload".
    - `LuiPayloadSerializer.Json` reads **strictly**: a payload read as a type of another shape (a member in the bytes
      the type does not have, a constructor parameter the bytes do not have) is a `LuiPayloadException`, not an object
      of defaults. A member removed from a payload type, or a constructor parameter added without a default, makes the
      payloads written before unreadable: write them again (`Sync`).
    - Two hits are equal when their values are, the payload compared by its content (as before, when the hit held the
      deserialised payload).
    - A definition with a key type this version does not know is a `LuiDefinitionMismatchException` when it is opened.
  - **`Linked` is documented precisely** (spec §7.3): LUI keeps no copy of the row — the normalised text in the search
    index, the weight and the priority in the order of the candidates; the text and the payload of a hit are read from
    the table when the hit is read.

- **The catalog creates and opens an index** ([ADR-0033](.agents/adr/0033-the-read-path-opens-an-index-without-writing.md), #56).
  The catalog is the entry point: it creates an index once — in the migration of the application — and opens it in the
  runtime path. The builder is no longer reachable from the catalog, so the runtime path cannot create an index. The
  write path of an index creates it — or opens it when it is there with the same definition. The name said "ensure",
  which is a promise the method cannot keep: it does not create a definition that is missing, it compares it.
  - **Migration** (the forms *before* this change are the ones named "before"):
    - before: `lui.Index(name).SingleWord().Ensure()` — now: `lui.Create(name, index => index.SingleWord())`. The old
      name is gone, not deprecated.
    - before: `lui.OpenIndex<TKey, TPayload>(name, payload)` — now: `lui.Index(name)` (and `lui.LinkedIndex(…)` for a
      linked index, ADR-0035). The name is `Index`: `LuiCatalog.Open(connectionString, options)` opens the *catalog* and
      stays as it is.
    - before: `lui.OpenIndex(name)` with the untyped facade `ILuiDocumentIndex` — the key as the text it is stored as,
      the payload as the raw JSON, a hit a `LuiDocumentHit` — now: nothing. The facade is gone; there is one way into
      an index.
    - before: `services.AddIndex(…)`, `services.AddLinkedIndex(…)` and `services.AddOpenIndex(…)` — now:
      `services.AddLui(…)`. The container holds the catalog and nothing else; the registrations and the hosted services
      are gone.
    - before: `services.AddIndex(…).EnsureOnStart()` and `WarmUpTypoDictionariesOnStart()` — now: the application
      calls `catalog.Create(…)` in its migration and `WarmUpTypoDictionary()` itself, when it wants to.


- **A query allocates far less** ([ADR-0032](.agents/adr/0032-the-allocation-of-a-query.md), #30). A single-word query with the
  budget of 500 candidates allocates **26.6 KB instead of 153.3 KB** (−83 %), a multi-word query **105.7 KB instead of
  185.2 KB** (−43 %); the cost per candidate falls from 302 B to 40 B. Three changes, all inside the library:
  - **LUI's own Jaro-Winkler** (`src/Nordmann.LUI/Similarity/JaroWinklerMeasure.cs`), bit-exact to
    `F23.StringSimilarity.JaroWinkler` 7.0.1 — including its quirks (single precision, a common prefix that is not capped
    at four characters, a weight that shrinks for long texts) — but without its four arrays per comparison. The default
    measure of the library allocated 55.9 KB per query; now it allocates 0.9 KB.
  - **The similarity is measured on spans.** The ranking writes the search form of a candidate into a buffer and measures
    it there; neither the form nor the part the match begins at becomes a string any more (−44.1 KB per query). A measure
    of an application that only speaks strings is measured as before — the difference is 40 KB per query.
  - **The buffers of the candidates are reused** per thread: the arrays of the candidates, of the groups and of the hits
    are kept instead of being thrown away after every query (−40 KB per query).
  The public API is unchanged — `ISpanSimilarity` is internal, and the exported types against the base of the branch are
  byte-identical. §16 carries the new target (p50 ≤ 60 KB, p95 ≤ 100 KB; measured p50 42 KB, p95 60 KB).

- **The four similarity measures are LUI's own** ([ADR-0030](.agents/adr/0030-the-similarity-measures-are-luis-own.md), #30). With
  the Jaro-Winkler of the entry above, the **Damerau-Levenshtein** and the **Levenshtein** move into the library too, and
  the runtime dependency on `F23.StringSimilarity` ends: the core ships **two** runtime dependencies now
  (`Microsoft.Data.Sqlite`, `symspell`), and the library becomes a **test** dependency — the parity tests compare
  against it.
  - The numbers do not change: both measures are **bit-exact** to `F23.StringSimilarity` **7.0.1** — the packed version,
    not the branch `main` (the two differ) — including the quirks that make up the definition: the Damerau-Levenshtein
    is the **unrestricted** distance (a substring may be edited more than once), and the division by the longer text
    and the subtraction from one keep the library's order, because the order decides where the rounding happens.
  - The proof is a test **without a tolerance**: known pairs, 10,000 random pairs, every length against every length
    up to 12, a long text — and **zero allocation** per comparison (the library allocated 5 KB for the
    Damerau-Levenshtein and 350 B for the Levenshtein, per comparison, and a query compares up to 500 candidates).
  - No change to the public API: the same four measures, with the same names and the same semantics.

- **The public API before the freeze** ([ADR-0024](.agents/adr/0024-the-api-polish-before-the-freeze.md), #42, from the
  architecture review of 2026-11-04). Renamed and tidied before the first release, so nobody has to migrate:
  - The registrations of the indexes — an embedded one and a linked one — are gone with #56
    ([ADR-0033](.agents/adr/0033-the-read-path-opens-an-index-without-writing.md)): the container holds the catalog and
    nothing else.
  - `LuiPriorityRanking` → `LuiPriorityMode`; `LuiQuery.Priorities(…)` and `LuiQueryOptions.Priorities` → `PriorityMode`.
  - `LuiTimings.Candidates` → `CandidateRead`: a duration, not the budget (`LuiQueryOptions.Candidates`) and not the count
    (`LuiResult.CandidatesConsidered`).
  - `Sync(source, …)` and `Check(source)` of `ILuiEmbeddedIndex` take `entries`.
  - `LuiRow` → `LuiPayloadRow`. A row that is kept and read after the mapping of `WithPayload` throws an
    `InvalidOperationException` instead of reading the next hit.
  - `LuiSyncResult` → `LuiLinkedSyncReport`: the linked index's sync is a different operation from the embedded one, with
    other counts, and now says so in its name.
  - New: `LuiException`, abstract and still an `InvalidOperationException`, is the base of `LuiEnvironmentException`,
    `LuiDefinitionMismatchException` and `LuiLinkedTableException`.
  - `ILuiIndex` only reads. `Check()`, `WarmUpTypoDictionary()` and `RebuildTypoDictionary()` moved to `ILuiEmbeddedIndex`
    and `ILuiLinkedIndex`; `TypoTolerance` and `TypoDictionaryState` stay.
  - `ILuiSimilarity.Name` is gone: nothing read it, and a measure of your own no longer has to invent one.

- **`Owned` is `Embedded`** ([ADR-0023](.agents/adr/0023-owned-becomes-embedded.md), #41). The content source in LUI's own
  table, `lui_<index>`, is named after where its rows live, and pairs with `Linked`. In .NET "owned" is the word of
  lifetime (Autofac's `Owned<T>`, an owned `IDisposable`), and the index is a singleton that is never disposed. Renamed
  before the first release, so nobody has to migrate:
  - `ILuiOwnedIndex<TKey, TPayload>` → `ILuiEmbeddedIndex<TKey, TPayload>`;
  - `LuiIndexBuilder<TKey, TPayload>` → `LuiEmbeddedIndexBuilder<TKey, TPayload>`, the builder of the embedded route as
    `LuiLinkedIndexBuilder` is that of the linked one; `LuiIndexBuilderExtensions` →
    `LuiEmbeddedIndexBuilderExtensions` (`LinkTo` stays an extension of the embedded builder);
  - the stored content source, `lui_meta.source`: `Owned` → `Embedded`. Nothing was released, so there is nothing to migrate.

- **The test tree** ([ADR-0019](.agents/adr/0019-the-test-tree-three-layers-one-map.md)). The one test project
  becomes three — `Nordmann.LUI.Unit.Tests` (the rules, without a database), `Nordmann.LUI.Integration.Tests`
  (the library against a real SQLite file) and `Nordmann.LUI.Architecture.Tests` — plus
  `Nordmann.LUI.Testing`, a library that holds the shared setup: the database, the corpora, the fixtures. The
  infrastructure that was copied, pasted and `<Compile Include>`-linked is in one place now. The unit lane takes
  about one second; it used to be part of a process of thirty-six seconds.
- **The map of the tests** is [`docs/testing.md`](docs/testing.md): which test holds which rule of the
  specification up. It is a guard, not a document — `Test_Landscape` fails when the map and the tree disagree.
- The pin of `Alba` is gone: no project referenced it.
- Two letters of the German transliteration had no test — the lower-case "ä" and the upper-case "Ö". The
  mutation check found them: it changed the "ä" to "a" instead of "ae" and the suite stayed green.
- A candidate of a linked index whose current text no longer matches does not count against the candidate budget any
  more: the read goes on past it, up to another budget of such rows (review of PR #16, F1).
- `Highlight` is not part of the library (owner's decision; struck, ADR-0028).
- Every index has `Check()` (on `ILuiEmbeddedIndex` and `ILuiLinkedIndex`). Inside, the catalog is taken apart (review DevEx D4, D6): no change to
  the public API besides.
- **The builder: an embedded index is the default** (ADR-0015). The builder starts with the kind: `SingleWord()`/`MultiWord()`
  return a `LuiEmbeddedIndexBuilder<long, LuiNoPayload>` — an embedded index, or with
  `LinkTo(…)` a linked one (review of PR #16, N1); the builder types and the `…<TKey>()` steps of earlier pre-release
  builds are gone, `WithKey<string>()` chooses a text key. The builder is the **parameter** of `Create` now
  ([ADR-0033](.agents/adr/0033-the-read-path-opens-an-index-without-writing.md)): `lui.Create("tags", index => index.SingleWord())`
  is a whole index.
- `DropIndex` decides by the stored source and refuses a source it does not know.

- **The API shape before the first release** ([ADR-0013](.agents/adr/0013-the-api-shape-before-the-first-release.md)) —
  a break on purpose, while it costs nothing:
  - A staged builder: `lui.Create(name, index => index.SingleWord().WithPayload(…).TypoTolerance())`. An index
    without a kind does not compile; the key type is checked at `WithKey<TKey>()`; a payload is optional
    (`LuiNoPayload` until `WithPayload`). `TypoTolerance(int maxEdits)` is `TypoTolerance(bool enabled = true)`.
    `Drop()` is `LuiCatalog.DropIndex(name)`.
  - Reading and writing are two interfaces: `ILuiIndex<TKey, TPayload>` (`Query`, `Search`, `Count`, the typo
    dictionary) and `ILuiEmbeddedIndex<TKey, TPayload>` (the writes). `Query(text, options)` is
    `Search(text, options = null)`.
  - `LuiEntry<TKey, TPayload>(Key, Text) { Weight, Priority, Payload }` is what an upsert stores; the short form
    `Upsert(key, text, weight, priority, transaction)` carries no payload.
  - `LuiHit`, `LuiResult` and `LuiTimings` have named `init` properties instead of positions; `LuiHit.Highlight`
    and `Highlight` are gone (struck, ADR-0028).
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

## [0.1.0] — not published

The first release. It ships the packages `Nordmann.LUI` and `Nordmann.LUI.Hosting` with the API of the
specification ([`.agents/spec/LUI-Spezifikation.md`](./.agents/spec/LUI-Spezifikation.md)):

- `SingleWord` and `MultiWord` indices, `Embedded` and `Linked` content sources
- FTS5 trigram search with priority-first candidates, word-start ranking and
  similarity scoring
- typo tolerance over a prefix dictionary
- `Sync`, `Check`, `Rebuild`, `Optimize`
- dependency injection (`AddLui`, the catalog in the container) in the second package `Nordmann.LUI.Hosting`,
  released with the same version
- a sample application with a search field in the browser

[Unreleased]: https://github.com/nordmanndev/LUI/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/nordmanndev/LUI/releases/tag/v0.1.0
