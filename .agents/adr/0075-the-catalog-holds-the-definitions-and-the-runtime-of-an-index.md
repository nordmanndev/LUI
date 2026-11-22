# 0075 — The catalog holds the definitions and the runtime of an index, and the handle is thin

- **Status:** Accepted (owner, 2026-11-20)
- **Date:** 2026-11-21
- **Supersedes:** the limitation recorded in the consequences of [0073](./0073-the-index-definition-and-the-typed-handles.md) (a handle of an index that was dropped and created again under the same name is not told apart from the new index)
- **Builds on:** [0073](./0073-the-index-definition-and-the-typed-handles.md) (the definition and the typed handles; the LUI format version),
  [0033](./0033-the-read-path-opens-an-index-without-writing.md) (opening an index writes nothing),
  [0067](./0067-the-read-path-opens-a-read-only-connection.md) (the read path uses a read-only connection),
  [0070](./0070-the-catalogue-holds-no-connection.md) (the catalog holds no connection),
  [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md) (one writer at a time, the busy timeout is the wait),
  [0015](./0015-the-linked-index-is-loosely-coupled.md) (the linked index; content is the application's)
- **Affects:**
  - `src/Nordmann.LUI/LuiCatalog.cs` — the definitions in memory, the cache of the handles, `Create`, `Index`, `DropIndex`
  - `src/Nordmann.LUI/Indexes/StoredIndexes.cs` — the stored definitions in memory, as snapshots of one SQLite schema counter
  - `src/Nordmann.LUI/Indexes/IndexRuntimes.cs`, `src/Nordmann.LUI/Indexes/IndexRuntime.cs` — the runtime of an index, built once per definition object
  - `src/Nordmann.LUI/Indexes/IndexGuard.cs`, `src/Nordmann.LUI/Storage/IConnectionGuard.cs` — the check of every operation
  - `src/Nordmann.LUI/Indexes/IndexStore.cs`, `src/Nordmann.LUI/Indexes/IndexStoreEntry.cs` — all definitions in one read, the checks of a stored entry
  - `src/Nordmann.LUI/Indexes/EmbeddedIndex.cs`, `src/Nordmann.LUI/Indexes/LinkedIndex.cs`, `src/Nordmann.LUI/Indexes/EmbeddedSync.cs`, `src/Nordmann.LUI/Indexes/EmbeddedCheck.cs`, `src/Nordmann.LUI/Querying/QueryRunner.cs` — the guard on the connection, in the transaction of the operation
  - `src/Nordmann.LUI/Storage/Database.cs`, `src/Nordmann.LUI/Storage/CommandCounter.cs`, `src/Nordmann.LUI/Storage/CountingConnection.cs` — `ReadSchemaCounter`, the counting seam of the tests
  - `docs/api.md`, `docs/testing.md`, `CHANGELOG.md`, `.agents/adr/README.md`, `.agents/adr/0073-the-index-definition-and-the-typed-handles.md`
  - `tools/mutation-check.sh` — the mutations of the guard, the handle and the missing name
- **Issue:** [#265](https://github.com/nordmanndev/LUI/issues/265), part of the epic of 0.4.0 ([#258](https://github.com/nordmanndev/LUI/issues/258))

## Context

With the decision that indexes are always opened through the catalog (0073), `catalog.Index(recipes)` runs on every
request. Measured on 0.3.0 (20,000 entries, review of 2026-11-20): an open call costs about 23 µs and 20,376 B; the read of
`lui_meta` is 1.6 µs of it. The rest is the object graph that every open call built anew — the index object *was* the runtime:
the query runner with its candidate reader, typo correction and hit reader (two arrays of 1,001 SQL strings), the sync, the check,
the source. A query through a fresh handle allocated 44.5 KB instead of 22.8 KB: the open call doubled the cost of a request.
Holding the definitions in memory alone would have saved 1.6 µs of the 23.

A catalog that holds definitions in memory has a second problem: another process can drop an index, create it, or create it again
with another definition. A handle must never read or write through a stale definition, and ADR-0073 left exactly this open: a
handle of an index that was dropped and created again under the same name worked on the new index.

## Decision

1. **The catalog holds the stored definitions and the runtime of each index — not state.** `Open`, `OpenOrCreate` and `OpenReadOnly`
   read every row of `lui_meta` once into memory (`StoredIndexes`). Only the definitions: what changes with the data — the content,
   the typo dictionary, `NeedsInitialSync` (#272), the sync lease (#271) — is read from the file when it is asked for, so what another
   process writes is always visible. A row that cannot be read (a kind this version does not know) is recorded as unreadable and says
   what is wrong with it when its index is asked for; it does not keep the catalog from opening.
2. **The runtime is built once per definition object and lives with it** (`IndexRuntimes`): the source, the rules, the query runner
   (candidate reader, typo correction, hit reader with its prepared SQL), the sync and the check — built from the stored
   definition, with the code the definition brings: its similarity and the map of its payload, which are runtime only (0073 §6).
   The catalog keeps it **with the handle, in the table keyed by the definition object** (a `ConditionalWeakTable`), not in a
   dictionary of its own: a host may build a definition per request, with a similarity or a map that captures something, and a
   runtime of about 20 KB per request that outlived its definition would be a leak for as long as the catalog lives. (An earlier
   draft keyed the runtimes by name, type, stored definition, similarity and map in a strong dictionary; it grew with every
   definition object, and kept one dead runtime per recreate.) Two definition objects of one index each have a runtime of their
   own — they are two views of the same index. The typo dictionary stays per catalog, name **and stored definition** (`TypoDictionaries`): it is built from the texts, the
   normalisation and the distance of one definition, so an index that another process created again under the same name with another
   one gets a dictionary of its own and the old one is let go; it is not duplicated in the runtime. A runtime whose guard failed is dropped from the table at the next open call.
3. **A handle is thin and cached per definition.** `catalog.Index(definition)` and `catalog.Create(definition)` return **the same
   handle instance for the same definition object**. At the first call the stored definition is found in memory and compared with the
   declared one, once; after that the call is a lookup in a `ConditionalWeakTable` — **0 B allocated, no `SqliteCommand`**
   (tests count both). The table does not keep a definition alive. The old calls — `Index(name)`, `Create(name, …)`,
   `LinkedIndex(name)` — keep building their own index object each time, unchanged, until #268 removes them.
4. **The two schema versions are named apart.** *SQLite's schema counter* is what `PRAGMA schema_version` reads: a number in the
   header of the file that changes whenever a table, an index or a trigger is created or dropped, and not when content is written.
   *The LUI format version* is `lui_meta.schema_version` (0073 §16): a value in a row that says in which format an index was written.
   They have nothing to do with each other, and the code, this record and the pages say "SQLite schema counter" and "LUI format
   version".
5. **Every operation of a handle checks the counter, on its own connection, before its first statement — and the check and the
   work are one step.** The mechanism is `PRAGMA schema_version`: one statement, no table read, on the connection the operation
   uses and inside the transaction the work runs in, so that the answer is true for the work: a write of LUI's own — a single
   `Upsert`, `UpdateWeight`, `UpdatePriority`, `Remove`, a batch, a sync batch, a rebuild — is `BEGIN IMMEDIATE`, check, work, commit:
   nobody can drop the index, or create it again, between the check and the statement, because the write lock is held (another
   process's drop waits for the commit); a read — a query, `Count`, `Pending`, the triggers — checks in the deferred snapshot it
   reads in; a check or a sync checks in each of its transactions; a write with a `transaction` of the application checks on the
   application's connection, in its transaction, after the transaction was found to be of this database. Only the typo
   dictionary reads on a connection of its own: it checks first and **after** — a dictionary built while the index was dropped or
   created again with another definition is let go and the handle throws — and a missing table under it is explained by a check, so
   that it surfaces as the proper exception and not as a bare SQLite error. When the counter differs, the guard judges **the row as
   the snapshot of the operation sees it** (it reads `lui_meta` on the connection and in the transaction of the operation), not the
   newest definitions: a read that pinned its snapshot between a drop and a create ends in the exception of that snapshot. The guard of the runtime (`IndexGuard`) compares it with the
   counter it validated at. Equal: go on. Different: the guard looks at the definitions of the catalog — reloaded from
   `lui_meta` — by each guard whose counter differs from the one of the snapshot, which costs a read and nothing else — and
   - the index is gone: the operation throws `LuiIndexNotFoundException`;
   - the stored definition is another one: `LuiDefinitionMismatchException`, naming the fields;
   - the index was written in another LUI format, with another key type or another folding: `LuiDefinitionMismatchException`;
   - the same declaration: the handle goes on — an index that was dropped and created again with the same definition is not stale
     (its typo dictionary is that of the process, `RebuildTypoDictionary()` brings the words of the new content).

   The guard **keeps the failure**: every later operation of that handle throws again, and the next open call builds a new runtime
   and a new handle (the failure is kept as a record, the exception is made anew every time). The operations that write or check inside
   transactions of their own check inside each of them; only the typo dictionary, which reads on a connection of its own, takes a read
   connection for the check. A change
   of the application's own tables only causes a harmless reload. A counter that was read inside a
   transaction of the application is that transaction's — it may have changed the schema itself, and may be rolled back — and is
   **never published** to the other handles; the guard remembers it apart, **keyed by that transaction object**, for that transaction's operations only (another transaction that
   reaches the same number is judged anew), so a migration
   that creates a table and fills an index in one transaction does not reload at every operation. A failure of another LUI format
   or key type is kept like the others, and reloads nothing again.
6. **Unknown name, and a stale snapshot under concurrency.** The catalog reloads the definitions **once**; if the index is still missing it throws
   `LuiIndexNotFoundException`, and the name is remembered **until the counter changes** — asking again costs the one statement that
   reads the counter, and no reload. When the stored definition differs from the declared one, the counter is looked at once before
   it is a mismatch: the definitions in memory may be old, and a new open call after a recreate by another process sees the new
   definition. **A decision is thrown only from a snapshot that is still the newest and at the counter of the file**: when another thread
   has replaced the snapshot meanwhile (it reloaded first), the call decides again from the current one — a false mismatch would
   advise the application to drop an index that is right. A reload never replaces a newer snapshot with an older one. **An entry that cannot be read** (another fold table, another key type) is a mismatch like another definition: it is thrown only from a snapshot that has been compared with the file — the host that opened a file with an index of an older fold, and whose migrator dropped and created it, must see the new index and not throw the old message for ever. A decision is made again only when the **counter** of the file moved from the one of the snapshot (a reload that read the same counter holds the same definitions), so the loops end when the file stops changing. The handles are
   made, and a failed one is replaced, under one lock: a definition object has one handle, whatever the number of threads, and no
   handle that was handed out escapes `DropIndex`.
7. **`DropIndex` and `Create` write the file and the memory.** `DropIndex` — under the lock of the handles, which a call that makes a handle takes, too, so that no handle escapes it, and through the guards of the handles that are alive, which the catalog holds weakly, so that a handle whose definition object is gone is found too — forgets
   the typo dictionary, marks every handle of the index that was handed out as dropped — the handles that were handed out are stale from now on ("index objects that were created before do not survive
   it", as the page says) — and reads the definitions again. `Create` already took the write lock (`BEGIN IMMEDIATE`) and read
   `lui_meta` inside the transaction: of two processes that create the same index at startup one creates it and the other finds it
   and compares — same definition: both succeed and one index is left; another definition: the second throws. The definitions are
   read into memory after the transaction.
8. **The definitions are shared state, and thread-safe.** The state is one immutable snapshot (the counter, the entries, the names
   that were asked for and are missing), replaced as a whole; the runtimes and the handles are created under a lock, and read
   without one.

## Consequences

- An open call is a lookup: the first call of a definition after `Open` reads nothing either; a request can take its handle where
  it needs it. A host that builds a definition object per request pays a runtime per request (about 20 KB, as in 0.3.0) and no
  more: it dies with the definition. The cost is measured once, at the end of the release: the benchmark of the open call
  (`OpenBenchmarks`) comes with the performance analysis, #275 (the owner, 2026-11-21: the performance is checked once, not per
  pull request).
- **The price is one statement per operation, and the transaction of the work** — a single write is `BEGIN IMMEDIATE` … `COMMIT`
  instead of one autocommit statement, a read has a snapshot; the check has to be in the transaction to be true. The check itself is `PRAGMA schema_version` on the connection of the
  operation: a command and a boxed number, no table read. Whether it is inside the budget is #275's question; the
  alternative that costs less, if it is not, is the owner's to choose — this record does not tune it away by removing the check.
- **What the counter does not see:** a row of `lui_meta` that changes without a change of the schema. LUI never writes one — an index
  is created and dropped with its tables — and the application never touches `lui_*` tables. A definition that appears or changes
  under an open catalog is seen at the first operation of the handle, or at the first open call of an unknown name or a mismatch; an
  open call for a known name with the same declaration does not look at the file (it is a lookup), and the handle it returns fails at
  its first operation if the index changed.
- A search of an **empty input** reads nothing and answers an empty result without the check, as it did before.
- Content is never cached: two catalogs on one file see each other's writes at once. The typo dictionary of a process learns only
  its own writes; the writes of other processes reach it with `RebuildTypoDictionary()` — unchanged and documented.
- A handle that failed is replaced at the next open call, so a held handle of a dropped index stays dropped: the application takes a
  fresh handle from `Create` or `Index` after it recreated the index (0073 §7).
- The typed handles still wrap `EmbeddedIndex` and `LinkedIndex`, which are the runtime; `IndexRuntime` is what the catalog keeps
  with each handle — the index and its guard — and `IndexRuntimes` only builds it.
- The seams of the tests are internal: `Database.CommandCounter` (counts the commands the connections of LUI create),
  `Database.AfterSchemaCheck` (runs between the check and the work — the tests swap the index there), `StoredIndexes.Reloads`
  (counts the reads of `lui_meta`), `LuiCatalog.CachedHandles` (the handles that are alive), `StoredIndexes.BeforeCounterCheck`,
  `LuiCatalog.AfterSlowPathStarted`, `LuiCatalog.BeforeHandleLock`, `LuiCatalog.AfterHandlesMarked` and `TypoDictionaries.BeforeReplace`
  (each runs at the place where another thread can come in, so that a test makes the interleaving and does not hope for it).

## Alternatives rejected

- **A generation counter per index in `lui_meta`.** It would need a write on every create and drop and a table read per operation;
  SQLite's schema counter is in the header of the file, costs no table read, and changes whenever the tables of an index do.
- **Reading `lui_meta` on every operation, or a `Exists` per operation.** One table read per request is what the thin handle is to
  avoid; the schema counter says whether a read is needed at all.
- **Checking only when an operation fails** (the way 0073 detected a dropped index). A recreate with another definition does not
  fail; it reads and writes through the old definition. That is the limitation this record removes.
- **Locking the handle cache on every call.** A lookup in a `ConditionalWeakTable` takes no lock and allocates nothing.
