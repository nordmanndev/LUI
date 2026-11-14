# 0064 — The catalogue owns the connection: the pragmas are a type with defaults

- **Status:** Proposed
- **Date:** 2026-11-14
- **Builds on:** [0009](./0009-writes-use-prepared-statements.md) (the writes use prepared statements on one writer
  connection), [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze — lifted for the API of
  0.3.0 by [0067](./0067-the-freeze-of-0-1-0-is-lifted-for-0-3-0.md)), [0032](./0032-the-allocation-of-a-query.md) (the
  allocation of a query), [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the read path opens an index
  without writing), [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md) (the busy timeout is the wait for a lock)
- **Affects:**
  - `src/Nordmann.LUI/LuiPragmas.cs` — the new type and its two enumerations
  - `src/Nordmann.LUI/LuiCatalogOptions.cs` — the pragmas move out of the options into the new type
  - `src/Nordmann.LUI/Storage/Database.cs` — the pragmas are applied once per connection, the file pragmas once per file
  - `src/Nordmann.LUI/Indexes/IndexStore.cs` — the setup of the file moves to the writer
  - `src/Nordmann.LUI/LuiCatalog.cs` — the entry point keeps its signature
  - `docs/api.md`, `CHANGELOG.md`
- **Issue:** the epic of 0.3.0

## Context

**The catalogue is the wrapper for the SQLite file and the only way into it.** `LuiCatalog.Open(connectionString,
options?)` is the single public entry point, and every index of a catalogue lives in the same file. The connection
settings therefore belong to the catalogue and to nothing below it — an index has no connection of its own to configure.

**Three layers, and where every parameter belongs.** The confusion this record settles is a confusion of layers:

| Layer | What it is | Its properties | Who owns it |
|---|---|---|---|
| **the file** | the SQLite database that holds the catalogue — or, in `Embedded` and `Linked`, the application's own database | `journal_mode`, the schema, and in principle `page_size` and `auto_vacuum` | the file outlives every connection; LUI holds the journal mode of every file it writes in (§3) |
| **the connection** | a handle to the file, with its own state | `busy_timeout`, `synchronous`, `cache_size`, `mmap_size`, `foreign_keys`, the transaction | LUI opens two kinds: the **writer** (read-write, one per catalogue) and the **read connections** (read-only, one per query) |
| **the statement** | one SQL statement with its parameters | the text, the prepared form, the bound values | the read path; three statements, constant |

The parameters of Microsoft.Data.Sqlite describe the **connection** — and, with `Data Source`, the **file**. The pragmas
of LUI describe the **connection** too, and one of them (`journal_mode`) the **file**. That is why the two kinds have to
be told apart inside LUI: a pragma of the file cannot be sent to a connection that may not write.

**Today the settings are in two places, and neither says which connection it belongs to.** The connection string carries
Microsoft.Data.Sqlite's keywords — the file (`Data Source`), the mode (`Mode`), the cache (`Cache`), the pooling, the
key of an encrypted database (`Password`), the VFS. `LuiCatalogOptions` carries four pragmas: `BusyTimeout`,
`CacheSizeMegabytes`, `MemoryMapMegabytes` and `WriteAheadLog`. The two places are named differently, and a reader of
the API page cannot tell which of them applies to which connection.

**Measured: the two places do not collide.** What the provider does with its keywords, and what LUI's pragma statement
does on top of it (`probes/lui_probe8/Program.cs`, the values are read back from a connection):

