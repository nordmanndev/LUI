# 0033 — The read path opens an index without writing

> **Note (ADR-0035, 2026-11-06):** the read path stays as decided here, its types do not. `Index<TKey, TPayload>(…)`,
> `Index<TPayload>(name, map)` and the payload type in the definition are gone: the index knows no types, the
> serialiser belongs to the catalog, and the payload is read where it is used (`hit.GetPayload<T>()`). An embedded index
> is opened with `Index(name)`, a linked one with `LinkedIndex(name)` or `LinkedIndex(name, map)`. "The untyped facade
> is gone" holds: the facade was a *second* way into the data beside the typed one; there is still one way, and it names
> no type. The text below is history and stays as it was written.

> **Note (ADR-0061, 2026-11-13, issue #125):** the specification this record was written against is deleted.
> Every section of it has a home — the ADR or the page that carries the same rule; the mapping is in
> [ADR-0061](./0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md). The sentences below
> that name the specification are history; the rule they carry lives in the ADR named beside them.
>
> **Note (ADR-0062, 2026-11-13, issue #145):** the two overloads of `Create` — one per content source, the compiler
> choosing by the return type of the lambda — are one overload and one named call: `Create` creates an embedded index,
> `CreateLinked` a linked one. The decision of 2026-11-04 stands in every other part: the builder chain is the parameter,
> the runtime path cannot create an index, and `Open` writes nothing.

- **Status:** Accepted
- **Date:** 2026-11-05
- **Builds on:** [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the public API grows and never shrinks),
  [0029](./0029-the-sample-fills-its-own-bulk-indexes-on-demand.md) (the sample fills its own bulk indexes on demand)
- **Affects:**
  - `src/Nordmann.LUI/LuiCatalog.cs` (the entry point: `Create`, `Index`, `Open`)
  - `src/Nordmann.LUI/Storage/Database.cs` (a catalog that may not write)
  - `src/Nordmann.LUI/Indexes/IndexStore.cs` (reading an index without writing)
  - `src/Nordmann.LUI/Schema/IndexDefinition.cs` (the payload type)
  - `src/Nordmann.LUI/LuiEmbeddedIndexBuilder.cs`, `src/Nordmann.LUI/LuiLinkedIndexBuilder.cs` (`Create()` instead of `Ensure()`)
  - `src/Nordmann.LUI.Hosting/LuiServicesBuilder.cs` (the container holds the catalog, nothing else)
  - ADR-0006, ADR-0033, docs/api.md, ADR-0016, ADR-0046, ADR-0015, ADR-0046; `docs/testing.md`, CHANGELOG, README
- **Issue:** #56

## Context

LUI has one way into an index: the builder. `catalog.Index("tags").SingleWord().Create()` describes the
index, **creates its tables** and hands out an object that writes as well as reads. Everything about it
writes:

- `Create()` compares the stored definition with the one that was described and **writes** it into
  `lui_meta` if the index is missing;
- it **creates** the tables of the index (`lui_tags`, `lui_tags_fts`) if they are missing;
- it takes the **write lock** of the file while it does so.

That is right for an application that owns the file. It is wrong for the three cases that came up:

1. **A read replica.** The application reads from a copy of the database that is written elsewhere. The
   copy is read-only — `Data Source=file:…?mode=ro` — and `Create()` fails on it with "attempt to write a
   readonly database", even though the application only wants to search.
2. **A service that must not write.** A service that only reads has no business creating tables or taking
   the write lock of a database that another service writes. It can even block the writer: `Create()`
   waits for the lock of the writer, and the writer waits for the reader — two services, one file, a
   deadlock of patience.
3. **A tool that reads what another tool wrote.** It wants the index as it is — not as the tool would
   have it.

The read path needs its own entry. It must be honest about what it does: **read the definition, rebuild
the index object from it, and nothing else.** No table, no definition, no write lock.

Two things were already in place and made it possible:

- the **definition** in `lui_meta` describes the index completely — the kind, the source, the key type,
  the transliteration, the fold table — so an index object can be rebuilt from it;
- the **similarity measure and the typo tolerance are not part of the definition**: they are
  properties of an instance and of the query, so an index that is opened can be read with the same rules
  as one that was created.

What was missing: the payload of a **linked** index is built from the columns of the application's table,
and those columns were not in the definition — so a linked index with a payload could not be opened at
all.

The shape of the API around that read path was decided with the owner of the repository, and it is
decided **before** 0.1.0 — the rules of ADR-0026 do not protect a version that was never published. The
decisions are:

1. **The catalog is the entry point, and it is the only thing in the container.** An application injects
   the catalog and asks it for what it needs. An index is not registered in the container and not
   injected.
2. **The creation is not part of the runtime path.** The builder describes an index that is created once,
   in the migration of the application. The runtime code opens an index that is there.
3. **The index keeps its APIs.** Querying, writing, synchronising, checking — they all stay on the index.
   There is one interface per capability, and the difference between a read-only and a writable index is
   the file, not the type.

## Decision

**The catalog is the entry point. It creates an index once and opens it in the runtime path.**

### 1. `Ensure()` is renamed `Create()`

The old name said what the method did when the index was missing ("make sure it exists") but not what it
did when it was there (compare the definition, and fail on a mismatch). The new name says what it is: the
write path. `Create()` stays **idempotent** — it creates the index if it is missing, and compares the
definition if it is there — because an application that starts on an empty database needs it.

The name is gone from the public API, not deprecated: ADR-0026 allows a change of a name **before** 0.1.0
and forbids it afterwards.

### 2. `LuiCatalog.Create(name, index)` creates an index — one call

```csharp
var catalog = LuiCatalog.Open("Data Source=app.db");
var tags = catalog.Create("tags", index => index.SingleWord().WithPayload<TagChip>(TagJsonContext.Default.TagChip));
```

The builder chain is the **parameter**, not the way in: the catalog takes a description and does the
creating itself. The builder is no longer reachable from the catalog — `catalog.Index(name)` is gone — so
the runtime path *cannot* create an index. There are two overloads, one per content source:

```csharp
public ILuiEmbeddedIndex<TKey, TPayload> Create<TKey, TPayload>(
    string name,
    Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder<TKey, TPayload>> index);

public ILuiLinkedIndex<TPayload> Create<TPayload>(
    string name,
    Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>> index);
```

The type parameters are inferred from the lambda; a name that is already taken is an error that says what
the stored definition is.

### 3. `LuiCatalog.Index(name, …)` opens an index — and writes nothing

```csharp
var tags = catalog.Index<long, TagChip>("tags", TagJsonContext.Default.TagChip);
```

`Index` reads the definition from `lui_meta` with a plain `SELECT` on a connection of its own: no
transaction, no write lock, no `CREATE TABLE`. It checks three things and says what is wrong when one of
them fails:

| The check | The error |
|---|---|
| the index exists | `LuiIndexNotFoundException` — "there is no index named 'tags'" |
| the schema version is the one this library writes | `LuiDefinitionMismatchException` with the version that was found |
| the fold table is the one this library folds with | `LuiDefinitionMismatchException` — the index was stored with another folding and has to be rebuilt |

A **linked** index is opened with the map of its payload — the columns are in the definition, the map is
the application's:

```csharp
var tags = catalog.Index<TagChip>("tags", row => new TagChip(row.GetString("name")));
```

The name is **`Index`**, not `Open`: `LuiCatalog.Open(string connectionString, …)` is the static factory
that opens the *catalog* — it is the first line of the quick start in the README and it stays. A second
`Open` on the same type would ask two different questions of one name.

### 4. One interface per capability — and no untyped facade

The interfaces stay as they are:

| The interface | What it is |
|---|---|
| `ILuiIndex<TKey, TPayload>` | reads: `Search`, `Count`, `TypoTolerance` |
| `ILuiEmbeddedIndex<TKey, TPayload>` | the above and writes: `Upsert`, `Remove`, `UpdateWeight`, `UpdatePriority` |
| `ILuiLinkedIndex<TPayload>` | the above and the sync of the table: `Sync`, `Refresh`, `ApplyPending` |

The **untyped facade** (`ILuiDocumentIndex`, `LuiDocumentHit` — the key as the text it is stored as, the
payload as the raw JSON) is **not** part of the API. It was a second implementation of the same index:
the same search, the same writes, the same sync — twice. One implementation path is worth more than the
convenience of reading an index whose types the caller does not know. A caller that wants the raw JSON
parses it.

The types of an index are the **types of the application**, not of the library: the key is a `long` or a
`string` (both are stored as text), the payload is the object of the application's own serialiser. The
application knows both — they are its own data.

### 5. The types are checked against the file

The **key type** is in the definition since the beginning; the **payload type** joins it now. The
definition remembers the type of the payload, and `Index` compares it with the one of the caller:

```
LuiDefinitionMismatchException: the index stores the payload type 'TagChip',
you asked for 'RecipeChip'
```

The serialiser itself is not stored — it is an object of the application, not of the file. The application
hands it over when it opens the index.

### 6. The connection says what may happen — not the index

The index that is opened **writes** if the file allows it. What decides that is the **connection**, as in
ADO.NET:

```csharp
// the writer
var catalog = LuiCatalog.Open("Data Source=app.db");

// the reader: the same code, a file that may not be written
var catalog = LuiCatalog.Open("Data Source=app.db;Mode=ReadOnly");
```

A catalog on a read-only file:

- does not switch the file to write-ahead logging,
- does not create `lui_meta` — the table has to be there; if it is not, the catalog says so,
- refuses every write with a `LuiReadOnlyException` — `Create`, `DropIndex`, `Upsert`, `Sync`.

The same application code therefore runs in the service that writes and in the service that only reads.

### 7. The container holds the catalog — and nothing else

```csharp
services.AddLui("Data Source=app.db");
```

The index is **not** registered and **not** injected. `AddIndex`, `AddLinkedIndex` and `AddOpenIndex` are
gone, and with them the registrations, the startup service and the warm-up of the typo dictionaries that
belonged to them: an index that is opened is not created on start, and its dictionary is built when a
query needs it.

An application that wants an index in a constructor asks the catalog for it:

```csharp
public sealed class SuggestEndpoint(LuiCatalog catalog)
{
    public IReadOnlyList<string> Suggest(string q) =>
        catalog.Index<long, RecipeChip>("recipes", RecipeJsonContext.Default.RecipeChip).Search(q)
            .Select(hit => hit.Text).ToList();
}
```

### 8. The index is a view, not a resource

The catalog owns the connections, the writer and the typo dictionaries. An index holds none of them: it
takes a connection from the pool for the time of an operation. **The index is not disposable** — there is
nothing in it to release — and the catalog is: the application disposes it, or the container does.

### 9. The defaults of an opened index

The similarity and the typo tolerance are not part of the definition (ADR-0029), so an opened index takes
the **default of its kind** (Jaro-Winkler for a single-word index, N-gram Dice for a multi-word one) and
the **typo tolerance off** — a query that wants a correction asks for it
(`LuiQueryOptions.TypoTolerance`, `LuiQueryOptions.Similarity`; both exist today). The typo dictionary is
**not built** by the read path: it belongs to the index of the catalog, and it is built when a query needs
it.

## Consequences

- **The public API changes before 0.1.0** — `Ensure()` is gone, `Index`, `Create` and the read-only
  connection are new, the facade and the index registrations are gone. After the tag, none of this may
  change again (ADR-0026).
- **The read path is honest.** It takes no write lock: a service that only reads never blocks the writer,
  and a reader on a read-only file works.
- **The runtime path cannot create an index** — the builder is not reachable from the catalog any more.
- **The definition grows** — the type of the payload. The files of today are free; the migration of a
  file that was created before is: an index created by an older version has no payload type in its
  definition, so it is dropped and created again (`DropIndex` + `Create()`).
- **The container is simpler** — one service, the catalog. The hosting package keeps `AddLui` and loses
  the rest.
- **The typo dictionary is not built on open** — the first query that needs a correction waits for it, as
  it does for a created index.
- The guard map (`docs/testing.md`) and the API shape guard follow the new members; the spec (ADR-0006, ADR-0033, docs/api.md,
  ADR-0016, ADR-0046, ADR-0015, ADR-0046), the README and the CHANGELOG (with the migration list) are updated in the same pull request.

## Alternatives

| The alternative | Why not |
|---|---|
| A `ReadOnly` flag on `LuiCatalogOptions` | The file says whether it may be written; a second switch could contradict it. The connection string is the one place where the mode of the file belongs. |
| `Open()` on the builder (`catalog.Index("tags").SingleWord().Open()`) | The kind would have to be stated again — it is in the definition. And a linked index would have to state the table it is linked to, which is also in the definition. |
| A separate `LuiReader` type | A second entry point into the same file, with a second set of options and a second typo dictionary. The catalog owns the file; it keeps the ownership. |
| The untyped facade next to the typed path | Two implementations of one index — every fix twice, and the facade is the one an application uses least. The application knows its own key type and its own payload; it does not need a second path. |
| An index in the container (`AddIndex`) | It is the pattern the owner does not want: the application should not have to know which index it gets, and the container should not describe indexes. The catalog does that, from the file. |
