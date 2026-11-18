# 0070 — The catalogue holds no connection

- **Status:** Accepted (owner, 2026-11-18)
- **Date:** 2026-11-18
- **Supersedes in part:** [0009](./0009-writes-use-prepared-statements.md) (the long-lived writer connection of the catalogue)
- **Builds on:** [0015](./0015-the-linked-index-is-loosely-coupled.md), [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md)
  (one writer per file; the busy timeout), [0066](./0066-the-catalogue-owns-the-connection.md) (the catalogue owns the
  connection), [0067](./0067-the-read-path-opens-a-read-only-connection.md) (the read path opens a read-only connection)
- **Affects:**
  - `src/Nordmann.LUI/Storage/Database.cs` — `_writer` and `Writer` disappear; `Dispose` clears the two pools in an order
  - `src/Nordmann.LUI/Indexes/EmbeddedIndex.cs`, `src/Nordmann.LUI/Indexes/EmbeddedSync.cs`,
    `src/Nordmann.LUI/Indexes/EmbeddedCheck.cs`, `src/Nordmann.LUI/Indexes/LinkedIndex.cs` — a write takes a connection from the pool
  - `docs/api.md`, `CHANGELOG.md`
- **Issue:** [#227](https://github.com/nordmanndev/LUI/issues/227)

## Context

**The catalogue held one connection for good.** [0009](./0009-writes-use-prepared-statements.md) gave it a long-lived writer
connection so that the prepared write statements stay alive: the upsert is expensive to *compile* (SQLite compiles the
generated columns, the indexes and the three triggers into it), measured at 184 µs through a fresh command and 59 µs
through a prepared one.

**The owner's decision (2026-11-18): the catalogue should hold no connection.** Nobody needs it, and it cost a defect: the
connections of the read path became read-only ([0067](./0067-the-read-path-opens-a-read-only-connection.md)), and closing
the writer *before* them left the data in the `-wal` and the file empty. A held connection is a lifetime to get right.

**What changed since 0009:** the provider keeps the plan of a statement per physical connection and reuses it (measured for
reads in 0067). Whether that carries the write statements was **not measured** — it is measured here.

## Decision

**The catalogue holds no `SqliteConnection`. Every operation takes a connection from the pool of the provider and gives it
back.**

1. **A write of LUI's own** (`Upsert`, `UpdateWeight`, `UpdatePriority`, `Remove`, and the rest outside a transaction of the
   application) opens a connection with the pragmas of the catalogue (`Mode=ReadWrite`, or the catalogue's mode), runs its
   statement and disposes the connection. The statements are built per call; the provider's plan per physical connection
   does the rest.
2. **A batch** (`UpsertMany`, the writes of `Check`, `Rebuild`, `Optimize`, the writes of a linked index) takes one connection
   for its transaction and gives it back at the end.
3. **A `Sync` takes one connection for its whole run.** The table of the run is temporary, and a temporary table lives and dies
   with its connection: the connection belongs to the **operation**, not to the catalogue.
4. **The lock of the writes stays** (`Database.WriterGate`, a `Lock`, no connection). Two writes of one catalogue run one after
   the other in the process, and wait on the lock instead of on SQLite's busy timeout — one writer per file anyway
   ([0015](./0015-the-linked-index-is-loosely-coupled.md), [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md)).
5. **The transaction of the application** is unchanged: its statements stay prepared per connection of the application
   ([0009](./0009-writes-use-prepared-statements.md) §1).
6. **`Dispose` clears the pool of the read connections first and the pool of the writes last.** Only a connection that may
   write checkpoints the write-ahead log into the file and removes the `-wal` and `-shm` when it is the last one to close; a
   test copies the file alone after `Dispose` and reads it.

## Consequences

- **A single write costs more, and stays far inside its budget.** Measured on the machine of the container this change was
  made in (4 cores, .NET 10.0.14, a local disk, 2026-11-18; 200,000 entries, three runs each):

  | | the held writer (0009) | a connection from the pool |
  |---|---|---|
  | `Upsert`, one at a time, p50 / p95 | 0,034 / 0,112–0,119 ms | 0,088–0,095 / 0,200–0,218 ms |
  | `UpdateWeight`, p50 / p95 | 0,045–0,051 / 0,143–0,181 ms | 0,073–0,077 / 0,203–0,236 ms |
  | `Remove`, p50 / p95 | 0,029–0,032 / 0,112–0,137 ms | 0,037–0,041 / 0,132–0,159 ms |
  | 200,000 entries by `UpsertMany` | 7,9 s | 7,4–7,8 s |
  | `Sync` of 50,000 entries | 7,2–7,5 s | 7,4–7,6 s |

  The budget of `Upsert` / `UpdateWeight` / `Remove` is **p95 ≤ 1 ms**: it holds at one fifth of it. A single `Upsert` is about
  2,5 times slower at the median (the compile of the write statement is the price of a new command per call); a batch and a
  sync, which is where the throughput of a build is, do not change.
- **The catalogue holds nothing that has to be closed in an order** — except the two pools, and `Dispose` knows their order.
- **A write that does not finish before the catalogue is disposed** is waited for, as before (`Begin()` counts the running
  operations); no connection of the catalogue outlives `Dispose`.
- **`PRAGMA` state set on a physical connection by the application** (for instance `wal_autocheckpoint`, which a test
  sets) belongs to that physical connection and goes to whoever the pool hands it to next.
