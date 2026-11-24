# The public API of LUI

> Everything a consumer of the package can call — every public type of `Nordmann.LUI`, every public type of
> `Nordmann.LUI.Hosting`, and nothing else. The [tutorial](./tutorial/README.md) shows how to use it; this page says
> what there is.
>
> **One namespace, one prefix, one way in.** Everything lives in `Nordmann.LUI`; every type begins with `Lui`. The
> catalog is the entry point, the index is what it hands out, and the rest stays on the index.
>
> **This page describes version 0.3.0.** The freeze of 0.1.0 was lifted once, for 0.3.0 ([ADR-0069](../.agents/adr/0069-the-freeze-of-0-1-0-is-lifted-for-0-3-0.md)); the
> members that moved are in the migration note of the [changelog](../CHANGELOG.md), and the API grows only additively again from here.

> **0.4.0 declares an index once.** An index is declared **once**, as a definition object, and the catalog hands out typed handles for it —
> see [the definition](#defining-an-index--the-definition) and [the typed handles](#the-typed-handles--what-a-definition-opens). The calls of
> 0.3.0 that named an index by a string (`Create(name, …)`, `CreateLinked`, `Index(name)`, `LinkedIndex`) and the untyped handles are gone
> ([ADR-0073](../.agents/adr/0073-the-index-definition-and-the-typed-handles.md); the migration note is in the [changelog](../CHANGELOG.md)).

The table at the end is the whole surface. The sections before it explain what the parts are for.

---

## The catalog — the entry point

The catalog is the one thing an application opens. It holds the settings and the indexes of one
database file.

```csharp
using Nordmann.LUI;

using var catalog = LuiCatalog.OpenOrCreate("app.db");
```

**One catalogue is one file.** `Embedded` and `Linked` indexes live side by side in it — mixing them is defined — and the
tables of the application lie beside the tables of LUI (`lui_meta`, `lui_<index>…`) in the same file. What separates the two
kinds is not *which file* but **who owns the content table**: an embedded index owns it (`lui_<index>`), a linked index
reads a table of the application. The file of the application becomes a catalogue with `OpenOrCreate`. LUI puts a file in
write-ahead logging only when `OpenOrCreate` **creates the file itself**; a file that is already there — with tables or
empty — keeps its mode: the application decides, and should choose write-ahead logging (in the rollback journal a read
blocks the writes of the application). LUI never switches the mode of a file again, and `Open` and `OpenReadOnly` write nothing
([ADR-0066](../.agents/adr/0066-the-catalogue-owns-the-connection.md),
[ADR-0068](../.agents/adr/0068-three-ways-to-open-a-catalogue.md)).

### `LuiCatalog`

| Member | What it does |
|---|---|
| `static LuiCatalog Open(string path, LuiCatalogOptions? options = null)` | opens the catalog of a file that is there, and **writes nothing**: a file that is missing or is no catalog (no SQLite database, no `lui_meta`) is a `LuiCatalogNotFoundException`. It never creates a file, never creates `lui_meta` and never sets the journal mode. It probes the SQLite it found (version, folding) and refuses a database it cannot search correctly. A file written in **another LUI format** — a file of 0.3.0 is format 1, 0.4.0 reads format 2 — is a `LuiFormatException` when it is opened (so are `OpenReadOnly` and `OpenOrCreate` of such a file): no migration, recreate the indexes. |
| `static LuiCatalog OpenOrCreate(string path, LuiCatalogOptions? options = null)` | opens the catalog and sets the file up: the file is created when it is missing, `lui_meta` is created when the file has none (the database of an application), and a catalog that is there is not written. **LUI sets the write-ahead logging only in a file it creates itself**; a file that is there keeps its journal mode, empty or not (ADR-0066, ADR-0068). |
| `static LuiCatalog OpenReadOnly(string path, LuiCatalogOptions? options = null)` | opens the catalog of a file that is there, **to read only**: every connection of it is read-only, and SQLite refuses every write, which LUI turns into a `LuiReadOnlyException`. A file that is missing or is no catalog is a `LuiCatalogNotFoundException`. |
| `LuiEmbeddedIndex<TKey> Create<TKey>(LuiIndexDefinition<TKey> definition)` | creates the index of an **embedded definition**, or opens it if it exists with the same definition — safe on every start — and returns its typed handle. It creates only: it never syncs and never installs triggers. A different stored definition is a `LuiDefinitionMismatchException` that names the fields. The handle is the one `Index(definition)` gives for the same definition object. When two processes create the same index at once, one creates it and the other opens it — see [several processes on one file](#several-processes-on-one-file). |
| `LuiLinkedIndex<TKey> Create<TKey>(LuiLinkedIndexDefinition<TKey> definition)` | the same for a **linked definition**: LUI's tables are created and the table of the application is checked, every time. The table is the application's own schema and has to exist before the call (`LuiLinkedTableException` naming the index otherwise). It syncs nothing and installs no trigger: the new index has `NeedsInitialSync == true` and `Triggers() == None` until the application calls `CreateTriggers()` if it wants them — **first**: a row written between the two calls is then noted in the outbox, which a sync that came first would miss — and `Sync()` (ADR-0077). |
| `LuiEmbeddedIndex<TKey, TPayload> Create<TKey,TPayload>(LuiIndexDefinition<TKey, TPayload> definition)` | the same for an **embedded definition with a payload**: the handle writes typed payloads with the serializer that the `Payloads` registry of the catalog holds for `TPayload`. The type is looked up **before anything is written**: a type that is not registered is a `LuiConfigurationException` that names the definition, and no index is created for it. The index stores the name of the payload type and the format of its serializer ([the fingerprint](#the-fingerprint-of-a-payload)). |
| `LuiLinkedIndex<TKey, TPayload> Create<TKey,TPayload>(LuiLinkedIndexDefinition<TKey, TPayload> definition)` | the same for a **linked definition with a payload**: the map of the definition builds the payload from the row. |
| `void Create(params LuiIndexDefinitionBase[] definitions)` | creates or opens **several** definitions in **one transaction**: all or none. The exception names the definition that failed — a mismatch and a missing index by their `IndexName`, a broken table of a linked index as a `LuiLinkedTableException` whose message starts `Index 'name': …` and whose inner exception is the original, a refused write on a read-only catalog by the index whose write was refused; a name that occurs twice is an `ArgumentException` before anything is written. It **returns nothing**: a variable typed `LuiIndexDefinitionBase` binds here, and the handles come from `Index(definition)`. |
| `LuiEmbeddedIndex<TKey> Index<TKey>(LuiIndexDefinition<TKey> definition)` | opens the index of an embedded definition that exists, and **writes nothing**. The stored definition is compared with the declared one: another one is a `LuiDefinitionMismatchException` naming the fields, a missing index a `LuiIndexNotFoundException`. **The catalog holds the stored definitions and the runtime of the index in memory** (read once, when it was opened): the call is a lookup — it returns **the same handle instance for the same definition object**, and after the first call it allocates nothing and executes no SQL, so a request can take its handle where it needs it. The comparison of the stored with the declared definition is done once, at the first call. |
| `LuiLinkedIndex<TKey> Index<TKey>(LuiLinkedIndexDefinition<TKey> definition)` | the same for a linked definition. **One name for both sources** — there is no `LinkedIndex(definition)` and no `CreateLinked(definition)`. |
| `LuiEmbeddedIndex<TKey, TPayload> Index<TKey,TPayload>(LuiIndexDefinition<TKey, TPayload> definition)` | opens the index of an embedded definition with a payload that exists, and **writes nothing**; compared like a definition without payload — the name of the payload type and the format of its serializer are two of the compared fields. The serializer of `TPayload` is looked up at the first call, once for the runtime of the index; an unregistered type is a `LuiConfigurationException` at that call. |
| `LuiLinkedIndex<TKey, TPayload> Index<TKey,TPayload>(LuiLinkedIndexDefinition<TKey, TPayload> definition)` | the same for a linked definition with a payload: the stored payload columns are compared, the map of the definition is code and takes effect. |
| `IReadOnlyList<LuiIndexInfo> Indexes()` | describes **every** index of the catalog by what is stored, in the order of the names — **one row that cannot be read (another LUI format, `null` or malformed JSON) makes the whole call throw a `LuiDefinitionMismatchException` that names the index; `DropIndex` is the way out** — without a definition and without a payload type ([maintenance by name](#maintenance-by-name--without-a-definition-or-a-payload-type), ADR-0076). The definition fields come from the memory of the catalog, with no statement that reads them; the typo dictionary state and `NeedsInitialization` are read when it is called (for a linked index that is one read of the file). |
| `LuiInitializeReport Initialize(LuiInitializeOptions? options = null, CancellationToken cancellationToken = default)` | **the one explicit, synchronous start call** (ADR-0076): brings every index to the state an application needs at the start of a process, in LUI's order and driven by the state of each index — the triggers of every linked index, then the first sync of every linked index that needs one, the outbox, and the typo dictionaries last and one after the other. A second call right after the first does nothing. See [Initializing](#initializing--the-start-of-a-process). There is no `InitializeAsync`: the work is synchronous SQLite. |
| `bool IsReadOnly { get; }` | whether the catalog was opened with `OpenReadOnly` — the way it was opened and no second switch (ADR-0068). The hosting package asks it to refuse `AddLuiOutbox` on a catalog that cannot apply an outbox. |
| `long Count(string name)` | the number of the entries of the index, embedded or linked; the check of the schema counter and the count are one step. A name that is not there is a `LuiIndexNotFoundException` that names it. |
| `LuiIntegrityReport Check(string name)` | compares the index with itself (embedded) or with its table (linked); the report has no keys. **Structure only: it reads no payload and calls no map** (the payload *columns* of a linked index are stored, and are checked). It holds the **write lock of the file for the whole of FTS5's integrity check**, which reads the whole index (an insert that is rolled back): cheap in what it reads, not in the lock it takes — do not wire it into a liveness probe of a second on a large index (#275 measures it). For the same reason a catalog opened with `OpenReadOnly` refuses it with a `LuiReadOnlyException`. |
| `void Optimize(string name)` | lets SQLite optimize the search index of the index, embedded or linked. A catalog opened with `OpenReadOnly` refuses it with a `LuiReadOnlyException` that names the index. |
| `void Rebuild(string name)` | rebuilds an **embedded** index from its content table — the triggers anew, the search index anew, merged, in one transaction; it reads no payload. For a **linked** index it throws an `InvalidOperationException` (`'chefs' is a linked index; rebuild it from its table with Sync() on its handle.`): its content is the application's table, and `Sync()` on its handle builds the search index from it. A catalog opened with `OpenReadOnly` refuses it with a `LuiReadOnlyException` that names the index. |
| `void WarmUpTypoDictionaries(CancellationToken cancellationToken = default)` | builds the typo dictionary of **every** index of the catalog — the warm-up of a host in one call (ADR-0043). The dictionary belongs to the index of the catalog: an index you open later sees it and reports it as built. |
| `void WarmUpTypoDictionaries(IEnumerable<string> names, CancellationToken cancellationToken = default)` | the same for the **named** indexes — a dictionary costs memory (194 MB for 685,739 words), and an index whose queries correct no typos needs none. Every name is checked before the first build: an unknown one is a `LuiIndexNotFoundException`. |
| `int ApplyPending(CancellationToken cancellationToken = default)` | applies the outbox of **every** linked index: the rows the triggers noted come into the index. The table of the application is not touched. Returns the number of rows brought in. |
| `bool DropIndex(string name, bool dropTriggers = false)` | drops the index. The triggers of a linked index are dropped with it only if you say so. The handles that were handed out for it are stale from now on: an operation on one throws a `LuiIndexNotFoundException`, also after the index was created again — take a new handle from `Create` or `Index`. **A row of `lui_meta` that cannot be read** (a definition of `null` or malformed JSON) is the one that this call is the way out of: it is dropped as what its source column says — a `Linked` row with its outbox, search index and entries, and with the refusal of its triggers unless `dropTriggers: true`, an `Embedded` row as an embedded index (also refused without the flag when a trigger of its names sits on another table). A row whose source column is neither is refused with a `LuiDefinitionMismatchException` that names the index; nothing is removed. |
| `string Path { get; }` | the full path of the file the catalog was opened with. |
| `void Dispose()` | closes the catalog: it waits for the operations that are running and clears the pools of the provider (the read connections first). The catalog holds no connection (ADR-0070); the indexes hold no resource of their own. |

The path is a **file path** (ADR-0066): LUI builds every connection itself — pooling on, a private cache, the mode of the
method that opened it. The application never sees a connection string. An empty path, `:memory:` and a URI (`file:…`) are
refused with an `ArgumentException`.

`Create(definition)` is **idempotent**: they create the index if it is missing, and compare the stored
definition with the one that was described if it is there. A difference is a `LuiDefinitionMismatchException` — never a
silent change.

`Index(definition)` is the read path. They never write, whatever the file allows afterwards: the file decides
what may happen.

### Maintenance by name — without a definition or a payload type

A tool, an admin endpoint or a health check inspects and maintains every index of a catalog **without knowing the definitions of the
application and without its payload types** (ADR-0076): `Indexes()`, `Count(name)`, `Check(name)`, `Optimize(name)` — and `Rebuild(name)` for
an embedded index. None of them returns a handle or can write an entry, so one index still has one kind of definition (ADR-0073). They open
the internal runtime from the **stored** definition: they read no payload and call no map, and they meet the same guards as a handle (ADR-0075) —
the schema counter is read inside the snapshot or the transaction of the work, so the check and the work are one step; an index that is gone
is a `LuiIndexNotFoundException` that names it. A call by name declares no definition, so an index that another process created again with
another definition is **used as it is stored** — there is nothing to mismatch.

- **A name that is not known** reloads the stored definitions once and is remembered as missing until the SQLite schema counter changes, so
  the names a user types on an admin endpoint cause no reload per request.
- **`Indexes()`** asks whether the schema counter moved — one statement — and reads the definitions again only then, so an index that another
  process created or dropped is in the list. It reads **no** definition from the file otherwise. `TypoDictionaryState` is the state of the
  dictionary in **this** catalog, read at the call; `NotBuilt` says that this catalog has not built it. `NeedsInitialization` is read like
  `Initialize()` reads it — for a linked index the triggers, the flag of the first sync and its lease, from the file in one snapshot; for an
  embedded index only the dictionary, from memory.
- **A read-only catalog** lists and counts; what writes — `Optimize`, `Rebuild`, and `Check`, whose integrity check is an insert that is rolled
  back — is a `LuiReadOnlyException` that names the index.

Which check for what:

| check | cost | use |
|---|---|---|
| `Check()` of a handle, `catalog.Check(name)` | structure only (definition, triggers, the payload columns of a linked index, FTS5's integrity check, the counts against the table); **holds the write lock of the file while FTS5 checks the index** and is refused on a read-only catalog | health checks on a schedule, not a liveness check of a second |
| `catalog.Indexes()` → `NeedsInitialization` | cheap: state only (one small read of the file per linked index) | readiness, every probe |
| `CheckPayloads()` of a typed handle with a payload (ADR-0074) | reads every stored payload | maintenance, after a change of the payload type |

### `LuiIndexInfo` — an index described by name

What `Indexes()` returns: a sealed record, one per index, a snapshot with named properties (`init`-only; the seven that a caller must set are `required`). It is no handle.

| Member | What it is |
|---|---|
| `required string Name { get; init; }` | the name of the index |
| `required LuiIndexSource Source { get; init; }` | `Embedded` or `Linked`. It is called `Source` because `Kind` already means single word or multi word |
| `required LuiIndexKind Kind { get; init; }` | `SingleWord` or `MultiWord` |
| `string? PayloadType { get; init; }` | the stored name of the payload type of an embedded index that was created with one (ADR-0072); `null` otherwise, and for every linked index |
| `string? PayloadFormat { get; init; }` | the format of the serializer that wrote the payloads; `null` exactly when `PayloadType` is |
| `required LuiKeyType KeyType { get; init; }` | the stored key type: `Long`, `Int`, `String` or `Guid` |
| `required LuiTypoDistance TypoDistance { get; init; }` | how many errors the index forgives |
| `required LuiTypoDictionaryState TypoDictionaryState { get; init; }` | `NotBuilt`, `Building` or `Current`, in this catalog, when `Indexes()` was called |
| `required LuiInitializationNeeds NeedsInitialization { get; init; }` | what the index still needs at the start of a process — `None` when it is ready; `[Flags]`: `Triggers`, `StaleTriggers`, `InitialSync`, `InitialSyncRunningElsewhere`, `TypoDictionary`; read from the state like `Initialize()` reads it (see [Initializing](#initializing--the-start-of-a-process)). On a read-only catalog the file steps are reported too: a reader asks `NeedsInitialization.HasFlag(LuiInitializationNeeds.TypoDictionary)`. |

### `LuiCatalogOptions`

The settings of the catalog. Every one of them has a default; none of them has to be set.

| Member | Default | What it is for |
|---|---|---|
| `LuiPragmas Pragmas` | `new LuiPragmas()` | the settings of the connections of the catalog — see below |
| `LuiPayloadRegistry? Payloads` | none | the serializers of the payloads, **by payload type** ([the payload registry](#luipayloadregistry)): `new LuiPayloadRegistry().Use(AppJsonContext.Default)`. The registry is **frozen when the catalog is opened** — a later call on it changes nothing for that catalog. |
| `TimeProvider TimeProvider` | `TimeProvider.System` | the clock of the catalog: the `created_at` of an index and the **lease of a linked sync** (ADR-0077 — its expiry is read from this clock, in UTC), and a seam for tests |

The file decides whether writing is allowed at all: a catalog opened with `OpenReadOnly` writes nothing, and SQLite refuses
every write — LUI turns the refusal into a `LuiReadOnlyException` that says why (ADR-0068).
Every read — a query, `Count`, the check against the table — runs on a read-only connection, whichever way the catalog was opened (ADR-0067).

### `LuiPragmas`

The settings of the connections of a catalog (ADR-0066). They belong to the catalog: an index has no connection of its own
to configure, and the application never holds one.

| Member | Default | What it is for |
|---|---|---|
| `TimeSpan BusyTimeout` | 5 s | how long a connection waits for the lock of the file — then `SqliteException` with `SQLITE_BUSY`. At least one second, at most ten minutes. It is checked when the catalog is opened. |
| `LuiSynchronous Synchronous` | `LuiSynchronous.Normal` | how the connections write: `Normal` or `Full` |
| `int? CacheSizeMegabytes` | the SQLite default | the page cache of **every** connection, in megabytes; 1 to 1024. It holds per connection: the upper bound of the read path is the number of parallel queries times this value. |

Every value is checked when the catalog is opened: a value out of range is an `ArgumentOutOfRangeException`, before a
connection is opened — so a file that is not written is not created either.

### `LuiSynchronous`

How the connections of a catalog write (ADR-0066).

| Value | What it is for |
|---|---|
| `Normal` | the usual one: the write-ahead log carries the durability of the file |
| `Full` | every write is flushed to the disk: the safest, and the slowest |

`Off` would put the file at risk on a power loss, and `Extra` would cost without a gain in the write-ahead log — a setting
that makes no sense does not exist.

---

## Defining an index — the definition

An index is declared **once**, as a definition object — name, source, kind, key type, options and, for a linked index, the
table — and every call site uses that object: nothing is repeated, and a call site cannot name the wrong type. The class
that holds them is called `SearchIndexes` here (not `Indexes`, which collides with the namespace `Nordmann.LUI.Indexes`):

```csharp
public static class SearchIndexes
{
    public static readonly LuiIndexDefinition<long> Tags =
        new("tags", index => index.SingleWord());

    public static readonly LuiLinkedIndexDefinition<long> Articles =
        new("articles", index => index.MultiWord().LinkTo(table: "articles", key: "id", text: "title").TypoTolerance());

    // the key type is the type argument: long, int, string or Guid (a linked index: long or int)
    public static readonly LuiIndexDefinition<Guid> Sessions =
        new("sessions", index => index.SingleWord());

    // with a payload: the type is declared once, in the definition
    public static readonly LuiIndexDefinition<long, RecipePayload> Recipes =
        new("recipes", index => index.MultiWord().TypoTolerance());

    public static readonly LuiLinkedIndexDefinition<long, ChefView> Chefs =
        new("chefs", index => index.MultiWord()
            .LinkTo(table: "chefs", key: "id", text: "name")
            .WithPayload(["name", "city"], row => new ChefView(row.GetString("name"), row.GetString("city")))
            .TypoTolerance());
}

// at the start of the application — safe every time
catalog.Create(SearchIndexes.Tags, SearchIndexes.Articles, SearchIndexes.Sessions, SearchIndexes.Recipes, SearchIndexes.Chefs);

// anywhere else, without type arguments
LuiEmbeddedIndex<long> tags = catalog.Index(SearchIndexes.Tags);
LuiLinkedIndex<long> articles = catalog.Index(SearchIndexes.Articles);
LuiEmbeddedIndex<Guid> sessions = catalog.Index(SearchIndexes.Sessions);
LuiEmbeddedIndex<long, RecipePayload> recipes = catalog.Index(SearchIndexes.Recipes);
LuiLinkedIndex<long, ChefView> chefs = catalog.Index(SearchIndexes.Chefs);
```

A definition is an immutable object with **reference equality** and without I/O. It knows no catalog and can be shared by any
number of catalogs. **`Name` is stored at once; its validation and the builder function run once, lazily, on first use** — also
when several threads use it for the first time at once — and the result, or its failure, is kept: **a definition whose builder function failed stays failed for the life of the process** —
every use throws a new `LuiConfigurationException` (it names the definition) with the same cause; fix the code and restart. An invalid definition (a
bad name, a key type that is not allowed, a step the definition does not know) throws a `LuiConfigurationException` on its first
use by a catalog — not in a static initializer, where it would surface as a `TypeInitializationException`.

### `LuiIndexDefinitionBase`

| Member | What it is |
|---|---|
| `string Name { get; }` | the name of the index, as it was given: `^[a-z][a-z0-9_]{0,40}$`, checked on first use |
| `LuiIndexSource Source { get; }` | `Embedded` or `Linked` — decided by the definition type |
| `Type KeyType { get; }` | the type of the keys: the type argument of the definition — `long`, `int`, `string` or `Guid` ([the key types](#the-key-types)) |
| `Type? PayloadType { get; }` | the type of the payload: the second type argument of a definition with a payload; `null` for a definition without |
| `string ToString()` | the definition in one line: `tags (embedded, long)`, with a payload `recipes (embedded, long, RecipePayload)` |

The class is abstract and **no other assembly can derive from it** — the four definition types (`LuiIndexDefinition<TKey>`,
`LuiLinkedIndexDefinition<TKey>` and their payload variants) are the whole family; they derive from this class and from nothing
else, so the overloads of `Create` and `Index` are unambiguous. A variable typed `LuiIndexDefinitionBase` passed to `Create` binds to
`Create(params LuiIndexDefinitionBase[])`, which returns nothing.

### `LuiIndexDefinition` — the definition of an embedded index

| Member | What it is |
|---|---|
| `LuiIndexDefinition(string name, Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder> index)` | declares the index; nothing runs until the first use. `LuiIndexDefinition<TKey>` is sealed, `where TKey : notnull`. |
| `LuiIndexDefinition(string name, Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder> index)` | **with a payload:** `LuiIndexDefinition<TKey, TPayload>` is sealed, `where TKey : notnull where TPayload : notnull`; the same constructor, and `catalog.Create` / `catalog.Index` give a `LuiEmbeddedIndex<TKey, TPayload>`. It does not derive from `LuiIndexDefinition<TKey>`. |

### `LuiLinkedIndexDefinition` — the definition of a linked index

| Member | What it is |
|---|---|
| `LuiLinkedIndexDefinition(string name, Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder> index)` | declares the index; the description names the table with `LinkTo(…)`. `LuiLinkedIndexDefinition<TKey>` is sealed, `where TKey : notnull`. A builder that ends in `WithPayload(…)` does not compile here. |
| `LuiLinkedIndexDefinition(string name, Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>> index)` | **with a payload:** `LuiLinkedIndexDefinition<TKey, TPayload>` is sealed, `where TKey : notnull where TPayload : notnull`. The function has to end in the typed builder of the **same** payload type — one without `WithPayload`, or one that maps to another type, does not compile. The map is part of the definition; each definition uses its own, also when two have the same view type. |

### `LuiIndexSource`

`Embedded` — the content lives in a table of the library; `Linked` — in a table of the application.

### The key types

`TKey` is the key type of the definition, and the stored definition names it **exactly**:

| `TKey` | stored key type | `LuiKeyType` | embedded | linked |
|---|---|---|---|---|
| `long` | `long` | `Long` | yes | yes |
| `int` | `int` | `Int` | yes | yes |
| `string` | `string` | `String` | yes | not yet — #267 |
| `Guid` | `guid` | `Guid` | yes | not yet — #267 |

- The key type is a **compared field**: opening an index with another key type — `int` against `long` included — is a changed
  definition and throws a `LuiDefinitionMismatchException` naming `keyType`. `DropIndex`, `Create`, fill it again.
- Any other `TKey` (`short`, `double`, a class …) is a `LuiConfigurationException` on first use that names the definition and the allowed
  types; `string` or `Guid` on a **linked** definition names #267 (a linked index has the `INTEGER PRIMARY KEY` of the application's table).
- A `Guid` is written and compared in the canonical form `"D"`, lower case (`0f8fad5b-d9cb-469f-a165-70867728950e`), so there is one
  stored key per GUID and no stored text that is not a GUID. An `int` is stored in invariant digits.
- An `int` key of an **embedded** index can only be written through an `int` handle, so it always fits. A **linked** `int` index reads
  the keys of the application's table: a row whose key is outside `0 … 2,147,483,647` is left out by `Sync`, `Refresh` and
  `ApplyPending`, counted in `KeyOutOfRange` of the `Sync` report, and never reaches a query.
- There is **no `CheckKeys()`**: a stored key always converts. A key that does not — a damaged file — fails the read with a
  `LuiKeyException` that names the index and the stored key.

### What the comparison compares

`Create(definition)` and `Index(definition)` compare the declared definition with the stored one:

| Compared field | From |
|---|---|
| `source` (embedded or linked) | the definition type |
| `kind` (single word or multi word) | `SingleWord()` / `MultiWord()` |
| `keyType` | `TKey` — exactly: `long`, `int`, `string` or `guid` |
| `germanTransliteration`, `foldTable` | `GermanTransliteration(…)`; the fold table of the library |
| `typoDistance` | `TypoTolerance(…)` |
| `link.table`, `link.key`, `link.text`, `link.weight`, `link.priority`, `link.payloadColumns` | `LinkTo(…)` and `WithPayload(…)` |
| `payloadTypeName`, `payloadFormat` (an embedded definition with a payload; a linked one stores neither) | `TPayload` — its `[LuiPayloadName]` or its computed name — and the `Format` of the serializer the registry holds for it ([the fingerprint](#the-fingerprint-of-a-payload)) |

| Runtime only | Why |
|---|---|
| `Similarity(…)` | a measure is code: it changes no table, so it is never a mismatch — it takes effect with the next start |
| the `map` of a linked payload | a lambda: code of the application — a changed map takes effect with the next start, without drop and recreate |
| the settings of the serializer | only its `Format` is stored: a serializer with other settings and the same format is the same definition |

### Changing a definition

LUI does not migrate a definition. The recipe, which the message of a `LuiDefinitionMismatchException` repeats:

1. `catalog.DropIndex(name)` (`dropTriggers: true` for a linked index that has triggers), then `catalog.Create(definition)`;
2. fill it again — `UpsertMany` or `Sync` for an embedded index (its stored content is lost with the drop), `Sync()` for a
   linked one (`NeedsInitialSync` says that it is still to be done), after `CreateTriggers()` if the application uses them — the triggers
   come first, so that no row written in between is lost.

Or give the new definition a **new name** (`recipes_v2`) next to the old index, switch the application over, and drop the old
one — there is no window without a search.

---

## The typed handles — what a definition opens

The handles are **concrete classes, not interfaces**: an application tests its search against a real catalog, not a mock.
They are views — no connection, not disposable, thread-safe like the catalog, reference equality. An operation on a handle
whose index was dropped throws a `LuiIndexNotFoundException`, and one whose index was dropped and **created again with another
definition** throws a `LuiDefinitionMismatchException` — also when another process did it, see
[several processes on one file](#several-processes-on-one-file); a handle never reads or writes through a stale definition. The failure is kept: take a
fresh handle from `Create` or `Index` after the recipe for a changed definition. An index created again with the **same**
definition is not stale — the handle goes on with it; but a handle that an operation met **between** the drop and the create is dead for good, so take a handle per request (a lookup) rather than holding one across someone else's recreate. On a disposed catalog an `ObjectDisposedException`; on a catalog
opened with `OpenReadOnly`, a write is a `LuiReadOnlyException`. **Use `LuiIndex<TKey>` where a method only reads,
`LuiEmbeddedIndex<TKey>` / `LuiLinkedIndex<TKey>` where it writes or syncs.** All of them are `where TKey : notnull`; `TKey` is
the key type of the definition — see [the key types](#the-key-types) — so `hit.Key` and every key parameter have that type and a key
of another type does not compile.

The API stays **synchronous**, also for an asynchronous source: `Sync(IAsyncEnumerable<…>)` blocks until the sync is done —
run it on a thread that may block.

### `LuiIndex` — the read side of both sources

`LuiIndex<TKey>` is abstract, with no constructor another assembly can call.

| Member | What it is |
|---|---|
| `string Name { get; }` | the name of the index |
| `LuiIndexKind Kind { get; }` | `SingleWord` or `MultiWord` |
| `LuiTypoDistance TypoDistance { get; }` | how many errors the index forgives |
| `LuiTypoDictionaryState TypoDictionaryState { get; }` | `NotBuilt`, `Building` or `Current` |
| `long Count()` | the number of the entries |
| `LuiQuery<TKey> Query(string text)` | a query that is configured step by step and then executed; `Execute` returns a `LuiResult<TKey>` |
| `LuiResult<TKey> Search(string text, LuiQueryOptions? options = null, CancellationToken cancellationToken = default)` | a query in one call |
| `void WarmUpTypoDictionary(CancellationToken cancellationToken = default)` | builds the typo dictionary before a query needs it |
| `void RebuildTypoDictionary(CancellationToken cancellationToken = default)` | builds it again, from scratch |
| `void Optimize()` | lets SQLite optimize the search index |
| `LuiIntegrityReport Check()` | compares the index with itself (embedded) or with its table (linked); the report has no keys, reads no payload and calls no map |

### `LuiEmbeddedIndex` — the write path of an embedded index

`LuiEmbeddedIndex<TKey>` is sealed and derives from `LuiIndex<TKey>`; `transaction` is an optional `SqliteTransaction` of the
application, `ct` an optional `CancellationToken`.

| Member | What it does |
|---|---|
| `void Upsert(TKey key, string text, int weight = 0, LuiPriority priority = LuiPriority.Normal, SqliteTransaction? transaction = null)` | writes one entry, without an object |
| `void Upsert(LuiEntry<TKey> entry, SqliteTransaction? transaction = null)` | writes one entry |
| `int UpsertMany(IEnumerable<LuiEntry<TKey>> entries, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)` | writes many entries in one transaction; returns how many |
| `LuiSyncReport Sync(IEnumerable<LuiEntry<TKey>> entries, LuiSyncOptions? options = null, CancellationToken cancellationToken = default)` | makes the index equal to the entries — the bulk path |
| `LuiSyncReport Sync(IAsyncEnumerable<LuiEntry<TKey>> entries, LuiSyncOptions? options = null, CancellationToken cancellationToken = default)` | the same from a stream; the call **blocks** until the sync is done and returns the report that the synchronous overload returns for the same entries |
| `bool Remove(TKey key, SqliteTransaction? transaction = null)` | removes an entry |
| `bool UpdateWeight(TKey key, int weight, SqliteTransaction? transaction = null)` | changes the weight; `false` when there is no such entry |
| `bool UpdatePriority(TKey key, LuiPriority priority, SqliteTransaction? transaction = null)` | changes the priority |
| `LuiIntegrityReport Check(IEnumerable<LuiEntry<TKey>> entries, CancellationToken cancellationToken = default)` | compares the index with a set of entries |
| `void Rebuild()` | rebuilds the search index from the content table |

### `LuiLinkedIndex` — the handle of a linked index

`LuiLinkedIndex<TKey>` is sealed and derives from `LuiIndex<TKey>`. A linked index keeps no copy of the row; the write path
belongs to the application.

| Member | What it does |
|---|---|
| `LuiLinkedSyncReport Sync(LuiSyncOptions? options = null, CancellationToken cancellationToken = default)` | takes the whole table into the index, **in batches** (ADR-0077): `options.BatchSize` rows of the table at a time, each batch in a transaction of its own, with no clear at the start — **a linked sync is not atomic**, while it runs the index is partly old and partly new. The entries of rows that are gone are removed at the end by their generation, the search index is merged in steps, and the transaction of the last step clears `NeedsInitialSync`. One sync of an index runs at a time (a lease): a second `Sync()` throws a `LuiSyncInProgressException`. `options.Progress` is told after every batch. See [the sync of a linked index](#the-sync-of-a-linked-index). |
| `bool Refresh(TKey key, SqliteTransaction? transaction = null)` | takes one row into the index |
| `int RefreshMany(IEnumerable<TKey> keys, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)` | takes many rows into the index |
| `int ApplyPending(CancellationToken cancellationToken = default)` | applies the changes a trigger collected |
| `long Pending()` | how many changes are waiting |
| `string TriggerScript()` | the SQL of the triggers — pure: built from the definition, no database is read, so it also answers on a handle whose index was dropped |
| `bool NeedsInitialSync { get; }` | `true` from the creation of the index until its first complete `Sync()` (ADR-0077). Stored in `lui_meta` and **read from the file on every call**: another process that completed the sync is seen at once, without a reopen. A cancelled or failing sync leaves it `true`; it is cleared in the transaction that commits a complete sync. LUI never syncs on its own — the application decides what to do with it. Throws `LuiIndexNotFoundException` / `LuiDefinitionMismatchException` like every operation of the handle. |
| `void CreateTriggers()` | brings the triggers on the table to the current ones — only when the application asks, **idempotent** (ADR-0077): `None` creates them, `Stale` replaces them, `Current` does nothing; it never throws because triggers exist |
| `void DropTriggers()` | drops them again |
| `LuiTriggerState Triggers()` | `None`, `Current` or `Stale` |

### The entry, the query, the result and the hit

| Type | What it is |
|---|---|
| `LuiEntry<TKey>` | a sealed record: `LuiEntry(TKey key, string text)`, `Key` and `Text` (read-only), `Weight` (0 to 536,870,911) and `Priority` (both `init`) — value equality, no `Deconstruct`; it checks itself like `LuiEntry` |
| `LuiQuery<TKey>` | a sealed class with the settings of `LuiQuery` — `Take`, `Candidates`, `Rank`, `PriorityMode`, `Similarity`, `TypoTolerance`, `TrackTimings` — and `Execute(ct)`, which returns a `LuiResult<TKey>` |
| `LuiResult<TKey>` | a sealed class (reference equality — a result holds a list): `Query`, `Hits`, `CandidatesConsidered`, `Truncated`, `CorrectedQuery`, `CorrectionSkipped`, `Timings`, with `IReadOnlyList<LuiHit<TKey>> Hits` |
| `LuiHit<TKey>` | a sealed record with named, initialised properties and no constructor with parameters: `Key` (a `TKey`), `Text`, `Weight`, `Priority`, `Similarity`, `AtWordStart` — value equality |

### Handles with a payload

A definition with a payload (`LuiIndexDefinition<TKey, TPayload>`, `LuiLinkedIndexDefinition<TKey, TPayload>`) opens the payload
variants of the handles: `LuiIndex<TKey, TPayload>`, `LuiEmbeddedIndex<TKey, TPayload>`, `LuiLinkedIndex<TKey, TPayload>` and the
entry, query, result and hit with the same two type arguments. They do **not derive** from the types without a payload — an index
with a payload is another type than one without, and no payload-less write reaches it. Both type arguments are `where … : notnull`.

**The payload is written with the serializer that the registry of the catalog holds for `TPayload`**
([the payload registry](#luipayloadregistry)): it is looked up when the index is created or opened — a type that is not registered is
a `LuiConfigurationException` at `Create` or `Index`, naming the definition — and the handle holds it, so a write never looks it up.
**The payload comes back with the hits** ([ADR-0074](../.agents/adr/0074-the-hit-carries-its-payload.md)): `hit.Payload` is a plain value
that is set when `Search` or `Execute` returns — see [the payload of a hit](#the-payload-of-a-hit).

#### `LuiIndex<TKey, TPayload>` — the read side

Abstract, with no constructor another assembly can call; the members are those of `LuiIndex<TKey>` — `Name`, `Kind`, `TypoDistance`,
`TypoDictionaryState`, `Count()`, `WarmUpTypoDictionary(ct)`, `RebuildTypoDictionary(ct)`, `Optimize()`, `Check()` — with:

| Member | What it is |
|---|---|
| `LuiQuery<TKey, TPayload> Query(string text)` | a query that is configured step by step and then executed; `Execute` returns a `LuiResult<TKey, TPayload>` |
| `LuiResult<TKey, TPayload> Search(string text, LuiQueryOptions? options = null, CancellationToken cancellationToken = default)` | a query in one call |

#### `LuiEmbeddedIndex<TKey, TPayload>` — the write path of an embedded index with a payload

Sealed, derives from `LuiIndex<TKey, TPayload>`. **Every write carries a payload:** there is no `Upsert(key, text, weight)`, no
`LuiEntry<TKey>` overload and no payload-less `Sync` — and with `TPayload = int`, `Upsert(1, "x", 5)` means the payload `5`, never a
weight. The payload comes **right after the text**, the order of the entry.

| Member | What it does |
|---|---|
| `void Upsert(TKey key, string text, TPayload payload, int weight = 0, LuiPriority priority = LuiPriority.Normal, SqliteTransaction? transaction = null)` | writes one entry; a `null` payload throws an `ArgumentNullException` |
| `void Upsert(LuiEntry<TKey, TPayload> entry, SqliteTransaction? transaction = null)` | writes one entry |
| `int UpsertMany(IEnumerable<LuiEntry<TKey, TPayload>> entries, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)` | writes many entries in one transaction; returns how many |
| `LuiSyncReport Sync(IEnumerable<LuiEntry<TKey, TPayload>> entries, LuiSyncOptions? options = null, CancellationToken cancellationToken = default)` | makes the index equal to the entries |
| `LuiSyncReport Sync(IAsyncEnumerable<LuiEntry<TKey, TPayload>> entries, LuiSyncOptions? options = null, CancellationToken cancellationToken = default)` | the same from a stream; **blocks** until the sync is done (there is no `SyncAsync`) |
| `bool Remove(TKey key, SqliteTransaction? transaction = null)` | removes an entry |
| `bool UpdateWeight(TKey key, int weight, SqliteTransaction? transaction = null)` | changes the weight |
| `bool UpdatePriority(TKey key, LuiPriority priority, SqliteTransaction? transaction = null)` | changes the priority |
| `LuiIntegrityReport Check(IEnumerable<LuiEntry<TKey, TPayload>> entries, CancellationToken cancellationToken = default)` | compares the index with a set of entries |
| `void Rebuild()` | rebuilds the search index from the content table |
| `LuiPayloadCheck<TKey> CheckPayloads(CancellationToken cancellationToken = default)` | **a full read of the index**: lists the entries whose payload cannot be read, by key — see [finding the payloads that cannot be read](#finding-the-payloads-that-cannot-be-read); `Check()` reads no payload |

#### `LuiLinkedIndex<TKey, TPayload>` — the handle of a linked index with a payload

Sealed, derives from `LuiIndex<TKey, TPayload>`; the members are those of `LuiLinkedIndex<TKey>` — `Sync(options, ct)`, `Refresh`,
`RefreshMany`, `ApplyPending`, `Pending`, `TriggerScript`, `CreateTriggers`, `DropTriggers`, `Triggers`, `NeedsInitialSync` — and the payload comes
from the map of the definition.

| Type | What it is |
|---|---|
| `LuiEntry<TKey, TPayload>` | a sealed record: `LuiEntry(TKey key, string text, TPayload payload)` — a `null` payload throws an `ArgumentNullException`; `Key`, `Text` and **`Payload` (read-only, no `init`: `entry with { … }` cannot set it to `null`)**, `Weight` and `Priority` (`init`) — value equality, the payload's included |
| `LuiQuery<TKey, TPayload>` | a sealed class with the settings of `LuiQuery` and `Execute(ct)`, which returns a `LuiResult<TKey, TPayload>` |
| `LuiResult<TKey, TPayload>` | a sealed class (reference equality) as `LuiResult<TKey>`, with `IReadOnlyList<LuiHit<TKey, TPayload>> Hits` |
| `LuiHit<TKey, TPayload>` | a sealed record as `LuiHit<TKey>` — named, initialised properties, no constructor with parameters — with **`TPayload Payload { get; init; }`**, the payload of the entry, set when the query returns. It does not derive from `LuiHit<TKey>`; a hit **without** a payload type has no payload. |
| `LuiPayloadCheck<TKey>` | a sealed class (reference equality — it holds a list), what `CheckPayloads()` returns: `IReadOnlyList<LuiUnreadablePayload<TKey>> Unreadable` and `bool IsHealthy` |
| `LuiUnreadablePayload<TKey>` | a sealed record: the `Key` of an entry whose payload cannot be read, and the `Message` — what a query that met the entry would say |

#### The payload of a hit

`hit.Payload` is **a plain value**: no I/O, no serializer call, no map call, no exception, no cache — it is set when the result is built and
never changes. **A query loads the payloads of its hits with the query**; there is no lazy mode.

- **Embedded index:** the stored bytes of the hits are read with the hit rows, in the one snapshot of the query, and deserialized with the
  serializer of the payload type before `Search` / `Execute` returns — **one call per hit**.
- **Linked index:** the map of the definition runs once per hit, before `Search` / `Execute` returns.
- **The cost is bounded by `MaxResults`** (20 by default, at most 1,000): at most that many payloads per query. The guarantee of
  ADR-0035 that *a payload that is never read costs nothing* is gone: a query on an index with a payload reads every payload of its
  result; a query on an index **without** one has none and selects no payload column.
- **The hit outlives the catalog:** reading `hit.Payload` works after the catalog was disposed.
- **A payload that cannot be read fails the query**, not the access to the hit: a serializer that throws, a stored payload that is
  missing, a map that throws and a map that returns `null` each throw a `LuiPayloadException` naming the **index, the key and the type**
  (the serializer's or the map's own exception is the inner one). One broken payload fails the whole query — fail fast — instead of
  returning a partial result. **What "cannot be read" means is the serializer's business, not LUI's:** the strict JSON of
  `LuiPayloadSerializer.Json` (`UnmappedMemberHandling.Disallow`, `RespectRequiredConstructorParameters`) refuses a payload of an old
  shape; a tolerant serializer registered with `Use<T>(…)` may read the same bytes with default values, and then nothing fails. Every
  statement below about old payloads is about `LuiPayloadSerializer.Json`.

| before | after |
|---|---|
| `hit.GetPayload()` of a typed hit | `hit.Payload` |
| `hit.TryGetPayload(out var payload)` of a typed hit | `hit.Payload` — an unreadable payload now fails the query; find it with `CheckPayloads()` |
| `hit.GetPayload<RecipePayload>()` of an untyped hit | declare the index as `LuiIndexDefinition<long, RecipePayload>`, open it with `catalog.Index(SearchIndexes.Recipes)` and read `hit.Payload` |
| `hit.HasPayload` | nothing — `LuiHit<TKey, TPayload>` always has a payload, `LuiHit<TKey>` never has one |

#### Finding the payloads that cannot be read

`LuiEmbeddedIndex<TKey, TPayload>.CheckPayloads(ct)` reads **every stored payload** with the serializer of the payload type and lists the
entries that do not read — keyed by `TKey`, with the reason — in the order of the rows of the index. It is a full read of the index, for
maintenance and not for every probe: one read connection, one snapshot, the check of the handle inside it as for any operation (a
dropped index is a `LuiIndexNotFoundException`, another definition a `LuiDefinitionMismatchException`), cancellable between the entries,
and a catalog that was opened read-only may call it. `Check()` stays the **structural** check — a report without keys that reads **no
payload** and calls **no map** (it holds the write lock of the file for FTS5's integrity check, see [maintenance by name](#maintenance-by-name--without-a-definition-or-a-payload-type)). A linked index has no `CheckPayloads()`: its payload is not stored.

```csharp
var check = recipes.CheckPayloads(cancellationToken);
if (!check.IsHealthy)
{
    foreach (var entry in check.Unreadable)
    {
        logger.LogWarning("Recipe {Key} has a payload that cannot be read: {Message}", entry.Key, entry.Message);
        recipes.Upsert(entry.Key, TextOf(entry.Key), PayloadOf(entry.Key));   // rewrite it
    }
}
```

---

## Initializing — the start of a process

An application has four start steps to know and to order — the triggers and the first sync of a linked index, the outbox, the typo
dictionaries — and every forgotten one fails silently: without its dictionary a typo-tolerant index **never corrects** (a query never builds
it, ADR-0042; it answers with `CorrectionSkipped = true`). `catalog.Initialize()` does all of it **in one explicit, synchronous call**
(ADR-0076). LUI never initializes on its own; the application decides **when** and **on which thread**, and `NeedsInitialization` makes
forgetting visible.

```csharp
var report = catalog.Initialize();                       // everything, for every index — a second call right after does nothing
catalog.Initialize(new LuiInitializeOptions { Indexes = [SearchIndexes.Chefs] });   // in parts
```

**The order**, step by step across the selected indexes — LUI's order, not the application's:

1. the **triggers** of every linked index whose triggers are missing or stale (`CreateTriggers()`, idempotent) — **before** the sync, so that a row written in between is noted in the outbox and not lost (ADR-0077);
2. the **first sync** of every linked index that still needs one, one after the other, in batches, through the lease of ADR-0077;
3. the **outbox** of every linked index that has rows in it, once;
4. the **typo dictionaries**, last — so that they know the words of the syncs and the outbox — and **one after the other**, so that the peak memory is that of the largest dictionary and not the sum.

**Per index only what its state needs**, read first: the triggers (`Triggers()`), `NeedsInitialSync`, the outbox (`Pending()`), the typo dictionary.
A typo dictionary is built only for an index that has no file step left (no triggers missing, no initial sync due or running elsewhere) —
otherwise it would miss the words of a sync still to come; the reason stays, and the next `Initialize()` builds it. A dictionary is
**per process** (it lives in memory): every process builds its own; that is expected work, not a repetition. **The cost**: each dictionary takes
seconds and some hundred MB at the size of the German word list (194 MB for 685,739 words); with several typo-tolerant indexes the time adds up.

**Several processes.** The first sync is guarded by the lease: of two processes that start together exactly one runs it (the flag is read in the
transaction that takes the lease). The other skips that sync — and the steps after it for that index — and reports
`LuiInitializationNeeds.InitialSyncRunningElsewhere`; it does not wait and does not fail. Triggers are idempotent, and the outbox is applied by
one writer at a time (ADR-0046).

**A failing index does not stop the others**: its failing step is recorded, its later steps are skipped, the other indexes go on, and at the end
the call throws a `LuiInitializeException` with the complete report and the error of each failed index (never swallowed). **Cancellation** is checked
between the steps and inside the syncs; it throws an `OperationCanceledException` (not the errors of indexes that had failed by then — the next call finds them again), what was finished stays, and the next call continues by state.
**A catalog opened with `OpenReadOnly`** builds the dictionaries and nothing else: the file steps cannot be done and are left to the writing process
— `NeedsInitialization` reports them, and the readiness of a reader looks only at `TypoDictionary`.

The single steps stay public for fine control: `CreateTriggers()`, `Sync()`, `ApplyPending()`, `WarmUpTypoDictionary()` and
`catalog.WarmUpTypoDictionaries()`.

### `LuiInitializeOptions`

A sealed record with `init` properties; the four switches can be bound from configuration, `Indexes`, `Sync` and `Progress` cannot. Every default is "all of it, for every index".

| Member | Default | What it says |
|---|---|---|
| `bool InstallTriggers { get; init; }` | `true` | linked: `CreateTriggers()` when the triggers are missing or stale. **Off, the first sync runs without triggers — the order that can lose a row written in between (ADR-0077).** An index whose triggers stay missing keeps the need `Triggers` and gets no dictionary from this call |
| `bool InitialSync { get; init; }` | `true` | linked: the first `Sync()`, in batches, when `NeedsInitialSync` — guarded by the lease |
| `bool ApplyPending { get; init; }` | `true` | the outbox, once, of every linked index that has rows in it |
| `bool WarmUpTypoDictionaries { get; init; }` | `true` | every index with a typo distance other than `None`, last, one after the other |
| `IReadOnlyCollection<LuiIndexDefinitionBase>? Indexes { get; init; }` | `null` | the indexes to initialize, by definition: `[SearchIndexes.Chefs]`; only the names are used. A definition of an index that does not exist throws a `LuiIndexNotFoundException` before any step. `null` is every index of the catalog, read from the stored definitions — a tool without definitions can initialize everything |
| `LuiSyncOptions? Sync { get; init; }` | `null` | the options of the first syncs, above all `BatchSize`; its `Progress` is called as well |
| `IProgress<LuiInitializeProgress>? Progress { get; init; }` | `null` | told right before each step and after every batch of a first sync |

### `LuiInitializeProgress`

| Member | What it says |
|---|---|
| `required string IndexName { get; init; }` | the index the step is for |
| `required LuiInitializeStep Step { get; init; }` | `Triggers`, `InitialSync`, `Outbox` or `TypoDictionary` — the step that is about to begin, or the first sync that is running (`ReadState` names only the place of a failure) |
| `LuiSyncReport? Sync { get; init; }` | what the first sync has done so far, after each batch; `null` when a step is told. A step that is not needed is not told: a second call tells nothing |

### `LuiInitializeReport`

What the call returns — and what `LuiInitializeException.Report` carries. `ToString()` is a readable summary for a log line:
`logger.LogInformation("Search initialized: {Report}", report)`.

| Member | What it says |
|---|---|
| `required IReadOnlyList<LuiInitializeIndexReport> Indexes { get; init; }` | one line per index, in the order of the names |
| `TimeSpan Elapsed { get; init; }` | the total |
| `bool Failed { get; }` | whether an index failed |
| `bool Ready { get; }` | whether every index of the run is ready: none failed and none needs anything |

### `LuiInitializeIndexReport`

One line of the report: what ran for the index, what is left, whether it failed and how long it took.

| Member | What it says |
|---|---|
| `required string Name { get; init; }` | the name of the index |
| `bool TriggersInstalled { get; init; }` | this call created or replaced the triggers |
| `LuiLinkedSyncReport? Sync { get; init; }` | what the first sync did; `null` when this call ran none |
| `int PendingApplied { get; init; }` | the rows of the outbox this call brought in |
| `bool TypoDictionaryBuilt { get; init; }` | this call built the dictionary in this process |
| `LuiInitializationNeeds Skipped { get; init; }` | what the index still needs when the call is through — read again from the state; `None` when it is ready |
| `bool Failed { get; init; }` | a step of the index failed; its later steps were skipped |
| `LuiInitializeStep? FailedStep { get; init; }` | the step that failed |
| `TimeSpan Elapsed { get; init; }` | the time spent on this index |

### `LuiInitializeException`

Thrown at the end of `Initialize()` when one or more indexes failed — after every other index was initialized. It derives from `LuiException`; its message names
every failed index and the step it failed at, and the first error is the inner exception. Only the library throws it.

| Member | What it says |
|---|---|
| `LuiInitializeReport Report { get; }` | the complete report — the failed indexes and the others |
| `IReadOnlyDictionary<string, Exception> Errors { get; }` | the exception of each failed index, by its name, as it was thrown |

### `LuiInitializationNeeds`

A `[Flags]` enumeration: what an index still needs at the start of a process. `None` is ready. `ToString()` is readable text for a log (`Triggers, InitialSync`).

| Flag | Value | The index needs |
|---|---|---|
| `None` | 0 | nothing |
| `Triggers` | 1 | a linked index has no triggers |
| `StaleTriggers` | 2 | a linked index has triggers that differ from the ones LUI writes now |
| `InitialSync` | 4 | the first complete sync of a linked index is due and nobody runs it |
| `InitialSyncRunningElsewhere` | 8 | the first sync is due and another sync holds the lease — instead of `InitialSync`, not beside it; the lease of a sync that stopped renewing expires, and the need is `InitialSync` again |
| `TypoDictionary` | 16 | the typo dictionary of an index that forgives typos is not built in this process (also while it is building) |

**The readiness check.** A host is not ready while any index has `NeedsInitialization != None`; on a catalog opened with `OpenReadOnly`
it asks `NeedsInitialization.HasFlag(LuiInitializationNeeds.TypoDictionary)`:

```csharp
bool Ready(LuiCatalog catalog) => catalog.Indexes().All(index => index.NeedsInitialization == LuiInitializationNeeds.None);
```

A linked index that an application keeps **without triggers** by choice is never ready by this check: it names `Triggers` for ever, and
`Initialize()` builds no dictionary for it. Such an application leaves the flag out of its check.

### The host pattern in short

The factory of `AddLui` only **opens** the catalog. Creating the indexes and initializing them are explicit steps after `Build()` — so every
configuration error (an invalid definition, a mismatch, an unregistered payload type, a missing table of a linked index) surfaces at a line of
`Program.cs`, not inside a service resolution, and a failing factory — which the container does not cache — never leaves an open catalog behind.
The documented default for a container is to initialize **in the background**: the host comes up, the readiness check says "not ready" until
`Initialize()` is done, and a long first sync does not fail a liveness probe and restart the pod. While an index is not initialized the
search answers without corrections, and a linked index shows the batches synced so far. No hosted service of LUI calls `Initialize()` — the
application's own does (see [the hosting package](#the-hosting-package)).

---

## Several processes on one file

A catalog is meant to be a singleton of one process, and the file may be open in several processes — a web application and a
worker, an application and a tool of its operator. The catalogs of the processes share the file and nothing else. What each of
them holds in memory is **definitions and runtimes only** ([ADR-0075](../.agents/adr/0075-the-catalog-holds-the-definitions-and-the-runtime-of-an-index.md)):
the stored definitions of the indexes, read from `lui_meta` when the catalog was opened, and the runtime of each index — its
statements, its query runner — built once. **Content and state are never cached**: what another process writes is visible at once.

- **An index that another process created** is found when the catalog meets a name it does not know: it reads `lui_meta` again, once.
  A name that is still missing is remembered until the file's schema changes, so asking for it again is cheap.
- **An index that another process dropped, or created again.** Every operation of a handle asks the file whether its definition is
  still the one it was built from — one statement, `PRAGMA schema_version`, on the connection the operation uses, before its first
  statement. That is **SQLite's schema counter**: a number in the header of the file that changes whenever a table, an index or a
  trigger is created or dropped, and not when content is written. (It is not the *LUI format version* of `lui_meta.schema_version`,
  which says in which format an index was written.) When the counter changed, the catalog reads the definitions again and the handle
  - throws a `LuiIndexNotFoundException` when its index is gone,
  - throws a `LuiDefinitionMismatchException` — naming the fields — when the index is there with another definition,
  - goes on when it is the same definition. A change of the application's own tables only costs a harmless reload.

  The check and the work are one step: a write holds the write lock from the check to its commit, a read checks in its own
  snapshot — so an index that another process drops and creates again at that very moment is never written through the old
  definition. A handle that threw keeps throwing; a new `Create(definition)` or `Index(definition)` call gives a new one.
- **Two processes that create the same index at startup.** `Create` takes the write lock, reads `lui_meta` inside the transaction
  and then creates the index, or opens the one the other process just created: the same definition succeeds in both and leaves one
  index, another definition is a `LuiDefinitionMismatchException` in the one that comes second.
- **Writes from several processes** work as they do from several threads: one writer at a time, and LUI waits for the lock for the
  busy timeout of the catalog ([`LuiPragmas.BusyTimeout`](#luipragmas)).
- **The typo dictionary of a process learns only its own writes.** The writes of other processes reach it with
  `RebuildTypoDictionary()`.

---

## Creating an index — the description

The builder chain is the **parameter** of an index definition (`new LuiIndexDefinition<long>("tags", index => index.SingleWord())`), not the way in:
the builders **describe**, the catalog creates (ADR-0073). A builder holds no catalog and no name. Every builder method
returns the builder, so the chain reads as one sentence.

### `LuiIndexKindBuilder` — the kind of the index

| Member | What it does |
|---|---|
| `LuiEmbeddedIndexBuilder SingleWord()` | the text is one unit: a tag, a word, a compound, a hashtag |
| `LuiEmbeddedIndexBuilder MultiWord()` | the text is many words: a name, a title. The words are ANDed — the order and the spaces do not matter. |

### `LuiEmbeddedIndexBuilder`

| Member | What it does |
|---|---|
| `LuiLinkedIndexBuilder LinkTo(string table, string key, string text, string? weight = null, string? priority = null)` | turns the description into a linked one: the table of the application, its `INTEGER PRIMARY KEY`, the column with the text, and — optional — the columns with the weight and the priority |
| `LuiEmbeddedIndexBuilder Similarity(ILuiSimilarity similarity)` | the measure of the ranking. The default of the kind is used when it is left out. |
| `LuiEmbeddedIndexBuilder GermanTransliteration(bool enabled)` | `ä` → `ae`, `ü` → `ue`, `ß` → `ss`. On by default. |
| `LuiEmbeddedIndexBuilder TypoTolerance(LuiTypoDistance distance = LuiTypoDistance.OneError)` | how many errors are forgiven: `None`, `OneError` or `TwoErrors`. Off by default — the dictionary of the word beginnings costs 194 MB for 685,739 words with one error (the list of the repository has 685,829 words; the measurement uses the ones of three letters or more, without a digit), and more with two ([`docs/typo-dictionary.md`](./typo-dictionary.md)). The distance is part of the stored definition: an index that is opened again forgives what it forgave before. |

### `LuiLinkedIndexBuilder`

Everything of the embedded builder except `LinkTo` — a linked index takes the keys of its table — plus:

| Member | What it does |
|---|---|
| `LuiLinkedIndexBuilder<TPayload> WithPayload<TPayload>(IReadOnlyList<string> columns, Func<LuiPayloadRow, TPayload> map)` | the columns of the payload and the map that builds it from the row; `where TPayload : notnull`. The columns are read live; the map is code of the application, typed end to end. **It returns the typed builder** (not derived from this one): only a `LuiLinkedIndexDefinition<TKey, TPayload>` with the same `TPayload` takes it, a `LuiLinkedIndexDefinition<TKey>` does not. |

### `LuiLinkedIndexBuilder<TPayload>`

What `WithPayload` returns. Sealed, `where TPayload : notnull`, and it does **not** derive from `LuiLinkedIndexBuilder`; it has the
same option methods, so the order of `WithPayload` and the options is free. It describes and holds no catalog; it has no `Create()`.

| Member | What it does |
|---|---|
| `LuiLinkedIndexBuilder<TPayload> Similarity(ILuiSimilarity similarity)` | the measure of the ranking — runtime only |
| `LuiLinkedIndexBuilder<TPayload> GermanTransliteration(bool enabled)` | `ä` → `ae`, `ü` → `ue`, `ß` → `ss`. On by default. |
| `LuiLinkedIndexBuilder<TPayload> TypoTolerance(LuiTypoDistance distance = LuiTypoDistance.OneError)` | how many errors are forgiven |

---

## Writing — the entry and the key

An entry is `LuiEntry<TKey>` — or `LuiEntry<TKey, TPayload>` for a definition with a payload type — and its key is a `TKey`: `long`,
`int`, `string` or `Guid`, **stored exactly** (ADR-0073). There is no key type of its own, no implicit conversion and no entry without
a type argument: `1` and `"1"` are different keys because they have different types. The members are in
[the typed handles](#the-typed-handles--what-a-definition-opens).

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
| `LuiQuery Similarity(ILuiSimilarity similarity)` | the measure of the ranking, for this query |
| `LuiQuery TypoTolerance(bool enabled = true)` | forgives a typo in this query — but only within the distance of the index: a query cannot widen it, and a distance of `None` forgives nothing |
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
| `ILuiSimilarity? Similarity` | the default of the kind of the index |
| `bool? TypoTolerance` | `null` — the distance of the index |
| `bool TrackTimings` | `false` |

Every value checks itself: a `MaxResults` of 0 or a `Rank` that is not defined throws `ArgumentOutOfRangeException`
before the query runs.

**One hit per entry — the uniqueness of a text belongs to the data.** Two entries that hold the same text in two
spellings — "vegan" and "Vegan" — are two hits: the library does not hide the second one, because it cannot know
which of them the application means. Where the uniqueness is stated depends on the kind of the index (ADR-0055):

- **`Embedded`** — the entries live in the table of the library, and that table holds the **transliterated** text in
  the generated column `s_norm` — case and diacritics untouched. The **folded** text, the one the tokenizer compares,
  lives inside the search index, and the **search form** of the C# side is computed by an internal helper: the fold is
  not part of the public API. A constraint over `s_norm` does not catch "vegan" and "Vegan", and `lower(s_norm)` is an
  ASCII approximation of the fold, not the fold — see [the normalisation](normalization.md).
- **`Linked`** — the table is the application's own, and so is the constraint: over the application's own column, with
  the application's own rule. The same caveat holds, because the library builds its search index over the raw column
  of the table.

In both cases the rule is the application's own: **the application keeps its own text unique, in its own data, over
its own column, with its own rule** — a rule that is not the fold of the library.

### `LuiResult`

| Member | What it is |
|---|---|
| `string Query { get; init; }` | the query that was run |
| `IReadOnlyList<LuiHit> Hits { get; init; }` | the hits, in the order of the ranking |
| `int CandidatesConsidered { get; init; }` | how many candidates the query looked at |
| `bool Truncated { get; init; }` | whether more candidates were found than the query took |
| `string? CorrectedQuery { get; init; }` | the query after the correction of a typo — `null` when there was none |
| `bool CorrectionSkipped { get; init; }` | whether a correction was skipped because the dictionary was not built — a query never builds it (ADR-0042) — or is being built |
| `LuiTimings? Timings { get; init; }` | the phases of the query, when `TrackTimings` was on |

### `LuiHit`

One hit. It is a `readonly record struct` — no allocation per hit.

| Member | What it is |
|---|---|
| `TKey Key { get; init; }` | the key of the entry |
| `string Text { get; init; }` | the text of the entry |
| `int Weight { get; init; }` | the weight of the entry |
| `LuiPriority Priority { get; init; }` | the priority of the entry |
| `double Similarity { get; init; }` | how near the text was to the query, 0 to 1 |
| `bool AtWordStart { get; init; }` | whether the query matched at the beginning of a word |

**The handle knows the types, the index layer does not** (ADR-0035, ADR-0073): the key and the payload type are the type arguments of the
definition, and the file stores text and bytes. The payload of a hit is `hit.Payload`. A payload that cannot be read
in its type is a `LuiPayloadException`.

### `LuiTimings`

The phases of a query: `CandidateRead`, `Correction`, `Ranking`, `Payload` and `Total` — each a `TimeSpan`.

### The payload in the type of the application

A handle with a payload type — `catalog.Index(definition)` of a `LuiIndexDefinition<TKey, TPayload>` — answers in the type of the
application: `hit.Payload` is a plain value of `TPayload`, loaded with the query (ADR-0074). There is no view over an index and no
`As<T>()` (they were removed in 0.4.0, ADR-0073): the definition names the type once, and the compiler knows it everywhere.

```csharp
foreach (var hit in catalog.Index(Chips).Search("veg").Hits)
{
    Console.WriteLine(hit.Payload.Colour);   // TagChip — the compiler knows it
}
```

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
| `int BatchSize` | 10,000 (1 to 1,000,000, `LuiSyncOptions.MaxBatchSize`) | how many entries one transaction holds — for a linked index, how many **rows of the table** |
| `IProgress<LuiSyncReport>? Progress` | none | the progress of the load — what the sample shows. A linked sync tells it `Read` (rows of the table read so far), `Batches` and `Removed`, and at its very end `Merged = true`; it has no `Added`, `Changed` or `Unchanged` — it does not compare. Told on the thread of the sync, outside of every lock. |

### `LuiSyncReport`

| Member | What it is |
|---|---|
| `long Read` | how many entries were read |
| `long Added` / `long Changed` / `long Unchanged` / `long Removed` | what happened to them |
| `int Batches` | how many transactions it took |
| `bool Merged` | whether the index was merged into the existing one instead of being replaced |

### `LuiLinkedSyncReport`

The report of a `Sync` of a linked index:

| Member | What it is |
|---|---|
| `long Indexed` | the entries in the index when the sync is through |
| `long WithoutText` | the rows with no text — they are not indexed |
| `long KeyOutOfRange` | the rows whose key cannot be the key of the index: outside `0 … 4,294,967,295`, and for an `int` index outside `0 … 2,147,483,647` |
| `int Batches` | how many batches of rows of the table were written, each in a transaction of its own (an empty table has none) |
| `long Removed` | how many entries were removed at the end because their row is not in the table any more |
| `TimeSpan LongestLock` | the longest time the sync held the write lock of the file in one transaction — a batch, a step of the removal or of the merge, the lease — the checkpoint before it plus the time from its begin to its commit; the time the sync waited for the lock of the application to begin is not counted. Another writer waits for the lock at most this long per transaction — plus the batches that follow each other: a writer can starve (see below) |

### The sync of a linked index

`Sync` of a linked index (ADR-0077, extending ADR-0016) works in batches, like the sync of an embedded index, and merges at the end:

- **Batches.** The rows of the table are read in the order of their key, `BatchSize` at a time; each batch is indexed — added or replaced by
  key — and committed in a transaction of its own. Between two batches another writer can take the file. There is **no clear at the start**:
  a search during the sync sees the old state plus the batches committed so far, never an empty index (except on the very first sync).
  **A linked sync is not atomic**: while it runs, the index is partly old and partly new.
- **Removal by generation.** Every sync takes the next generation number (`lui_meta.sync_generation`), and every row a batch writes carries
  it (`gen` of LUI's table of the index). When every batch is written, the entries that still carry an older generation — the rows that are
  in the index and not in the table any more — are removed, in batches of `BatchSize`, each in a transaction of its own. Until then nothing
  of the previous state goes. A row that the application deletes *during* the sync, in a range that is done, is removed by the triggers'
  outbox (`ApplyPending()`) or by the next sync: **create the triggers before the first sync** — what `Initialize()` does by its order.
- **The merge, in steps.** FTS5's automatic merge is off while a batch loads and back on before its commit. At the end the search index is
  merged by FTS5's `merge` command in steps of 4,096 pages, each in a transaction of its own, until a step reports that nothing is left —
  not by one `optimize`, which over a large index is one long write transaction. The transaction of the last step clears
  `NeedsInitialSync`. A built typo dictionary is rebuilt once, after it.
- **One sync at a time.** A sync takes a **lease** on its index — the owner and the time it expires (60 seconds, in UTC, from the clock of
  the catalog) in `lui_meta` — and renews it in every transaction. A second `Sync()` of the same index — in this process or another —
  while the lease is valid throws a `LuiSyncInProgressException` that names the owner and the expiry; it does not wait for the lease. (A sync that cannot get the **write lock of the file** within the busy timeout — a writer of the application that holds it — fails like any write of LUI with the `SqliteException` `SQLITE_BUSY` of ADR-0046; run it again.) The lease of a
  process that stopped expires, and the next sync takes it over; a sync that finds at its next batch that it lost its lease stops with the
  same exception and writes nothing more.
- **Cancellation and failure.** A sync that is cancelled or fails keeps the batches it committed, removes nothing that was not removed yet,
  does not finish the merge, leaves `NeedsInitialSync` as it was, and releases its lease. Running it again is always correct.
- **The cost, and what is not promised.** Other writers wait for the file for one transaction of the sync at a time — `LongestLock` of the
  report says how long that was at most — instead of for the whole sync, and the write-ahead log grows by one transaction at a time and not by the whole index (a few tens of megabytes at a million rows: 42 MB for a database of 322 MB, measured; a reader of the application that holds a snapshot for long still stops the log from starting over). **A writer can still
  starve.** SQLite has no queue for its one write lock: a waiting writer polls (at most every 100 ms), and the sync takes the lock again
  within microseconds of a commit, so beside a very large sync (hundreds of thousands of rows or more) and a busy application a write can wait
  a long time — in three million-row runs a writer with a busy timeout of twice a batch (2 s) had 4 of 38, 0 of 45 and 6 of 35 writes fail with `SQLITE_BUSY` (the longest wait of a write that
  succeeded: 1.99 s, 1.96 s and 1.82 s; in the third run, which also had a writer with a timeout of 30 s, that one was never refused). LUI adds **no pause between the transactions** (ADR-0077 records why). What to do: for a **very large table** run the
  **first sync before the application accepts writes** (a small or medium table can be initialized in the background — see [the host pattern](#the-host-pattern)); give the application's writers a **generous busy timeout** (30 seconds, set explicitly — `Default Timeout=30` in the connection string of a `Microsoft.Data.Sqlite` connection; measured, a plain connection waits 30 s on its own,
  but a setting that is not stated is not controlled; LUI's own connections wait `LuiPragmas.BusyTimeout`, five seconds by default) and **retry** a write that fails with `SQLITE_BUSY`; open the application's own write transactions with `BEGIN IMMEDIATE`
  (`connection.BeginTransaction(deferred: false)`), so that a busy file is found at their start. The README has the same advice, with the code.

### `LuiIntegrityReport`

The report of a `Check`: `Problems` (the list of what is wrong), `IsHealthy`, `Missing`, `Extra` and `Different`.

---

## The payload

### `LuiPayloadRegistry`

Maps **payload types** to the serializers that write and read them (ADR-0072). The serializer is bound to the type, not to the
catalog: one catalog holds an index of JSON payloads next to an index of MessagePack payloads, each read with the serializer of its own
type. Set it in `LuiCatalogOptions.Payloads`; the registry is **frozen when the catalog is opened** (a later call changes nothing for
that catalog), and registration is safe from many threads.

```csharp
var options = new LuiCatalogOptions
{
    Payloads = new LuiPayloadRegistry()
        .Use(AppJsonContext.Default)                       // every type the context lists, as strict JSON
        .Use<Attachment>(new MessagePackAttachmentSerializer()),   // this type, in another format
};
```

| Member | What it does |
|---|---|
| `LuiPayloadRegistry()` | an empty registry |
| `LuiPayloadRegistry Use(JsonSerializerContext context)` | registers a source-generated JSON context: every payload type it lists is written and read as **strict JSON** with the format `json-strict-v1`. A context cannot list its types, so a type is looked up in the contexts when an index needs it. The same context twice is one registration. Returns the registry, to chain. |
| `LuiPayloadRegistry Use<TPayload>(ILuiPayloadSerializer<TPayload> serializer)` | registers a serializer for **exactly one type** — the way to use any other format (MessagePack, protocol buffers, reflection-based `JsonSerializerOptions`, a format of the application). It wins over the contexts. `TPayload` is `where TPayload : notnull`; the `Format` of the serializer is read now. |

**No silent precedence, no catch-all:**

- a `Use<TPayload>` registration **wins** over the contexts — it is an explicit choice;
- the same type registered twice with `Use<TPayload>` is a `LuiConfigurationException` **at the registration**;
- **two contexts that both know a type** an index needs are a `LuiConfigurationException` when the index is created or opened, naming
  both contexts — the order of the `Use` calls never decides;
- there is no way to register one serializer for every type.

A context also knows the types its listed types are made of (`int`, `string`): they need no entry of their own. **A payload type is
one concrete type**: `object`, an interface and an abstract class are refused — a `LuiConfigurationException` naming the definition at
`Create`/`Index` (embedded and linked), and at `Use<TPayload>` — because an index of one would hold the payloads of any type. Declare a
sealed class or record, or a sealed envelope that holds the variants.

### `LuiPayloadSerializer`

| Member | What it does |
|---|---|
| `static ILuiPayloadSerializer<TPayload> Json<TPayload>(JsonSerializerContext context)` | the strict JSON serializer of **one payload type**, the one `Use(context)` gives, with the format `json-strict-v1`. A type that is not in the context is a `LuiConfigurationException` that names both. `TPayload` is `where TPayload : notnull`. |

Both read **strictly**: a member in the bytes that the type does not have, or a parameter of its constructor that the bytes do not
have, is a `JsonException` — not an object filled with defaults.

### `ILuiPayloadSerializer<TPayload>`

The serializer of **one payload type** (ADR-0072): `string Format { get; }`, `byte[] Serialize(TPayload payload)` and
`TPayload Deserialize(ReadOnlySpan<byte> bytes)`. `Format` is a name — `json-strict-v1`, `msgpack-v1` — that an embedded index
stores and compares when it is opened; **a serializer that changes its wire format must change its `Format`**, LUI compares the name,
not the bytes. An implementation is used from many threads at once. There is no untyped serializer and no
catch-all.

### `LuiPayloadNameAttribute`

`[LuiPayloadName("recipe")]` on a class, struct, interface or enum gives the payload type the name an embedded index stores for it.

| Member | What it does |
|---|---|
| `LuiPayloadNameAttribute(string name)` | the name; not empty |
| `string Name { get; }` | the name |

**Put it on every payload type.** Without it LUI computes a stable name from the namespace and the class, so renaming the class or its
namespace is a changed definition — and for an embedded index that means drop and fill again. With it the class can be renamed and
moved, and the stored name stays. **Two different types with the same name look the same to LUI**: a duplicate is not detected across
types. On a generic payload type the attribute keeps the type arguments after its name (`wrapped<MyApp.Tag>`).

### The fingerprint of a payload

An **embedded** index with a payload stores two fields next to its definition in `lui_meta`, and compares both whenever it is created
or opened (part of LUI format 2):

| Field | What it is |
|---|---|
| `payloadTypeName` | the value of `[LuiPayloadName]`, or the **stable name that LUI computes** — without assembly name, version or key |
| `payloadFormat` | the `Format` of the serializer that writes the payloads |

The computed name of a non-generic type is its namespace-qualified name (`MyApp.Recipes.RecipePayload`, nested types `Outer+Inner`); of
a generic type the namespace-qualified name without the arity and with the names of its arguments
(`MyApp.Envelope<MyApp.Recipes.RecipePayload>`, `System.Collections.Generic.Dictionary<System.String,System.Int32>`); of an array the
name of its element type with `[]` (`[,]` for two dimensions). The name is the same in two builds with different assembly versions.

- **Another type** is a `LuiDefinitionMismatchException`: `payload type: stored 'recipe', declared 'MyApp.Other'` — with the hint to
  use `[LuiPayloadName]` when the two names differ only in the namespace.
- **The same type in another format** is one, too: `the payloads of 'recipes' were written in format 'json-strict-v1', the registered
  serializer writes 'msgpack-v1'`.
- **One index, one kind of definition**: an index stored with a payload type opens only with a `LuiIndexDefinition<TKey, TPayload>`, an
  index stored without one only with a `LuiIndexDefinition<TKey>` — otherwise a mismatch that says which to use.
- A **linked** index stores neither field and needs no registry entry: the map of its definition builds the payload from the columns.
- Only the **name** of the format is stored — not the settings of the serializer. The fingerprint checks **identity, not shape**: adding,
  renaming or removing a field of the payload is the business of the serializer and the application — see
  [changing a payload type](#changing-a-payload-type).

**Errors.** A configuration that is wrong is a `LuiConfigurationException`, never a `LuiPayloadException`: a payload type that no
registration knows, that two contexts know, a catalog without a registry — thrown by `Create` and `Index`, with the name of the
definition, before anything is written —, a type registered
twice. `LuiPayloadException` stays for a payload that cannot be read. There is no untyped payload: an
entry of an index without a declared payload type cannot carry one.

### Changing a payload type

**In 0.4.0 an unreadable payload fails the query** — upgrading payloads (tolerant reading by LUI, skipping unreadable payloads, upgrade
functions, versioned payloads) belongs to the migration topic of a later release. With `LuiPayloadSerializer.Json`:

- **Removing or renaming a field** of the payload type makes the stored payloads of the old shape unreadable, and **every query whose result
  contains such an entry fails — the search of that index is effectively down until the entries are rewritten.** The index opens (the
  fingerprint is the same name and format), `CheckPayloads()` lists the entries still missing.
- **Adding a field with a default value** keeps the old payloads readable (`record Dish(string Title, int Minutes, double Rating = 0)`); a
  field without a default is a required constructor parameter, and the old payloads do not read.

**How long the window is** — the rewrite runs at the rate of `UpsertMany`, 26,052 entries per second on the reference machine
([the batch](benchmarks.md#the-batch)), so **duration ≈ entries ÷ 26,052/s: about 40 seconds per million entries**. Loading a whole large
index is slower (1,000,000 entries took 87 s in [the build](benchmarks.md#the-build)), and a payload adds to every write; plan with
the slower figure — **about 1½ minutes per million entries** — and measure on your own file.

The two ways:

1. **Without a window — a new index.** Declare the new payload type in a new definition (`recipes_v2`, another name), fill it from the
   source with `Sync`, switch the application to it, drop the old one (`catalog.DropIndex("recipes")`) — the recipe for a changed
   definition (see [changing a definition](#changing-a-definition)). The old index answers until the switch; no query fails.
2. **In place.** Deploy the new payload type and rewrite every entry with `Upsert`, `UpsertMany` or `Sync` — `CheckPayloads()` lists the
   ones still missing, and is healthy when the last one is written. Until then every query that meets an old entry throws a
   `LuiPayloadException`.

### `LuiPayloadRow`

The row of the table of the application, as the map of a linked index sees it. It knows the columns of the definition —
and only those.

| Member | What it does |
|---|---|
| `string GetString(string column)` / `long GetInt64(string column)` / `double GetDouble(string column)` / `T Get<T>(string column)` | reads a column |
| `bool IsNull(string column)` | whether the column is null |

The conversion is Microsoft.Data.Sqlite's. A `NULL`:
- `Get<T>` gives `null` when `T` can hold it — a nullable value type (`Get<long?>`) or a reference type (`Get<string>`);
- `Get<T>` throws an `InvalidOperationException` when `T` cannot (`Get<long>`): a missing value read as `0` would be a
  wrong value without a word ([ADR-0052](../.agents/adr/0052-a-null-column-is-null-for-a-type-that-can-hold-it.md));
- `GetString`, `GetInt64` and `GetDouble` throw: ask `IsNull` first.

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
| `LuiInitializationNeeds` | `None`, `Triggers`, `StaleTriggers`, `InitialSync`, `InitialSyncRunningElsewhere`, `TypoDictionary` | what an index still needs at the start of a process (flags) — [Initializing](#initializing--the-start-of-a-process) |
| `LuiInitializeStep` | `ReadState`, `Triggers`, `InitialSync`, `Outbox`, `TypoDictionary` | a step of the call `catalog.Initialize`, in the order it takes them; `ReadState` only names where a failure happened |
| `LuiKeyType` | `Long`, `Int`, `String`, `Guid` | the exact type of the keys of an index, as the stored definition names it (long, int, string, guid); the exception for a damaged key carries it |
| `LuiPriority` | `System`, `High`, `Elevated`, `Normal` | the priority of an entry |
| `LuiPriorityMode` | `First`, `CandidatesOnly` | how the priority enters the ranking |
| `LuiRankingOrder` | `WordStartThenSimilarity`, `WordStartThenWeight`, `SimilarityOnly`, `WeightOnly` | the order of the hits |
| `LuiTriggerState` | `None`, `Current`, `Stale` | the state of the triggers of a linked index |
| `LuiTypoDictionaryState` | `NotBuilt`, `Building`, `Current` | the state of the dictionary of the typos |
| `LuiTypoDistance` | `None`, `OneError`, `TwoErrors` | how many errors an index forgives. An enumeration and not a number: the dictionary of the word beginnings grows with every additional error, so the distances are the ones that were measured ([`docs/typo-dictionary.md`](./typo-dictionary.md)) |

---

## The exceptions

All of them inherit from `LuiException`, which inherits from `InvalidOperationException` — one `catch` for the whole
library.

| Type | When it is thrown |
|---|---|
| `LuiException` | the base of them all |
| `LuiEnvironmentException` | the environment is not what the library needs — the SQLite is too old, or it folds differently than the one the library was measured with |
| `LuiDefinitionMismatchException` | the index in the file was written with another definition, another LUI format (an index that changes under an open catalog — a file that is opened with another format is rejected at once, with the exception for a wrong format), another key type, another folding, another payload type or another payload format. It carries `IndexName`, `Stored` and `Requested`; for a definition, the message names the fields that differ and the way to change a definition. |
| `LuiIndexNotFoundException` | there is no index of that name in the database — or the handle belongs to an index that was dropped. It carries `IndexName`. |
| `LuiConfigurationException` | the configuration is wrong: an index definition that is not valid (a name that is no name, a key type that is not allowed, a step the definition does not know) — thrown on the **first use** of the definition by a catalog, never in the initializer of its field —, or the payload registry (a payload type that no registration knows or that two contexts know, a type registered twice). The message names the definition. An invalid argument of a call stays an argument error. |
| `LuiCatalogNotFoundException` | there is no catalog at the path: the file is missing, it is no SQLite database, or it has no lui_meta table. The ways to open a catalog that is there do not create one; the way that creates one refuses a file that is no SQLite database. It carries `Path`. |
| `LuiLinkedTableException` | the table of the application is missing, or a column of it, or its key is not the `INTEGER PRIMARY KEY` |
| `LuiFormatException` | the file was written in another LUI format than this version reads — thrown when the catalog is **opened** (Open, OpenReadOnly and OpenOrCreate of a file that has indexes). Format 2 is the format of 0.4.0; a file of 0.3.0 has format 1 and is not read (no migration): "The file 'app.db' was written by LUI 0.3.0 (format 1); this version reads format 2. Recreate the indexes." A newer format is rejected the same way. It carries `Path`, `FoundFormat` and `SupportedFormat`. |
| `LuiKeyException` | a stored key does not convert to the key type of its definition — a damaged file, not an expected state (LUI only writes keys that convert, so there is no check to run). The message names the index and the stored key. It carries `IndexName`, `StoredKey` and `KeyType`. |
| `LuiPayloadException` | a payload cannot be read in the type asked for — and, for a handle with a payload, a query meets a payload that is missing, that the serializer cannot read, or that a map did not build (it threw, or it returned null): the query fails and the message names the index, the key and the type. It carries `IndexName`, `Key` and `RequestedType`; the exception of the serializer or the map is the inner one. |
| `LuiSyncInProgressException` | another sync of the linked index is running — or this one lost its lease (it did not renew it in time and another sync took it over, or the index was dropped and created again), and stops before it writes another batch. Thrown at once, the sync does not wait. It carries `IndexName`, `Owner` and `ExpiresAt` (UTC) of the lease — the last two are not set (null) when nobody holds it now. |
| `LuiInitializeException` | one or more indexes failed in `catalog.Initialize` — after every other index was initialized. It carries the complete `Report` and the `Errors` by index name; the message names every failed index and the step. See [Initializing](#initializing--the-start-of-a-process). |
| `LuiReadOnlyException` | SQLite refused a write because the file may not be written. The message names the reason — the catalog was opened read-only, or the file and its directory may not be written — the operation and the index; the error of SQLite is the inner exception. It carries `Operation`. |

---

## The hosting package

`Nordmann.LUI.Hosting` carries the dependency injection — and, only if the application asks for them, the **outbox loop**
(`AddLuiOutbox`, ADR-0076). It is one service of the host and tells the logger of the host what it did and what failed (ADR-0045). The start of the search is
`catalog.Initialize()` — the application's own call (ADR-0076); the warm-up service of `0.3.0` (`AddLuiWarmup`) is gone.

### `LuiServiceCollectionExtensions`

| Member | What it does |
|---|---|
| `IServiceCollection AddLui(this IServiceCollection services, Func<IServiceProvider, LuiCatalog> open)` | puts the **catalog** into the container: it opens it when it is first needed and disposes it with the container. The way the catalog is opened is the caller's — `services.AddLui(_ => LuiCatalog.OpenOrCreate("app.db"))` (ADR-0066). Once per container: a second call throws. |
| `IServiceCollection AddLuiOutbox(this IServiceCollection services, TimeSpan interval)` | registers **the one background loop LUI needs** (ADR-0076): it calls `catalog.ApplyPending()` every `interval` and stops with the host. It does **nothing at the start** — that is `catalog.Initialize()`, which the application calls; LUI offers **no hosted service that calls `Initialize()`**. Only with linked indexes that have triggers. Once per container: a second call, with either overload, throws. |
| `IServiceCollection AddLuiOutbox(this IServiceCollection services, Action<LuiOutboxOptions> configure)` | the same with the settings (`Interval`, `MaxBackoff`). **A failing round never stops the host**: it is logged through the host's `ILogger` (a warning, with the exception), the next round waits with exponential backoff up to `MaxBackoff`, and the first round that works goes back to `Interval`. **On a catalog opened with `OpenReadOnly` the host does not start**: `InvalidOperationException` — "a read-only catalog cannot apply the outbox". |

### `LuiOutboxOptions`

| Member | Default | What it says |
|---|---|---|
| `TimeSpan Interval { get; set; }` | 30 s | the time between two rounds that worked; positive, at most 24 days (a longer wait is refused at the registration, with a message that names `LuiOutboxOptions.Interval`) |
| `TimeSpan MaxBackoff { get; set; }` | 5 min | the longest wait while rounds fail: the wait doubles after each failed round — `Interval` × 2, × 4, … — up to this; positive, at most 24 days. A value below `Interval` is **not an error**: it is the interval — the wait never shrinks below it |

```csharp
builder.Services.AddLuiOutbox(TimeSpan.FromSeconds(10));                       // the interval alone
builder.Services.AddLuiOutbox(options => options.MaxBackoff = TimeSpan.FromMinutes(1));
```

**It competes with a long first sync for the write lock**: while the sync holds it, rounds can fail and are logged as warnings, and the backoff can reach `MaxBackoff` — with a large table choose a longer `Interval`, or register the loop after the first sync. **Every process with the registration runs its own loop.** That is safe — the outbox is applied by one writer at a time (ADR-0046), and a second loop only
finds less to do. A round that fails tells the logger of the host (`Warning`, with the exception, how many rounds in a row failed and when the next one is);
the first round that works after failures says so (`Information`); the rows an ordinary round applied are a `Debug` line. The loop is a task of the service: the host
that stops it waits for a round that runs.

### The host pattern

The factory of `AddLui` only **opens** the catalog; creating and initializing the indexes are explicit steps after `Build()`, so that a configuration error stops the start
at a line of `Program.cs` (see [Initializing](#initializing--the-start-of-a-process)):

```csharp
builder.Services.AddLui(_ => LuiCatalog.OpenOrCreate("app.db", options));    // open only
builder.Services.AddLuiOutbox(TimeSpan.FromSeconds(10));                      // only with linked indexes and triggers
builder.Services.AddHostedService<SearchInitializer>();                       // the application's own background start

var app = builder.Build();
// … the application's own schema migrations run here, before the catalog is used …

var catalog = app.Services.GetRequiredService<LuiCatalog>();
catalog.Create(SearchIndexes.Recipes, SearchIndexes.Chefs, SearchIndexes.Tags);   // fast; configuration errors stop the start here

app.MapHealthChecks("/ready");      // readiness: not ready while an index has NeedsInitialization != None
app.Run();

// The application's own service: the host starts at once, the search becomes complete when Initialize() says Ready.
sealed class SearchInitializer(LuiCatalog catalog, ILogger<SearchInitializer> logger, TimeProvider? time = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clock = time ?? TimeProvider.System;
        var pause = TimeSpan.FromSeconds(5);
        try
        {
            // Initialize() is idempotent and safe to repeat: repeat it until the report says Ready — also after a failure, which may be transient
            // (a busy file while another replica syncs), and while an index waits for a first sync that another process runs
            while (true)
            {
                try
                {
                    var report = await Task.Run(() => catalog.Initialize(cancellationToken: stoppingToken), stoppingToken);
                    if (report.Ready)
                    {
                        logger.LogInformation("Search initialized: {Report}", report);
                        return;
                    }

                    logger.LogInformation("Search is not ready yet: {Report}", report);
                    pause = TimeSpan.FromSeconds(5);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // a LuiInitializeException, or the error of SQLite itself before the first step: never let it escape — an exception that leaves
                    // ExecuteAsync stops the host. A permanent failure is logged again at every attempt
                    pause = TimeSpan.FromTicks(Math.Min(pause.Ticks * 2, TimeSpan.FromMinutes(1).Ticks));
                    logger.LogWarning(exception, "Search could not be initialized yet, trying again in {Pause}", pause);
                }

                await Task.Delay(pause, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // the host stops
        }
    }
}
```

**The rule: repeat `Initialize()` until the report says `Ready` — inside the loop, after a failure too.** It is idempotent and safe to repeat. A call can end "not ready" without a failure — another process runs the first sync (`InitialSyncRunningElsewhere`), or the lease of a killed predecessor has not run out (a minute) — and then it skips that index and its dictionary and does not wait: the next call does the rest. A call can also fail for a reason that passes (SQLITE_BUSY while another replica runs its batches): the initializer above logs the `LuiInitializeException` as a warning, waits longer each time (10 s after the first failure, doubling up to a minute; 5 s after a call that is only not ready) and calls again; a permanent failure is logged again at every attempt. An exception that escapes `ExecuteAsync` stops the host (the default of .NET), so none escapes. An initializer that calls it once, or ends at the first failure, leaves the host "not ready" for good.

**Which readiness for which process.** The loop above is for the process that **writes** the file, with the defaults. (1) A linked index kept **without triggers by choice** (`InstallTriggers = false`) never becomes `Ready`: it names `Triggers` for ever, `report.Ready` cannot be masked, and **a typo-tolerant one gets no dictionary** from `Initialize()` (it withholds it for an index with a file step left) — leave such an index out of the loop (`Indexes = […]` of the others), build its dictionary with `WarmUpTypoDictionary()` and check its readiness yourself (`NeedsInitialization & ~LuiInitializationNeeds.Triggers`). Its first sync runs **without triggers**, the order ADR-0077 says can lose a row written in between — switch the triggers off only for an index that you keep current with `Refresh` or `Sync()` yourself. (2) A process on a catalog opened with `OpenReadOnly` does not loop on `Ready`: the report names file steps only a writer can do, so one call builds its dictionaries and its readiness is "no index has `TypoDictionary`" (`!NeedsInitialization.HasFlag(LuiInitializationNeeds.TypoDictionary)`). **Start the readers after the writer is ready**: a dictionary that a reader builds from a table whose first sync is only half done is marked built and is not rebuilt by the writer's later rows (#287).

**What "not ready" means, and the size of the table.** "Not ready" is a fact about the **search**: reads are served — without corrections, and with the batches that are synced so far. It does not stop your application's **own writes**: the triggers are installed first, so a row written during the first sync is noted in the outbox and applied — none is lost. What the first sync costs the writers is the write lock: SQLite has no queue, and a sync of hundreds of thousands of rows beside a busy application can make the application's writes wait (see "Linked indexes and the sync"). So the rule is by the size of the table. **A small or medium table** (the first sync takes seconds): the background initializer above is right — the host is up at once, readers wait for "ready", writers proceed. **A very large table** (hundreds of thousands of rows or more) **with a busy application**: run `Initialize()` — or just the first sync — **before the application accepts writes**: a startup step before `app.Run()`, before the listener opens, or the writer deployed as a step of its own; or keep the background initializer and give the application's writes a generous busy timeout (30 seconds, set explicitly) and a retry on `SQLITE_BUSY`. "Ready" means for readers that the search is complete; for writers it means nothing — they never wait for it.

This background form is the **documented default** for containers: the host comes up, the readiness check says "not ready" until `Initialize()` is done, and a long first sync does not
fail a liveness probe and restart the pod. For a small application or a tool the short form is enough — `catalog.Initialize();` right before `app.Run()` — and an application
that wants it in parts says `Initialize(new LuiInitializeOptions { Indexes = [SearchIndexes.Chefs] })`. While an index is not initialized, the search answers without
corrections, and a linked index shows the batches synced so far. `IsReadOnly` tells a reader-only process which readiness it has: the dictionaries alone.

The container holds the catalog and — if the application asks for it — the outbox loop: **an index is not registered and
not injected**. The application asks the catalog for the index it needs. A second `AddLui` throws — the catalog is the
one thing of LUI in the container.

The outbox loop is a service of the host, not a thread of the library: the host starts it, stops it and disposes it
(ADR-0076). The library starts nothing on its own.

---

## The whole surface

Sixty-one public type names in `Nordmann.LUI` (seventy-one types: a generic type counts once for each number of type parameters), two in `Nordmann.LUI.Hosting`. Nothing else is public.

| Type | Kind | Where it is described |
|---|---|---|
| `LuiCatalog` | class | [the entry point](#the-catalog--the-entry-point) |
| `LuiCatalogOptions` | record | [the entry point](#the-catalog--the-entry-point) |
| `LuiPragmas` | record | [the entry point](#the-catalog--the-entry-point) |
| `LuiSynchronous` | enum | [the entry point](#the-catalog--the-entry-point) |
| `LuiIndexDefinitionBase` | class | [the definition](#defining-an-index--the-definition) |
| `LuiIndexDefinition` | class | [the definition](#defining-an-index--the-definition) |
| `LuiLinkedIndexDefinition` | class | [the definition](#defining-an-index--the-definition) |
| `LuiIndexSource` | enum | [the definition](#defining-an-index--the-definition) |
| `LuiIndexInfo` | record | [maintenance by name](#maintenance-by-name--without-a-definition-or-a-payload-type) |
| `LuiInitializeOptions` | record | [initializing](#initializing--the-start-of-a-process) |
| `LuiInitializeProgress` | record | [initializing](#initializing--the-start-of-a-process) |
| `LuiInitializeReport` | record | [initializing](#initializing--the-start-of-a-process) |
| `LuiInitializeIndexReport` | record | [initializing](#initializing--the-start-of-a-process) |
| `LuiInitializationNeeds` | enum | [initializing](#initializing--the-start-of-a-process) |
| `LuiInitializeStep` | enum | [initializing](#initializing--the-start-of-a-process) |
| `LuiIndex` | class | [the typed handles](#the-typed-handles--what-a-definition-opens) |
| `LuiEmbeddedIndex` | class | [the typed handles](#the-typed-handles--what-a-definition-opens) |
| `LuiLinkedIndex` | class | [the typed handles](#the-typed-handles--what-a-definition-opens) |
| `LuiQuery` | class | [the typed handles](#the-typed-handles--what-a-definition-opens) |
| `LuiResult` | class | [the typed handles](#the-typed-handles--what-a-definition-opens) |
| `LuiHit` | record | [the typed handles](#the-typed-handles--what-a-definition-opens) |
| `LuiIndexKindBuilder` | class | [creating an index](#creating-an-index--the-description) |
| `LuiEmbeddedIndexBuilder` | class | [creating an index](#creating-an-index--the-description) |
| `LuiLinkedIndexBuilder` | class | [creating an index](#creating-an-index--the-description) |
| `LuiKeyType` | enum | [the enumerations](#the-enumerations) |
| `LuiQuery` | class | [reading](#reading--the-query-the-result-the-hit) |
| `LuiQueryOptions` | record | [reading](#reading--the-query-the-result-the-hit) |
| `LuiResult` | record | [reading](#reading--the-query-the-result-the-hit) |
| `LuiHit` | struct | [reading](#reading--the-query-the-result-the-hit) |
| `LuiTimings` | record | [reading](#reading--the-query-the-result-the-hit) |
| `LuiSyncOptions` | record | [the bulk path](#the-bulk-path--loading-many-entries) |
| `LuiSyncReport` | record | [the bulk path](#the-bulk-path--loading-many-entries) |
| `LuiLinkedSyncReport` | record | [the bulk path](#the-bulk-path--loading-many-entries) |
| `LuiIntegrityReport` | record | [the bulk path](#the-bulk-path--loading-many-entries) |
| `LuiPayloadCheck` | class | [finding the payloads that cannot be read](#finding-the-payloads-that-cannot-be-read) |
| `LuiUnreadablePayload` | record | [finding the payloads that cannot be read](#finding-the-payloads-that-cannot-be-read) |
| `LuiPayloadSerializer` | class | [the payload](#the-payload) |
| `ILuiPayloadSerializer` | interface | [the payload](#the-payload) |
| `LuiPayloadRegistry` | class | [the payload](#the-payload) |
| `LuiPayloadNameAttribute` | class | [the payload](#the-payload) |
| `LuiPayloadRow` | class | [the payload](#the-payload) |
| `ILuiSimilarity` | interface | [the similarity](#the-similarity) |
| `LuiSimilarity` | class | [the similarity](#the-similarity) |
| `LuiIndexKind` | enum | [the enumerations](#the-enumerations) |
| `LuiPriority` | enum | [the enumerations](#the-enumerations) |
| `LuiPriorityMode` | enum | [the enumerations](#the-enumerations) |
| `LuiRankingOrder` | enum | [the enumerations](#the-enumerations) |
| `LuiTriggerState` | enum | [the enumerations](#the-enumerations) |
| `LuiTypoDictionaryState` | enum | [the enumerations](#the-enumerations) |
| `LuiTypoDistance` | enum | [the enumerations](#the-enumerations) |
| `LuiException` | class | [the exceptions](#the-exceptions) |
| `LuiEnvironmentException` | class | [the exceptions](#the-exceptions) |
| `LuiDefinitionMismatchException` | class | [the exceptions](#the-exceptions) |
| `LuiIndexNotFoundException` | class | [the exceptions](#the-exceptions) |
| `LuiFormatException` | class | [the exceptions](#the-exceptions) |
| `LuiKeyException` | class | [the exceptions](#the-exceptions) |
| `LuiCatalogNotFoundException` | class | [the exceptions](#the-exceptions) |
| `LuiLinkedTableException` | class | [the exceptions](#the-exceptions) |
| `LuiPayloadException` | class | [the exceptions](#the-exceptions) |
| `LuiReadOnlyException` | class | [the exceptions](#the-exceptions) |
| `LuiSyncInProgressException` | class | [the exceptions](#the-exceptions) |
| `LuiInitializeException` | class | [the exceptions](#the-exceptions) |
| `LuiConfigurationException` | class | [the exceptions](#the-exceptions) |
| `LuiServiceCollectionExtensions` | class | [the hosting package](#the-hosting-package) |
| `LuiOutboxOptions` | class | [the hosting package](#the-hosting-package) |

---

> The surface is checked against the code: the architecture test
> [`PublicApiDocumentationTests`](../tests/Nordmann.LUI.Architecture.Tests/PublicApiDocumentationTests.cs) reads the
> assemblies and fails when a public type is missing here — or when a name stands here that the code does not have.
