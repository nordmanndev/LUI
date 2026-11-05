# 0033 — The read path opens an index without writing

- **Status:** Accepted
- **Date:** 2026-11-05
- **Builds on:** [0026](./0026-the-public-api-grows-and-never-shrinks.md) (the public API grows and never shrinks),
  [0029](./0029-the-sample-fills-its-own-bulk-indexes-on-demand.md) (the sample fills its own bulk indexes on demand)
- **Affects:**
  - `src/Nordmann.LUI/LuiCatalog.cs` (the read path: `OpenIndex`)
  - `src/Nordmann.LUI/Indexes/IndexStore.cs` (reading an index without writing)
  - `src/Nordmann.LUI/Indexes/DocumentIndex.cs`, `src/Nordmann.LUI/Indexes/ReadOnlyEmbeddedIndex.cs`,
    `src/Nordmann.LUI/Indexes/IndexStoreEntry.cs` (new — the index that is opened)
  - `src/Nordmann.LUI/Schema/DocumentSource.cs`, `src/Nordmann.LUI/Querying/RawJsonPayload.cs` (new — the facade)
  - `src/Nordmann.LUI/Schema/IndexDefinition.cs`, `src/Nordmann.LUI/Schema/LinkDefinition.cs` (the payload columns)
  - `src/Nordmann.LUI/LuiEmbeddedIndexBuilder.cs`, `src/Nordmann.LUI/LuiLinkedIndexBuilder.cs` (`Create()` instead of `Ensure()`)
  - `src/Nordmann.LUI/ILuiDocumentIndex.cs`, `src/Nordmann.LUI/LuiDocumentHit.cs`, `src/Nordmann.LUI/LuiIndexNotFoundException.cs` (new — the untyped facade)
  - `src/Nordmann.LUI.Hosting/LuiServicesBuilder.cs`, `src/Nordmann.LUI.Hosting/Hosting/OpenIndexRegistration.cs`,
    `src/Nordmann.LUI.Hosting/Hosting/LuiRegistrations.cs`, `src/Nordmann.LUI.Hosting/Hosting/LuiStartup.cs` (open instead of create)
  - spec §7.1, §12, §14, §15; `docs/testing.md`, CHANGELOG, README
- **Issue:** #56

## Context

LUI has one way into an index: the builder. `catalog.Index("tags").SingleWord().Ensure()` describes the
index, **creates its tables** and hands out an object that writes as well as reads. Everything about it
writes:

- `Ensure()` compares the stored definition with the one that was described and **writes** it into
  `lui_meta` if the index is missing;
- it **creates** the tables of the index (`lui_tags`, `lui_tags_fts`) if they are missing;
- it takes the **write lock** of the file while it does so.

That is right for an application that owns the file. It is wrong for the three cases that came up:

1. **A read replica.** The application reads from a copy of the database that is written elsewhere. The
   copy is read-only — `Data Source=file:…?mode=ro` — and `Ensure()` fails on it with "attempt to write a
   readonly database", even though the application only wants to search.
2. **A service that must not write.** A service that only reads has no business creating tables or taking
   the write lock of a database that another service writes. It can even block the writer: `Ensure()`
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

## Decision

**There is a second way into an index: `OpenIndex`. It reads and writes nothing.**

### 1. `Ensure()` is renamed `Create()`

The old name said what the method did when the index was missing ("make sure it exists") but not what it
did when it was there (compare the definition, and fail on a mismatch). The new name says what it is: the
write path. `Create()` stays **idempotent** — it creates the index if it is missing, and compares the
definition if it is there — because an application that starts on an empty database needs it.

The name is gone from the public API, not deprecated: ADR-0026 allows a change of a name **before** 0.1.0
and forbids it afterwards.

### 2. `LuiCatalog.OpenIndex(name)` opens an index without writing

```csharp
var catalog = LuiCatalog.Open("Data Source=app.db");   // the catalog — unchanged
var tags = catalog.OpenIndex("tags");                   // the index — reads nothing, writes nothing
```

`OpenIndex` reads the definition from `lui_meta` with a plain `SELECT` on a connection of its own: no
transaction, no write lock, no `CREATE TABLE`. It checks three things and says what is wrong when one of
them fails:

| The check | The error |
|---|---|
| the index exists | `LuiIndexNotFoundException` — "there is no index named 'tags'" |
| the schema version is the one this library writes | `LuiDefinitionMismatchException` with the version that was found |
| the fold table is the one this library folds with | `LuiDefinitionMismatchException` — the index was stored with another folding and has to be rebuilt |

The name is **`OpenIndex`**, not `Open`: `LuiCatalog.Open(string connectionString, …)` is the static
factory that opens the *catalog* — it is the first line of the quick start in the README and it stays. A
second `Open` on the same type would hide it (CS0108, an error here) and would ask two different
questions of one name.

### 3. The typed path: `OpenIndex<TKey, TPayload>(name, payload)`

