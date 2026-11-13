# Issue #56 — The read path and the catalog as the entry point

> **Note (ADR-0057, 2026-11-12, issue #121):** this paper was written in German and has been translated into English.
> The translation keeps what the paper said at its time — the decisions, the numbers, the names, the branches, the issue
> numbers and the dates. The text below is the paper of its time.

- **Branch:** `feature/read-path-open` (PR #57, against `main`)
- **ADR:** [0033 — The read path opens an index without writing](../adr/0033-the-read-path-opens-an-index-without-writing.md)
- **Issue:** #56
- **State:** 2026-11-05, after the owner's decision on the shape of the API

## The goal

LUI gets a read path that does **not** write to the file, and an API that demands as little as
possible:

1. The **catalog is the entry point** — it is injected, it is the only one in the container.
2. **Creating** is a call of its own (`Create`) and belongs in the migration of the application.
3. At run time the index is **opened** (`Index`) — the remaining APIs stay on the index.
4. **One interface per capability**, no second implementation: the untyped facade
   (`ILuiDocumentIndex`) falls away.
5. The file decides **what is allowed**: a read-only connection → the catalog does not write.

## The API that emerges

```csharp
// The catalog — static factory, unchanged.
public static LuiCatalog Open(string connectionString, LuiCatalogOptions? options = null);

// Creating — once, in the migration.
public ILuiEmbeddedIndex<TKey, TPayload> Create<TKey, TPayload>(
    string name,
    Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder<TKey, TPayload>> index);

public ILuiLinkedIndex<TPayload> Create<TPayload>(
    string name,
    Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder<TPayload>> index);

// At run time — the index is opened, the file stays untouched.
public ILuiEmbeddedIndex<TKey, TPayload> Index<TKey, TPayload>(
    string name,
    ILuiPayloadSerializer<TPayload>? payload = null);

public ILuiLinkedIndex<TPayload> Index<TPayload>(
    string name,
    Func<LuiPayloadRow, TPayload> map);

public ILuiLinkedIndex<LuiNoPayload> Index(string name);
```

`catalog.Index(name)` (the builder) **falls away** — with that the run-time path can no longer create
an index. `catalog.OpenIndex(...)` **falls away** — `Open` is the static factory of the catalog,
`Index` is the index. `ILuiDocumentIndex`, `LuiDocumentHit`, `DocumentIndex`, `DocumentSource`,
`RawJsonPayload` and `ReadOnlyEmbeddedIndex` **fall away**.

### The read-only file

```csharp
// The writer
var catalog = LuiCatalog.Open("Data Source=app.db");
// The reader — the same application code
var catalog = LuiCatalog.Open("Data Source=app.db;Mode=ReadOnly");
```

A catalog on a read-only file:

- does **not** switch to WAL,
- does **not** create **`lui_meta`** — the table has to be there; if it is missing, the catalog says so,
- refuses **every write** with `LuiReadOnlyException` (new, inherits from `LuiException`):
  `Create`, `DropIndex`, `Upsert`, `Sync`.

The file decides that, not a switch in `LuiCatalogOptions` — and not the index.

### The definition remembers the payload type

`IndexDefinition` gets `PayloadType` (the full name of the type, `null` without a payload). When
creating, it is written from the type parameter of the builder; when opening, it is compared:

```
LuiDefinitionMismatchException: the index stores the payload type 'TagChip',
you asked for 'RecipeChip'
```

The **serialiser** is not stored — it is an object of the application. The application hands it over
when it opens the index.

### The container

```csharp
services.AddLui("Data Source=app.db");
```

`AddIndex`, `AddLinkedIndex`, `AddOpenIndex`, `EnsureOnStart` and `WarmUpTypoDictionariesOnStart`
**fall away** — along with `LuiRegistrations`, `LuiStartup`, `IndexRegistration`,
`LinkedIndexRegistration`, `OpenIndexRegistration`. `AddLui` returns `IServiceCollection`.

## The work, step by step

### A. The library

1. `LuiCatalog`:
   - remove `Index(string)` (the builder); introduce `Create(...)` with the two overloads after
     `LuiIndexKindBuilder`/`LuiEmbeddedIndexBuilder`/`LuiLinkedIndexBuilder`.
   - replace `OpenIndex(...)` with `Index(...)`: read the definition from `lui_meta`, build the index
     from it, write nothing. For `Linked` take the columns from the definition and the mapping of
     the application.
   - `DropIndex` stays.
2. `Database`: derive `ReadOnly` from the connection string
   (`new SqliteConnectionStringBuilder(connectionString).Mode == SqliteOpenMode.ReadOnly`).
   The writer throws `LuiReadOnlyException` in a read-only catalog.
3. `LuiCatalog.Open`: with `ReadOnly` no WAL and no `CREATE TABLE lui_meta`; if the table is
   missing, a clear error comes.
4. `IndexDefinition`: add `PayloadType`; write it when creating, check it when opening.
5. Delete the files of the facade (see above) and resolve every reference.

### B. The callers

- `catalog.Index("x")....Create()` → `catalog.Create("x", index => index....)`. **Careful:** the
  lambda parameter is called `index`, the result must not be called `index` (CS0136) — for example
  `var recipes = catalog.Create("recipes", index => index.MultiWord().WithPayload<Recipe>(ctx));`
- `catalog.OpenIndex<TKey, TPayload>(name, payload)` → `catalog.Index<TKey, TPayload>(name, payload)`.
- `catalog.OpenIndex(name)` → falls away.
- The container: switch `AddIndex`/`AddLinkedIndex`/`AddOpenIndex` and their callers (the probe, the
  tests) to `catalog.Create(...)`/`catalog.Index(...)`.

### C. The probe

The endpoints of the probe inject the indexes out of the container today. They now inject **the
catalog** and get the index when they need it:

```csharp
var index = catalog.Index<long, RecipeChip>("recipes", RecipeJsonContext.Default.RecipeChip);
```

The probe creates its indexes itself (it fills them on demand) — `catalog.Create(...)` in the
probe, not in the container.

### D. The tests

- `tests/Nordmann.LUI.Integration.Tests/ReadPathTests.cs` — adjust: the facade falls away, the typed
  path stays, new are the read-only catalog and the check of the payload type.
- `tests/Nordmann.LUI.Integration.Tests/OpenIndexHostingTests.cs` — falls away (the container knows
  no indexes any more).
- New: `tests/Nordmann.LUI.Integration.Tests/ReadOnlyCatalogTests.cs` — the catalog on a read-only
  file: `Open` does not write, `Upsert`/`Create`/`DropIndex`/`Sync` throw `LuiReadOnlyException`,
  `Search` works.
- `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs` — the new members, the fallen ones.
- `docs/testing.md` — the test map: every new test file needs a row, every fallen one loses its
  row (the guard `Test_Landscape` checks that).

### E. The documents

- **Specification** ADR-0006, ADR-0033, docs/api.md, ADR-0016, ADR-0046, ADR-0015, ADR-0046: `Create`, `Index`, the read-only file, the `PayloadType` in
  the definition, the container without indexes.
- **README**: the quick start (`AddLui`, `Create`, `Index`).
- **CHANGELOG**: the migration list (what from `Ensure`/`OpenIndex`/`AddIndex` to what).
- **ADR-0033** is written (it lies in the branch) — adjust the row in `.agents/adr/README.md` if the
  title changes.

## The rules of the repository

- Read `AGENTS.md`. One type per file, methods of at most 60 lines, no regex without a timeout,
  culture-safe formatting (Meziantou.Analyzer), `string.Equals(…, StringComparison.Ordinal)`.
- `TreatWarningsAsErrors` is on: the build must yield **0 warnings**.
- An ADR is only needed if `Directory.Build.props`, `Directory.Packages.props` or the dependencies
  of `Nordmann.LUI`/`Nordmann.LUI.Hosting` are touched — that is **not** the case here (ADR-0033
  exists already).
- No new dependency.

## The assumptions the work is measured against

1. `catalog.Create("tags", index => index.SingleWord().WithPayload<TagChip>(ctx))` creates the index
   and yields `ILuiEmbeddedIndex<long, TagChip>`; a second call with the same description changes
   nothing; an index with another definition is a `LuiDefinitionMismatchException`.
2. `catalog.Index<long, TagChip>("tags", ctx)` opens an existing index, **writes nothing** — the
   red proof: with an open write transaction `Index` returns immediately, `Create` blocks.
3. `catalog.Index<long, TagChip>("tags", ctx)` on an index without this payload type is a
   `LuiDefinitionMismatchException` that names the type.
4. `catalog.Index<long, RecipeChip>("tags", ctx)` on an index with a `string` key is a
   `LuiDefinitionMismatchException` that names the key type.
5. `LuiCatalog.Open("Data Source=x.db;Mode=ReadOnly")` opens a read-only file: no WAL, no
   `CREATE TABLE`, `Search` works, `Upsert`/`Create`/`DropIndex`/`Sync` throw
   `LuiReadOnlyException`.
6. `AddIndex`, `AddLinkedIndex`, `AddOpenIndex`, `ILuiDocumentIndex` and `LuiCatalog.OpenIndex` do
   not exist any more — in the public API and in the container.
7. The container knows only the catalog: `services.AddLui("Data Source=app.db")` and nothing else.

## The acceptance

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `dotnet test`: everything green, the number of tests in the report.
- `dotnet pack -c Release` and `npm test` in `samples/Lui.Sample`: green.
- No force push, `main` stays untouched, no merge.
