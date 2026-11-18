# 0066 — The catalogue owns the connection: a path, and the settings are typed

- **Status:** Accepted (owner, 2026-11-15)
- **Date:** 2026-11-14, revised 2026-11-15 (the owner's decision: a path instead of a connection string, only the settings that make sense)
- **Builds on:** [0009](./0009-writes-use-prepared-statements.md) (the writes use prepared statements on one writer
  connection), [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze — lifted for the API of
  0.3.0 by ADR-0069), [0032](./0032-the-allocation-of-a-query.md) (the
  allocation of a query), [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the read path opens an index
  without writing), [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md) (the busy timeout is the wait for a lock)
- **Affects:**
  - `src/Nordmann.LUI/LuiPragmas.cs`, `src/Nordmann.LUI/LuiSynchronous.cs` — the new types
  - `src/Nordmann.LUI/LuiCatalogOptions.cs` — the pragmas move into the new type, two members go
  - `src/Nordmann.LUI/Storage/Database.cs` — LUI builds the connections; the pragmas once per connection, the file pragma once per file
  - `src/Nordmann.LUI/Indexes/IndexStore.cs` — the set-up of the file moves to `OpenOrCreate`
  - `src/Nordmann.LUI/LuiCatalog.cs` — the entry points take a path (ADR-0068)
  - `src/Nordmann.LUI.Hosting/LuiServiceCollectionExtensions.cs` — `AddLui` takes a factory
  - `docs/api.md`, `CHANGELOG.md`
- **Issue:** [#169](https://github.com/nordmanndev/LUI/issues/169) (ADR-0066), the epic of 0.3.0 ([#166](https://github.com/nordmanndev/LUI/issues/166))

## Context

**The catalogue is the wrapper for the SQLite file and the only way into it.** Every index of a catalogue lives in the
same file. The connection settings therefore belong to the catalogue and to nothing below it — an index has no
connection of its own to configure.

**Three layers, and where every parameter belongs:**

| Layer | What it is | Its properties | Who owns it |
|---|---|---|---|
| **the file** | the SQLite database that holds the catalogue — or, in `Embedded` and `Linked`, the application's own database | `journal_mode`, the schema | the file outlives every connection; LUI sets the journal mode only of a file it creates itself (§4) |
| **the connection** | a handle to the file, with its own state | the mode, the pooling, the cache mode, `busy_timeout`, `synchronous`, `cache_size`, the transaction | **LUI**: the **writer** (read-write, one per catalogue) and the **read connections** (read-only, one per query) |
| **the statement** | one SQL statement with its parameters | the text, the prepared form, the bound values | the read path; three statements, constant |

**Today the application hands LUI a connection string, and with it every knob of the provider.** Some of them are
harmless, some cost: `Pooling=False` makes every query open a physical connection (136,6 µs instead of 13,0 µs,
ADR-0067); `Cache=Shared` puts the connections of one process into
SQLite's shared-cache mode, with table locks (`SQLITE_LOCKED`, which the busy timeout does not wait for) instead of the
concurrency of WAL; `Default Timeout` looks like the busy timeout and is not — measured, a connection with only the
provider's keywords waits **0 ms** for a lock. Beside the string, `LuiCatalogOptions` carries four pragmas, named
differently, and a reader cannot tell which of the two places applies to which connection.

**The connections are LUI's, not the application's.** The application never holds one: the writer and the read
connections are opened, configured and closed by LUI, and LUI's guarantees (the busy timeout of ADR-0046, the read-only
read path of ADR-0067, the budgets) depend on how they are opened.

**The journal mode is a property of the file, and LUI changes it when it opens.** `PRAGMA journal_mode = WAL` is a write.
It runs in `IndexStore.Initialize`, which `LuiCatalog.Open` calls — in the `Embedded` case on the application's own
database. **The answer cannot be a question.** It has to be a rule that gives the right answer without asking — and it
has to look at what is **in** the file: "the file did not exist" cannot be observed after the provider's first
connection has created it.

## Decision

### 1. The catalogue takes a path; LUI builds the connections

`Open`, `OpenOrCreate` and `OpenReadOnly` (ADR-0068) take the path of the file
and `LuiCatalogOptions`. LUI builds every connection from the path: `Data Source` is the full path, the `Mode` follows
the method and the kind of connection, **pooling is on, the cache is private** — fixed, not a setting.

### 2. The settings are typed, and only the ones that make sense exist

```csharp
public sealed record LuiPragmas
{
    public TimeSpan BusyTimeout { get; init; } = TimeSpan.FromSeconds(5);      // PRAGMA busy_timeout — 1 s to 10 min
    public LuiSynchronous Synchronous { get; init; } = LuiSynchronous.Normal;  // PRAGMA synchronous — Normal or Full
    public int? CacheSizeMegabytes { get; init; }                              // PRAGMA cache_size — 1 to 1024, per connection
}

public enum LuiSynchronous { Normal, Full }
```

`LuiCatalogOptions.Pragmas` carries them; `PayloadSerializer` and `TimeProvider` stay; nothing else. **A setting that
makes no sense or degrades the library does not exist:** `mmap_size` (measured: no gain), `synchronous = OFF` (the file
is at risk on a power loss), `synchronous = EXTRA` (cost without gain in WAL), the pooling, the shared cache. Every value
is checked when the catalogue is opened; a value out of range is an `ArgumentOutOfRangeException` before any connection
is opened.

### 3. Two classes of pragmas

| Class | Pragmas | When |
|---|---|---|
| **the connection** | `busy_timeout`, `synchronous`, `cache_size` | on every connection LUI opens, once |
| **the file** | `journal_mode = WAL` — **not a parameter** | once, when `OpenOrCreate` creates the file itself (§4) |

**A read-only connection cannot set the journal mode.** Measured (`probes/lui_probe8/Program.cs`, part C): `PRAGMA
journal_mode = WAL` on a read-only connection fails with *SQLite Error 8: 'attempt to write a readonly database'*, and
it would take the working pragmas of the statement with it. The read path opens read-only
(ADR-0067) ⇒ **the file pragma is never in the pragma statement of a
connection.**

### 4. The journal mode is set once, when LUI creates the file

**LUI starts SQLite in WAL mode — when it creates the file itself. A file that is there keeps its mode, empty or not.**
LUI sets it once and never changes it again; there is no parameter, and nothing overrides it. The rule is the owner's
(2026-11-18): it replaces the earlier one ("when the schema of the file is empty"), which put WAL into the fresh,
still empty file of an application that had chosen its own mode.

| Case | What happens |
|---|---|
| `OpenOrCreate`, the file is missing | LUI creates the file (exclusively, before SQLite does) and sets the journal mode: WAL |
| `OpenOrCreate`, the file is there — with tables, or empty | the mode stays what it is |
| `Open`, the file is there | the mode stays what it is |
| `Open`, `OpenReadOnly`, the file is missing | `LuiCatalogNotFoundException` — nothing is created |
| `OpenReadOnly`, the file is there | nothing is written (ADR-0068) |

**LUI creates the file itself** (`FileMode.CreateNew`) instead of letting the provider do it, because "the file did not
exist" cannot be observed after the provider's first connection has created it. Of two processes that open a missing
file at the same time, one creates it and sets the mode; the other finds a file that is there and leaves it alone.
If the opening fails after LUI created the file and the file is still empty — it has no mode yet — LUI deletes it
(best effort), so that the next `OpenOrCreate` creates it again and sets the mode. LUI creates the file, not its
directory: a missing directory is a `DirectoryNotFoundException`.

### 5. The pragmas are applied on every connection

The pragma statement runs when a connection is opened — and the read path opens one per query
(ADR-0067). **That is the price of the pool of the provider, and it
is what makes the busy timeout true on every physical connection:** the pool may hand out a fresh physical connection,
and a fresh connection has `busy_timeout = 0`. Measured: the pragma statement costs **11,2 µs and 976 B** per query.

### 6. The host takes a factory

`AddLui(Func<IServiceProvider, LuiCatalog> open)` replaces the two overloads with a connection string. The host does not
mirror the three ways to open; the application writes the one it means — `services.AddLui(_ =>
LuiCatalog.OpenOrCreate("app.db"))`. The container owns the catalogue and disposes it.

## Consequences

- **The API of 0.3.0 breaks** (ADR-0069): the entry points take a path;
  `BusyTimeout` and `CacheSizeMegabytes` move into `LuiPragmas`; `MemoryMapMegabytes` and `WriteAheadLog` disappear;
  `AddLui` takes a factory.
- **No application can degrade LUI through a keyword** — the pooling, the cache mode and the mode of a connection are
  LUI's.
- **No encrypted database, no VFS of its own, no URI file name, no `:memory:`.** Deliberate: a setting comes back only
  when an application needs it, and then as a typed one with a measured reason.
- **The page cache of the read path is the number of parallel queries times `CacheSizeMegabytes`** — the upper bound of
  1024 MB keeps a typo from becoming gigabytes, and the API page names the product.
- **A catalogue in a file that is not in WAL serialises the writes of the application.** Measured, a writer next to a
  query waits **1,81 ms** in WAL and **5,65 ms** in the rollback journal; a query longer than the busy timeout makes the
  write fail with `SQLITE_BUSY`. The application that shares its file with LUI puts it in WAL — the file is the
  application's.
- **The typed pragmas are the place where the next setting goes** — with a measurement that says why it should exist.

---

**Der rote Beweis zuerst** (Repo-Regel): erst der Test, der auf dem alten Code rot ist, dann der Fix. Der rote Zwischenstand bleibt im Verlauf des Branches stehen.

**Der Nachweis am Ende:** `dotnet build -c Release`, die vier Testsuites, `dotnet pack`. Die vollständige Folge steht im Epic #166.
