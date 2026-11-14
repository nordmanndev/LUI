# 0065 — The read path opens a read-only connection

- **Status:** Proposed
- **Date:** 2026-11-14
- **Builds on:** [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the read path opens an index without
  writing), [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md) (the busy timeout is the wait for a lock),
  [0064](./0064-the-catalogue-owns-the-connection.md) (the catalogue owns the connection)
- **Affects:**
  - `src/Nordmann.LUI/Storage/Database.cs` — the read connection is read-only
  - `src/Nordmann.LUI/Querying/QueryRunner.cs` — the read path opens the read connection
  - `src/Nordmann.LUI/Querying/CandidateReader.cs`, `src/Nordmann.LUI/Querying/HitReader.cs` — the statements are prepared
  - `src/Nordmann.LUI/Indexes/IndexStore.cs` — the file pragma belongs to the writer
  - `docs/api.md`, `CHANGELOG.md`
- **Issue:** the epic of 0.3.0

## Context

**The read path opens a connection per query.** `QueryRunner.Execute` calls `_database.OpenConnection()`, which opens a
connection and runs the pragma statement on it.

**Two proposals were on the table.** One was a *read session*: a catalogue holds prepared read connections in a pool of
its own, a query takes one and gives it back. The other was the plain one: **the read path opens a read-only
connection, the statements are prepared, and nothing else.**

**The plain one wins, and the measurement says why.** All three claims of the plain design were checked
(`probes/lui_probe10/Program.cs`, `probes/lui_probe11/Program.cs`, `probes/lui_probe12/Program.cs`):

| Claim | Measured |
|---|---|
| the pool of the provider does the job of the session | **yes**: the physical open costs **136,6 µs**, the checkout out of the pool **13,0 µs** |
| the provider prepares the statements | **yes**: the same SQL text on the same physical connection costs **20,7 µs** per execution, a *new* text **147,3 µs** (a new plan of the FTS5 query) — the provider keeps a prepared statement per connection and reuses it |
| an explicit `Prepare()` is needed | **no**: with `Prepare()` 0,2125 ms, without 0,2140 ms — the same |
| what the session adds on top | **20,7 µs** and **≈ 2,2 KB** per query: the new command, the two parameters, the binding |

Twenty microseconds and two kilobytes per query are **four to twelve per cent of the latency** of a query over a
million entries (0,65–2,1 ms) and **about five per cent of the allocation budget** (p50 ≤ 60 KB). For that, the
session needs a pool of its own, a lifetime, and a lock. **The plain design is the right one.**

**What the pool does not take over, measured:** the pragma statement runs on every checkout — **11,2 µs and 976 B** per
query. It has to: the pool may hand out a *fresh* physical connection, and a fresh connection has SQLite's defaults,
among them `busy_timeout = 0` — a lock would be refused at once instead of waited for (ADR-0046). The pragma statement
per checkout is the price of the pool, and it is small.

**What the pool costs besides that:** every physical connection has its own page cache. Eight parallel checkouts hold
eight connections, each with the configured `cache_size` — the ceiling of the pool is *the number of parallel queries
times the cache size* (with the default of SQLite's own 2 MB it is negligible; with `CacheSizeMegabytes = 512` it is
4 GB at eight parallel queries). The ceiling is a limit, not a promise: the measured working set grew by 2 MB.

## Decision

**The read path opens a read-only connection. The statements are prepared. Nothing else is held.**

1. **The connection of the read path is read-only.** The read path never writes — that is
   [0033](./0033-the-read-path-opens-an-index-without-writing.md) — and a read-only connection says it to SQLite
   instead of a convention. The connection string of the read path is the connection string of the catalogue with
   `Mode=ReadOnly`; the writer keeps the string of the application.
2. **The file pragma belongs to the writer.** `PRAGMA journal_mode = WAL` is a write, and a read-only connection may
   not do it: *SQLite Error 8: 'attempt to write a readonly database'* (measured). The pragma statement of a
   connection therefore carries the four pragmas of the connection
   ([0064](./0064-the-catalogue-owns-the-connection.md)) and **not** the journal mode.
3. **The statements are prepared.** The provider prepares them once per physical connection and reuses the plan; an
   explicit `Prepare()` is free and states the intent.
4. **The snapshot stays.** The read path keeps one transaction for the candidates and the hits — a writer that commits
   between the two statements could hand a hit the text of a freed entry and the key of a new one. The transaction is
   ended before the connection goes back to the pool.
5. **No session, no pool of LUI's own, no dictionary of statements.** The pool of the provider is the pool.

## Consequences

- **The read path cannot write — not by convention, but by SQLite.** A query that wanted to would fail with
  `SQLITE_READONLY`.
- **The read path can no longer trigger a checkpoint.** A read-only connection does not write the WAL back; the
  automatic checkpoint of the writer (every 1000 pages) is unaffected.
- **A read-only connection needs the `-shm` of a WAL file.** SQLite creates it if the directory may be written; a
  database in a directory that may not be written needs `immutable=1` in the connection string.
- **A read-only connection cannot recover a hot WAL.** After a crash of the writer, the first reader with write access
  recovers it; a reader that only reads waits for it. (Measured: a read-only connection opens a hot WAL without
  complaint; the uncommitted row stays invisible.)
- **The pragma statement runs per query** (11,2 µs). It is the price of the pool, and it is what makes the busy timeout
  of ADR-0046 true on every physical connection.
- **The page cache of the pool is the number of parallel queries times the cache size.** The catalogue says so in the
  API page.
