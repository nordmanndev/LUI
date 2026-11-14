# 0066 — The open of a catalogue writes nothing

- **Status:** Proposed
- **Date:** 2026-11-14
- **Builds on:** [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the read path opens an index without
  writing), [0062](./0062-the-creation-names-the-route-create-and-createlinked.md) (the creation names the route),
  [0064](./0064-the-catalogue-owns-the-connection.md) (the catalogue owns the connection)
- **Affects:**
  - `src/Nordmann.LUI/Indexes/IndexStore.cs` — `Initialize` disappears, the check of the file replaces it
  - `src/Nordmann.LUI/LuiCatalog.cs` — the open checks the file instead of writing to it
  - `src/Nordmann.LUI/Storage/Database.cs` — the setup of the file moves to the writer
  - `docs/api.md`, `CHANGELOG.md`
- **Issue:** the epic of 0.3.0

## Context

**The open of a catalogue writes to the file today.** `LuiCatalog.Open` calls `IndexStore.Initialize`, which opens a
connection, runs `PRAGMA journal_mode = WAL` and creates `lui_meta` if it is missing. Both are writes. Two rules of the
repository are in the way:

- the note in [0033](./0033-the-read-path-opens-an-index-without-writing.md) (from [0062](./0062-the-creation-names-the-route-create-and-createlinked.md)):
  **"`Open` writes nothing"**;
- the route of the creation: the builder creates, the runtime path does not ([0062](./0062-the-creation-names-the-route-create-and-createlinked.md)).

**The consequence for a foreign file.** An application that opens its own database with `LuiCatalog.Open("Data
Source=app.db")` gets a `lui_meta` table in it and its journal mode changed — for a file that holds no index at all.
The catalogue then reports that the index is missing, and the file carries the trace of the attempt.

**The journal mode belongs to the file, and the file has an owner.** In the `Embedded` case the application owns the
file; LUI changes the mode of a file it does not own.

## Decision

**The open of a catalogue reads; it does not write.**

1. **The open checks that the file is a catalogue** — the table `lui_meta` has to be there — and says so when it is
   not. A file that is not a catalogue is refused; it is not turned into one.
2. **The writer creates `lui_meta`** — in the route that creates an index ([0062](./0062-the-creation-names-the-route-create-and-createlinked.md)),
   where the other tables are created too.
3. **The file pragmas are applied when LUI sets a file up** ([0064](./0064-the-catalogue-owns-the-connection.md)): a
   file without a table is set up, a file with tables keeps its properties.
4. **The catalogue's own read-only notion goes away.** The connection string decides whether the file may be written,
   and SQLite refuses the write; LUI translates that refusal into `LuiReadOnlyException`. The type stays, its meaning
   gets narrower: it is no longer "the catalogue was opened read-only", it is "SQLite refused this write".

## Consequences

- **`Open` on a file that is not a catalogue fails** with a message that names the reason. Today it succeeds and
  leaves a `lui_meta` table behind.
- **`LuiReadOnlyException` keeps its place in the public API** — the freeze of 0.1.0 is not broken by it, and the
  application that opens the file read-only still gets a clear error when it tries to write.
- **A file in the rollback journal serialises the reads and the writes** ([0064](./0064-the-catalogue-owns-the-connection.md)):
  the read path holds one snapshot, and the snapshot blocks the writer in that mode.
- **The tests that open a catalogue on a fresh file have to create the catalogue first** — the runtime path no longer
  creates what it needs.
