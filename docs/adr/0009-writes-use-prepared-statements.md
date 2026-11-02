# 0009 — Writes use prepared statements on one writer connection

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `src/Nordmann.LUI/Indexing/WriteStatements.cs`, `src/Nordmann.LUI/Indexing/OwnedIndex.cs`,
  `src/Nordmann.LUI/LuiCatalog.cs`

## Context

The first version of `Upsert` created a command per call: open a pooled
connection, set pragmas, build the statement, execute. Measured through the
public API on 50,000 words in one transaction:

| | per entry |
|---|---:|
| `Upsert` through a fresh command (first version) | **184 µs** |
| the same statement, prepared once and reused | **59 µs** |
| the same without the three triggers (content table, generated columns, four indexes) | 11 µs |

The statement is expensive to *prepare*: SQLite compiles the generated
columns, the indexes and the three triggers into it. At 184 µs the 3,000,000
entry build of §16 takes ten minutes; at 59 µs plus the search index it takes
about three.

## Decision

**The write statements of an index are prepared once per connection and used
again** (`WriteStatements`: upsert, update weight, update priority, remove),
guarded by a lock — a connection is used by one thread at a time.

- **Writes inside the application's transaction** use the statements of the
  application's connection: one set per `SqliteConnection`, kept in a
  `ConditionalWeakTable` so that it lives and dies with the connection.
- **Writes without a transaction** use one long-lived **writer connection** of
  the catalog. SQLite has one writer per file anyway (§15), so a single
  connection loses nothing, and it keeps the prepared statements alive. Reads
  still use their own pooled connections and run in parallel (WAL).
- The writer connection is closed by `Dispose()`, after the running
  operations have finished.

## Consequences

- `Upsert`, `UpdateWeight`, `UpdatePriority` and `Remove` outside a
  transaction are 0.2 ms at the median over a 200,000-entry index
  (`docs/benchmarks.md`).
- Several indexes of one catalog share the writer connection, so they share
  its lock: two threads writing to two indexes are serialised, which they
  would be by SQLite anyway.
- A cached statement refers to the tables of its index. `Drop()` and
  `Ensure()` build a new index object, and with it new statements; SQLite
  re-prepares a statement whose schema changed.
- The queries do not use this: a query opens a pooled connection and prepares
  its statement each time (about 100 µs of a 0.9 ms query). If the budget ever
  needs it, the same technique applies.