| | only the keywords of the provider | after the pragmas of LUI |
|---|---|---|
| `busy_timeout` | **0** | **5000** |
| `synchronous` | 2 (FULL) | **1** (NORMAL) |
| `cache_size` | **−2000** (SQLite's own 2 MB) | **−524288** |
| `mmap_size` | 0 | **268435456** |
| `journal_mode` | delete | delete (LUI does not set it here) |
| `foreign_keys` | **1** (the keyword `Foreign Keys=True`) | 1 |
| `recursive_triggers` | **1** (the keyword) | 1 |

**The provider sets none of the four pragmas LUI owns.** `Cache=Shared` is the *shared cache mode* of SQLite, not the
page cache — the page cache stays at SQLite's own −2000. `Default Timeout` is the provider's own retry, not SQLite's
`busy_timeout`: the connection still waits 0 ms for a lock. So LUI's pragma statement always wins, and there is no
precedence to remember. The two places are not two answers to one question: **the keywords of the provider describe the
handle, the pragmas of LUI describe the work.**

**Every connection LUI opens is set up again.** `Database.OpenConnection()` builds the pragma statement and runs it on
each connection. The read path opens a connection per query (`QueryRunner.Execute`), so the pragmas are paid per query,
although they do not change between two queries of the same catalogue.

**The journal mode is a property of the file, not of the connection — and LUI changes it when it opens.** `PRAGMA
journal_mode = WAL` is a write. It runs in `IndexStore.Initialize`, which `LuiCatalog.Open` calls: the open of a
catalogue writes to the file today, twice (the journal mode and `CREATE TABLE IF NOT EXISTS lui_meta`). The note in
[0033](./0033-the-read-path-opens-an-index-without-writing.md) says "`Open` writes nothing" — the code says otherwise. In
the `Embedded` case the file is the application's own database, and LUI changes the mode of a file it does not own.

**The owner asks the question that settles it:** when LUI creates the file for the first time, somebody has to say
whether it is in WAL mode; in the `Embedded` case the file is already there, and the application cannot be asked.
**The answer cannot be a question.** It has to be a rule that gives the right answer in both cases without asking.

## Decision

### 1. The pragmas are a type with defaults

```csharp
public sealed record LuiPragmas
{
    public TimeSpan BusyTimeout { get; init; } = TimeSpan.FromSeconds(5);   // PRAGMA busy_timeout
    public LuiSynchronous Synchronous { get; init; } = LuiSynchronous.Normal; // PRAGMA synchronous
    public int? CacheSizeMegabytes { get; init; }                            // PRAGMA cache_size
    public int? MemoryMapMegabytes { get; init; }                            // PRAGMA mmap_size
}
```

`LuiPragmas` carries the pragmas LUI owns, each with its default, and it hangs on the catalogue:
`LuiCatalogOptions.Pragmas`. The enumeration `LuiSynchronous` names the values SQLite knows; it is a new public type.

### 2. Two classes of pragmas

| Class | Pragmas | When |
|---|---|---|
| **the connection** | `busy_timeout`, `synchronous`, `cache_size`, `mmap_size` | on every connection LUI opens, once |
| **the file** | `journal_mode = WAL` — **not a parameter** | whenever the writer opens a file it may write |

The distinction is not a detail: `busy_timeout` is a property of a connection, `journal_mode` is a property of the file.
A connection that asks for a different journal mode than the file has gets the file's mode — SQLite ignores it.

**And it is not a convention either — a read-only connection cannot set the journal mode.** Measured
(`probes/lui_probe8/Program.cs`, part C):

```
PRAGMA busy_timeout = 5000          OK
PRAGMA synchronous = NORMAL         OK
PRAGMA cache_size = -524288         OK
PRAGMA mmap_size = 268435456        OK
PRAGMA foreign_keys = ON            OK
PRAGMA journal_mode = WAL           FEHLER: SQLite Error 8: 'attempt to write a readonly database'.
```

The read path of LUI opens its connection read-only ([0065](./0065-the-read-path-opens-a-read-only-connection.md)) ⇒ **the file
pragmas may not be in the pragma statement of a connection** — they would fail the whole statement and take the four
working pragmas with it. The two classes are what keeps the pragma statement of a read connection legal.

### 3. The journal mode is set once, when LUI creates the file

**LUI starts SQLite in WAL mode — when LUI creates the file. A file that exists keeps its mode.** The journal mode is
a setting of the file, and it cannot be undone; there is no parameter, and nothing overrides it.

The writer therefore sets `PRAGMA journal_mode = WAL` exactly once: when the catalogue creates the database file — the
file was not there before the catalogue opened it. Every later open finds the file and leaves its mode alone.

| Case | What happens |
|---|---|
| the catalogue creates the file | the journal mode is set: WAL |
| the file is there (the application shares it with LUI) | the mode stays what the application gave it — LUI does not change it, and it does not change it back |

A read-only connection never sets it: it may not (`SQLite Error 8: 'attempt to write a readonly database'` — the
measurement in the Context). That is why the file pragma is not in the pragma statement of a connection.

### 4. The entry point keeps its signature

`LuiCatalog.Open(connectionString, options?)` stays. The connection string keeps the keywords of the provider (the
file, the mode, the key of an encrypted database, the VFS) — it is the escape hatch, and a typed connection object
would have to name every keyword of the provider to keep it. The typed part is the part LUI owns: the pragmas.

### 5. The pragmas are applied on every connection

The pragma statement runs when a connection is opened — and the read path opens one per query
([0065](./0065-the-read-path-opens-a-read-only-connection.md)). **That is the price of the pool of the provider, and
it is what makes the busy timeout true on every physical connection:** the pool may hand out a connection that was
opened a moment ago and never saw a pragma (SQLite's defaults among them: `busy_timeout = 0` — a lock would be
refused at once instead of waited for, ADR-0046). Measured: the pragma statement costs **11,2 µs and 976 B** per
query.

## Consequences

- **The API of 0.3.0 breaks** ([0067](./0067-the-freeze-of-0-1-0-is-lifted-for-0-3-0.md)): `BusyTimeout`,
  `CacheSizeMegabytes` and `MemoryMapMegabytes` leave `LuiCatalogOptions` and enter `LuiPragmas`; `WriteAheadLog`
  **disappears** — the journal mode is not a parameter. Three members move, one goes; the rest of the options
  (`PayloadSerializer`, `TimeProvider`) stay where they are.
- **The open of a catalogue writes nothing** ([0066](./0066-the-open-of-a-catalogue-writes-nothing.md)): the note in
  ADR-0033 becomes true, and a foreign file is no longer polluted with a `lui_meta` table.
- **A catalogue in a file that is not in WAL serialises the writes of the application.** The read path holds one
  snapshot for the candidates and the hits, and a snapshot in the rollback journal blocks the writer until the query
  ends: measured, a writer that writes next to a query waits **1,81 ms** in WAL and **5,65 ms** in the rollback
  journal; a query that runs longer than the busy timeout makes the write fail with `SQLITE_BUSY`. The application
  that shares its file with LUI puts it in WAL — it is a property of the file, and the application owns it.
- **`mmap_size` buys nothing.** Measured: no gain on a local disk (0,490 → 0,474 ms), a loss of 4 % on a network
  mount (3,440 → 3,305 ms); it costs 4 µs per connection open. The knob stays (it is a file property an application
  may want), it is applied once per connection, and the API page says what it buys.
- **The typed pragmas are the place where the next pragma goes** — the type is the answer to "where does a pragma
  belong?", not a container that has to be extended.
