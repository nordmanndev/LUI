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
  - `docs/api.md`, `docs/testing.md`, `CHANGELOG.md`, `.agents/adr/README.md`
  - `tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs` — the guard of the shape of the typed handles
- **Issue:** [#261](https://github.com/nordmanndev/LUI/issues/261), part of the epic of 0.4.0 ([#258](https://github.com/nordmanndev/LUI/issues/258)); #269 (payloads),
  #270 (key types) and #268 (the removal of the old calls) add their parts to this record

## Context

An index was named and typed at every call site: `catalog.Index("recipes")` repeats a string, `.As<RecipeCard>()` repeats a type,
a linked index repeats its map on every open call, and a key is an untyped `LuiKey`. A typed view could not write, a linked index
could not be typed. And the builders were bound to the catalog: `LuiIndexKindBuilder` held the catalog and the name, and the
chain ended in a `Create()` that the catalog called — the description and the creation were one object.

## Decision

1. **An index is declared once, as a definition object.** `LuiIndexDefinition<TKey>` (embedded) and
   `LuiLinkedIndexDefinition<TKey>` (linked) hold the name, the source, the kind, the key type, the options and, for a linked
   index, the table. Every call site uses that object. The base class `LuiIndexDefinitionBase` has a `private protected`
   constructor: the two types are the whole family, they do not derive from each other, so the overloads of `Create` and
   `Index` are unambiguous. A variable typed `LuiIndexDefinitionBase` binds to `Create(params …)`, which returns nothing.
2. **A definition is immutable, has reference equality and does no I/O.** It knows no catalog. `Name` is stored at once; the
   validation and the builder function run **once, lazily**, on first use — `Lazy<T>` with `ExecutionAndPublication`, so also
   under concurrent first use — and the result or its exception is kept. A definition is declared as a `static readonly`
   field, and a field that threw would surface as a `TypeInitializationException` of its class: nothing throws at the
   declaration.
3. **`LuiConfigurationException`** (derives from `LuiException`) is the exception for a configuration that is wrong — a bad
   name, a key type that is not allowed, `WithKey` or `WithPayload` inside a definition. It is thrown on the first use by a
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
   definition that failed, and a duplicate name throws before anything is written. `Index(definition)` writes nothing,
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

## Consequences

- The old calls (`Create(name, …)`, `CreateLinked`, `Index(name)`, `LinkedIndex`, `As<T>()`, the untyped handles) keep working
  unchanged in this change — the integration suite stayed green; #268 removes them and moves every caller.
- `CHANGELOG.md` carries the migration note: additive here, the removal comes with #268.
- `PublicApiShapeTests` gets the list of the members of every typed handle — the target each later pull request extends (#269 the
  payload variants, #262 `Payload` and `CheckPayloads()`, #272 `NeedsInitialSync`, #273 and #266 the catalog operations).
- The typed handles wrap the internal index of 0.3.0 and convert `TKey` ↔ `LuiKey` at the edge; #265 moves the runtime into the
  catalog. A handle detects a dropped index by the missing table (a catalog read only on that path), so the hot path pays nothing.
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
