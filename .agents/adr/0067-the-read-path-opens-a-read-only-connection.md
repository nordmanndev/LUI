# 0067 — The read path opens a read-only connection

- **Status:** Accepted (owner, 2026-11-18)
- **Date:** 2026-11-14, revised 2026-11-15 (the catalogue takes a path; LUI builds the connection)
- **Builds on:** [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the read path opens an index without
  writing), [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md) (the busy timeout is the wait for a lock),
  [0066](./0066-the-catalogue-owns-the-connection.md) (the catalogue owns the connection),
  [0068](./0068-three-ways-to-open-a-catalogue.md) (three ways to open a catalogue)
- **Affects:**
  - `src/Nordmann.LUI/Storage/Database.cs` — the read connection is read-only
  - `src/Nordmann.LUI/Querying/QueryRunner.cs` — the read path opens the read connection
  - `src/Nordmann.LUI/Querying/CandidateReader.cs`, `src/Nordmann.LUI/Querying/HitReader.cs` — the statements are prepared
  - `src/Nordmann.LUI/Indexes/IndexStore.cs`, `src/Nordmann.LUI/Indexes/EmbeddedIndex.cs`,
    `src/Nordmann.LUI/Indexes/LinkedIndex.cs`, `src/Nordmann.LUI/Indexes/TypoSource.cs` — whatever only reads opens the read connection
  - `docs/api.md`, `CHANGELOG.md`
