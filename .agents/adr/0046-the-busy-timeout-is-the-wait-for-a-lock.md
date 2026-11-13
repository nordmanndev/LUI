# 0046 — The busy timeout of the catalog is the wait for a lock

- **Status:** Accepted
- **Date:** 2026-11-08
- **Builds on:** [0015](./0015-the-linked-index-is-loosely-coupled.md) (a long `Sync` holds the write lock; writers set a
  longer busy timeout), [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze of 0.1.0)
- **Affects:**
  - `src/Nordmann.LUI/Storage/Database.cs` (the timeout of the commands of every connection of the catalog)
  - `src/Nordmann.LUI/LuiCatalogOptions.cs` (what `BusyTimeout` promises)
  - `tests/Nordmann.LUI.Integration.Tests/BusyTimeoutTests.cs` (the wait, measured; the long statement)
  - `tools/mutation-check.sh`, ADR-0046 and ADR-0046, `docs/api.md`, the CHANGELOG
- **Issue:** #70

## Context

`LuiCatalogOptions.BusyTimeout` promises: "how long a connection waits for a lock before SQLite reports `SQLITE_BUSY`".
Issue #70 measured that it does not: with `BusyTimeout = 1 s`, a write that waits for the lock of a reader waits
**31 s**, and the `PRAGMA busy_timeout` of the connection reads `1000`.

The issue guessed that the provider overwrites the pragma. **The source of Microsoft.Data.Sqlite 10 says otherwise**
(`SqliteDataReader.NextResult`, `SqliteCommand.PrepareAndEnumerateStatements`): the provider never touches
`sqlite3_busy_timeout`. It **retries** a command that SQLite answers with `SQLITE_BUSY` or `SQLITE_LOCKED` — sleeps
150 ms, steps again — until `CommandTimeout` has passed (30 s by default; `0` means forever). Every retry waits the busy
timeout of SQLite once more. 30 s of retries on top of the pragma: 31 s.

The issue also feared that a short `CommandTimeout` would interrupt the long statements (a sync of a million rows). The
same source shows it does not: the timeout is checked only inside the loop of a **busy** step. A statement that runs
is never interrupted by it. The Microsoft documentation says the same: *"The timeout is used when the command is
waiting to obtain a lock on the table."*

## Decision

**Every connection of the catalog sets `DefaultTimeout = 1`** — the smallest value of the provider that is not
"forever" — right after it opens, before its first command. Every command of the connection inherits it, the
commands of the provider's own (`BeginTransaction`) too.

- The wait for a lock is the **busy timeout of SQLite** (`PRAGMA busy_timeout = BusyTimeout`), as the option promises.
  After it, the provider looks at the clock, sees that more than a second has passed, and stops retrying.
- **The floor is one second.** A busy timeout below a second waits about one second: the provider retries until its
  timeout of one second has passed. `BusyTimeout` stays "not negative" — the option keeps its shape, and its page says
  what a value below a second does.
- A statement that runs longer than a second is not interrupted — a test holds it (a statement of four seconds runs to
  its end).

Measured (`BusyTimeoutTests`, a reader holds its snapshot without the write-ahead log):

| `BusyTimeout` | before | after |
|---:|---:|---:|
| 1,000 ms | 31 s | 1,011 ms |
| 2,000 ms | 30 s | 2,006 ms |
| 200 ms | 30 s | 1,259 ms (the floor) |

## Consequences

- **A writer that waits for a lock now waits what its catalog says** — five seconds by default, not 30. An application
  whose writer waited for a long `Sync` of a linked index (26.7 s for a million rows, ADR-0015) and got through only
  because of the 30 s of the provider now gets `SQLITE_BUSY` after its busy timeout. That is the contract ADR-0015
  already wrote down ("writers set a longer busy timeout"); the CHANGELOG names it under *Fixed*, with the way out.
- The fix is internal: no public member changes, and the option keeps its name, its type and its default (ADR-0026).
- The mutation `the-provider-retries-for-thirty-seconds` takes the line out — the test of the wait has to notice.

## Alternatives

| The alternative | Why not |
|---|---|
| A retry loop of our own around every write, with `BusyTimeout` as the deadline | The provider has the loop already. Two loops are the 31 s of the issue in another shape. |
| `DefaultTimeout = ceil(BusyTimeout in seconds)`, the pragma at 0 | The wait becomes whole seconds and the retries sleep 150 ms — a busy timeout of 1.5 s would wait 2 s, and every value would be rounded. The pragma waits to the millisecond. |
| `DefaultTimeout = ceil(BusyTimeout in seconds)`, the pragma as it is | Every retry waits the pragma again: 1.5 s becomes about 3.2 s. |
| The timeout in the connection string (`Default Timeout=1`) | The connection string is the application's (ADR-0033: it says what the file allows). The catalog sets the property on its own connections and leaves the string alone. |
| A `BusyTimeout` of at least one second, validated | A break of the shape of an option for a floor that the documentation states as well. |
