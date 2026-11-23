# 0073 — The index definition is the single declaration of an index, and the catalog hands out typed handles

- **Status:** Accepted (owner, 2026-11-20)
- **Date:** 2026-11-21
- **Supersedes:** —
- **Builds on:** [0035](./0035-the-index-knows-no-types.md) §2 (the typed, writable embedded and linked handles; the keys are typed;
  there is no untyped handle — this record carries it out, and #268 removes the untyped one),
  [0044](./0044-the-typed-view-over-the-untyped-index.md) (the view becomes `LuiIndex<TKey>`, reached only through the catalog),
  [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the read path writes nothing),
  [0062](./0062-the-creation-names-the-route-create-and-createlinked.md) (the route is named in the call — for a definition the type names it),
  [0071](./0071-the-freeze-comes-with-1-0-0.md) (a break before 1.0.0 needs a reason, a migration note and a record)
- **Affects:**
  - `src/Nordmann.LUI/LuiIndexDefinitionBase.cs`, `LuiIndexDefinition.cs`, `LuiLinkedIndexDefinition.cs`, `LuiIndexSource.cs`,
    `LuiConfigurationException.cs` — the definition and its exception
  - `src/Nordmann.LUI/LuiIndexOfKey.cs`, `LuiEmbeddedIndexOfKey.cs`, `LuiLinkedIndexOfKey.cs`, `LuiEntryOfKey.cs`, `LuiQueryOfKey.cs`,
    `LuiResultOfKey.cs`, `LuiHitOfKey.cs` — the typed handles
  - `src/Nordmann.LUI/LuiCatalog.cs`, `LuiIndexKindBuilder.cs`, `LuiEmbeddedIndexBuilder.cs`, `LuiLinkedIndexBuilder.cs` — the catalog
    creates from a description, the builders only describe
  - `src/Nordmann.LUI/Indexes/IndexStore.cs`, `src/Nordmann.LUI/Schema/IndexDescription.cs` — the creation in one transaction, the comparison by field
  - `src/Nordmann.LUI/LuiIndexDefinitionOfKeyAndPayload.cs`, `LuiLinkedIndexDefinitionOfKeyAndPayload.cs`, `LuiLinkedIndexBuilderOfPayload.cs`,
    `LuiIndexOfKeyAndPayload.cs`, `LuiEmbeddedIndexOfKeyAndPayload.cs`, `LuiLinkedIndexOfKeyAndPayload.cs`, `LuiEntryOfKeyAndPayload.cs`,
    `LuiQueryOfKeyAndPayload.cs`, `LuiResultOfKeyAndPayload.cs`, `LuiHitOfKeyAndPayload.cs` — the payload variants (#269)
  - `src/Nordmann.LUI/LuiKeyType.cs`, `src/Nordmann.LUI/LuiKeyException.cs`, `src/Nordmann.LUI/LuiFormatException.cs`, `src/Nordmann.LUI/KeyConversion.cs`,
    `src/Nordmann.LUI/Schema/KeyCodec.cs`, `src/Nordmann.LUI/Schema/KeyTypes.cs`, `src/Nordmann.LUI/Schema/Meta.cs`, `src/Nordmann.LUI/Schema/LinkedSource.cs`,
    `src/Nordmann.LUI/Indexes/IndexStore.cs`, `src/Nordmann.LUI/Indexes/LinkedIndex.cs` — the key types and the LUI format version (#270)
  - `docs/api.md`, `docs/testing.md`, `CHANGELOG.md`, `.agents/adr/README.md`
  - `tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs` — the guard of the shape of the typed handles
- **Issue:** [#261](https://github.com/nordmanndev/LUI/issues/261), part of the epic of 0.4.0 ([#258](https://github.com/nordmanndev/LUI/issues/258)); [#269](https://github.com/nordmanndev/LUI/issues/269) (payloads, the sections 10 to 14 below),
  #270 (key types and the LUI format version, the sections 15 and 16 below) and #268 (the removal of the old calls) add their parts to this record

## Context

An index was named and typed at every call site: `catalog.Index("recipes")` repeats a string, `.As<RecipeCard>()` repeats a type,
a linked index repeats its map on every open call, and a key is an untyped `LuiKey`. A typed view could not write, a linked index
could not be typed. And the builders were bound to the catalog: `LuiIndexKindBuilder` held the catalog and the name, and the
chain ended in a `Create()` that the catalog called — the description and the creation were one object.

## Decision

1. **An index is declared once, as a definition object.** `LuiIndexDefinition<TKey>` (embedded) and
   `LuiLinkedIndexDefinition<TKey>` (linked) hold the name, the source, the kind, the key type, the options and, for a linked
   index, the table. Every call site uses that object. The base class `LuiIndexDefinitionBase` has a `private protected`
   constructor: the types are the whole family (two in #261, four with the payload variants of #269), they do not derive from each other, so the overloads of `Create` and
   `Index` are unambiguous. A variable typed `LuiIndexDefinitionBase` binds to `Create(params …)`, which returns nothing.
2. **A definition is immutable, has reference equality and does no I/O.** It knows no catalog. `Name` is stored at once; the
   validation and the builder function run **once, lazily**, on first use — `Lazy<T>` with `ExecutionAndPublication`, so also
   under concurrent first use — and the result or its exception is kept. A definition is declared as a `static readonly`
   field, and a field that threw would surface as a `TypeInitializationException` of its class: nothing throws at the
   declaration.
3. **`LuiConfigurationException`** (derives from `LuiException`) is the exception for a configuration that is wrong — a bad
   name, a key type that is not allowed, `WithKey` inside a definition (and, in #261 only, `WithPayload` — decision 10 replaces that rule). It is thrown on the first use by a
   catalog. `ArgumentException` stays for the invalid argument of a call. The error that the builder function raised is its
   inner exception.
4. **The builders describe, the catalog creates.** `LuiIndexKindBuilder`, `LuiEmbeddedIndexBuilder` and `LuiLinkedIndexBuilder`
   hold no catalog and no name; they collect an `IndexDescription`. One internal core of the catalog, `CreateCore`, creates from a
   description; `Create(definition)`, `Create(params …)` and the old `Create(name, …)` / `CreateLinked(name, …)` all go
   through it. The old calls are a **thin layer**: they describe, build a definition and create it — #268 only deletes. The
   public `Create()` of the builders throws until #268 removes it.
5. **The catalog is the only way to a handle: `Create(definition)` and `Index(definition)`, one name for both sources.**
   `Create(definition)` creates the index or opens it when it exists with the same definition; it never syncs and never
   installs triggers. `Create(a, b, c)` creates or opens several in one transaction — all or none, the exception names the
   definition that failed — the least API-changing way: a mismatch and a missing index already carry `IndexName`; a broken table of a
   linked index stays a `LuiLinkedTableException`, with `Index 'name': ` in front of the message and the original as inner exception;
   a refused write on a read-only catalog names the index whose write was refused — and a duplicate name throws before anything is written. `Index(definition)` writes nothing,
   compares the stored definition and throws `LuiDefinitionMismatchException` naming the fields, or
   `LuiIndexNotFoundException`. LUI offers no way to inject an index (`AddLui` keeps holding the catalog only).
6. **What is compared, and what is runtime only.** *Stored and compared:* the source, the kind, the key type, the German
   transliteration, the fold table, the typo distance, the link (table, key, text, weight and priority columns, payload columns),
   and — from #260 — the payload type name and format of an embedded index. *Runtime only, never compared:* `Similarity(…)` and the
   `map` of a linked payload; a change takes effect with the next start, without drop and recreate. The stored definition of the
   old path does not change: the same `lui_meta` row, byte for byte (`SchemaGoldenTests`).
7. **A changed definition is not migrated.** LUI does not migrate it (#258, out of scope). The recipe — also in the message of the
   mismatch and on `docs/api.md`: `DropIndex(name)`, `Create(definition)`, fill it again (`UpsertMany` or `Sync` for an embedded
   index — its stored content is lost with the drop —, `Sync()` for a linked one); or a **new name** (`recipes_v2`) next to the old
   index, switched over, the old one dropped: no window without a search.
8. **The handles are concrete classes, not interfaces:** `LuiIndex<TKey>` (abstract, the read side of both sources),
   `LuiEmbeddedIndex<TKey>` and `LuiLinkedIndex<TKey>` (sealed), `LuiQuery<TKey>` and `LuiResult<TKey>` (sealed classes,
   reference equality — a result holds a list), `LuiEntry<TKey>` and `LuiHit<TKey>` (sealed records). An application tests its search
   against a real catalog, not a mock — the rule LUI follows for itself ("real SQLite, no mocks", `docs/testing-strategy.md`).
   A handle is a view, thread-safe like the catalog. An operation on a handle of a dropped index throws
   `LuiIndexNotFoundException`; on a disposed catalog `ObjectDisposedException`. `TKey` is `long` in this version (#270 opens
   `int`, `string` and `Guid`); the type is one internal check, `KeyTypes.Validate`.
9. **The API stays synchronous — also for an asynchronous source.** There is no `SyncAsync`: SQLite has no asynchronous I/O,
   and a `Task`-returning method would be a lie about latency (ADR-0016). The input may be asynchronous:
   `LuiEmbeddedIndex<TKey>.Sync(IAsyncEnumerable<LuiEntry<TKey>>, LuiSyncOptions?, CancellationToken)` blocks until the sync is
   done, as today's `ILuiEmbeddedIndex.Sync(IAsyncEnumerable<LuiEntry>)` does; its doc comment says to run it on a thread that may
   block. The guard `The_public_api_is_synchronous` stays without an exception.

10. **The payload is declared in the definition: four definition types, no untyped payload (#269).** `LuiIndexDefinition<TKey, TPayload>` and
    `LuiLinkedIndexDefinition<TKey, TPayload>` (`where TKey : notnull where TPayload : notnull`) join the two of #261. All four derive from
    `LuiIndexDefinitionBase` and from nothing else, so the overloads of `Create` and `Index` stay unambiguous: `catalog.Create(definition)`
    returns `LuiEmbeddedIndex<TKey, TPayload>` / `LuiLinkedIndex<TKey, TPayload>`, both types inferred. `PayloadType` is the second type
    argument. The payload type is **not** stored or compared until #260 (the payload registry): a definition with a payload and one
    without compare equal until then.
11. **The typed linked builder.** `LuiLinkedIndexBuilder.WithPayload<TPayload>(columns, map)` returns `LuiLinkedIndexBuilder<TPayload>` (it
    returned the untyped builder, and the map was a `Func<LuiPayloadRow, object?>` inside). The typed builder does **not** derive from the
    untyped one and has the same option methods (`Similarity`, `GermanTransliteration`, `TypoTolerance`), so the order of `WithPayload` and
    the options is free. The type does the checking: a `LuiLinkedIndexDefinition<long>` (no payload) cannot be given a builder with a payload,
    and a `LuiLinkedIndexDefinition<long, ChefView>` only accepts a builder that ends in `LuiLinkedIndexBuilder<ChefView>` — both are
    compile errors, not a `LuiConfigurationException`. The columns are stored and compared; the map is code, runtime only, and two
    definitions with the same view type each use their own. The old `WithPayload` rule — "inside a definition it throws" — is gone.
12. **The bridge for the old path.** `catalog.CreateLinked<TPayload>(string, Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>>)` takes
    the typed builder and goes through `Create(definition)` (an internal `LuiLinkedIndexDefinition<long, TPayload>`), so every existing
    `CreateLinked(name, index => index.….WithPayload(…))` keeps compiling and returns `ILuiLinkedIndex`. It is not ambiguous with
    `CreateLinked(string, Func<…, LuiLinkedIndexBuilder>)`: the lambda decides by the type it returns, and `TPayload` is inferred from it.
    #268 removes the bridge with the other old calls.
13. **No write without a payload on a handle with a payload.** `LuiEmbeddedIndex<TKey, TPayload>` has `Upsert(TKey key, string text, TPayload
    payload, int weight = 0, LuiPriority priority = Normal, SqliteTransaction? transaction = null)` — the payload right after the text, the
    order of the entry —, `Upsert(LuiEntry<TKey, TPayload>, …)`, `UpsertMany`, the two `Sync` overloads (one for an `IAsyncEnumerable`, which
    blocks — there is no `SyncAsync`), `Remove`, `UpdateWeight`, `UpdatePriority`, `Check(entries)` and `Rebuild`; it has no
    `Upsert(key, text, weight)`, no `LuiEntry<TKey>` overload and no payload-less `Sync`, so no entry without a payload reaches an index with
    one (#260), and with `TPayload = int` a call `Upsert(1, "x", 5)` means the payload `5`, never a weight.
    `LuiEntry<TKey, TPayload>` takes its payload in the constructor — a `null` throws `ArgumentNullException` — and `Payload` has no `init`,
    so `entry with { … }` cannot make it `null`. `LuiIndex<TKey, TPayload>` does not derive from `LuiIndex<TKey>`, and `LuiHit<TKey, TPayload>`
    not from `LuiHit<TKey>` (a shared base can be added later without a break); the value equality of a hit will include the payload's own
    equality from #262 on — today's `LuiHit` compares stored bytes.
14. **The transition until #260 and #262.** *Writing:* the typed handles serialize with the catalog's `LuiCatalogOptions.PayloadSerializer`, with the
    type the payload has at run time; a typed write on a catalog without one throws a `LuiConfigurationException` that names the index —
    before anything is written, for `Upsert`, `UpsertMany` and both `Sync`s. *Reading:* `hit.Payload` arrives with #262 (a map that returns
    `null` fails the query there); until then the written payloads are read through the old untyped path, `catalog.Index(name)` and
    `hit.GetPayload<T>()`. *The fingerprint:* the payload type name and format are stored and compared from #260 on.

15. **The key types: `long`, `int`, `string` and `Guid`, stored exactly (#270).** `TKey` is one of the four for an embedded definition and
    `long` or `int` for a linked one; `string` or `Guid` on a linked definition is a `LuiConfigurationException` that names #267 (a linked
    index has the `INTEGER PRIMARY KEY` of the application's table), any other type one that names the allowed types — a new exception on
    every use, as for every invalid definition. The stored definition names the **exact** key type — `long`, `int`, `string`, `guid` — instead
    of the two names of 0.3.0, and it is a compared field: `int` against `long` is a changed definition and a `LuiDefinitionMismatchException`
    naming `keyType`. The public enum `LuiKeyType` (`Long`, `Int`, `String`, `Guid`) names the four; it replaces `LuiKeyKind` (numbers or texts),
    which stays until #268 removes it with the old calls, and the report on an index (#273) will carry it. The file still stores every key as
    text; `KeyCodec` converts per type: a `long` and an `int` in invariant digits, a `Guid` in the canonical form `"D"`, lower case — so a
    `guid` index holds one key per GUID and no text that is not a GUID, because it can only be opened with a `Guid` definition (the old,
    untyped handle of such an index refuses a text that is no GUID, and an `int` index a number outside the `int` range, on the write). An
    `int` key of an embedded index can only be written through an `int` handle, so it always fits. A **linked** `int` index reads the keys of
    the application's table: a row whose key is outside `0 … 2,147,483,647` is left out by `Sync`, `Refresh` and `ApplyPending` (the same
    statement that leaves out a key outside the document ids, with a smaller bound) and counted in `KeyOutOfRange` — it never reaches a query.
    **There is no `CheckKeys()`**: a stored key always converts. A key that does not — a damaged file — fails the read that meets it with a
    `LuiKeyException` (derives from `LuiException`; it carries the index, the stored key and the `LuiKeyType`), never with a quiet default.
16. **The LUI format version is 2, and a file of format 1 is rejected when the catalog is opened (#270).** `Meta.SchemaVersion` — what
    `lui_meta.schema_version` holds, the **LUI format version**, not SQLite's own schema counter (#265) — goes from 1 to 2. Format 2 is the
    format of 0.4.0: the later issues of the round (the payload type and format of #260, `NeedsInitialSync` of #272, the generation column
    of #271) add to it before the release — there is no 0.4.0 file with format 2 but without them. `Open`, `OpenReadOnly` and `OpenOrCreate`
    of a file that has indexes read the table once and reject a file of another format with a **`LuiFormatException`** (derives from
    `LuiException`; it carries the path, the found and the supported format): "The file 'app.db' was written by LUI 0.3.0 (format 1); this
    version reads format 2. Recreate the indexes." A file of a newer format is rejected the same way. The check is at open time and not at
    the first index, where the answer would be a misleading "another definition"; an index that changes under an open catalog keeps its own
    check (`LuiDefinitionMismatchException`). There is no migration from format 1: 0.4.0 does not read the files of 0.3.0 (#258, out of
    scope). The catalog does not open, so `DropIndex` is out of reach too: the application deletes the file (a file that holds only LUI), or
    drops the `lui_*` tables and `lui_meta` of its own database once, from a migration — the one case where the tables are touched by hand —,
    and creates the indexes again. The golden schema of `lui_meta` does not change — the format is a value in a row, not a column.

17. **The payload registry takes over (#260, [0072](./0072-the-serializer-is-bound-to-the-payload-type-through-a-registry.md)).** The transition of §14
    ends for the typed handles: they serialize with the serializer that `LuiCatalogOptions.Payloads` holds for the payload type — looked up when
    the index is created or opened, so an unregistered type is a `LuiConfigurationException` at `Create`/`Index` and not at the first write —, and
    the payload type name and format are stored and compared from now on (§6, §10: a definition with a payload and one without are two
    definitions). `PayloadSerializer` serves the old calls only, until #268.

## Consequences

- The old calls (`Create(name, …)`, `CreateLinked`, `Index(name)`, `LinkedIndex`, `As<T>()`, the untyped handles) keep working
  unchanged in this change — the integration suite stayed green; #268 removes them and moves every caller.
- `CHANGELOG.md` carries the migration note: additive here, the removal comes with #268.
- `PublicApiShapeTests` gets the list of the members of every typed handle — the target each later pull request extends (#269 added the
  payload variants, #262 `Payload` and `CheckPayloads()`, #272 `NeedsInitialSync`, #273 and #266 the catalog operations).
- The typed handles wrap the internal index of 0.3.0 and convert `TKey` ↔ `LuiKey` at the edge (an `int` is a number, a `Guid` a text in the form `"D"`).
  **#265 moved the runtime into the catalog, and with it removed the limitation this record first had** — a handle of an index that was
  dropped and **created again under the same name** was not told apart from the new index and worked on it: since
  [0075](./0075-the-catalog-holds-the-definitions-and-the-runtime-of-an-index.md) every operation of a handle reads SQLite's schema counter on its
  own connection, and a handle whose definition is gone throws `LuiIndexNotFoundException`, one whose definition is another
  `LuiDefinitionMismatchException`; the failure is kept, and the next open call builds a new handle. A dropped index is no longer detected by the
  missing table alone, and an index created again with the **same** definition is not stale — the handle goes on with it.
- `LuiIndex<TKey>.Search` converts the result twice: the internal `LuiResult` (struct hits), then `LuiResult<TKey>` with a `LuiHit<TKey>` per
  hit. Avoiding it needs the query path to build the typed result itself — it is not part of #265 (the thin handle); recorded in #280.
- `TriggerScript()` is pure (built from the definition, no database read), so it also answers on a handle of a dropped index.
- An invalid definition keeps its failure, not its exception object: every use throws a new `LuiConfigurationException` with the same message and cause.
- A `LuiLinkedIndex<TKey>.Sync(LuiSyncOptions?, …)` accepts the options and does not use them yet: the batches come with #271.

## Alternatives rejected

- **Interfaces for the handles (`ILuiIndex<TKey>`).** An interface invites a mock, and a mock of a search proves nothing; the
  tests of LUI use a real SQLite and so should the application's. A concrete class is also free to grow a member without a break.
- **A `SyncAsync(IAsyncEnumerable<…>)`.** The owner's rule is a synchronous API; the one asynchronous thing is the input, and a
  blocking `Sync` over it is what 0.3.0 already had.
- **Validating the definition in its constructor.** A `static readonly` field would throw in the static initializer of its class
  and surface as a `TypeInitializationException` that hides the cause.
- **Keeping the catalog in the builders.** A builder that can create is a builder that can be called twice, from anywhere, with a
  catalog that is not the one of the application.
- **A payload as an optional member of `LuiIndexDefinition<TKey>`.** An index then has a payload or not by a runtime fact, and an entry without
  one could reach an index that needs one. Four definition types make it a fact of the type.
- **`LuiLinkedIndexBuilder<TPayload>` deriving from `LuiLinkedIndexBuilder`.** A `LuiLinkedIndexDefinition<long>` would then accept a
  builder with a payload and ignore its map; with two unrelated builders the compiler refuses it.

## Removal (#268, 2026-11-23)

The old calls are removed, not deprecated (ADR-0071): `ILuiIndex`, `ILuiEmbeddedIndex`, `ILuiLinkedIndex`, the non-generic `LuiHit`, `LuiResult`,
`LuiEntry` and `LuiQuery`, `LuiTypedIndex<T>` and its siblings, `LuiKey` and `LuiKeyKind`, `Create(name, …)`, `CreateLinked`, `Index(name)`,
`LinkedIndex`, the builders' `Create()` and `WithKey`, `LuiCatalogOptions.PayloadSerializer`, the non-generic `ILuiPayloadSerializer`, `GetPayload`,
`TryGetPayload`, `HasPayload`, and in the hosting package `AddLuiWarmup` and `LuiWarmupOptions`. Below the handles the index layer speaks the key as the
text the file stores (`KeyConversion` at the border) and the internal `RawEntry`, `RawHit` and `RawResult`; `LuiPayloadException.Key` is the stored key
text. The guard `The_index_knows_no_types` of ADR-0035 is `The_index_layer_references_no_application_types`: the index and storage layers have no type
parameter, name no type of another assembly and use no serializer. The migration lines are in `CHANGELOG.md`.
