# 0013 — The API shape before the first release: steps, one namespace, entries, named results

- **Status:** Accepted
- **Date:** 2026-11-03
- **Supersedes:** the namespace layout of ADR-0001 (the package decision stands)
- **Amended by:** [0015](./0015-the-linked-index-is-loosely-coupled.md) — `Owned<long>` is the default; the source step is gone, `WithKey<string>()` and `LinkTo(…)` take its place
- **Affects:** every public type in `src/Nordmann.LUI/`, `src/Nordmann.LUI.csproj`,
  `docs/spec/LUI-Spezifikation.md` §8.3, §11, §12, §17, `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs`,
  `tools/package-smoke-test.sh`, `README.md`

## Context

A DevEx and architecture review of `main` after PR #8 (issue #11) asked one question: how fast does an application
developer understand, use and extend LUI without making a mistake? Its answer for the public API was "it works, but it
asks for too much prior knowledge":

- a payload type is required even for an index without one (`Index<long, Chip>` 150 times in the tests, `.Payload(…)`
  13 times);
- a missing kind or content source shows up as an exception in `Ensure()`, not at compile time — and `Linked`
  (PR-006) would add source-specific options that combine wrongly;
- `ILuiIndex` mixes reading and writing, although a `Linked` index has no write methods (§12.4): it would have to throw
  `NotSupportedException`;
- `Upsert` takes six positional parameters, and that it replaces the *whole* entry is not visible in its signature;
  there is no batch;
- `LuiHit`, `LuiResult` and `LuiTimings` are positional records — PR #10 inserted `Correction` into `LuiTimings`, and
  five `TimeSpan`s swap places without a compiler error; `LuiHit.Highlight` is a field that is never filled;
- `Priority`, `IndexKind`, `RankingOrder` have no prefix (`Priority` collides in almost every application);
  `Query(text)` is a builder and `Query(text, options)` a result;
- a consumer with a similarity and a serialiser of their own needs three `using`s; `Similarity` in
  `Nordmann.LUI.Similarity` forced an alias and a suppressed analyzer rule;
- `LuiCatalogOptions` cannot be bound from configuration; a wrong value in a query option surfaces in `Execute()`;
- the package references three `Microsoft.Extensions.*` abstractions that no code uses.

Before the first release a break costs nothing; after it, each of these is a major version. And `Linked` should be
built on the shape it keeps, not on the one it would have to break.

## Decision

**A staged builder.** Each step is a type that offers only what may follow it:

```csharp
ILuiOwnedIndex<long, RecipeChip> recipes = lui
    .Index("recipes")                              // LuiIndexBuilder:        the kind
    .MultiWord()                                   // LuiIndexSourceBuilder:  the content source
    .Owned<long>()                                 // LuiOwnedIndexBuilder<long, LuiNoPayload>
    .WithPayload(RecipeJson.Default.RecipeChip)    // LuiOwnedIndexBuilder<long, RecipeChip>
    .TypoTolerance()
    .Ensure();
```

An index without a kind or a source does not compile. The key type is chosen at the source and checked there
(`Owned<Guid>()` throws at once). A payload is optional: an index starts with `LuiNoPayload`, and `WithPayload`
changes the type and keeps the settings made before it — so "a payload without a serialiser" can no longer be written.
`TypoTolerance(bool enabled = true)` replaces `TypoTolerance(int maxEdits)`: more than one edit is not supported, so
the number only invited a runtime error. `Drop()` moves to the catalog as `DropIndex(name)`: removing an index needs
neither its kind nor its source. `Linked` (PR-006) becomes a second method on `LuiIndexSourceBuilder` with a builder of
its own.

**Reading and writing are two interfaces.** `ILuiIndex<TKey, TPayload>` is what every index has: `Name`, `Kind`,
`Count()`, `Query(text)`, `Search(text, options?)`, `WarmUpTypoDictionary()`, `RebuildTypoDictionary()`. The typo
dictionary stays here: a `Linked` index has one too. `ILuiOwnedIndex<TKey, TPayload> : ILuiIndex<…>` adds the writes.
A suggest endpoint depends on the first, an import job on the second.

**An entry, and a batch.** `LuiEntry<TKey, TPayload>(Key, Text) { Weight, Priority, Payload }` is what an upsert
stores — all of it. It is a record class, not a struct: a `default` struct would carry the priority `System` (0) and a
null text. Its values are checked in their `init` accessors. The short form `Upsert(key, text, weight = 0,
priority = Normal, transaction = null)` stays for the entry without a payload. `UpsertMany(entries, transaction?,
cancellationToken)` writes in **one** transaction: LUI's own on the catalog's writer (all or nothing; the writer's lock
is held for the batch) or the application's. The typo dictionary takes its write lock once for the batch.

