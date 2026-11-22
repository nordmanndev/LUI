# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

The public API may change until 1.0.0 ([ADR-0071](.agents/adr/0071-the-freeze-comes-with-1-0-0.md)): every break has a reason that makes the API clearer, a
minor version, a migration note and its own record, and no member is marked `[Obsolete]` before 1.0.0.

### Added

- **The index definition** ([ADR-0073](.agents/adr/0073-the-index-definition-and-the-typed-handles.md), #261): an index is
  declared once, as `LuiIndexDefinition<long>` (embedded) or `LuiLinkedIndexDefinition<long>` (linked) — typically a
  `static readonly` field — and the catalog hands out **typed handles** for it: `catalog.Create(definition)` creates the index or
  opens it (and compares the stored definition), `catalog.Index(definition)` opens one that is there, and
  `catalog.Create(a, b, c)` creates several in one transaction. The handles are concrete classes — `LuiEmbeddedIndex<long>`,
  `LuiLinkedIndex<long>`, the read side `LuiIndex<long>` — with typed `LuiEntry<long>`, `LuiQuery<long>`, `LuiResult<long>` and
  `LuiHit<long>`. New: `LuiIndexDefinitionBase`, `LuiIndexSource`, `LuiConfigurationException`. An invalid definition throws
  a `LuiConfigurationException` on its first use, not where its field is initialised; a mismatch names the fields that differ,
  and the similarity is never one of them. The API stays synchronous: `Sync(IAsyncEnumerable<LuiEntry<long>>)` blocks.

- **The payload variants of the definitions and handles** ([ADR-0073](.agents/adr/0073-the-index-definition-and-the-typed-handles.md), #269):
  `LuiIndexDefinition<long, RecipePayload>` and `LuiLinkedIndexDefinition<long, ChefView>` declare the payload type once;
  `catalog.Create(definition)` / `catalog.Index(definition)` return `LuiEmbeddedIndex<long, RecipePayload>` /
  `LuiLinkedIndex<long, ChefView>` with the typed `LuiIndex<TKey, TPayload>`, `LuiEntry<TKey, TPayload>`, `LuiQuery<TKey, TPayload>`,
  `LuiResult<TKey, TPayload>` and `LuiHit<TKey, TPayload>`. The payload comes right after the text in
  `Upsert(key, text, payload, weight, priority, transaction)`; a handle with a payload has **no write without a payload**, a `null`
  payload throws, and `entry with { … }` cannot set one to `null`. The payload is written with the serializer that the
  payload registry of the catalog holds for its type (#260, below). The hit carries it as `hit.Payload` (#262, below). The new `LuiLinkedIndexBuilder<TPayload>` has the options of the untyped builder, so `WithPayload` and the
  options can be called in any order; the map of a linked definition is code, never stored or compared, and each definition uses its own.

- **The payload registry — a serializer per payload type** ([ADR-0072](.agents/adr/0072-the-serializer-is-bound-to-the-payload-type-through-a-registry.md), #260):
  the serializer is bound to the **type** of the payload, not to the catalog, so two indexes of one file can use two formats.
  `LuiPayloadRegistry` — `Use(JsonSerializerContext)` registers every type of a source-generated context as strict JSON, `Use<TPayload>(serializer)`
  registers a serializer for exactly one type (MessagePack, protocol buffers, a format of the application) — is set in the new
  `LuiCatalogOptions.Payloads`. There is **no silent precedence and no catch-all**: `Use<T>` wins over the contexts, a type registered twice
  with it, and two contexts that both know a type an index needs, are a `LuiConfigurationException` (the second one naming both contexts), and
  a type that no registration knows is one too — thrown by `catalog.Create(definition)` and `catalog.Index(definition)`, with the name of the
  definition, before anything is written. The registry is frozen when the catalog is opened. New: `ILuiPayloadSerializer<TPayload>`
  (`Format`, `Serialize`, `Deserialize`; **a serializer that changes its wire format must change its `Format`**),
  `LuiPayloadSerializer.Json<TPayload>(context)` (the format `json-strict-v1`), `LuiPayloadRegistry` and `LuiPayloadNameAttribute`.

- **The fingerprint of an embedded payload index** (ADR-0072, #260): an embedded index with a payload stores the **name of its payload
  type** and the **format** of its serializer (`payloadTypeName`, `payloadFormat` in `lui_meta`, part of LUI format 2) and compares both when it is
  created or opened — another type, or the same type in another format, is a `LuiDefinitionMismatchException` that says what differs
  (*stored 'recipe', declared 'MyApp.Other'*; *the payloads of 'recipes' were written in format 'json-strict-v1', the registered serializer writes
  'msgpack-v1'*). The name is the value of **`[LuiPayloadName("recipe")]`**, or a stable name that LUI computes from the namespace and the class
  — without assembly name or version (`MyApp.Envelope<MyApp.Tag>`, `Outer+Inner`, `MyApp.Tag[]`): **put the attribute on every payload type**,
  because renaming the class or its namespace is otherwise a changed definition (drop and fill again). An index stored with a payload type opens
  only with a `LuiIndexDefinition<TKey, TPayload>`, and one stored without only with a `LuiIndexDefinition<TKey>`. A linked index stores neither
  field and needs no registry entry. Two different types with the same name look the same to LUI. A payload type is one concrete type:
  `object`, an interface and an abstract class are a `LuiConfigurationException` (an index of one would be a catch-all).

- **The key types `int`, `string` and `Guid`** ([ADR-0073](.agents/adr/0073-the-index-definition-and-the-typed-handles.md), #270): a definition
  takes `long`, `int`, `string` or `Guid` as its first type argument — `LuiIndexDefinition<Guid>`, `LuiLinkedIndexDefinition<int>` — and
  `hit.Key` and every key parameter of the handle have that type. An embedded index takes all four; a linked index takes `long` and `int`
  (`string` and `Guid` on a linked definition is a `LuiConfigurationException` that names #267). The stored definition names the exact key
  type (`long`, `int`, `string`, `guid`), and opening an index with another one — `int` against `long` included — is a
  `LuiDefinitionMismatchException` naming `keyType`. A `Guid` is stored in the canonical form `"D"`, lower case. A linked `int` index leaves
  the rows whose key is outside the `int` range out of `Sync`, `Refresh` and `ApplyPending` and counts them in
  `LuiLinkedSyncReport.KeyOutOfRange`. Any other key type is a `LuiConfigurationException` on first use that names the allowed types.
  New: `LuiKeyType` (`Long`, `Int`, `String`, `Guid`; `LuiKeyKind` stays until #268), `LuiKeyException` (a stored key that does not convert — a
  damaged file; it names the index and the key; there is no `CheckKeys()`, because a stored key always converts) and `LuiFormatException`.

- **`hit.Payload` — the hit carries its payload, loaded with the query** ([ADR-0074](.agents/adr/0074-the-hit-carries-its-payload.md), #262):
  `LuiHit<TKey, TPayload>.Payload` is a plain value, set when `Search` or `Execute` returns — no I/O, no serializer call, no map call, no
  exception, no cache — and it works after the catalog has been disposed. An embedded index reads the payload bytes of the hits **with the hit
  rows, in the snapshot of the query**, and deserializes them before the query returns (one call per hit); a linked index runs the map once per
  hit. **A payload that cannot be read fails the query**: a serializer that throws, a stored payload that is missing, a map that throws or
  returns `null` throw a `LuiPayloadException` naming the index, the key and the type — one broken payload fails the whole query instead of
  returning a partial result, and accessing a hit never throws. What "unreadable" means is the serializer's verdict: the strict JSON of LUI
  refuses a payload of an old shape, a tolerant serializer may read it. The cost is bounded by `MaxResults`; **"a payload that is never read
  costs nothing" (ADR-0035) no longer holds** for an index with a payload — and a hit of an index **without** a payload type has none and
  its query selects no payload column. The untyped `LuiHit` keeps `GetPayload<T>()` until #268.

- **`CheckPayloads()` — finding the payloads that cannot be read** (ADR-0074, #262): `LuiEmbeddedIndex<TKey, TPayload>.CheckPayloads(ct)` returns a
  `LuiPayloadCheck<TKey>` (`Unreadable`, `IsHealthy`) of `LuiUnreadablePayload<TKey>` (`Key`, `Message`): **a full read of the index** — every stored
  payload once, in one snapshot, with the guard of the handle inside it — for maintenance, not for every probe. `Check()` stays the structural
  check and reads **no payload** and calls **no map**. A linked index has no `CheckPayloads()`: its payload is not stored. **Changing a payload
  type** (with `LuiPayloadSerializer.Json`): removing or renaming a field makes the old payloads unreadable, and every query whose result holds one
  fails — **the search of that index is down until the entries are rewritten**, about 40 seconds per million entries at the rate of `UpsertMany`
  (26,052/s, `docs/benchmarks.md`), and longer for a whole large index (87 s per million in "The build"); adding a field with a default keeps the
  old payloads readable. The ways: a new index (`recipes_v2`) filled from the source and switched to — no window —, or deploy the new type and
  rewrite every entry with `Upsert`, `UpsertMany` or `Sync`, `CheckPayloads()` listing the ones still missing. Upgrading payloads belongs to the
  migration topic of a later release (`docs/api.md`, "Changing a payload type").

- **The state of a linked index — `NeedsInitialSync`** ([ADR-0077](.agents/adr/0077-the-state-of-a-linked-index.md), #272): `LuiLinkedIndex<TKey>.NeedsInitialSync` and
  `LuiLinkedIndex<TKey, TPayload>.NeedsInitialSync` are `true` from the creation of the index until its first complete `Sync()`. The flag is the new
  column `needs_initial_sync` of `lui_meta` (part of LUI format 2; the stored definition of an index does not change), it is **read from the file on every call**,
  so a second catalog sees a sync that another process completed at once, and it is cleared in the transaction that commits a complete sync — a cancelled
  or failing sync leaves it `true`. LUI does nothing on its own: the application reads the state and decides. `Create(definition)` of a linked index
  still syncs nothing and installs no trigger, and checks that the table of the application exists (a `LuiLinkedTableException` naming the index otherwise).

### Changed

- **A payload definition needs a payload registry** (#260, ADR-0072): `LuiIndexDefinition<TKey, TPayload>` is created and opened with the serializer
  that `LuiCatalogOptions.Payloads` holds for `TPayload`. It no longer uses `LuiCatalogOptions.PayloadSerializer` — a typed definition on a catalog
  that has only that is a `LuiConfigurationException` at `Create` or `Index`, which says where to set the registry —, and setting `Payloads`
  **and** `PayloadSerializer` is one when the catalog is opened. `PayloadSerializer` and the non-generic `ILuiPayloadSerializer` stay for the
  old calls until #268 removes them. With a registry set, an index that was created with the old `Create(name, …)` cannot carry a payload
  (there is no untyped payload), and `hit.GetPayload<T>()` on `catalog.Index(name)` resolves `T` through the registry — a type it does not
  know is a `LuiConfigurationException`, not a `LuiPayloadException`.

  **Migration:**

  ```csharp
  // before
  new LuiCatalogOptions { PayloadSerializer = LuiPayloadSerializer.Json(AppJsonContext.Default) }

  // after — and put [LuiPayloadName("recipe")] on every payload type
  new LuiCatalogOptions { Payloads = new LuiPayloadRegistry().Use(AppJsonContext.Default) }
  ```

- **LUI format version 2 — 0.4.0 does not read the files of 0.3.0** (#270, ADR-0073): the LUI format version (the `schema_version` of
  `lui_meta`, not SQLite's own schema counter) goes from 1 to 2, and `Open`, `OpenReadOnly` and `OpenOrCreate` of a file whose indexes
  were written in another format throw a `LuiFormatException` **when the catalog is opened**: "The file 'app.db' was written by LUI 0.3.0
  (format 1); this version reads format 2. Recreate the indexes." A file of a newer format is rejected the same way. The later changes of
  0.4.0 add to format 2 before the release. There is no migration.

- The stored definition of an index names its key type exactly: an embedded index with `long` or `string` keys is stored as before, and
  `int` and `guid` are new names. The word of the mismatch about the version of an index is now "LUI format", not "schema version".

- **`LuiLinkedIndexBuilder.WithPayload<TPayload>` returns `LuiLinkedIndexBuilder<TPayload>`** (it returned the untyped
  `LuiLinkedIndexBuilder`), and `TPayload` is `where TPayload : notnull`. The map stays typed end to end: the cast per hit and the
  "the map built an 'X'" error of the payload path are gone for a definition. A `LuiLinkedIndexDefinition<long>` (no payload) no
  longer takes a builder with `WithPayload`, and it is a compile error rather than a `LuiConfigurationException`. The old
  `catalog.CreateLinked(name, index => index.….WithPayload(…))` keeps compiling: the new `CreateLinked<TPayload>(string,
  Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>>)` takes the typed builder and goes through `Create(definition)`
  (#268 removes it with the other old calls). Only a call that kept the result of `WithPayload` in a variable of the type
  `LuiLinkedIndexBuilder` has to change its type.

- **The builders describe, the catalog creates.** `LuiIndexKindBuilder`, `LuiEmbeddedIndexBuilder` and `LuiLinkedIndexBuilder`
  hold no catalog and no name any more. Their public `Create()` — documented as "the catalog calls this; an application does
  not" — now throws an `InvalidOperationException`; it never created anything on its own.

- The message of a `LuiLinkedTableException` from the old `CreateLinked` path now starts with `Index '<name>': `, and the original
  exception is its inner exception (ADR-0073: every creation names the index that failed). The type is unchanged.

- **The catalog holds the definitions and the runtime of each index; a handle is thin** ([ADR-0075](.agents/adr/0075-the-catalog-holds-the-definitions-and-the-runtime-of-an-index.md), #265).
  `LuiCatalog.Open`, `OpenOrCreate` and `OpenReadOnly` read every stored definition once, and the runtime of an index — its source,
  its statements, its query runner — is built once and kept. `catalog.Index(definition)` (and `Create(definition)`) returns **the same
  handle instance for the same definition object**: after the first call it allocates nothing and executes no SQL, so a request can
  take its handle where it needs it (0.3.0: 23 µs and 20 KB per open call). The definitions are the catalog's memory, the **content is
  never cached** — what another process writes is visible at once. Every operation of a handle reads SQLite's schema counter
  (`PRAGMA schema_version`) on the connection it uses, before its first statement: when another process dropped the index, the
  operation throws a `LuiIndexNotFoundException`, when it created the index again with another definition, a
  `LuiDefinitionMismatchException` — **a handle of an index that was dropped and created again under the same name no longer works on the new
  index** (0.3.0 and the first versions of 0.4.0 did; take a new handle from `Create` or `Index`). An index created again with the same
  definition is not stale. An unknown name reloads the definitions once. `Create` is atomic across processes: two that create the same
  index at once leave one index. A section on several processes on one file is on `docs/api.md`. The SQLite schema counter is not the LUI
  format version of `lui_meta.schema_version`.

- **`CreateTriggers()` is idempotent** (#272, ADR-0077): on `LuiTriggerState.None` it creates the triggers, on `Stale` it replaces them, on `Current` it
  does nothing — it never throws because triggers exist. Before, every call dropped the three triggers and created them anew, also when they were current.
  The state is read and acted on in one guarded write transaction.

### Migration

**A break the compiler does not see — read it first: files of 0.3.0 are not read.** A catalog file that holds an index written by 0.3.0
(format 1) cannot be opened by 0.4.0: `LuiCatalog.Open`, `OpenReadOnly` and `OpenOrCreate` throw a `LuiFormatException` before any index is
used — and `DropIndex` is out of reach too, because the catalog does not open. Recreate the indexes once, before the first start of 0.4.0:

1. a file that holds only LUI (a search file next to the application's database): delete it — the indexes are created again by
   `catalog.Create(definition)` and filled from their source (`UpsertMany` or `Sync` for an embedded index, `Sync()` for a linked one);
2. a catalog in the application's own database: drop the tables of LUI from a migration of the application — `lui_meta`, and the
   tables `lui_<name>`, `lui_<name>_fts` (and, for a linked index, `lui_<name>_pending`) of every index; the triggers of a linked index on
   the application's table (`lui_<name>_ai`, `lui_<name>_au`, `lui_<name>_ad`) are dropped by the same migration. The application's own tables stay.

The typed key is new, not a break: `LuiIndexDefinition<long>` and `LuiLinkedIndexDefinition<long>` keep working, and an index created by
the old calls with text keys (`WithKey(LuiKeyKind.Text)`) opens with `LuiIndexDefinition<string>`.

The rest is additive in this version: every call of 0.3.0 still works (`Create(name, …)`, `CreateLinked`, `Index(name)`, `LinkedIndex`,
`As<T>()`) and is now a thin layer over the definition. They are removed with #268, before 0.4.0 is released. Move a call site
over like this:

```csharp
// before (0.3.0) — the name and the type repeat at every call site
catalog.Create("tags", index => index.SingleWord());
var tags = catalog.Index("tags");

// after — declared once, used everywhere
public static readonly LuiIndexDefinition<long> Tags = new("tags", index => index.SingleWord());
var tags = catalog.Create(Tags);          // at the start; or catalog.Index(Tags) where it is only read
```

A changed definition is still not migrated: `DropIndex(name)`, `Create(definition)`, fill it again — or a new name next to the
old index (`docs/api.md`, "Changing a definition").

**The payload of a hit** (#262): on a handle with a payload type, read `hit.Payload` where you read `hit.GetPayload()` of the typed view, or
`hit.GetPayload<T>()` of an untyped hit, and declare the index with `LuiIndexDefinition<long, T>`. A payload that cannot be read now fails the
whole query with a `LuiPayloadException` instead of throwing where the hit is read — find the entries with `CheckPayloads()` and write them
again. A hit of an index without a payload type has no payload; `HasPayload` has no counterpart. A map of a linked index that throws is wrapped
in a `LuiPayloadException` on a typed handle (the inner exception is the one the map threw); the old calls keep the exception of the map.

A handle that was taken before the index was dropped and created again (by your code or another process) used to work on the new
index; it throws now, and the open call gives a new handle. Take the handle from `catalog.Index(definition)` where you use it — it is
a lookup now — instead of holding it across a recreate.

**`CreateTriggers()` and the state of a linked index** (#272): a call on triggers that are current no longer rewrites them — the triggers an application
installed itself with its own white space stay as they are — and no code needs to guard the call with `Triggers()` any more. A start that wants a
working linked index reads the state instead of remembering an event: `index.CreateTriggers(); if (index.NeedsInitialSync) index.Sync();`. **The triggers come first**: a
row that the application writes between the two calls then lands in the outbox (`ApplyPending()` or the sync takes it), whereas a sync that came first would miss it —
and the flag, already `false`, would never ask for another one.
The new column of `lui_meta` belongs to LUI format 2, which no release has had: a file that an early build of 0.4.0 wrote without it is refused when the catalog is
opened, with a `LuiFormatException` that names the column — recreate its indexes like the files of 0.3.0. A catalog of 0.3.0 that holds no index opens as before; its first
`Create` adds the column.

## [0.3.0] — 2026-11-19

The round of **0.3.0**: the catalogue takes a **path** and owns its connections, there are **three ways** to open it, the read
path is read-only, and the findings of the audit are in. **The public API breaks in named places** — the freeze of
[ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) is lifted for this version and holds again
from it ([ADR-0069](.agents/adr/0069-the-freeze-of-0-1-0-is-lifted-for-0-3-0.md)); the migration note comes first.

### Migration from 0.2.x

**Two breaks the compiler does not see — read them first.**

1. **`Open` no longer creates or sets up anything.** It opens a catalogue that is there, and a file that is missing or has no
   `lui_meta` is a `LuiCatalogNotFoundException` that names `OpenOrCreate`. An application that called `Open` on a new file
   compiles — and fails at its first start on a new machine. Call `OpenOrCreate`:

   ```csharp
   // before (0.2.x): created the file, switched it to WAL, created lui_meta
   var catalog = LuiCatalog.Open("Data Source=app.db");

   // after (0.3.0)
   var catalog = LuiCatalog.OpenOrCreate("app.db");
   ```

   **The argument is a path now.** A connection string still compiles — the parameter is a `string` — and is taken as a file
   name: `OpenOrCreate("Data Source=app.db")` creates a file that is called exactly that. Change the argument, not only the
   method.

2. **Read-only is the method `OpenReadOnly`.** There is no connection string left to say `Mode=ReadOnly`. A write on such a
   catalogue is a `LuiReadOnlyException` whose message names the reason:

   ```csharp
   // before (0.2.x)
   var catalog = LuiCatalog.Open("Data Source=app.db;Mode=ReadOnly");

   // after (0.3.0)
   var catalog = LuiCatalog.OpenReadOnly("app.db");
   ```

**Every moved member**, with the old and the new form:

| before (0.2.x) | after (0.3.0) |
|---|---|
| `LuiCatalog.Open(string connectionString, LuiCatalogOptions? options = null)` | `LuiCatalog.Open(string path, …)`, and the new `OpenOrCreate(string path, …)` and `OpenReadOnly(string path, …)` |
| `LuiCatalog.ConnectionString` | `LuiCatalog.Path` — the full path of the file |
| `new LuiCatalogOptions { BusyTimeout = TimeSpan.FromSeconds(2) }` | `new LuiCatalogOptions { Pragmas = new LuiPragmas { BusyTimeout = TimeSpan.FromSeconds(2) } }` — one second to ten minutes |
| `new LuiCatalogOptions { CacheSizeMegabytes = 64 }` | `new LuiCatalogOptions { Pragmas = new LuiPragmas { CacheSizeMegabytes = 64 } }` — 1 to 1024, per connection |
| `LuiCatalogOptions.MemoryMapMegabytes` | **gone** — measured: no gain |
| `LuiCatalogOptions.WriteAheadLog` | **gone** — the journal mode is not a setting: LUI sets WAL once, in a file it creates itself |
| `services.AddLui("Data Source=app.db", options)` and `services.AddLui(sp => connectionString, sp => options)` | `services.AddLui(_ => LuiCatalog.OpenOrCreate("app.db", options))` — one `AddLui`, which takes a factory |

**New:** `LuiPragmas` (`BusyTimeout`, `Synchronous`, `CacheSizeMegabytes`), `LuiSynchronous` (`Normal`, `Full`) and
`LuiCatalogNotFoundException` (with `Path`). `LuiReadOnlyException` stays and means less: SQLite refused a write, and the
message says why.

**Not supported any more, on purpose:** an encrypted database (`Password`), a VFS of your own, a URI file name and `:memory:` —
the path is a file path, and LUI builds every connection itself (pooling on, a private cache). An application that needs one of
them cannot upgrade to 0.3.0.

### Changed

- **The catalogue owns the connection: a path, and the settings are typed** (#169,
  [ADR-0066](.agents/adr/0066-the-catalogue-owns-the-connection.md)): the entry points take the **path** of the file,
  and LUI builds every connection itself — pooling on, a private cache, the mode of the method that opened it. The
  application never hands LUI a connection string any more.

  | before | after |
  |---|---|
  | `LuiCatalog.Open(connectionString, options?)` | `LuiCatalog.Open(path, options?)` |
  | `LuiCatalog.ConnectionString` | `LuiCatalog.Path` — the full path of the file |
  | `LuiCatalogOptions.BusyTimeout` | `LuiCatalogOptions.Pragmas.BusyTimeout` |
  | `LuiCatalogOptions.CacheSizeMegabytes` | `LuiCatalogOptions.Pragmas.CacheSizeMegabytes` |
  | `services.AddLui(connectionString, options?)` | `services.AddLui(_ => LuiCatalog.OpenOrCreate(path))` |
  | `services.AddLui(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("app")!, options?)` | `services.AddLui(sp => LuiCatalog.OpenOrCreate(sp.GetRequiredService<IConfiguration>().GetValue<string>("App:Path")!))` |

  A path is a **file path**: an empty path, `:memory:` and a URI (`file:…`) are refused with an `ArgumentException`.

- **The values of the connections are checked when the catalogue is opened** — not when they are set. A value out of
  range is an `ArgumentOutOfRangeException`, **before** a connection is opened: a file that is not written is not created
  either. The busy timeout is **one second to ten minutes** (below a second, Microsoft.Data.Sqlite retries a busy
  command until a second has passed anyway, [ADR-0046](.agents/adr/0046-the-busy-timeout-is-the-wait-for-a-lock.md)).

- **The journal mode is set once, and only in a file LUI creates.** `OpenOrCreate` puts a file it creates itself into
  WAL; a file that is there — the database of an application, a catalogue that is already there, even an empty file —
  keeps the mode it has. `Open` and `OpenReadOnly` never set it. The mode is not a setting any more, and nothing
  overrides it.

- **Three ways to open a catalogue** (#171, [ADR-0068](.agents/adr/0068-three-ways-to-open-a-catalogue.md)) — **the
  first behaviour change comes first: `Open` no longer creates anything.**

  | | the file is missing | the file is a database without `lui_meta` | the file is a catalogue |
  |---|---|---|---|
  | `Open` | `LuiCatalogNotFoundException` | `LuiCatalogNotFoundException` | opened — **nothing is written** |
  | `OpenOrCreate` | created and set up (WAL, `lui_meta`) | `lui_meta` is added; the journal mode stays | opened |
  | `OpenReadOnly` | `LuiCatalogNotFoundException` | `LuiCatalogNotFoundException` | opened, read only |

  An application that called `Open` on a new file **compiles and fails at its first start**: call `OpenOrCreate`. A file
  that is no catalogue — an application's database without `lui_meta`, or a file that is no SQLite database — is no longer
  turned into one by `Open`.

- **The read path opens a read-only connection** (#170, [ADR-0067](.agents/adr/0067-the-read-path-opens-a-read-only-connection.md)):
  every query, every `Count` and every check of the file runs on a connection with `Mode=ReadOnly`, **whichever way the
  catalogue was opened** — SQLite refuses a write, not a convention. The statements of a query are prepared. Behaviour you
  may notice: a catalogue opened with `Open` or `OpenOrCreate` now has **two pools** (the writer's and the read path's), and
  a catalogue in write-ahead logging in a directory that may not be written is refused when it is opened, with a
  `LuiReadOnlyException` that names the directory (a read-only connection needs the `-wal` and `-shm` files even to read).
  A catalogue in write-ahead logging that was **only read** leaves its `-wal` (empty) and `-shm` next to the file when it is
  closed — a read-only connection cannot remove them; a catalogue that was written is checkpointed and clean, as before.
  Measured on the same corpus, the query is no slower and allocates no more.

- **The catalogue holds no connection** (#227, [ADR-0070](.agents/adr/0070-the-catalogue-holds-no-connection.md)): a write takes a
  connection from the pool of the provider and gives it back (`Sync` keeps one for its run), instead of the long-lived writer
  connection of ADR-0009. Behaviour you may notice: a **single** `Upsert`, `UpdateWeight` or `Remove` is slower — measured, the
  median of an `Upsert` goes from 0,03 ms to 0,09 ms, p95 from 0,12 ms to 0,21 ms, far inside the budget of p95 ≤ 1 ms — while
  a batch (`UpsertMany`) and `Sync` take as long as before. Nothing in the API changes.

- **The prefix path reads only the rowid of its candidates** (#173, R3 of the audit): the statement of the prefix path of an
  `Embedded` index (one or two characters) no longer reads the text of up to 500 candidates; the ranking there is the weight
  alone, and the text of a hit — and its word start — comes from the statement that reads the hits anyway. The answer is the
  same (a test holds the hits of fifteen prefixes against the output of the code before). Measured on the same corpus and
  machine (200,000 entries, 4 cores, 2026-11-19): one and two characters p50 0,46 / 0,52 ms → 0,23 / 0,23 ms, the allocation
  of a query that spends its budget of candidates p50 40 KB → 15 KB, all queries p50 24 KB → 15 KB. The search index (three
  characters and more) is unchanged — measured alone and mixed in, base and branch alternating, its median (three characters
  0,6–0,7 ms, five to eight 0,3–0,4 ms) does not move beyond the noise. A `Linked` index reads
  the text as before: it checks it against the table.

- **The word start is made when the ranking needs it** (#174, R4 of the audit): the search form of each of up to 500 candidates
  was made for a word start that only the ranking and the typo correction use — every ranking but `WeightOnly` (it sorts by the
  word start, or it measures the similarity from it) and a typo correction that asks whether any candidate has one. With
  `WeightOnly` and no typo tolerance the hits make it instead, for the twenty that are left, from the one search form they make
  anyway for their similarity (the prefix path made two for each hit before). The answer is the same. Measured (200,000 entries,
  4 cores, 2026-11-19, base and branch alternating, two rounds): with `Rank = WeightOnly` and no typo tolerance the median of
  three and four characters goes from 0,61–0,66 to 0,49–0,52 ms; with every other ranking, and with the typo tolerance on,
  nothing changes. A `Linked` index makes it as before: it makes the search form to check the text against the table.

- **A refused write is a `LuiReadOnlyException` that says why.** SQLite refuses (`SQLITE_READONLY`), LUI translates, and the
  message names the reason: the catalogue was opened with `OpenReadOnly` (with the operation, the index, and how to write),
  or the file or its directory may not be written. The error of SQLite is the inner exception. A write in the transaction
  of the application on a catalogue opened with `OpenReadOnly` is refused the same way. **A write that has nothing to
  write is no longer refused:** `Create` of an index that exists with the same definition, `ApplyPending` and `DropTriggers`
  with nothing to do now return on a catalogue opened with `OpenReadOnly` — SQLite refuses a write, not a call.

- **The benchmark of the correction measures the correction** (#179, D7/R2 of the audit): the fixture of the benchmarks built its
  indexes without `.TypoTolerance()` — the distance was `None`, there was no dictionary, a query with the correction on skipped
  it and found nothing (`Hits = 0` in all four lines of the report). The lines measured the path without the correction, and
  the price of the typo tolerance was measured nowhere. The fixture has the tolerance now; the run **stops** if a case with the
  correction on ranks no candidate, and a test holds the committed reports to the same rule. The four corrected queries find
  72–402 candidates and cost 266–1,315 μs with 24–43 KB (the lines of before: 58–330 μs, 5.5 KB, uncorrected). **All five
  reports are measured again on one machine** (a test requires it): the container of this round, 4 cores, .NET 10.0.14 —
  not the machine of the earlier reports. The new reports differ from the old in three things at once — the machine, the code
  of the round (#173, #174, #170) and the fixture — so no line is a comparison with the line of before. The tolerance of the
  fixture is not the cause of the higher allocation of the write benchmarks (436 B → 2.7 KB, 820 B → 4 KB): measured with and
  without it in one run, the allocation is the same (2.73 / 3.96 KB); where it comes from in the code of the round is not
  examined here. The guard is narrow: the harness stops a case that corrects and ranks no candidate, and the Hits of the
  lines of `SettingsBenchmarks` with the correction on (prefix inputs that hit without it) do not prove that the correction ran.

- **The write benchmark runs on a local disk, and every report names its disk** (#178, R1 of the audit): the 2.04 ms of the
  write benchmark against the ≤ 1 ms of the budget were an artifact of the file system — measured per entry (p95), the same
  write took 1.85 ms on the network drive of the audit's workplace and 0.225 ms on a local disk (those numbers are of the
  audit, not of a report of this repository). Every report now carries a `[Disk]` line (the file system
  of the directory of the databases, `local` or `network`, and the directory), and a test fails on a report that was measured
  on a network disk. `tools/benchmarks.sh` puts `TMPDIR` on a local disk and, if the repository is on a network file system,
  copies the **whole** tree there first (with `tar`, without `bin/`, `obj/` and `.git`; a part of the tree is a build that
  breaks), and a failed run keeps its files instead of throwing an hour of measurements away. New reports (ext4, local,
  2026-11-19, the container of the round — not the machine of the record): `Upsert` 0.13–0.31 ms, `UpdateWeight` 0.33 ms,
  the mean of one write in a transaction of its own.

- **The column "Error" of the benchmark tables is called "CI 99.9 %"** (#180, D6/D8 of the audit): it is the half width of the
  99.9 % confidence interval of the mean (BenchmarkDotNet's own legend), not a spread and not a percentile — the old name
  invited a misreading. The report writer renames it, the committed reports and the tables of the README carry the new name, and
  a test fails on a table of benchmarks with a column "Error". The audit (#168) found two runs of the same class on the same class of machine that differed
  by 1.55–1.66× in the latency with the allocation equal to the byte, so a latency line is quoted with its run (date, number
  of measurements, machine, journal mode and disk — all in the report since #124 and #178); `docs/benchmarks.md` says so.
  The name is written with a dot (`99.9`), as the English text of the repository writes numbers.

- **The lane of the schedule leaves its numbers behind** (#177, D3/R5 of the audit): the lane wrote its `[perf]` lines only
  into the step summary of the run, which is gone after the run — the last record in the repository was of 2026-11-12.
  `tools/perf-record.sh` now makes a file of them with the date, the iteration and the disk of the run, and a job of its own
  (`record`, the only one with the token to write; it runs no code of the repository) publishes it as
  `docs/benchmarks-latest.md` on the branch `perf/latest` — not on `main`, `dev` or the branch of a release, so a person looks
  at it before it goes anywhere. A run without a `[perf]` line writes no record. Where the file lives is a decision of the
  owner that can change in one line (the branch, or a pull request); this is the narrowest one.

- **The limit of the dictionary of two errors is 260 MB** (#175, decision of the owner, 2026-11-14): it was 240 MB — exactly what
  was measured, 100 % of the limit, no reserve. The limit moves to 260 MB (+ 8.3 %) in the file of the budgets, the README and
  `docs/benchmarks.md`; the measured 240 MB stays in `docs/typo-dictionary.md` as an observation. A test holds the number in
  the file and the tables against it (#181). Superseded by #242 (below): the limit is 420 MB.

- **The dictionary of two errors is measured against its limit — and the numbers of the record were wrong** (#242): no test held
  a size against the limit of 240, then 260 MB; now one does (`BudgetPerformance`, on every run of the lane, and a test that holds
  every budget of the file to a measurement). It measured **388 MB**, and the dictionary of one error **194 MB** — not the
  240 MB and 138 MB of the record of 2026-11-12, which could not be reproduced: the code of `0.1.1`, `0.2.0` and this version
  gives the same sizes, on the container and on a hosted runner. The limit of the dictionary of two errors is now **420 MB**
  (+ 8 % over the measurement, decision of the owner), the record and the documentation of the public API carry 194 and
  388 MB (times: 4.0–5.2 s and 10.7–12.2 s, the container of the round). The limit of one error stays at 200 MB — it is met
  with 194 MB, **a reserve of 3 %**.

- **A catalogue is one file** (#183): `docs/api.md` and the README say that `Embedded` and `Linked` indexes live side by side in
  one catalogue — mixing them is defined — with the tables of the application beside the tables of LUI, that what separates
  them is **who owns the content table**, and that the file of the application becomes a catalogue with `OpenOrCreate` (write-ahead
  logging only if `OpenOrCreate` creates the file itself, otherwise the mode of the file stays — empty or not; LUI never
  switches it again). A test per document
  holds the sentences; integration tests hold the behaviour in both orders (LUI first, the application first) and that a later
  `Open` writes nothing. The behaviour itself is the one of #169 to #172 — nothing in the library changed for this issue.

- **A write no longer applies the pragmas of its connection again** (#237): since the catalogue holds no connection (ADR-0070) every write
  takes one from the pool of the provider, and every one of them ran the pragma statement again — a command and two
  statements, about a kilobyte per write: `UpdateWeight` allocated 2,430 B and `Upsert` 3,693 B (0.2.0: 422 B and 869 B). The
  pragmas stay with the physical connection, so a connection that comes out of the pool is not given them again (a
  connection of another catalogue with other pragmas still is). Now: 1,494 B and 2,733 B. **A kilobyte per write remains
  compared with 0.2.0** — not the checkout of the connection (about 170 B) but the write path, which makes its statement objects
  again on every write; that is not changed here (#248). The settings do not change; tests read them back from the connections of the
  pool, also for two catalogues of one file with other pragmas.

- **The README carries the measurements of the round** (2026-11-19): the table of the budgets names the lane of 2026-11-19 (a hosted
  runner, not the machine of the budgets): query p50 0.87 ms, p95 ≤ 1.7 ms (1–4 characters) and ≤ 4.5 ms (5–8), with a
  correction 8.4 ms, 20 / 44 KB per query, 67.0 s for the 1.9 million entries of the word list, 243 MB for a million titles. It no
  longer says that every target is asserted in its scenario: the three-million scenario is not reached yet (#176).

### Added

- **`LuiPragmas`** — the settings of the connections of a catalogue: `BusyTimeout` (5 s), `Synchronous` (`Normal` or
  `Full`) and `CacheSizeMegabytes` (1 to 1024, **per connection**; without a value SQLite's own 2 MB).
  **`LuiSynchronous`** — the two values of `synchronous` that make sense: `Off` would put the file at risk on a power
  loss, `Extra` costs without a gain in the write-ahead log.
- **`LuiCatalog.OpenOrCreate(path, options?)`** and **`LuiCatalog.OpenReadOnly(path, options?)`** — the ways to open a
  catalogue that a path makes possible.
- **`LuiCatalogNotFoundException`** (with `Path`) — there is no catalogue at the path of `Open` or `OpenReadOnly`: the file is
  missing, it is no SQLite database, or it has no `lui_meta`.

### Removed

- **`LuiCatalogOptions.MemoryMapMegabytes`** — measured: no gain (0,490 → 0,474 ms on a local disk, 3,440 → 3,305 ms on
  a network mount, and 4 µs per opened connection).
- **`LuiCatalogOptions.WriteAheadLog`** — the journal mode is a property of the file, and LUI sets it once.
- **The two `AddLui` overloads with a connection string** — replaced by
  `AddLui(this IServiceCollection services, Func<IServiceProvider, LuiCatalog> open)`. The container owns the catalogue
  and disposes it; the way it is opened is the caller's.

## [0.2.0] — 2026-11-15

The release of **0.2.0**: the round of the measured work — six pull requests, and every one of them ends in a number, with
the machine beside it in the record. The public API does not move — the freeze of
[ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) holds, and nothing in this release
needs a migration note.

### Added

- **The probe of the measured window** (#134): the unit lane can be dispatched with the probe armed, and the probe
  names the iteration of a dirty window that allocated and the amount — the run of 43228720565 said only that
  1912 bytes had been allocated inside the window of `The_measure_allocates_nothing`.
  `tools/allocation-probe.sh` dispatches the runs, collects their reports and says how many runs there were.
- **The run under load** (#154): `tools/load-run.sh` runs the four lanes of the suite at once, with the measurement
  layer beside them, keeps the whole output and the trx of every lane, and names the test that failed — the name of the
  failure of that day was lost with the filtered output of the run. The hunt named it:
  `LuiWarmupTests.The_host_waits_for_a_round_that_runs_when_it_is_disposed`, in 4 of the 30 runs.
- **The contract with SymSpell is measured** (#44, [ADR-0064](.agents/adr/0064-the-dependency-on-symspell-stays.md)):
  the dependency of the core on `symspell` **stays** — the owner decided it on 2026-11-14 — and the contract between
  LUI and the package is pinned: the beginnings of three to ten letters, the count of a word (its weight plus one), the
  count threshold of one, the order of the corrections, and the head of ten letters. Over the whole word list: 685,739
  words, 138 MB, and a fingerprint of the corrections that an own implementation would have to reproduce.

### Changed

- **The first load keeps its path, and its budget is three minutes again** (#32,
  [ADR-0065](.agents/adr/0065-the-first-load-keeps-its-path-and-its-budget-is-three-minutes.md)): the lever of the issue — a
  longer write-ahead log while the first load runs — was measured on two machines and **rejected** (no gain, and a log of
  424 MB instead of 229 MB); the two other levers of the issue are **moot**, because the target holds again. The build of
  the whole corpus of the repository (1,908,798 entries) by `Sync`, in batches of 1,000,000, takes **120.4 s** on the
  machine of the record and **165.1–172.9 s** on the machine of the report, so the budget `build-3m` goes **back to
  3 minutes** (ADR-0020). The public API does not move.

### Fixed

- **The warm-up of the host waits for the round itself** (#185): the host that stops the service waits for the **task**
  of the round, and not for the flag of the service — the flag is cleared a moment before the round itself is over. The
  service also counts the rounds that have begun: a claim that stays true, where the claim of a round that *runs* holds
  only while the round runs. The test of the hunt of issue #154 waited for that momentary claim, and a loaded machine
  delayed the poll of the test by more than the life of the round (1841 ms against 1.16 s) — the round began and ended
  between two polls, and the claim was never seen. The public API does not move.

## [0.1.1] — 2026-11-13

The release of **0.1.1**: the guards of the repository and the dead paths of the records. The public API does not move —
the freeze of [ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) holds, and nothing in this
release needs a migration note.

### Added

- **The guards of the round** — four of them, and each of them comes with a mutation that proves it bites:
  - the paths of the affected files of a record name files that exist (#137);
  - the index of the records names the records that exist, and every record has a row (#129);
  - the API page names only the members that the types have (#130);
  - the C# blocks of the tutorial are compiled, run, and compared with the pages (#128).

### Fixed

- **The dead paths of the records**: six paths in ADR-0032, ADR-0041 and ADR-0056 named files that had moved with the
  measurements of the repository (#137).
- **The index of the records**: two rows carried a topic that the table of the index does not name (#129).
- **The API page**: it wrote `TypoTolerance` on a type that has `TypoDistance` — the guard of the types did not see it,
  because it compared types and not members (#130).
- **The tutorial**: the typed view of the payload declared `chip` again, the name that the block above had already
  declared — a reader who pasted the two blocks into one scope got CS0128 (#128).

## [0.1.0] — 2026-11-13

The release of **0.1.0**. The public API freezes with it
([ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md)): from this version on it grows only
additively, and a breaking change needs a new minor version, a migration note here and an ADR. The changes below stand
between `0.1.0-alpha` and `0.1.0` — three of them break the API of the alpha, and each says what to write instead.

### Added

- **The chapter that says why the tests are built the way they are**
  ([the testing strategy](docs/testing-strategy.md), #81). The layers, the techniques and their reason — real SQLite and no
  mocks, a file and not `:memory:`, the names as sentences, the three kinds of oracle, a measurement that is not a test,
  the guards that read the documentation, the mutation check — what is deliberately not done, and what a new test brings
  with it. The drift of the prose is gone: the durations of the map are orders of magnitude now, and the soak of the
  concurrency tests says six readers over four thousand entries, as the code does. **No change to the public API.**
- **The surface of 0.1.0, written against the review of 2026-11-04**
  ([ADR-0063](.agents/adr/0063-the-surface-of-0-1-0-is-the-review-of-2026-11-04-applied.md), #144). Every point of the
  independent review of 2026-11-04 — the ones that were decided and the ones that were declined — and the type or the
  member that carries it today; the two changes that came after the review; and the points that later decisions took
  away, with the record that took them. The member-by-member list stays where it was: in
  [`docs/api.md`](docs/api.md). **No change to the public API.**

### Changed

- **The creation names the route**
  ([ADR-0062](.agents/adr/0062-the-creation-names-the-route-create-and-createlinked.md), #145). `LuiCatalog.Create`
  creates an **embedded** index, `LuiCatalog.CreateLinked` a **linked** one — the route is named in the call, as it is on
  the side of opening (`Index` / `LinkedIndex`). **A breaking change** — and still free: the freeze of
  [ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) begins with the tag `v0.1.0`, and
  there is no such tag yet.
  - **Migration**: `catalog.Create(name, index => index.…LinkTo(…))` → `catalog.CreateLinked(name, index => index.…LinkTo(…))`.
    An embedded index is created with `Create` as before — the compiler does not choose the route any more.
- **The typo distance is an enumeration, and it belongs to the index**
  ([ADR-0054](.agents/adr/0054-the-typo-distance-is-an-enumeration.md), #118). **A breaking change** — and still
  free: the freeze of
  [ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) begins with the tag `v0.1.0`,
  and there is no such tag yet.
  - **`LuiTypoDistance`** (`None`, `OneError`, `TwoErrors`) is the new public type. The distance is an enumeration and
    not a number: the dictionary of the word beginnings keeps the delete variants of SymSpell, and their number grows
    with the distance — so the distances are the ones that were measured, and a caller cannot ask for a tenth. What the
    distances cost is measured in [`docs/typo-dictionary.md`](docs/typo-dictionary.md).
  - **The builder takes the distance**: `TypoTolerance(LuiTypoDistance distance = LuiTypoDistance.OneError)`. The bool
    overload is gone — `TypoTolerance(false)` was "no correction", which is `LuiTypoDistance.None` now, and
    `TypoTolerance(true)` is `LuiTypoDistance.OneError`.
  - **The query keeps the bool**: `Query(text).TypoTolerance(bool)` and `LuiQueryOptions.TypoTolerance` are unchanged. A
    query switches the correction on or off; the distance belongs to the index, because the dictionary is built with it.
    **A query cannot exceed the distance of the index**: `TypoTolerance(true)` asks for the correction, but a distance of
    `None` forgives nothing at all — the query asks, the index does not correct, and the query says so
    (`CorrectionSkipped`).
  - **The distance is part of the stored definition.** An index that is opened again knows how its dictionary was
    built, and its queries correct — unless the query says otherwise. Before this change an opened index never
    corrected, and only a query that asked for it did.
  - **A distance of `None` builds no dictionary at all.** The state of the dictionary stays `NotBuilt`, and the memory
    of one is not taken.
  - **Migration.**
    - `TypoTolerance()` and `TypoTolerance(true)` → `TypoTolerance(LuiTypoDistance.OneError)` (or `TypoTolerance()`
      without an argument — its parameter default is `OneError`; **not calling it at all means `None`**, an index that
      forgives nothing).
    - `TypoTolerance(false)` → `TypoTolerance(LuiTypoDistance.None)`. **The difference is the query:** the old `false`
      meant "the index corrects nothing by itself, a query may still ask for it", and `None` means "nothing is
      corrected at all" — a query cannot widen the distance of its index. An application that used the builder's `false`
      together with a query's `true` loses its correction; it wants `OneError` on the builder now.
    - `index.TypoTolerance` (a bool) → `index.TypoDistance` (a `LuiTypoDistance`).
    - An application that reads `ILuiIndex` itself (an own implementation of the interface) has to implement
      `TypoDistance` instead of `TypoTolerance`.
    - **An index of the old library has no distance in its stored definition** — it reads back as `None`, an index that
      corrected nothing. The step that decides whether the application comes up after the upgrade is its `Create`:
      - An index that is created again **with** a distance is a `LuiDefinitionMismatchException` (the stored
        definition says `None`, the new one says `OneError`): **the application does not come up.**
      - An index that is created again **without** a distance (the default of the builder) matches the stored one: the
        application comes up, but the index corrects nothing at all — not even what it corrected before.
      - The way out is `DropIndex(name)` and a new `Create` — **and for an embedded index that empties the index**: its
        entries live in the content table of the library, and the drop takes them with it, so the application has to
        load its data again. For a linked index the content lives in the table of the application and survives; the
        index is empty after the drop, and `Sync()` fills it again.
    - An application that creates its indexes in its migration **describes an index the same way every time** — also
      after the upgrade, because the distance is part of the definition: a change of the distance wants a
      `DropIndex(name)` and a new index.
- **The budgets live in one file**
  ([ADR-0056](.agents/adr/0056-the-budgets-live-in-a-file.md), #120). The budgets of ADR-0020 were written down three
  times — in the specification, in the tables of the entrance and of the record, and as a number at the call site of
  the measurement. They live in one file now
  ([`tests/Nordmann.LUI.Testing/Budgets/budgets.json`](./tests/Nordmann.LUI.Testing/Budgets/budgets.json)), an
  embedded resource of the shared test infrastructure. Every budget carries its scenario (the corpus file, the prefix
  length, the condition), its limit, its unit and its kind — a time or a size. The measured value does not belong in
  the file: it stays in the record, beside the machine it was measured on.
  - The table of the entrance and the two tables of the record are checked against the file; a limit that drifts fails
    the build. The mutation check has two mutations for it: a limit of the file edited by hand turns the build red.
  - A new row: the typo dictionary of the distance `TwoErrors` — 685,739 words, 240 MB, measured 2026-11-12. It has
    no time limit: a build time is a property of the machine it was measured on, and it is measured in the record, not
    promised.
  - **No change to the public API** — the file and its reader live in a test project, and the package ships neither.

- **The repository states its own rules** ([ADR-0056](.agents/adr/0056-the-budgets-live-in-a-file.md), #120).
  `AGENTS.md` does not inherit a contract from outside the repository any more: it states the rules of this
  repository, and the references to that contract are out of the documentation, of the build files and of the CI. The
  specification keeps its number and its paragraph references, and points at the file of the budgets.
  - **No change to the public API.**
- **The repository is English, and its decisions speak in its own terms**
  ([ADR-0057](.agents/adr/0057-the-repository-of-a-public-library-is-english.md), #121). The working papers under
  `.agents/plan/` are translated, the five tutorials carry English file names, and the six ADRs that named the external
  contract of the time are restated in the terms of this repository — each with a dated note, so that the history of the
  work stays visible. **No change to the public API.**
### Removed

- **`DistinctText` is gone** ([ADR-0055](.agents/adr/0055-the-uniqueness-of-a-text-belongs-to-the-data.md), #119).
  The option showed one hit per normalised text — the system tag "vegan" and the ordinary tag "Vegan" were one hit
  instead of two. It was the most expensive option in the library: it kept the folded text of every candidate (**+28 KB**
  per query with one word, **+84 KB** with several, ADR-0032), and a duplicate used up a place of the candidate budget,
  so the option made the search shallower, not only the answer shorter.
  - **Gone**: `LuiQueryOptions.DistinctText` and `LuiQuery.DistinctText(bool)`. The option was **off by default**, so a
    query that did not ask for it behaves exactly as before — only an application that asked for it sees a difference.
  - **Migration**: an application that used the option takes the uniqueness of a text into its own data — **the
    application keeps its own text unique, in its own data, over its own column, with its own rule**, and that rule is
    not the fold of the library. The table of the library holds the **transliterated** text in the generated column
    `s_norm` (case and diacritics untouched); the **folded** text, the one the tokenizer compares, lives inside the
    search index, and the **search form** of the C# side is computed by an internal helper — the fold is not part of the
    public API. A constraint over `s_norm` therefore does not catch "vegan" and "Vegan", and `lower(s_norm)` is an ASCII
    approximation of the fold, not the fold (see [the normalisation](docs/normalization.md)). In the `Linked` case the
    table is the application's own, and the constraint is the application's own over its own column, with the same
    caveat. Without a rule of its own, an application gets two hits for a text in two spellings — the library does not
    hide the second one, because it cannot know which of them the application means.
  - The sample's checkbox and its `distinct` parameter are gone; the tutorial, the README and
    [`docs/api.md`](docs/api.md) follow.

### Fixed

- **The host waits for a round of the outbox that runs when it stops**
  ([ADR-0043](.agents/adr/0043-the-warm-up-is-a-service-of-the-host.md), #148). The warm-up is a service of the host:
  the host runs it, the host stops it, the host disposes it. What was missing is the round that **runs** when the host
  stops — the timer died, the round went on, it held the file of the application, and the host that stopped the service
  was gone while LUI still wrote. The stop and the disposal wait for that round now, and a round that begins after the
  stop does not begin at all. **No change to the public API.**
- **The repository speaks one language, down to the comments**
  ([ADR-0057](.agents/adr/0057-the-repository-of-a-public-library-is-english.md), #142). The pull requests of the
  series left nine files in German — the comment of the schedule of the lane, the documentation of the corpus of
  the measurements, two comments of the report writer — and two quotations of names that exist in English now.
  They are translated. The sample data stays German on purpose: it shows what the normalisation is for.
  **No change to the public API.**

## [0.1.0-alpha] — 2026-11-11

The first release, and a **prerelease** of 0.1.0
([ADR-0053](.agents/adr/0053-the-first-release-is-a-prerelease.md), #115). It is a release to try out: the public API
may still change until `0.1.0`, each change with an ADR and an entry here; the freeze of
[ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md) begins with `0.1.0`. Install it with
`dotnet add package Nordmann.LUI --prerelease`.

It ships the packages `Nordmann.LUI` and `Nordmann.LUI.Hosting` with the API of the
decisions ([.agents/adr/](.agents/adr/README.md)):

- `SingleWord` and `MultiWord` indices, `Embedded` and `Linked` content sources
- FTS5 trigram search with priority-first candidates, word-start ranking and
  similarity scoring
- typo tolerance over a prefix dictionary
- `Sync`, `Check`, `Rebuild`, `Optimize`
- dependency injection (`AddLui`, the catalog in the container) in the second package `Nordmann.LUI.Hosting`,
  released with the same version
- a sample application with a search field in the browser

The sections below are the changes made on the way to it.

### Added

- **The typed view over the untyped index** ([ADR-0044](.agents/adr/0044-the-typed-view-over-the-untyped-index.md),
  #59). The index knows no type ([ADR-0035](.agents/adr/0035-the-index-knows-no-types.md)) — the type of the payload
  stands where the payload is used, and a typo in it is a runtime error instead of a compile error. The view is the
  way out: `index.As<TagChip>()` gives the index as the application sees it, with the payload in the type of the
  application.
  - `LuiIndexExtensions.As<TPayload>()` is an extension method — the type stays out of the index, and the index keeps
    no type. The view opens nothing, writes nothing, and holds no index of its own.
  - `LuiTypedIndex<TPayload>` (the view), `LuiTypedResult<TPayload>` (the answer) and `LuiTypedHit<TPayload>` (the
    hit) mirror the three untyped types — `ILuiIndex`, `LuiResult` and `LuiHit`. The key stays `LuiKey`; the payload
    is not cached, it is read on every call of `LuiTypedHit<TPayload>.GetPayload()` / `TryGetPayload(out …)`, exactly
    like `GetPayload<T>()` — a method, not a property, because it does work and can fail.
  - `LuiResult.As<TPayload>()` is the typed end of the fluent query: `index.Query("veg").Take(5).Execute().As<TagChip>()`.
  - The untyped way stays: `hit.GetPayload<T>()` is still there, and still the one way for the application that does
    not want a view. The view is additive ([ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md)).

- **The warm-up is a service of the host** ([ADR-0043](.agents/adr/0043-the-warm-up-is-a-service-of-the-host.md), #68).
  After [ADR-0042](#unreleased) the application builds the typo dictionary — and an application forgets. The warm-up is
  the opt-in answer: the host asks for it, the library starts nothing on its own.
  - `AddLuiWarmup(Action<LuiWarmupOptions>? configure = null)` in `Nordmann.LUI.Hosting` registers a hosted service
    that applies the outbox of every linked index at the start of the host, builds the typo dictionaries, and applies
    the outbox on an interval. **Off by default** — without the call the host starts nothing of LUI. Once per
    container. The start of the host waits for the dictionaries.
  - `LuiWarmupOptions`: `WarmUpTypoDictionaries` (default `true`), `TypoDictionaryIndexes` (the indexes that get a
    dictionary; empty: every index — a dictionary costs 138 MB for 685,829 words) and `OutboxInterval` (default 30 s).
  - `LuiCatalog.WarmUpTypoDictionaries()`, `LuiCatalog.WarmUpTypoDictionaries(IEnumerable<string> names)` and
    `LuiCatalog.ApplyPending()` do the same work in one call, for an application that warms the catalog itself. All
    are additive ([ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md)).
  - **The warm-up tells the logger of the host** what it did and what failed
    ([ADR-0045](.agents/adr/0045-the-warm-up-tells-the-logger-of-the-host.md)): the dictionaries it built
    (`Information`), the rows it applied (`Debug`), and a round of the outbox that failed (`Warning`, with the
    exception) — instead of swallowing it.
  - The hosting package carries `Microsoft.Extensions.Hosting.Abstractions` again — a hosted service is a type of a
    host, and the dependency set of a package is its contract ([ADR-0034](.agents/adr/0034-the-hosting-package-carries-only-the-dependency-injection.md)) —
    and names `Microsoft.Extensions.Logging.Abstractions`, which was in its closure already.

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
  query options with their defaults, the ranking and the priority, the typo tolerance, the ADR-0020 budgets and the results
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

- **The performance budgets of ADR-0020, measured** ([ADR-0020](.agents/adr/0020-the-performance-budgets-measured.md)): a test per
  target in its scenario (`PerformanceBudgets`, gated), a generated corpus for the measurements, and the benchmark
  project (`benchmarks/Nordmann.LUI.Benchmarks`, BenchmarkDotNet). At three million entries the query median (1.6 ms),
  the single write (p95 0.5–0.8 ms) and the typo dictionary are met. Four targets of ADR-0020 are
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
  that column alone — ADR-0011 and ADR-0008 say so. One and two letters
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
- `Normalizer.TryWriteSearchForm` — the search form without an allocation,
  for the path that normalises every candidate.

### Removed

- **`Highlight` is struck from the contract** ([ADR-0028](.agents/adr/0028-lui-has-no-highlight-a-correction-is-shown-as-a-question.md), #20
  closed): LUI delivers no found positions (`LuiHit.Highlight`, `.Highlight(…)`, a map from the search form to the original). It never
  existed in the code; ADR-0028 says "falls away". An application shows a correction with `LuiResult.CorrectedQuery`.

- **The freeze** ([ADR-0026](.agents/adr/0026-the-public-api-from-0-1-0-on-grows-only-additively.md), #43). From the tag `v0.1.0` on, the
  public API of `Nordmann.LUI` and `Nordmann.LUI.Hosting` grows only additively. A breaking change — a removed or renamed
  member, a changed signature (a new optional parameter counts), a changed meaning of a documented behaviour — needs a new
  minor version, a migration note in this file and an ADR. A new interface member comes as a default member; a new enum value
  is a line here ("adds `X` to `Y`"). The record also lists the points of the architecture review of 2026-11-04 that were
  decided against, by name, with their reasons.

### Fixed

- **`BusyTimeout` is the wait for a lock** ([ADR-0046](.agents/adr/0046-the-busy-timeout-is-the-wait-for-a-lock.md),
  #70). A write that waited for a lock waited **31 s**, whatever `LuiCatalogOptions.BusyTimeout` said:
  Microsoft.Data.Sqlite retries a busy command on its own until the timeout of the command (30 s), and every retry
  waited the busy timeout of SQLite again. Every connection of the catalog sets the timeout of its commands to one
  second now, and the wait is the busy timeout — 1,011 ms for `BusyTimeout = 1 s`, 2,006 ms for 2 s. Below a second
  the wait is about one second. A statement that runs is not interrupted.
  - **What an application may notice:** a writer of the catalog that waited for a long `Sync` of a linked index and got
    through on the 30 s of the provider now gets `SQLITE_BUSY` after its busy timeout (5 s by default). Give the
    catalog the busy timeout the wait needs — `new LuiCatalogOptions { BusyTimeout = TimeSpan.FromSeconds(60) }` —
    as ADR-0015 and ADR-0016 advise for a large sync.

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

- **`LuiPayloadRow.Get<T>` reads a `NULL` as `null` when `T` can hold it**
  ([ADR-0052](.agents/adr/0052-a-null-column-is-null-for-a-type-that-can-hold-it.md), #111). `Get<long?>`,
  `Get<DateTime?>` and `Get<string>` gave an `InvalidOperationException` for a `NULL`; they give `null`. `Get<object>`
  gave `DBNull.Value`; it gives `null`. A `T` that cannot hold null (`Get<long>`) still throws, and `GetString`,
  `GetInt64` and `GetDouble` are unchanged.

- **The budget table of the README is the promise of ADR-0020, and a test holds it up**
  ([ADR-0040](.agents/adr/0040-the-budget-table-of-the-entrance-is-a-promise.md), #38). The limits of the table were
  copied by hand, and they had drifted: the allocation still promised the old target of ≤ 250 / ≤ 350 KB, while the
  budgets and the budget test have held ≤ 60 / ≤ 100 KB since ADR-0032.
  - A new architecture test (`Readme_Budgets`) reads the target table of ADR-0020 out of the budgets and compares it
    with the budget table of the README, with the target table of the record and with its status table — in order, so
    a swapped limit is caught as well as a changed one.
  - The status table of the record stood apart from the contract twice: it promised the old limits, and it listed the
    allocation before the correction. Its rows follow the order of the contract now.
  - The allocation row of both tables carries the limits of ADR-0020 and the measured values of the run of 2026-11-06
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
  of contents, the install, one quick start with its output, the budgets of ADR-0020, one table for the two index kinds and
  the two content sources, the payload, `AddLui` in a host, reading only, the most important types, the sample and the
  map of the documentation.
  - The five concept sections (the kinds of index, the sources of the content, the query, the ranking and the
    priority, the typo tolerance) leave the README: the [decisions](.agents/adr/README.md) have them, in
    full, and they are the contract.
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
  - **`Linked` is documented precisely** (ADR-0015): LUI keeps no copy of the row — the normalised text in the search
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
  byte-identical. ADR-0020 carries the new target (p50 ≤ 60 KB, p95 ≤ 100 KB; measured p50 42 KB, p95 60 KB).

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

- **The query does not build the typo dictionary — the application asks for it**
  ([ADR-0042](.agents/adr/0042-the-query-does-not-build-the-typo-dictionary.md), #35). The dictionary was built by the
  first query that needed a correction, inside the read transaction of that query: the build takes seconds at three
  million entries, the WAL grows while it runs, and with the WAL switched off a writer that waits and the query that
  builds held each other up until the busy timeout.
  - **A query never builds the dictionary.** It asks whether one is there; if not, it answers with what the input found
    and sets `CorrectionSkipped` — as it always did while another build ran.
  - **The application asks for the build**: `WarmUpTypoDictionary()`, `RebuildTypoDictionary()`, and the writes that
    learn the new beginnings. A failed build now says so where it was asked for: the warm-up throws, instead of the
    query swallowing it.
  - **The state of the dictionary stays** `NotBuilt`, `Building`, `Current`.
  - **No change to the public API**: `WarmUpTypoDictionary()` and `RebuildTypoDictionary()` exist already, on the
    embedded and on the linked index.
  - **What an application has to do:** call `WarmUpTypoDictionary()` after it has loaded its entries, if it wants the
    correction. The sample does it — after the seed and after a fill.

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

[Unreleased]: https://github.com/nordmanndev/LUI/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/nordmanndev/LUI/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/nordmanndev/LUI/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/nordmanndev/LUI/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/nordmanndev/LUI/compare/v0.1.0-alpha...v0.1.0
[0.1.0-alpha]: https://github.com/nordmanndev/LUI/releases/tag/v0.1.0-alpha
