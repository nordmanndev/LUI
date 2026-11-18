# 0068 — Three ways to open a catalogue: Open, OpenOrCreate, OpenReadOnly

- **Status:** Accepted (owner, 2026-11-18)
- **Date:** 2026-11-14, revised 2026-11-15 (the owner's decision: a path instead of a connection string, three methods
  instead of options) and 2026-11-18 (the owner's decision: the journal mode is set only in a file LUI creates)
- **Builds on:** [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the read path opens an index without
  writing), [0062](./0062-the-creation-names-the-route-create-and-createlinked.md) (the creation names the route),
  [0066](./0066-the-catalogue-owns-the-connection.md) (the catalogue owns the connection)
- **Affects:**
  - `src/Nordmann.LUI/LuiCatalog.cs` — the three methods, the translation of SQLite's refusal to open
  - `src/Nordmann.LUI/LuiCatalogNotFoundException.cs` — the exception
  - `src/Nordmann.LUI/LuiReadOnlyException.cs` — derived from SQLite's refusal, and names the reason
  - `src/Nordmann.LUI/Indexes/IndexStore.cs` — `Initialize` is split: `SetUp` (`OpenOrCreate`) and `Verify` (`Open`, `OpenReadOnly`)
  - `src/Nordmann.LUI/Storage/Database.cs` — `ReadOnly` and `ThrowIfReadOnly` disappear
  - `src/Nordmann.LUI/Indexes/EmbeddedIndex.cs`, `src/Nordmann.LUI/Indexes/LinkedIndex.cs` — they translate the refusal
  - `docs/api.md`, `CHANGELOG.md`
- **Issue:** [#171](https://github.com/nordmanndev/LUI/issues/171), the epic of 0.3.0 ([#166](https://github.com/nordmanndev/LUI/issues/166))

## Context

**The open of a catalogue wrote to the file.** `LuiCatalog.Open` called `IndexStore.Initialize`, which opened a
connection, ran `PRAGMA journal_mode = WAL` and created `lui_meta` if it was missing. Both are writes. And the provider's
default mode, `ReadWriteCreate`, creates the file when it is not there — a typo in the path was a new, empty database,
silently. The note in [0033](./0033-the-read-path-opens-an-index-without-writing.md) says "`Open` writes nothing"; the code
said otherwise.

**The consequence for a foreign file.** An application that opens its own database with LUI got a `lui_meta` table in it
and its journal mode changed — for a file that holds no index at all.

**Somebody has to create the file, and it cannot be `Create`.** `Create` and `CreateLinked` are methods of the
catalogue: the catalogue has to be open before an index can be created. If every open refused a file without
`lui_meta`, the first index of a new file — or of the application's own database — could never be created.

**"The file did not exist" cannot be observed after the fact.** The first connection of the provider creates an empty
file. A rule that depends on it has to be decided **before** the first connection.

**Whether an open may create, and whether it may write, is a decision of the caller — and the caller should see it where
it is made.** An option that says so is invisible at the call; two options that say so can contradict each other
(create *and* read-only).

## Decision

**The catalogue is opened with a file path, and the method says what the open may do.**

```csharp
public static LuiCatalog Open(string path, LuiCatalogOptions? options = null);
public static LuiCatalog OpenOrCreate(string path, LuiCatalogOptions? options = null);
public static LuiCatalog OpenReadOnly(string path, LuiCatalogOptions? options = null);
```

| Call | The file is missing | The file is no catalogue (no `lui_meta`, or no SQLite database) | The file is a catalogue |
|---|---|---|---|
| `Open` | `LuiCatalogNotFoundException` | `LuiCatalogNotFoundException` | opened, read **and** write — **the open writes nothing** |
| `OpenOrCreate` | **created and set up**: WAL, `lui_meta` | **set up**: `lui_meta`; the journal mode stays | opened, nothing written |
| `OpenReadOnly` | `LuiCatalogNotFoundException` | `LuiCatalogNotFoundException` | opened, **read only** — every write is a `LuiReadOnlyException` |

1. **The path is a file path**, resolved with `Path.GetFullPath` and kept as `LuiCatalog.Path`. An empty path,
   `:memory:`, a URI and a directory are an `ArgumentException`. LUI builds every connection itself
   ([0066](./0066-the-catalogue-owns-the-connection.md)).
2. **`Open` checks and writes nothing.** Its connections carry `Mode=ReadWrite`, so the provider never creates a file: a
   missing file is SQLite's refusal (`SQLITE_CANTOPEN`), translated into `LuiCatalogNotFoundException` — after the file
   system confirms that the file is not there: a directory that may not be searched is a problem of rights and stays
   SQLite's error. Nothing is asked **before** the open, so there is no window between the check and the open. A file that is no SQLite database (`SQLITE_NOTADB`) is a
   `LuiCatalogNotFoundException` with that message, not a bare `SqliteException`. A file without `lui_meta` is refused
   with a `LuiCatalogNotFoundException` that names the table; it is not turned into a catalogue.
3. **`OpenOrCreate` creates and sets up completely.** It is the only method that creates, and **it creates the file
   itself** (`FileMode.CreateNew`), before SQLite does — so it knows whether the file is its own:

   | The file | What `OpenOrCreate` does |
   |---|---|
   | is missing | creates it, `PRAGMA journal_mode = WAL`, `lui_meta` |
   | is there — with tables or empty — without `lui_meta` | `lui_meta`; **the journal mode stays** |
   | is there and is no SQLite database | `LuiCatalogNotFoundException` — the file is not touched |
   | is a catalogue | nothing |

   **The journal mode is set once, and only in a file LUI creates itself** (the owner, 2026-11-18; it replaces the rule of
   [0066](./0066-the-catalogue-owns-the-connection.md) §4 as first written — "when the schema is empty"). Of two processes that
   open a missing file at the same time, one creates it and sets the mode; the other finds a file that is there and leaves
   it alone. If the open fails after LUI created the file and the file is still empty — it has no mode yet — LUI deletes it
   (best effort, and the window in which another process has opened the empty file already is narrow), so that the next
   `OpenOrCreate` creates it again. LUI creates the file, not its directory; a directory that may not be written is a
   `LuiReadOnlyException`.
4. **`OpenReadOnly` checks like `Open`, and every connection of the catalogue is read-only** (`Mode=ReadOnly`). It is
   the catalogue's only read-only notion: `Database.ReadOnly` and `ThrowIfReadOnly` disappear, and an architecture test
   says so.
5. **SQLite refuses, LUI translates — and says why.** A refused write (`SQLITE_READONLY`, 8) becomes a
   `LuiReadOnlyException` whose message names the reason: a catalogue opened with `OpenReadOnly` says so, with the operation
   and the index, and names `Open` and `OpenOrCreate`; any other refusal names the file and says that the file or its
   directory may not be written. The exception is derived from SQLite's refusal — it is its `InnerException` — and never
   thrown ahead of it. **A catalogue in write-ahead logging in a directory that may not be written** is refused at the open
   itself — SQLite needs the `-wal` and `-shm` files even to read — and is a `LuiReadOnlyException` as well.
6. **One refusal is LUI's own:** a write in the **transaction of the application** on a catalogue that was opened with
   `OpenReadOnly`. The transaction is on the application's connection, which may write, so SQLite cannot refuse it; LUI
   does, with the same exception, and says the catalogue was opened with `OpenReadOnly`.

## Consequences

- **The API of 0.3.0 breaks** (ADR-0069 (issue #172)): `Open(string connectionString,
  …)` becomes `Open(string path, …)`, `OpenOrCreate` and `OpenReadOnly` are new, `LuiCatalog.ConnectionString` becomes
  `LuiCatalog.Path`.
- **`Open` on a missing file fails.** That is a change of behaviour the compiler does not see: an application that relied
  on `Open` creating the file compiles and fails at its first start on a new machine. The message of
  `LuiCatalogNotFoundException` names `OpenOrCreate`, and the migration note says so.
- **The application's own database becomes a catalogue only when it asks for it** (`OpenOrCreate`), and its journal mode is
  never changed — not when it is empty either.
- **A typo in the path is an error**, not a new database — unless the caller wrote `OpenOrCreate`.
- **One new public type:** `LuiCatalogNotFoundException : LuiException`, carrying `Path`.
- **`LuiReadOnlyException` keeps its place in the public API**; its meaning gets narrower ("SQLite refused this write"),
  and its message tells the developer what to change.
- **No encrypted database, no VFS of its own, no URI file name, no `:memory:`** — the price of the path; deliberate.
- **A file in the rollback journal serialises the reads and the writes** ([0066](./0066-the-catalogue-owns-the-connection.md)).
