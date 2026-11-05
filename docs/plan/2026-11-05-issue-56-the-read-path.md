# Issue #56 — The read path opens an index without writing

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:executing-plans` (inline) or
> `superpowers:subagent-driven-development` to implement this plan task by task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** A second, read-only way into an index: `Open` reads the definition out of `lui_meta` and
writes nothing — no table, no definition, no write lock — so a read replica, a `mode=ro` file and a
service that must not write can use LUI.

**Architecture:** The catalog keeps the file; the definition in `lui_meta` is the one description of an
index. The write path (`Create`) builds the tables and compares the definition; the new read path
(`Open`) only reads it, rebuilds the content source from it and hands out an index object. Two shapes:
the typed one (`OpenIndex<TKey, TPayload>`) and the untyped facade (`OpenIndex(name)`), which returns the
key as the text it is stored as and the payload as the raw JSON.

**Tech Stack:** .NET 10, C#, SQLite (Microsoft.Data.Sqlite), xUnit + Shouldly, NetArchTest.

## Global Constraints

- **ADR-0026** — before 0.1.0 the public API may still change; after the tag it may only grow. Every
  change of a name, every new overload and every new dependency needs an ADR **in the same pull request**.
- **ADR-0018** — the core takes no `Microsoft.Extensions.*` package; that is the hosting package's.
- **One type per file**, methods ≤ 60 lines, no regex without a timeout, culture-safe formatting
  (`Meziantou.Analyzer`, `TreatWarningsAsErrors`).
- **The public API is additive-only** after the tag — so the renames below are the last chance.
- The spec wins: `docs/spec/` (§7.1, §12, §14, §15) is updated in the same pull request as the code.
- `docs/testing.md` is a guard map, not a document: every new test file is listed there.
- The sample is `IsPackable=false`; `Bogus` stays a sample-only dependency.
- Commands run from the repository root with `DOTNET_ROOT=/usr/share/dotnet` and `HOME=/root`.

---

## The decisions (from issue #56, confirmed by the owner)

| # | Decision |
|---|----------|
| 1 | `Open()` reads the definition from `lui_meta` and writes nothing. |
| 2 | `Ensure()` is renamed `Create()`. `Create()` stays idempotent: create if missing, compare if there. |
| 3 | The untyped facade returns the key as the text it is stored as, the payload as the raw JSON, `null` when the index stores none. |
| 4 | The typed path: `Open<TKey, TPayload>(name)` returns the typed index. |
| 5 | `Open()` checks the existence, the schema version and the fold version — and says what is wrong. |
| 6 | The DI gets a way to open instead of create: `AddIndex` creates or opens (as today), the new one only opens. |
| 7 | The payload columns go into the definition, so a linked index with a payload can be opened. |
| 8 | The similarity and the typo tolerance are not part of the definition: they come from the query options (`LuiQueryOptions.Similarity` / `.TypoTolerance`), which exist today. |

**Names the owner left open** (the issue, "Open"): the types of the untyped facade. This plan proposes
`ILuiDocumentIndex` (the index) and `LuiDocumentHit` (one hit) — "document" is the library's word for a
stored row (the `doc_id`), and a hit is a hit. The owner may rename them; the pull request says so.

**Naming conflict, and how it is resolved:** `LuiCatalog.Open(string connectionString, …)` is the static
factory that opens the *catalog* and is the first line of the README's quick start. An instance method
`Open(string name)` on the same type would hide it (CS0108 → error, `TreatWarningsAsErrors`). The read
path therefore gets its own name: **`OpenIndex`** — `catalog.OpenIndex("tags")` and
`catalog.OpenIndex<long, TagPayload>("tags", serializer)`.

---

## Task 1: The ADR

**Files:**
- Create: `docs/adr/0033-the-read-path-opens-an-index-without-writing.md`
- Modify: `docs/adr/README.md` (the row, after 0032)

**Interfaces:**
- Produces: ADR-0033, the reference for every later task.

- [ ] **Step 1: Write the ADR.** The context (an application that may only read: a replica, a `mode=ro`
  file, a service that must not write), the decision (the eight rows above, the `OpenIndex` name, the
  `Ensure` → `Create` rename, the facade names as proposed), the consequences (the definition grows the
  payload columns — free now, ADR-0026 — the schema version is read on open, the write lock is not
  taken), the alternatives (a `ReadOnly` flag on the catalog: rejected, it would be a second mode of the
  same object; `Open` on the builder: rejected, the kind is read from the definition).
- [ ] **Step 2: Add the row** to `docs/adr/README.md` — number 0033, the title, the status "accepted",
  the date 2026-11-05.
- [ ] **Step 3: Commit** `git add docs/adr && git commit -m "docs: the read path opens an index without writing (ADR-0033)"`.

## Task 2: `Ensure()` becomes `Create()`

**Files:**
- Modify: `src/Nordmann.LUI/LuiEmbeddedIndexBuilder.cs` (`Ensure()` → `Create()`)
- Modify: `src/Nordmann.LUI/LuiLinkedIndexBuilder.cs` (`Ensure()` → `Create()`)
- Modify: `src/Nordmann.LUI.Hosting/LuiServicesBuilder.cs` (the two call sites)
- Modify: `src/Nordmann.LUI/Indexes/IndexStore.cs` (`Ensure` → `Create`)
- Modify: `samples/Lui.Sample/**` (the call sites)
- Modify: `docs/spec/*.md`, `README.md`, `CHANGELOG.md`
- Test: every test file that calls `Ensure()` (the compiler finds them)

**Interfaces:**
- Consumes: nothing.
- Produces: `LuiEmbeddedIndexBuilder<TKey, TPayload>.Create()`, `LuiLinkedIndexBuilder<TPayload>.Create()`.

- [ ] **Step 1: Write the failing test.** `tests/Nordmann.LUI.Integration.Tests/` — a test that the
  builder offers `Create()` and not `Ensure()` is a shape test, it belongs in the architecture tests:
  in `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs` add `Create` to the allowed members of
  the two builders and assert that `Ensure` is gone (the guard fails until the rename is done).
- [ ] **Step 2: Run it** — `dotnet test tests/Nordmann.LUI.Architecture.Tests` — expected: the guard
  fails on `Ensure`.
- [ ] **Step 3: Rename.** `IndexStore.Ensure` → `IndexStore.Create`; both builders' `Ensure()` →
  `Create()`; the hosting's two call sites; the sample's call sites (`BulkSampleIndex` creates the
  index lazily). The XML documentation of every renamed member is rewritten: "creates the index if it is
  missing and compares the definition if it is there".
- [ ] **Step 4: Run the build and the tests** — `dotnet build -c Release` (0 warnings) and
  `dotnet test` — expected: green.
- [ ] **Step 5: Update the docs** — the spec (§12.2, §12.4), the README (the quick start, the API
  table), the CHANGELOG (the breaking change, under "Changed").
- [ ] **Step 6: Commit** `git commit -m "feat!: Ensure() is now Create()"`.

## Task 3: The definition carries the payload columns

**Files:**
- Modify: `src/Nordmann.LUI/Schema/LinkDefinition.cs` — `PayloadColumns: IReadOnlyList<string>`
- Modify: `src/Nordmann.LUI/Schema/IndexDefinition.cs` — the property flows through
- Modify: `src/Nordmann.LUI/Indexes/IndexStore.cs` — the writer writes the columns
- Modify: `src/Nordmann.LUI/LuiLinkedIndexBuilder.cs` — the builder hands the columns over
- Test: `tests/Nordmann.LUI.Integration.Tests/`

**Interfaces:**
- Produces: `LinkDefinition.PayloadColumns` — the columns of the payload of a linked index, in order.

- [ ] **Step 1: Write the failing test.** "A linked index with a payload stores the columns of its
  payload in the definition" — read `lui_meta.definition` as JSON and check that the columns are in it.
- [ ] **Step 2: Run it** — expected: the columns are missing.
- [ ] **Step 3: Implement.** `LinkDefinition` gets `IReadOnlyList<string>? PayloadColumns`; the builder
  passes its `_payloadColumns` on; `IndexStore` writes them into the definition. The JSON stays
  source-generated (`LuiJsonContext`) — the list is a `string[]`, which the context serializes.
- [ ] **Step 4: Run the tests** — expected: green.
- [ ] **Step 5: Commit** `git commit -m "feat: the definition carries the payload columns of a linked index"`.

## Task 4: `IndexStore` reads an index without writing

**Files:**
- Modify: `src/Nordmann.LUI/Indexes/IndexStore.cs`
- Test: `tests/Nordmann.LUI.Integration.Tests/`

**Interfaces:**
- Produces: `IndexStore.ReadIndex(SqliteConnection, string name)` → `IndexStoreEntry?` — the definition,
  the schema version and the source, or `null` when there is no such index. It opens its own connection,
  starts **no** transaction and takes **no** write lock.

- [ ] **Step 1: Write the failing tests.**
  - "Reading an index takes no write lock": open a write transaction on the file (`BEGIN IMMEDIATE`),
    then read the index — the read returns at once (the old path, `Create()`, blocks).
  - "Reading an index of a read-only database works": the file is opened with
    `Data Source=file:…?mode=ro` — the read succeeds, the tables are not created.
  - "Reading an index that does not exist returns nothing" — `null`, not an exception.
- [ ] **Step 2: Run them** — expected: the first one fails (the read goes through the write path).
- [ ] **Step 3: Implement.** `ReadIndex` reads `kind, source, definition, schema_version` from
  `lui_meta` with a plain `SELECT` on its own connection — no `CREATE TABLE IF NOT EXISTS`, no
  transaction, no `Database.Begin()`. The catalog's `Open` (the static factory) still creates
  `lui_meta` if it is missing — that is the catalog's business, not the index's.
- [ ] **Step 4: Run the tests** — expected: green.
- [ ] **Step 5: Commit** `git commit -m "feat: the index store reads an index without writing"`.

## Task 5: The catalog opens an index

**Files:**
- Create: `src/Nordmann.LUI/ILuiDocumentIndex.cs` — the untyped facade
- Create: `src/Nordmann.LUI/LuiDocumentHit.cs` — one hit of the facade
- Create: `src/Nordmann.LUI/Indexes/DocumentIndex.cs` — the implementation (internal)
- Create: `src/Nordmann.LUI/Querying/RawJsonPayload.cs` — the payload reader that returns the raw JSON
- Modify: `src/Nordmann.LUI/LuiCatalog.cs` — `OpenIndex(name)`, `OpenIndex<TKey, TPayload>(name, …)`
- Modify: `src/Nordmann.LUI/Schema/LinkedSource.cs` — the hits SQL for the raw payload (`json_object`)
- Test: `tests/Nordmann.LUI.Integration.Tests/`

**Interfaces:**
- Produces:
  ```csharp
  public interface ILuiDocumentIndex
  {
      string Name { get; }
      LuiIndexKind Kind { get; }
      bool TypoTolerance { get; }
      long Count();
      IReadOnlyList<LuiDocumentHit> Search(string text, LuiQueryOptions? options = null, CancellationToken cancellationToken = default);
  }

  public readonly record struct LuiDocumentHit
  {
      public required string Key { get; init; }        // the key as it is stored
      public required string Text { get; init; }
      public string? Payload { get; init; }            // the raw JSON, null when the index stores none
      public int Weight { get; init; }
      public LuiPriority Priority { get; init; }
      public double Similarity { get; init; }
      public bool AtWordStart { get; init; }
  }

  public ILuiDocumentIndex LuiCatalog.OpenIndex(string name);
  public ILuiEmbeddedIndex<TKey, TPayload> LuiCatalog.OpenIndex<TKey, TPayload>(string name, ILuiPayloadSerializer<TPayload>? payload = null);
  ```
- Consumes: `IndexStore.ReadIndex` (task 4), `IndexDefinition` (task 3).

- [ ] **Step 1: Write the failing tests.** One per acceptance point:
  - "Opening an index that does not exist says so" — `LuiIndexNotFoundException` (a new exception type in
    `src/Nordmann.LUI/`) with a message that names the index; not "no such table".
  - "Opening an index whose schema version is foreign says so" — the test writes `schema_version = 99`
    into `lui_meta` and expects a clear error.
  - "Opening an index whose fold version differs says so" — the test writes a foreign `fold_table` into
    the definition and expects a clear error.
  - "Opening an index does not write" — after `OpenIndex`, `lui_meta` has no new row and the file's
    modification time did not change (the tables of the index are not created).
  - "The untyped facade returns the key as the text it is stored as" — an index with the keys `1`, `2`,
    `42` returns `"1"`, `"2"`, `"42"`.
  - "The untyped facade returns the payload as the raw JSON" — the payload of a hit is the stored JSON
    text; `null` when the index stores no payload.
  - "The typed facade refuses a type the file does not have" — `OpenIndex<string, …>` on an index whose
    keys are numbers → a clear error.
  - "A linked index with a payload can be opened" — the facade returns the payload as the JSON object of
    the columns.
- [ ] **Step 2: Run them** — expected: they do not compile (the API does not exist yet).
- [ ] **Step 3: Implement.**
  - `LuiCatalog.OpenIndex(name)`: validate the name, `IndexStore.ReadIndex` → `null` → throw
    `LuiIndexNotFoundException`; check the schema version (`Meta.SchemaVersion`) → throw with the version
    that was found; check the fold table against `TokenizerFold.Version` → throw; build the
    `ContentSource` from the definition (`EmbeddedSource` / `LinkedSource`); build the index object with
    the default similarity of the kind and the typo tolerance off (the query options override both).
  - `OpenIndex<TKey, TPayload>(name, payload)`: the same checks, plus the key type (`KeyCodec<TKey>.KeyType`
    against `definition.KeyType`) and the payload (`definition.Payload` against `payload is not null`);
    a mismatch throws `LuiDefinitionMismatchException`.
  - The untyped facade: an `EmbeddedIndex<string, string?>`-shaped object — the key codec of `string`
    reads the stored text, the payload reader returns the stored bytes as UTF-8 text (the raw JSON).
    For a linked index the hits SQL selects `json_object('col', h.col, …)` instead of the columns, so the
    payload is a JSON object of the columns.
- [ ] **Step 4: Run the build and the tests** — expected: green, 0 warnings.
- [ ] **Step 5: Commit** `git commit -m "feat: the catalog opens an index without writing"`.

## Task 6: The container opens an index without creating it

**Files:**
- Modify: `src/Nordmann.LUI.Hosting/LuiServicesBuilder.cs` — `AddOpenIndex(…)`
- Modify: `src/Nordmann.LUI.Hosting/Hosting/IndexRegistration.cs` — the registration grows an "open" path
- Modify: `src/Nordmann.LUI.Hosting/Hosting/LuiStartup.cs` — the start opens instead of creates
- Test: `tests/Nordmann.LUI.Integration.Tests/`

**Interfaces:**
- Produces: `AddOpenIndex<TKey, TPayload>(string name, Action<LuiEmbeddedIndexBuilder<TKey, TPayload>> configure)`
  — registers the index like `AddIndex`, but the host **opens** it: the index must exist, the file is
  never written.

- [ ] **Step 1: Write the failing test.** "A container that opens an index does not create it": a
  container with `AddOpenIndex` over a database without the index → the start throws a clear error, and
  no table is created. "A container that opens an existing index queries it" → the hits come back.
- [ ] **Step 2: Run them** — expected: they fail (no `AddOpenIndex`).
- [ ] **Step 3: Implement.** The registration gets a second delegate: `Open` beside `Ensure`. The startup
  calls `Open` for an "open" registration and `Ensure` for a "create" one. `AddIndex` keeps its meaning
  (create or open); `AddOpenIndex` is the new one.
- [ ] **Step 4: Run the tests** — expected: green.
- [ ] **Step 5: Commit** `git commit -m "feat: the container can open an index instead of creating it"`.

## Task 7: The documents follow the code

**Files:**
- Modify: `docs/spec/*.md` — §7.1 (the definition), §12 (the public API), §14/§15 (the lifetime)
- Modify: `README.md` — the quick start, the API table, a new section on the read path
- Modify: `CHANGELOG.md` — the breaking changes and the migration list
- Modify: `docs/testing.md` — every new test file
- Modify: `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs` — the new members

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: The spec.** §7.1: the definition carries the payload columns. §12: `Create()` replaces
  `Ensure()`, `OpenIndex(name)` and `OpenIndex<TKey, TPayload>(name, …)` are new, `ILuiDocumentIndex` and
  `LuiDocumentHit` are new. §14/§15: the read path takes no write lock, does not create tables, does
  not build the typo dictionary.
- [ ] **Step 2: The README.** The quick start uses `Create()`; a new section "Only read" shows
  `catalog.OpenIndex("tags")` and the facade's hit.
- [ ] **Step 3: The CHANGELOG.** Under "Changed": `Ensure()` → `Create()` (a rename — the old name is
  gone), the new `OpenIndex` methods, the new exception types. A **migration list**: three lines
  (`Ensure()` → `Create()`, `LuiCatalog.Open(conn)` stays, the read path is new).
- [ ] **Step 4: The test map.** Every new test file gets a row in `docs/testing.md`; the guard
  `Test_Landscape` must pass.
- [ ] **Step 5: The API shape.** `Public_API_Shape` lists the new members and asserts that `Ensure` is
  gone.
- [ ] **Step 6: Run everything** — `dotnet build -c Release`, `dotnet test`, `dotnet pack`, `npm test`
  in `samples/Lui.Sample` — expected: green.
- [ ] **Step 7: Commit** `git commit -m "docs: the read path in the spec, the README and the changelog"`.

## Task 8: The pull request

- [ ] **Step 1: Push the branch** `git push -u origin feature/read-path-open` — no force.
- [ ] **Step 2: Open the pull request** against `main`: the what, the why, the ADR-0033, the **Verification**
  section with the real output of `dotnet build`, `dotnet test`, `dotnet pack`, `npm test`, the changed
  tests, and the CHANGELOG note.
- [ ] **Step 3: Watch the CI** — all four jobs green — and merge with a merge commit (the repo's way).

---

## Self-review

**Spec coverage:** every acceptance point of issue #56 has a test in task 5 (the existence, the schema
version, the fold version, the write lock, the read-only database, the untyped facade, the typed
facade, the linked index with a payload). The DI is task 6, the documents are task 7, the rename is
task 2, the schema is task 3, the read without writing is task 4.

**Gaps that the plan states openly:** the names of the facade types are the owner's (the issue says so);
the plan proposes `ILuiDocumentIndex` and `LuiDocumentHit` and says so in the pull request.

**Type consistency:** `OpenIndex` (the catalog, both overloads) — used in tasks 5, 6, 7;
`LuiIndexNotFoundException` (new, task 5) — used in task 5 and the CHANGELOG; `IndexStore.ReadIndex`
(task 4) — used in task 5; `LinkDefinition.PayloadColumns` (task 3) — used in task 5.
