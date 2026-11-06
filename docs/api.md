# The public API of LUI

> Everything a consumer of the package can call — every public type of `Nordmann.LUI`, every public type of
> `Nordmann.LUI.Hosting`, and nothing else. The [tutorial](./tutorial/README.md) shows how to use it; this page says
> what there is.
>
> **One namespace, one prefix, one way in.** Everything lives in `Nordmann.LUI`; every type begins with `Lui`. The
> catalog is the entry point, the index is what it hands out, and the rest stays on the index.

The table at the end is the whole surface. The sections before it explain what the parts are for.

---

## The catalog — the entry point

The catalog is the one thing an application opens. It holds the connection, the settings and the indexes of one
database file.

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.Open("Data Source=app.db");
```

### `LuiCatalog`

| Member | What it does |
|---|---|
| `static LuiCatalog Open(string connectionString, LuiCatalogOptions? options = null)` | opens the catalog. It probes the SQLite it found (version, folding) and refuses a database it cannot search correctly. |
| `ILuiEmbeddedIndex Create(string name, Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder> index)` | creates an **embedded** index — the content lives in the library's own table. It is the write path and belongs in the migration of the application. |
| `ILuiLinkedIndex Create(string name, Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder> index)` | creates a **linked** index — the content stays in the table of the application. |
| `ILuiEmbeddedIndex Index(string name)` | opens an embedded index that exists, and **writes nothing**: the definition is read out of the file, no table, no write lock. |
| `ILuiLinkedIndex LinkedIndex(string name)` | opens a linked index without payload columns. |
| `ILuiLinkedIndex LinkedIndex<TPayload>(string name, Func<LuiPayloadRow, TPayload> map)` | opens a linked index with payload columns; the map builds the payload from the row of the table. |
| `bool DropIndex(string name, bool dropTriggers = false)` | drops the index. The triggers of a linked index are dropped with it only if you say so. |
| `string ConnectionString { get; }` | the connection string the catalog was opened with. |
| `void Dispose()` | closes the catalog. It holds the connection pool; the indexes hold no resource of their own. |

`Create` is **idempotent**: it creates the index if it is missing, and compares the stored definition with the one that
was described if it is there. A difference is a `LuiDefinitionMismatchException` — never a silent change.

`Index` and `LinkedIndex` are the read path. They never write, whatever the file allows afterwards: the file decides
what may happen.

### `LuiCatalogOptions`

The settings of the catalog. Every one of them has a default; none of them has to be set.

| Member | Default | What it is for |
|---|---|---|
| `int? CacheSizeMegabytes` | the SQLite default | the page cache of the file |
| `int? MemoryMapMegabytes` | the SQLite default | the memory map of the file |
| `TimeSpan BusyTimeout` | 5 s | how long a writer waits for the lock of the file |
| `bool WriteAheadLog` | `true` | the WAL — readers do not block the writer |
| `ILuiPayloadSerializer? PayloadSerializer` | none | the serialiser of the payloads of this catalog — one for every index of it |
| `TimeProvider TimeProvider` | `TimeProvider.System` | the clock of the catalog, for the timings and for tests |

The file decides whether writing is allowed at all: a catalog opened with `Mode=ReadOnly` writes nothing, and every
write throws a `LuiReadOnlyException`.

---

## Creating an index — the description

The builder chain is a **parameter** of `Create`, not the way in: the catalog takes a description and does the creating
itself. Every builder method returns the builder, so the chain reads as one sentence.

### `LuiIndexKindBuilder` — the kind of the index

| Member | What it does |
|---|---|
| `LuiEmbeddedIndexBuilder SingleWord()` | the text is one unit: a tag, a word, a compound, a hashtag |
| `LuiEmbeddedIndexBuilder MultiWord()` | the text is many words: a name, a title. The words are ANDed — the order and the spaces do not matter. |

### `LuiEmbeddedIndexBuilder`

| Member | What it does |
|---|---|
| `LuiEmbeddedIndexBuilder WithKey(LuiKeyKind kind)` | the kind of the key: `Number` (the default) or `Text` for a ULID or any other text key |
| `LuiLinkedIndexBuilder LinkTo(string table, string key, string text, string? weight = null, string? priority = null)` | turns the description into a linked one: the table of the application, its `INTEGER PRIMARY KEY`, the column with the text, and — optional — the columns with the weight and the priority |
| `LuiEmbeddedIndexBuilder Similarity(ILuiSimilarity similarity)` | the measure of the ranking. The default of the kind is used when it is left out. |
| `LuiEmbeddedIndexBuilder GermanTransliteration(bool enabled)` | `ä` → `ae`, `ü` → `ue`, `ß` → `ss`. On by default. |
| `LuiEmbeddedIndexBuilder TypoTolerance(bool enabled = true)` | one typo forgiven. Off by default — the dictionary costs 138 MB for 685,829 words. |
| `ILuiEmbeddedIndex Create()` | creates the index. The catalog calls this; an application does not. |

### `LuiLinkedIndexBuilder`

Everything of the embedded builder except `WithKey` and `LinkTo` — a linked index takes the keys of its table — plus:

| Member | What it does |
|---|---|
| `LuiLinkedIndexBuilder WithPayload<TPayload>(IReadOnlyList<string> columns, Func<LuiPayloadRow, TPayload> map)` | the columns of the payload and the map that builds it from the row. The columns are read live; the map is code of the application. |

---

## The index — what the catalog hands out

An index is a **view**, not a resource: it holds no connection and no native handle, and it is not disposable. Every
operation borrows a connection from the pool of the catalog for the time it takes.

### `ILuiIndex` — what both kinds have

| Member | What it is |
|---|---|
| `string Name { get; }` | the name of the index |
| `LuiIndexKind Kind { get; }` | `SingleWord` or `MultiWord` |
| `LuiKeyKind KeyKind { get; }` | `Number` or `Text` |
| `bool TypoTolerance { get; }` | whether the index forgives a typo |
| `LuiTypoDictionaryState TypoDictionaryState { get; }` | `NotBuilt`, `Building` or `Current` |
| `long Count()` | the number of the entries |
| `LuiQuery Query(string text)` | a query that is configured step by step and then executed |
| `LuiResult Search(string text, LuiQueryOptions? options = null, CancellationToken cancellationToken = default)` | a query in one call |

### `ILuiEmbeddedIndex` — the write path of an embedded index

| Member | What it does |
|---|---|
| `void Upsert(LuiEntry entry, SqliteTransaction? transaction = null)` | writes one entry |
| `void Upsert(LuiKey key, string text, int weight = 0, LuiPriority priority = LuiPriority.Normal, SqliteTransaction? transaction = null)` | writes one entry, without an object |
| `int UpsertMany(IEnumerable<LuiEntry> entries, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)` | writes many entries in one transaction; returns how many were written |
| `bool UpdateWeight(LuiKey key, int weight, SqliteTransaction? transaction = null)` | changes the weight of an entry; `false` when there is no such entry |
| `bool UpdatePriority(LuiKey key, LuiPriority priority, SqliteTransaction? transaction = null)` | changes the priority of an entry |
| `bool Remove(LuiKey key, SqliteTransaction? transaction = null)` | removes an entry |
| `LuiSyncReport Sync(IEnumerable<LuiEntry> entries, LuiSyncOptions? options = null, CancellationToken cancellationToken = default)` | makes the index equal to the given entries — **the bulk path** |
| `LuiSyncReport Sync(IAsyncEnumerable<LuiEntry> entries, LuiSyncOptions? options = null, CancellationToken cancellationToken = default)` | the same, from a stream |
| `LuiIntegrityReport Check()` | compares the index with itself: the schema, the counts, the fold table |
| `LuiIntegrityReport Check(IEnumerable<LuiEntry> entries, CancellationToken cancellationToken = default)` | compares the index with a set of entries |
| `void Rebuild()` | rebuilds the index from its content table |
| `void Optimize()` | lets SQLite optimize the file |
| `void WarmUpTypoDictionary(CancellationToken cancellationToken = default)` | builds the dictionary of the typos before a query needs it |
| `void RebuildTypoDictionary(CancellationToken cancellationToken = default)` | builds it again, from scratch |

### `ILuiLinkedIndex` — the write path of a linked index

A linked index keeps **no copy of the row**: the text of a hit is read from the table of the application when the hit is
read. The write path belongs to the application.

| Member | What it does |
|---|---|
| `bool Refresh(long key, SqliteTransaction? transaction = null)` | takes one row of the table into the index |
| `int RefreshMany(IEnumerable<long> keys, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)` | takes many rows into the index |
| `LuiLinkedSyncReport Sync(CancellationToken cancellationToken = default)` | takes the whole table into the index |
| `int ApplyPending(CancellationToken cancellationToken = default)` | applies the changes a trigger collected — the outbox |
| `long Pending()` | how many changes are waiting |
| `string TriggerScript()` | the SQL of the triggers, for the application that wants to install them itself |
| `void CreateTriggers()` | creates the triggers on the table of the application — only when the application asks for it |
| `void DropTriggers()` | drops them again |
| `LuiTriggerState Triggers()` | `None`, `Current` or `Stale` |
| `LuiIntegrityReport Check()` | compares the index with its table |
| `void Optimize()` | lets SQLite optimize the file |
| `void WarmUpTypoDictionary(CancellationToken cancellationToken = default)` | builds the dictionary of the typos before a query needs it |
| `void RebuildTypoDictionary(CancellationToken cancellationToken = default)` | builds it again, from scratch |

---

## Writing — the entry and the key

### `LuiEntry`

What is written: a key, a text, a weight, a priority and — optionally — a payload.

```csharp
new LuiEntry(1, "Gedeckter Apfelkuchen") { Weight = 9000, Priority = LuiPriority.High }
```

| Member | What it is |
|---|---|
| `LuiEntry(LuiKey key, string text)` | the two members that every entry has. The text may not be empty. |
| `LuiKey Key { get; init; }` | the key of the entry |
| `string Text { get; init; }` | the text of the entry — what the search finds |
| `int Weight { get; init; }` | the weight: 0 to 536,870,911. The default is 0. |
| `LuiPriority Priority { get; init; }` | the priority: `System`, `High`, `Elevated`, `Normal`. The default is `Normal`. |
| `object? Payload { get; init; }` | the payload of the application — any type the serialiser of the catalog writes |

### `LuiKey`

The key of the application: a number or a text. `1` and `"1"` are **different** keys.

```csharp
LuiKey key = 42;          // implicit
LuiKey ulid = "01H...";   // implicit
long number = key;        // explicit — throws for a text key
string text = key;        // explicit — throws for a number key
```

| Member | What it is |
|---|---|
| `static LuiKey FromInt64(long number)` / `static LuiKey FromString(string text)` | the same, without the implicit conversion |
| `LuiKeyKind Kind { get; }` | `Number` or `Text` |
| `long ToInt64()` / `string ToText()` | the value back; a conversion into the other kind throws |

---

## Reading — the query, the result, the hit

### `LuiQuery`

The query that is configured step by step. Every method returns the query, and `Execute` runs it.

| Member | What it does |
|---|---|
| `LuiQuery Take(int maxResults)` | how many hits the query returns. 1 to 1000, the default 20. |
| `LuiQuery Candidates(int candidates)` | how many candidates the query looks at. 1 to 100,000, the default 500. |
| `LuiQuery Rank(LuiRankingOrder order)` | the order of the hits |
| `LuiQuery PriorityMode(LuiPriorityMode mode)` | `First` (the default) puts the priority before everything; `CandidatesOnly` uses it while the candidates are collected |
| `LuiQuery DistinctText(bool distinct = true)` | the same text only once in the hits |
| `LuiQuery Similarity(ILuiSimilarity similarity)` | the measure of the ranking, for this query |
| `LuiQuery TypoTolerance(bool enabled = true)` | forgives a typo in this query — over the setting of the index |
| `LuiQuery TrackTimings(bool track = true)` | measures the phases of the query |
| `LuiResult Execute(CancellationToken cancellationToken = default)` | runs the query |

### `LuiQueryOptions`

The same settings as a record — for the configuration of an application, for a settings page or for a call of
`Search`.

| Member | Default |
|---|---|
| `int MaxResults` | 20 (1 to 1000, `LuiQueryOptions.MaxResultsLimit`) |
| `int Candidates` | 500 (1 to 100,000, `LuiQueryOptions.CandidatesLimit`) |
| `LuiRankingOrder Rank` | `WordStartThenSimilarity` |
| `LuiPriorityMode PriorityMode` | `First` |
| `bool DistinctText` | `false` |
| `ILuiSimilarity? Similarity` | the default of the kind of the index |
| `bool? TypoTolerance` | `null` — the setting of the index |
| `bool TrackTimings` | `false` |

Every value checks itself: a `MaxResults` of 0 or a `Rank` that is not defined throws `ArgumentOutOfRangeException`
before the query runs.

### `LuiResult`

| Member | What it is |
|---|---|
| `string Query { get; init; }` | the query that was run |
| `IReadOnlyList<LuiHit> Hits { get; init; }` | the hits, in the order of the ranking |
| `int CandidatesConsidered { get; init; }` | how many candidates the query looked at |
| `bool Truncated { get; init; }` | whether more candidates were found than the query took |
| `string? CorrectedQuery { get; init; }` | the query after the correction of a typo — `null` when there was none |
| `bool CorrectionSkipped { get; init; }` | whether a correction was skipped because the dictionary was not ready |
| `LuiTimings? Timings { get; init; }` | the phases of the query, when `TrackTimings` was on |

### `LuiHit`

One hit. It is a `readonly record struct` — no allocation per hit.

| Member | What it is |
|---|---|
| `LuiKey Key { get; init; }` | the key of the entry |
| `string Text { get; init; }` | the text of the entry |
| `int Weight { get; init; }` | the weight of the entry |
| `LuiPriority Priority { get; init; }` | the priority of the entry |
| `double Similarity { get; init; }` | how near the text was to the query, 0 to 1 |
| `bool AtWordStart { get; init; }` | whether the query matched at the beginning of a word |
| `bool HasPayload { get; }` | whether the hit carries a payload |
| `T? GetPayload<T>()` | the payload, in the type where it is used. `default` when there is none. |
| `bool TryGetPayload<T>(out T payload)` | the same, without the `null` |

**The index knows no types** — there is no type parameter on the index, the hit, the result, the query or the entry.
The type of the payload stands where the payload is used: `hit.GetPayload<TagChip>()`. A payload that cannot be read in
the type asked for is a `LuiPayloadException`.

### `LuiTimings`

The phases of a query: `CandidateRead`, `Correction`, `Ranking`, `Payload` and `Total` — each a `TimeSpan`.

---

## The bulk path — loading many entries

`Sync` is the way to load an index with many entries. It reads the entries in batches, writes them in one transaction
per batch, and reports what it did. The difference to `UpsertMany`: `UpsertMany` writes the entries it is given, `Sync`
makes the index **equal** to them — the entries that are gone are removed.

```csharp
var report = index.Sync(entries, new LuiSyncOptions { BatchSize = 100_000 });
Console.WriteLine($"{report.Added} added, {report.Changed} changed, {report.Removed} removed.");
```

### `LuiSyncOptions`

| Member | Default | What it is |
|---|---|---|
| `int BatchSize` | 10,000 (1 to 1,000,000, `LuiSyncOptions.MaxBatchSize`) | how many entries one transaction holds |
| `IProgress<LuiSyncReport>? Progress` | none | the progress of the load — what the sample shows |

### `LuiSyncReport`

| Member | What it is |
|---|---|
| `long Read` | how many entries were read |
| `long Added` / `long Changed` / `long Unchanged` / `long Removed` | what happened to them |
| `int Batches` | how many transactions it took |
| `bool Merged` | whether the index was merged into the existing one instead of being replaced |

### `LuiLinkedSyncReport`

The report of a `Sync` of a linked index: `Indexed`, `WithoutText` (the rows with no text — they are not indexed) and
`KeyOutOfRange` (the rows whose key is not a valid `INTEGER PRIMARY KEY`).

### `LuiIntegrityReport`

The report of a `Check`: `Problems` (the list of what is wrong), `IsHealthy`, `Missing`, `Extra` and `Different`.

---

## The payload

### `LuiPayloadSerializer`

| Member | What it does |
|---|---|
| `static ILuiPayloadSerializer Json(JsonSerializerContext context)` | the serialiser of a source-generated JSON context — safe for trimming and AOT |

### `ILuiPayloadSerializer`

| Member | What it does |
|---|---|
| `byte[] Serialize(object payload)` | writes a payload |
| `T Deserialize<T>(ReadOnlySpan<byte> bytes)` | reads a payload in the type asked for |

The serialiser belongs to the **catalog**: one for every index of it. A catalog without a serialiser writes entries
without a payload.

### `LuiPayloadRow`

The row of the table of the application, as the map of a linked index sees it. It knows the columns of the definition —
and only those.

| Member | What it does |
|---|---|
| `string GetString(string column)` / `long GetInt64(string column)` / `double GetDouble(string column)` / `T Get<T>(string column)` | reads a column |
| `bool IsNull(string column)` | whether the column is null |

---

## The similarity

### `ILuiSimilarity`

| Member | What it does |
|---|---|
| `double Similarity(string query, string text)` | how near the text is to the query, 0 to 1 |

### `LuiSimilarity`

The four measures of the library — all of them its own, without a dictionary per comparison.

| Member | What it is |
|---|---|
| `ILuiSimilarity JaroWinkler { get; }` | the default of a single-word index |
| `ILuiSimilarity NgramDice { get; }` | the default of a multi-word index |
| `ILuiSimilarity DamerauLevenshtein { get; }` | the distance that counts a transposition as one edit |
| `ILuiSimilarity Levenshtein { get; }` | the distance that counts the edits |

---

## The enumerations

| Type | Values | What they are for |
|---|---|---|
| `LuiIndexKind` | `SingleWord`, `MultiWord` | the kind of an index |
| `LuiKeyKind` | `Number`, `Text` | the kind of a key |
| `LuiPriority` | `System`, `High`, `Elevated`, `Normal` | the priority of an entry |
| `LuiPriorityMode` | `First`, `CandidatesOnly` | how the priority enters the ranking |
| `LuiRankingOrder` | `WordStartThenSimilarity`, `WordStartThenWeight`, `SimilarityOnly`, `WeightOnly` | the order of the hits |
| `LuiTriggerState` | `None`, `Current`, `Stale` | the state of the triggers of a linked index |
| `LuiTypoDictionaryState` | `NotBuilt`, `Building`, `Current` | the state of the dictionary of the typos |

---

## The exceptions

All of them inherit from `LuiException`, which inherits from `InvalidOperationException` — one `catch` for the whole
library.

| Type | When it is thrown |
|---|---|
| `LuiException` | the base of them all |
| `LuiEnvironmentException` | the environment is not what the library needs — the SQLite is too old, or it folds differently than the one the library was measured with |
| `LuiDefinitionMismatchException` | the index in the file was written with another definition, another version of the schema or another folding. It carries `IndexName`, `Stored` and `Requested`. |
| `LuiIndexNotFoundException` | there is no index of that name in the database. It carries `IndexName`. |
| `LuiLinkedTableException` | the table of the application is missing, or a column of it, or its key is not the `INTEGER PRIMARY KEY` |
| `LuiPayloadException` | a payload cannot be read in the type asked for. It carries `IndexName`, `Key` and `RequestedType`. |
| `LuiReadOnlyException` | the file may not be written. It carries `Operation`. |

---

## The hosting package

`Nordmann.LUI.Hosting` carries the dependency injection and nothing else — no background service, no timer.

### `LuiServiceCollectionExtensions`

| Member | What it does |
|---|---|
| `IServiceCollection AddLui(this IServiceCollection services, string connectionString, LuiCatalogOptions? options = null)` | puts the **catalog** into the container |
| `IServiceCollection AddLui(this IServiceCollection services, Func<IServiceProvider, string> connectionString, Func<IServiceProvider, LuiCatalogOptions>? options = null)` | the same, with the connection string and the settings resolved from the container |

The container holds the catalog and nothing else: **an index is not registered and not injected**. The application asks
the catalog for the index it needs. A second `AddLui` throws — the catalog is the one thing of LUI in the container.

---

## The whole surface

Thirty-eight public types in `Nordmann.LUI`, one in `Nordmann.LUI.Hosting`. Nothing else is public.

| Type | Kind | Where it is described |
|---|---|---|
| `LuiCatalog` | class | [the entry point](#the-catalog--the-entry-point) |
| `LuiCatalogOptions` | record | [the entry point](#the-catalog--the-entry-point) |
| `LuiIndexKindBuilder` | class | [creating an index](#creating-an-index--the-description) |
| `LuiEmbeddedIndexBuilder` | class | [creating an index](#creating-an-index--the-description) |
| `LuiLinkedIndexBuilder` | class | [creating an index](#creating-an-index--the-description) |
| `ILuiIndex` | interface | [the index](#the-index--what-the-catalog-hands-out) |
| `ILuiEmbeddedIndex` | interface | [the index](#the-index--what-the-catalog-hands-out) |
| `ILuiLinkedIndex` | interface | [the index](#the-index--what-the-catalog-hands-out) |
| `LuiEntry` | record | [writing](#writing--the-entry-and-the-key) |
| `LuiKey` | struct | [writing](#writing--the-entry-and-the-key) |
| `LuiKeyKind` | enum | [the enumerations](#the-enumerations) |
| `LuiQuery` | class | [reading](#reading--the-query-the-result-the-hit) |
| `LuiQueryOptions` | record | [reading](#reading--the-query-the-result-the-hit) |
| `LuiResult` | record | [reading](#reading--the-query-the-result-the-hit) |
| `LuiHit` | struct | [reading](#reading--the-query-the-result-the-hit) |
| `LuiTimings` | record | [reading](#reading--the-query-the-result-the-hit) |
| `LuiSyncOptions` | record | [the bulk path](#the-bulk-path--loading-many-entries) |
| `LuiSyncReport` | record | [the bulk path](#the-bulk-path--loading-many-entries) |
| `LuiLinkedSyncReport` | record | [the bulk path](#the-bulk-path--loading-many-entries) |
| `LuiIntegrityReport` | record | [the bulk path](#the-bulk-path--loading-many-entries) |
| `LuiPayloadSerializer` | class | [the payload](#the-payload) |
| `ILuiPayloadSerializer` | interface | [the payload](#the-payload) |
| `LuiPayloadRow` | class | [the payload](#the-payload) |
| `ILuiSimilarity` | interface | [the similarity](#the-similarity) |
| `LuiSimilarity` | class | [the similarity](#the-similarity) |
| `LuiIndexKind` | enum | [the enumerations](#the-enumerations) |
| `LuiPriority` | enum | [the enumerations](#the-enumerations) |
| `LuiPriorityMode` | enum | [the enumerations](#the-enumerations) |
| `LuiRankingOrder` | enum | [the enumerations](#the-enumerations) |
| `LuiTriggerState` | enum | [the enumerations](#the-enumerations) |
| `LuiTypoDictionaryState` | enum | [the enumerations](#the-enumerations) |
| `LuiException` | class | [the exceptions](#the-exceptions) |
| `LuiEnvironmentException` | class | [the exceptions](#the-exceptions) |
| `LuiDefinitionMismatchException` | class | [the exceptions](#the-exceptions) |
| `LuiIndexNotFoundException` | class | [the exceptions](#the-exceptions) |
| `LuiLinkedTableException` | class | [the exceptions](#the-exceptions) |
| `LuiPayloadException` | class | [the exceptions](#the-exceptions) |
| `LuiReadOnlyException` | class | [the exceptions](#the-exceptions) |
| `LuiServiceCollectionExtensions` | class | [the hosting package](#the-hosting-package) |

---

> The surface is checked against the code: the architecture test
> [`Public_API_Documentation`](../tests/Nordmann.LUI.Architecture.Tests/Public_API_Documentation.cs) reads the
> assemblies and fails when a public type is missing here — or when a name stands here that the code does not have.