- **Issue:** [#170](https://github.com/nordmanndev/LUI/issues/170), the epic of 0.3.0 ([#166](https://github.com/nordmanndev/LUI/issues/166))

## Context

**The read path opened a connection per query.** `QueryRunner.Execute` called `_database.OpenConnection()`, which opened a
connection in the mode of the catalogue and ran the pragma statement on it. The connection could write — the read path
did not, by convention.

**Two proposals were on the table.** One was a *read session*: a catalogue holds prepared read connections in a pool of
its own, a query takes one and gives it back. The other was the plain one: **the read path opens a read-only
connection, the statements are prepared, and nothing else.**

**The plain one wins, and the measurement says why** (`probes/lui_probe10`, `lui_probe11`, `lui_probe12`):

| Claim | Measured |
|---|---|
| the pool of the provider does the job of the session | **yes**: the physical open costs **136,6 µs**, the checkout out of the pool **13,0 µs** |
| the provider prepares the statements | **yes**: the same SQL text on the same physical connection costs **20,7 µs** per execution, a *new* text **147,3 µs** (a new plan of the FTS5 query) — the provider keeps a prepared statement per connection and reuses it |
| an explicit `Prepare()` is needed | **no**: with `Prepare()` 0,2125 ms, without 0,2140 ms — the same |
| what the session adds on top | **20,7 µs** and **≈ 2,2 KB** per query: the new command, the two parameters, the binding |

Twenty microseconds and two kilobytes per query are **four to twelve per cent of the latency** of a query over a million
entries (0,65–2,1 ms) and **about five per cent of the allocation budget** (p50 ≤ 60 KB). For that, the session needs a
pool of its own, a lifetime, and a lock. **The plain design is the right one.**

**What the pool does not take over:** the pragma statement runs on every checkout — **11,2 µs and 976 B** per query. It
has to: the pool may hand out a *fresh* physical connection, and a fresh connection has SQLite's defaults, among them
`busy_timeout = 0` (ADR-0046).

**What the pool costs besides that:** every physical connection has its own page cache. Eight parallel checkouts hold
eight connections, each with the configured `cache_size` — the ceiling of the pool is *the number of parallel queries
times the cache size* ([0066](./0066-the-catalogue-owns-the-connection.md) bounds the cache size at 1024 MB).

## Decision

**The read path opens a read-only connection. The statements are prepared. Nothing else is held.**

1. **The connection of the read path is read-only — always.** The read path never writes (that is
   [0033](./0033-the-read-path-opens-an-index-without-writing.md)), and a read-only connection says it to SQLite instead
   of a convention. LUI builds it from the path of the catalogue with `Mode=ReadOnly`, pooling on and a private cache —
   whether the catalogue was opened with `Open`, `OpenOrCreate` or `OpenReadOnly`
   ([0068](./0068-three-ways-to-open-a-catalogue.md)). It has a pool of its own, because the provider keeps one pool per
   connection string; `Dispose` clears both. **Whatever only reads uses it** — the read path, `Count`, `Pending`,
   `Triggers`, the comparison of `Check` with the table, the names and definitions in `lui_meta`, the texts for the typo
   dictionary, the check that the file is a catalogue.
2. **The file pragma does not belong to a connection.** `PRAGMA journal_mode = WAL` is a write, and a read-only
   connection may not do it: *SQLite Error 8: 'attempt to write a readonly database'* (measured). The pragma statement of
   a connection carries the three pragmas of the connection ([0066](./0066-the-catalogue-owns-the-connection.md)) and
   **not** the journal mode; the journal mode is set by `OpenOrCreate`, in a file it creates.
3. **The statements are prepared.** The provider prepares them once per physical connection and reuses the plan; an
   explicit `Prepare()` is free and states the intent. The candidates (the prefix path and FTS5) and the hits are
   prepared; a guard reads the sources.
4. **The snapshot stays.** The read path keeps one transaction for the candidates and the hits — a writer that commits
   between the two statements could hand a hit the text of a freed entry and the key of a new one. The transaction is
   ended before the connection goes back to the pool (a test begins a transaction on the next connection of the pool).
5. **No session, no pool of LUI's own, no dictionary of statements.** The pool of the provider is the pool. The
   catalogue holds no connection at all ([0070](./0070-the-catalogue-holds-no-connection.md)); a guard says so.

## Consequences

- **The read path cannot write — not by convention, but by SQLite.** A query that wanted to would fail with
  `SQLITE_READONLY`.
- **The read path can no longer trigger a checkpoint.** A read-only connection does not write the WAL back; the automatic
  checkpoint of the writer (every 1000 pages) is unaffected.
- **A read-only connection needs the `-shm` and `-wal` of a WAL file.** SQLite opens a WAL file read-only when the two
  files exist or can be created. In a directory that may not be written and without them, the open fails — and
  `immutable=1`, the escape of SQLite, is a URI parameter, which the path of
  [0066](./0066-the-catalogue-owns-the-connection.md) does not carry. LUI translates the failure into a
  `LuiReadOnlyException` (a `LuiException`) that names the directory. A catalogue on a read-only medium is a file in the
  rollback journal, or a WAL file shipped with its `-shm`.
- **The writer closes last.** `Dispose` clears the pool of the read connections first and closes the writer after it: only a
  connection that may write checkpoints the write-ahead log into the file and removes the `-wal` and `-shm` when it is the
  last one to close (a test copies the file alone after `Dispose` and reads it).
- **A catalogue that was only read leaves its `-wal` and `-shm` behind.** No connection of it may write, so none of them can
  remove the two files: they stay (the `-wal` empty) next to the file until a connection that may write closes last.
- **A read-only connection cannot recover a hot rollback journal either.** After a crash of the writer the first connection
  that may write rolls the journal back; a reader that only reads waits for it or fails with `SQLITE_BUSY`.
- **A read-only connection cannot recover a hot WAL.** After a crash of the writer, the first connection with write
  access recovers it; a reader that only reads waits for it.
- **The pragma statement runs per query** (11,2 µs). It is the price of the pool, and it is what makes the busy timeout of
  ADR-0046 true on every physical connection.
- **The page cache of the pool is the number of parallel queries times the cache size.** The API page says so.
- **Two pools instead of one:** a catalogue opened with `Open` or `OpenOrCreate` has the pool of its writer connections
  and the pool of its read connections.

## Measurement

Measured with `BudgetPerformance.The_single_word_index_at_three_million_entries` on the machine of the container this
change was made in (4 cores, .NET 10.0.14, a local disk, 2026-11-18; the scale of the test is 200,000 entries — the
numbers are for the comparison, not for the budgets of the README): before this change (`3dada4f`) and after it.

| | before | after |
|---|---|---|
| query, p50 / p95 / max | 0,49 ms / 1,22 ms / 3,8 ms | 0,47 ms / 0,96 ms / 1,6 ms |
| allocation per query, p50 / p95 | 24 KB / 43 KB | 24 KB / 43 KB |
| corrected query, p50 / p95 / max | 0,56 ms / 2,08 ms / 6,1 ms | 0,58 ms / 2,11 ms / 6,3 ms |
| allocation of a corrected query, p50 / p95 | 21 KB / 55 KB | 21 KB / 55 KB |

`Prepare()` itself is a statement of intent: measured with and without it, a query takes the same time and allocates the
same. The read path is no slower and allocates no more: the budget of the allocation (p50 ≤ 60 KB) and of the latency hold.