**Named results.** `LuiHit` (still a `readonly record struct`, for the allocation), `LuiResult` and `LuiTimings` have
`init` properties and no positional constructor; `Key`, `Text`, `Query` and `Hits` are `required`. A new field is an
addition, not a break, and a test of the application can build a result. `Highlight` is gone until the highlighting is
built; it comes back as an addition — no longer with PR-008 but a later version (owner's decision, ADR-0017, #20).

**One rule for names, one namespace.** Every public type lives in `Nordmann.LUI` and starts with `Lui` (`ILui` for an
interface): `LuiPriority`, `LuiIndexKind`, `LuiRankingOrder`, `LuiPriorityRanking`, `ILuiSimilarity` with
`LuiSimilarity.JaroWinkler` … , `ILuiPayloadSerializer<T>`, `LuiJsonPayloadSerializer<T>`. The folders `Similarity/` and
`Payloads/` are gone. `Normalizer`, `NormalizedText` and `NormalizationOptions` are **internal**: no use case needs them
outside, and a public normaliser would make every change of the normalisation rule a break. A query with options is
`Search(text, options)`; without options, `Search(text)` takes the defaults.

**Options that check themselves.** `LuiCatalogOptions` is a record with `init` properties (`CacheSizeMegabytes`,
`MemoryMapMegabytes`, `BusyTimeout`, `WriteAheadLog`, `TimeProvider`) — bindable from configuration — and `Open` takes
it as a value: `LuiCatalog.Open(cs, new LuiCatalogOptions { … })`. `LuiQueryOptions` checks `MaxResults`, `Candidates`,
`Rank` and `Priorities` in their `init` accessors, so `Take(0)` throws at the call, not in `Execute()`.

**The dependencies of the package are the ones it uses**: `Microsoft.Data.Sqlite`, `symspell`, `F23.StringSimilarity`.
The `Microsoft.Extensions.*` abstractions come back with the dependency injection (PR-008), and that PR decides which
it needs. `Directory.Packages.props` keeps their versions; nothing else changes there.

## What was measured

The behaviour of the queries does not change; the golden, query-plan and quality tests pass unchanged in substance.

| Measurement (same machine, same session) | Result |
|---|---:|
| query, 200,000 entries, SingleWord (gated `QueryPerformance`) | p50 0.35–0.69 ms, p95 0.78–1.22 ms per input length 1–8; alloc p95 244 KB — no regression against `docs/benchmarks.md` (p95 1.8 ms) |
| 100,000 upserts, each in its own transaction | 16.9 s (5,913/s) |
| the same 100,000 in one `UpsertMany` (gated `BatchPerformance`) | **3.8 s (26,052/s), 4.4× faster** |

The batch is the short-term half of the bulk path; the long-term half — filling the content table without the
triggers and building the search index sorted — stays in PR-007 (ADR-0010).

## Consequences

- **The typo dictionary is not part of a transaction of the application** (review of pull request 12). It learns
  the words of a write when they are written — by `Upsert` and `UpsertMany` alike — because LUI does not own the
  transaction and does not learn of its end. A rollback leaves the beginnings of its texts behind; an input that
  is exactly such a beginning then counts as a word of the index and is not corrected, and the answer can be
  empty where the committed state would correct it. `RebuildTypoDictionary()` brings the dictionary back in step.
  This is older than this ADR (the single `Upsert` behaves the same since ADR-0012); it is written down here,
  where the transaction semantics of the batch are, and pinned by a test. Not learning inside a transaction of the
  application would trade this rare case for a common one — every committed transaction would stay unknown to
  the dictionary until a rebuild.
- Every caller of the old API breaks once; the package has not been published, so the callers are this repository's
  own tests, the smoke test and the specification.
- The review's internal seams — the kind as a strategy, the content source as a strategy, the direction of the
  namespaces, a smaller `QueryRunner` — are **not** part of this decision: they come in a refactoring pull request
  before PR-006, with the requirements of `Linked` at hand.
- Not done here, by decision: caching the index instances in the catalog (review A7; the typo dictionary is already
  one per index) and an `IHostedService` that warms the dictionary — both with the dependency injection (PR-008);
  `Suggest(text, take)` as a shortcut — `Search(text)` covers the default case.

**Verified by:** `tests/Nordmann.LUI.Integration.Tests/ApiShapeTests.cs` (the entry, the batch: all or nothing, cancellation, the
application's transaction, the typo dictionary — and what a rollback leaves in it —, beside other writers; the options and the parameter names of their errors; the key type; the read interface;
`DropIndex`), `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs`
(`A_consumer_needs_one_using_and_every_public_type_says_it_is_lui`,
`The_results_and_options_have_named_properties_not_positions`), `Repository_Layout.cs`
(`The_library_ships_only_the_documented_runtime_dependencies`), and the package smoke test, which runs the quick start
of the README.