```csharp
var tags = catalog.OpenIndex<long, TagPayload>("tags", new LuiJsonPayloadSerializer<TagPayload>(typeInfo));
```

The typed path returns the same `ILuiEmbeddedIndex<TKey, TPayload>` as the builder — it can query, it can
count, and it cannot write: the write methods of the interface are not part of the read path (the index
object it builds is the read-only one).

The types are checked against the file: a key type the index does not have, or a payload the index does
not store, is a `LuiDefinitionMismatchException` that says which one it is.

### 4. The untyped facade: `ILuiDocumentIndex`

The caller that does not know — or does not care — whether the key of the index is a number or a text,
and whether it has a payload, gets the **untyped facade**:

```csharp
public interface ILuiDocumentIndex
{
    string Name { get; }
    LuiIndexKind Kind { get; }
    bool TypoTolerance { get; }
    long Count();
    IReadOnlyList<LuiDocumentHit> Search(string text, LuiQueryOptions? options = null, CancellationToken cancellationToken = default);
}
```

The hit carries the **key as the text it is stored as** (`"42"`, not `42`), the text, the weight, the
priority, the similarity — and the **payload as the raw JSON**: the JSON that was stored, exactly as it
was written; `null` when the index stores no payload. For a linked index the payload is the JSON object
of its columns, so the columns are in the definition (see 5).

The types are named `ILuiDocumentIndex` and `LuiDocumentHit` — "document" is the library's word for a
stored row (the `doc_id`), and a hit is a hit. **The owner may rename them**; the issue left the names
open.

### 5. The payload columns go into the definition

`LinkDefinition` gets the columns of the payload in their order. The definition is written when the index
is created and compared when it is ensured — so the columns are there, and a linked index with a payload
can be opened.

The definition grows: the files that exist today are free (ADR-0026), never again after 0.1.0.

### 6. The container can open instead of create

```csharp
services.AddLui("Data Source=app.db")
        .AddIndex("tags", index => index.SingleWord());          // creates it if it is missing
        .AddOpenIndex("tags");                                    // opens it — it must exist
```

`AddIndex` keeps its meaning (create or open, as today). `AddOpenIndex` is the new one: the host opens
the index, the file is never written. A service that must not write registers its indexes this way.

An opened index needs **no description**: its kind, its source and its payload come from the definition —
that is the point of the read path. The untyped form registers the facade (`ILuiDocumentIndex`); the typed
one takes the serialiser of the payload and registers the typed index:
`AddOpenIndex<long, TagChip>("tags", TagJsonContext.Default.TagChip)`.

### 7. The defaults of an opened index

The similarity and the typo tolerance are not part of the definition (ADR-0029), so an opened index takes
the **default of its kind** (Jaro-Winkler for a single-word index, N-gram Dice for a multi-word one) and
the **typo tolerance off** — a query that wants a correction asks for it
(`LuiQueryOptions.TypoTolerance`, `LuiQueryOptions.Similarity`; both exist today). The typo dictionary is
**not built** by the read path: it belongs to the index of the catalog, and it is built when a query needs
it.

## Consequences

- **The public API changes before 0.1.0** — `Ensure()` is gone, `OpenIndex` and the facade are new. After
  the tag, none of this may change again (ADR-0026).
- **The read path is honest.** It takes no write lock: a service that only reads never blocks the writer,
  and a reader on a read-only file works.
- **The definition grows** — the payload columns of a linked index. The files of today are free; the
  migration of a file that was created before is: an index created by an older version has no columns in
  its definition, so it is opened by the **untyped** facade (the payload is `null`), or it is dropped and
  created again (`DropIndex` + `Create()`).
- **The catalog is still the one that writes.** `LuiCatalog.Open(connectionString)` creates `lui_meta` if
  it is missing — that is the catalog's business, not the index's. On a file that is read-only *and* has
  no `lui_meta`, the catalog fails; the file of a database with an index has one.
- **The typo dictionary is not built on open** — the first query that needs a correction waits for it, as
  it does for a created index.
- The guard map (`docs/testing.md`) and the API shape guard follow the new members; the spec (§7.1, §12,
  §14, §15), the README and the CHANGELOG (with the migration list) are updated in the same pull request.

## Alternatives

| The alternative | Why not |
|---|---|
| A `ReadOnly` flag on `LuiCatalogOptions` | One object with two modes: every method would have to ask which mode it is in. The read path is a different operation, so it gets a different name. |
| `Open()` on the builder (`catalog.Index("tags").SingleWord().Open()`) | The kind would have to be stated again — it is in the definition. And a linked index would have to state the table it is linked to, which is also in the definition. |
| A separate `LuiReader` type | A second entry point into the same file, with a second set of options and a second typo dictionary. The catalog owns the file; it keeps the ownership. |
| The untyped facade only (no typed path) | The typed path is what an application with a known key type wants — the payload is a typed object, not a JSON text. Both are needed. |
