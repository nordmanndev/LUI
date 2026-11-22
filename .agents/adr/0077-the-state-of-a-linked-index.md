# 0077 — The state of a linked index is a fact of the file: `NeedsInitialSync` is stored and read from the file, `CreateTriggers()` is idempotent, and the linked sync works in batches under a lease

- **Status:** Accepted (owner, 2026-11-20)
- **Date:** 2026-11-22
- **Builds on:** [0073](./0073-the-index-definition-and-the-typed-handles.md) (the typed handles; LUI format 2),
  [0075](./0075-the-catalog-holds-the-definitions-and-the-runtime-of-an-index.md) (the guard inside the snapshot of an operation; what another process can change without a change of the schema is read from the file),
  [0015](./0015-the-linked-index-is-loosely-coupled.md) (the linked index is loosely coupled: no trigger unless the application asks),
  [0016](./0016-sync-check-rebuild.md) (sync, check, rebuild),
  [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md) (one writer at a time),
  [0071](./0071-the-freeze-comes-with-1-0-0.md) (a break before 1.0.0 needs a reason, a migration note and a record)
- **Affects:**
  - `src/Nordmann.LUI/Schema/Meta.cs` — the columns `needs_initial_sync`, `sync_generation`, `sync_lease_owner` and `sync_lease_expires` of `lui_meta`
  - `src/Nordmann.LUI/Indexes/LinkedSync.cs`, `src/Nordmann.LUI/Indexes/SyncLease.cs` (new, #271) — the sync in batches, the generation, the lease, the removal, the merge in steps
  - `src/Nordmann.LUI/Schema/LinkedSource.cs` — `gen INTEGER NOT NULL` and its index on LUI's table of a linked index, the statements of a batch, of the removal and of a merge step
  - `src/Nordmann.LUI/LuiSyncInProgressException.cs` (new, #271), `src/Nordmann.LUI/LuiLinkedSyncReport.cs` — `Batches`, `Removed`, `LongestLock`
  - `src/Nordmann.LUI/Indexes/IndexStore.cs` — the flag is written with a new row (1 for a linked index, 0 for an embedded one), read in a given snapshot and cleared in a given transaction
  - `src/Nordmann.LUI/Indexes/LinkedIndex.cs` — `NeedsInitialSync`, the flag cleared in the transaction of `Sync`, `CreateTriggers` as a state
  - `src/Nordmann.LUI/LuiLinkedIndexOfKey.cs`, `src/Nordmann.LUI/LuiLinkedIndexOfKeyAndPayload.cs`, `src/Nordmann.LUI/ILuiLinkedIndex.cs`, `src/Nordmann.LUI/LuiCatalog.cs` — the property and the words of the documentation
  - `tests/TestData/golden/embedded-schema.txt` — the golden DDL of `lui_meta`
  - `docs/api.md`, `docs/testing.md`, `CHANGELOG.md`, `.agents/adr/README.md`
  - `tests/Nordmann.LUI.Integration.Tests/Linked/LinkedStateTests.cs`, `tests/Nordmann.LUI.Integration.Tests/Linked/LinkedTriggerTests.cs`, `tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs`, `tools/mutation-check.sh` — the rules and the guards
- **Issues:** [#272](https://github.com/nordmanndev/LUI/issues/272) (the state), [#271](https://github.com/nordmanndev/LUI/issues/271) (the sync in batches, generation, merge, lease), part of the epic of 0.4.0 ([#258](https://github.com/nordmanndev/LUI/issues/258))

## Context

A linked index needs two things before it works: its triggers, so that the writes of the application reach the outbox, and a first
full sync, when the table already has rows. Neither was visible as a state. Whether the first sync was done was known nowhere,
and `CreateTriggers()` on triggers that exist was not specified — it dropped and created them every time.

The owner decided on 2026-11-20: the setup of a linked index is driven by **state** stored in the file, never by the event of its
creation, and LUI does nothing on its own. An application — or the host of #266 — reads the state and acts.

## Decision

1. **`NeedsInitialSync` is a stored fact.** `LuiLinkedIndex<TKey>.NeedsInitialSync` and `LuiLinkedIndex<TKey, TPayload>.NeedsInitialSync`
   are `true` from the creation of the index until its first complete `Sync()`. The flag is the column `needs_initial_sync INTEGER NOT NULL
   DEFAULT 0` of `lui_meta`, part of LUI format 2. It is **a column and no part of the stored definition**: the definition JSON of every
   index — its comparison, its mismatch fields — stays byte for byte what it was. The golden schema of the file (`SchemaGoldenTests`)
   changes with the DDL of `lui_meta`, in this pull request.
2. **It is read from the file on every access.** Another process can complete the sync without changing SQLite's schema counter, so the
   definitions held in memory (0075) cannot carry it. The property opens a read connection, begins a deferred snapshot, **checks the guard
   of the handle in that snapshot** (a dropped index is a `LuiIndexNotFoundException`, one created again with another definition a
   `LuiDefinitionMismatchException`), and reads its row — check and read are one step. A second catalog on the same file sees the change
   as soon as it is committed, with no reopen and no earlier operation. It works on a catalog opened read-only.
3. **`Create` sets it, and only sets it.** A new linked index has `NeedsInitialSync == true` and `Triggers() == None`; `Create` never
   syncs and never installs a trigger. Opening an existing index with `Create` leaves the state as it is. An embedded index stores 0 and
   has no property. `Create` checks that the table of the application exists, every time — the table is the application's own schema,
   and a missing table is a `LuiLinkedTableException` that names the index.
4. **A complete `Sync()` clears it, in the transaction that commits the sync.** The statement runs on the connection and in the transaction
   of the sync, after its last statement and inside the check of the guard that opened the write: the content and the flag commit together
   or not at all. A sync that is cancelled or fails rolls the transaction back and leaves the flag `true`. A sync of an empty table is
   complete. Nothing else clears it — not a refresh, not the outbox, not `Check()`. Since #271 "complete" means: every batch written, the
   removal done and the merge finished — the flag is cleared **in the transaction of the last step of the merge** (decision 10).
   **The edge:** `Sync()` rebuilds a typo dictionary that is built *after* the commit of its last step. A cancellation during that rebuild
   throws out of `Sync()` with the flag already `false` — the index itself is complete; the dictionary stays as it was until the next
   `Sync()` or `RebuildTypoDictionary()`. The flag says that the index was filled, not that the process of the caller ended without an exception.
5. **`CreateTriggers()` is idempotent.** In one guarded write transaction it reads the state of the triggers and acts: `None` creates them,
   `Stale` drops and creates them, `Current` does nothing — nothing is written, the schema counter does not move, and triggers an
   application installed with its own white space (still `Current`, 0016) stay as they are. It never throws because triggers exist. The
   reading and the work are one step, so no other process can change the triggers between them. On a read-only catalog a call on `Current`
   triggers does nothing and succeeds; one that would write is refused with the usual `LuiReadOnlyException`.
6. **The old interface does not get the property.** `ILuiLinkedIndex` (the old path, removed by #268) shares the implementation — its
   `Sync()` clears the flag and its `CreateTriggers()` is idempotent — but a new abstract member of an interface is a break, and the
   interface goes away.

7. **The linked sync works in batches, like the embedded sync (#271).** `Sync(options, ct)` reads the rows of the application's table **in the order of their
   key, `BatchSize` rows at a time** (`LuiSyncOptions.BatchSize`, 10,000 by default), and indexes each batch — added or replaced by key — in a transaction of its
   own. There is **no clear at the start**: a search during the sync sees the old state plus the batches committed so far, never an empty index, except on
   the very first sync. The docs say it plainly: **a linked sync is not atomic** — while it runs the index is partly old and partly new. This extends
   [0016](./0016-sync-check-rebuild.md): the linked sync follows the rules of the embedded one — batches in transactions of their own, FTS5's `automerge` off
   while a batch loads and **back on before its commit**, a merge at the end, a cancelled sync keeps its batches. What it does not do is compare: the
   contentless search index holds no text to compare with, so a batch writes every row, and `LuiSyncOptions.Progress` of a linked sync tells `Read`,
   `Batches`, `Removed` and, at the very end, `Merged`.
8. **Removal by generation.** LUI's table of a linked index gets a column `gen INTEGER NOT NULL` with an index on it (format 2). Every sync takes the next
   generation — `lui_meta.sync_generation` plus one, in the transaction that takes the lease —, and every row a batch writes carries it. A single
   `Refresh` / `ApplyPending` writes **minus** the generation of the last sync: the minus says "written by a refresh beside that sync". The removal leaves such an entry — a
   row refreshed in a range that is done must survive it — but the sync does **not count it as reached** (review of #291, B1): a batch whose range holds such an entry for a key
   whose row is gone, or has no text, removes the entry and its search entry itself, and it forgets an outbox key only for an entry that the batch wrote from an indexable
   row (`gen = +current`). Otherwise a row deleted after a refresh and before its batch would stay in the index with its note lost. A later sync takes the next generation, and
   a minus entry of an earlier one is an old entry for it. When the last batch
   is written, the entries that carry **an older generation** are removed, `BatchSize` at a time, each batch in a transaction of its own — no anti-join over the
   application's table. The statement says `gen < current` (and not `-current`), not `gen <> current`: generations only grow and the lease holds that no other sync runs, so the two
   mean the same, and the first is a range of the index on `gen`, where `<>` would read the whole table for every batch. **Nothing of the previous state is
   removed before the new one is complete** — a sync that is cancelled leaves the index as it was plus the batches it wrote. A row the application deletes
   *during* the sync, in a range that is done, is noted by the triggers (the outbox: `ApplyPending()`) or found by the next sync — which is why the triggers come
   **before** the first sync (decision "Triggers first" below). A batch takes the keys it brought in step out of the outbox; a key whose row is gone stays there.
   The report counts the rows of a batch as it reads them (`WithoutText`, `KeyOutOfRange`), so no second pass over the table is made for the report.
9. **One sync at a time: the lease.** A sync takes a **lease** on its index — `sync_lease_owner` and `sync_lease_expires` in the row of the index in
   `lui_meta`, the expiry in **UTC milliseconds** from the clock of the catalog (`LuiCatalogOptions.TimeProvider`), 60 seconds from now. It is taken in
   a transaction of its own (`BEGIN IMMEDIATE`, the guard checked in it) by **one statement** that checks and takes: the lease is free or has expired. A
   valid lease of another owner is a **`LuiSyncInProgressException`** that names the index, the owner and the expiry; the sync does not wait and has written
   nothing, not even a generation. The owner is a string of the process id, a random id of the catalog instance and the number of the run — **per run**,
   because two syncs of one catalog must not take each other for the holder. The lease is **renewed in every transaction** of the sync — one statement
   `WHERE owner = me` at the start, which is also the check, and again before the commit, so that a long batch does not leave the expiry at the time the batch
   began. A sync whose renewal changes no row **lost** its lease — it paused for longer than the lease and another sync took it over, or the index was
   dropped and created again with the same definition (the guard says nothing about that: the handle of the same definition goes on) — and stops before
   its first statement with the same exception (its owner and expiry are the holder's now, or not set when nobody holds the lease). It writes nothing after
   it lost the lease. The lease is **released in `finally`** — also after a cancellation and a failure — and **only the lease of this run**: the release is
   `WHERE owner = me`. A release that cannot be made (the file is busy) does not hide the exception that ended the sync: the lease expires on its own, which is the
   whole point of an expiry — a process that crashed holds its index for a minute at most. `Dispose` of a catalog waits for a running sync, as it waits for
   every operation, so the sync ends and releases before the connections are closed. The lease is a row that a short transaction writes; no other writer waits
   for it longer than for any transaction of the sync.
10. **The merge in steps.** After the removal the search index is merged by FTS5's `'merge', N` — `N` pages of its segments — in a **transaction of its own per step**, until a
    step reports that nothing is left, instead of one `optimize`, which over a large index would be one write transaction of the length of the whole merge. A step
    reports through SQLite's **`total_changes()`**, read before and after it on the connection: more than the one change of the `INSERT` itself means that FTS5
    wrote, exactly one means nothing was left. **`changes()` says 1 after every step and is no signal** — the behaviour is pinned in `LinkedMergeStepTests`, because
    the loop rests on it. "Nothing left" is FTS5's own measure (every level holds fewer segments than `usermerge`, 4), not "one segment" as `optimize` leaves. The step that finds
    nothing left is the **last step of the sync**: in its transaction `needs_initial_sync` is cleared, the entries are counted for the report and the
    lease is released — the content, the flag and the lease commit together or not at all (decision 4). `N` is **4,096 pages** (`LinkedSync.DefaultMergePages`):
    a probe on a trigram index of 400,000 rows took 130–190 ms for a step of that size against about 700 ms for a batch of 10,000 rows, so a step stays below a batch
    — the order the plan asks for; the million-row measurement of #275 looks at it again (it reports the longest step). A built typo dictionary is rebuilt once,
    after the last step.
11. **The report.** `LuiLinkedSyncReport` gets `Batches` (the batches of rows written — an empty table has none), `Removed` (the entries of an older generation that
    were removed) and `LongestLock` — the longest time the sync held the write lock of the file in one transaction (a batch, a step of the removal or of the merge, the
    lease), measured from the begin of the transaction to its commit. It is what another writer waits for.
12. **Every transaction of the sync is guarded** (0075): the guard is checked inside each transaction — the lease, every batch, every step of the removal and of
    the merge — together with the lease, in the same transaction that does the work, so an index that another process drops and creates again between two
    batches is a `LuiIndexNotFoundException`, a `LuiDefinitionMismatchException` (another definition) or, for the same definition, the lost lease of decision 9 — and
    nothing is written into the new index. The sync holds one connection for its whole length, as the sync of an embedded index does (0070).

13. **The sync gives the write-ahead log back before each transaction (review of #291, B2).** The batches exist to keep the log small, and the first version did not: in a
    million-row run the `-wal` file peaked at 341 MB for a database of 322 MB. SQLite starts the log over — writes from its beginning again — only when the connection that
    begins to write has read nothing from it (every frame is backfilled) and no reader holds it. The checkpoint at the commit of a transaction is not enough: a writer
    of the application that commits between that checkpoint and the next begin leaves frames that are not backfilled, the next transaction appends to the log, and the file
    only grows (it keeps its high-water mark; it is not truncated by a checkpoint). A probe at 400,000 rows without writers showed the log at 20 MB, one batch, in every
    phase; with two polling writers it reached 44 MB, and the probe's `wal_checkpoint(PASSIVE)` showed the log growing by a transaction each time the restart was missed.
    The run therefore executes `PRAGMA wal_checkpoint(TRUNCATE)` on its own connection **right before the begin** of each transaction, in the same hold of the writer gate, with
    the busy timeout set to **0 for that one statement** and put back after it: best effort and never a wait — a reader or a writer in the way leaves the log as it is, and a long
    reader of the application does not hold the sync up by five seconds a batch. It changes no setting of the file and no pragma that the application can see (ADR-0066:
    the connection's busy timeout is restored); a file that is not in write-ahead logging has nothing to do. After it the log is the biggest single transaction of the sync plus
    what the application's writers committed in between; the biggest transaction is a batch, or a step of the removal (10,000 deletes write into the tombstones of every segment:
    about 40 MB at 400,000 rows). The test `The_write_ahead_log_stays_a_batch_in_size_when_another_writer_commits_between_the_batches` commits a write between every two batches and
    asserts that the log stays smaller than the database (a third of it, measured; 3.3 times without the checkpoint); the million-row test measures each sync from a truncated log
    and asserts half of the database. **The million-row run after the change** (each sync from a truncated log; the peak of the `-wal` file after each phase): first sync 16 MB
    (batches) and 17 MB (merge); second sync 42 MB (batches) and 17 MB (merge); third sync, beside two writers, 35 MB (batches), 37 MB (removal), 17 MB (merge) — **42 MB at most, for a
    database of 322 MB; 341 MB before.** The generous writer (30 s) had 39 writes and 0 failures, the longest wait 2.41 s; the writer with the tight timeout (2 s) 29 writes and 6 failures
    (logged, not asserted); the longest lock 1.73 s (a merge step; the removal steps 0.37 s). `journal_size_limit` was not taken: it shortens a file after a reset and does not prevent the growth.

## Alternatives

| | For | Against |
|---|---|---|
| **A — a column of `lui_meta`** (this decision) | one row per index, dropped with it; a plain `UPDATE` bumps no schema counter; written in the transaction of the sync | the golden DDL of `lui_meta` changes with format 2 |
| **B — a table of its own per linked index** | the golden stays | a second table per index to create and drop; the issue names `lui_meta` |
| **C — a field of the definition JSON** | no new column | the flag would take part in the comparison of definitions and change every stored definition; it changes without the definition changing |
| **D — a flag in memory (the map of 0075)** | no read | another process completes the sync without a change of the schema; the catalog would not see it |

The batched sync (decisions 7–12) had alternatives too:

| | For | Against |
|---|---|---|
| **A — batches, generation, lease** (this decision) | other writers get the file between the transactions; a cancelled sync keeps its work; one removal rule, no anti-join | a linked sync is not atomic; a column and three columns of `lui_meta` |
| **B — a shadow build and a swap** (0016, "Open for the owner" 2) | atomic for the reader | a second write path for every refresh, which has to reach both tables; the owner decided against it on 2026-11-14 and again on 2026-11-20 |
| **C — remove the rows that are not in the table by an anti-join at the end** | no new column | reads the application's table against the index in one statement — one long transaction again, or a temporary table of the keys |
| **D — a lease without an expiry** | no clock | a process that crashed locks the index for ever |
| **E — a lease per catalog instance, not per run** | the owner is the catalog | two syncs of one catalog take each other for the holder, and the second one writes beside the first |

## Consequences

- **Format 2 gains a column; no release had format 2.** `EnsureFormat` checks the column when the catalog opens, **for a `lui_meta` that has rows**: a table without a row has no
  format to reject (#270) — the empty `lui_meta` of 0.3.0 opens as before, and the first `Create` adds the column in its own transaction. A file with
  indexes that an early build of 0.4.0 wrote without the column is refused with a `LuiFormatException` that names the column (`FoundFormat` = `SupportedFormat` = 2), and is
  recreated like a file of 0.3.0 (the CHANGELOG says so). Files of 0.3.0 are rejected as before.
- **`CreateTriggers()` no longer rewrites current triggers.** Before, every call dropped and created them. The CHANGELOG has a migration note;
  no existing test asserted the old behaviour as a promise (a second call never threw).
- **One read connection and one small read per access** of `NeedsInitialSync` — the cost of reading from the file. Performance is looked
  at once at the end of the release (#275).
- **Triggers first, then the first sync.** The order of a setup is `CreateTriggers()`, then `Sync()` when `NeedsInitialSync`: a row that is
  written between the two lands in the outbox and `ApplyPending()` or the sync brings it in. With the sync first, a row written before the
  triggers exist is in neither — the sync did not see it, no trigger noted it — and since the flag is already `false`, no later start
  would sync again. This is also the order of #266. The docs say it wherever they name both calls.
- **The application decides.** `NeedsInitialSync`, `Triggers()` and the two calls are the whole setup; `catalog.Initialize()` (#266) is
  a loop over them, not a new mechanism. It treats a lease that is held as "running elsewhere" and skips (#266).
- **A linked sync is not atomic, and the write lock is shared, not queued: starvation is accepted and documented (#271, the owner's option C).** Another writer waits
  for one transaction of the sync at a time — `LongestLock` of the report — instead of for the whole sync (26.7 s for a million rows in 0.3.0), and the batches keep
  the **write-ahead log small**, which is the reason for them. SQLite has no queue: a waiting writer polls with a pause of at most 100 ms, and the sync takes the lock again
  within microseconds of a commit, so the file is given to the others by chance and not by a bound (0016, "Consequences"). **What was measured** (4-core container; the
  test `A_linked_sync_of_a_million_rows_beside_a_writer_…`, formerly `…never_gets_busy`): in the first million-row run a writer with a busy timeout of twice the longest batch
  (2 s) had 4 of 38 writes fail with `SQLITE_BUSY`, the longest wait of a success 1.99 s; in the second (after the removal had automerge off) 0 of 45 failed, the longest wait
  1.96 s — a pass by 40 ms, which is luck and not fairness. **The first criterion of the issue — a writer with a busy timeout of twice a batch never gets `SQLITE_BUSY` —
  is withdrawn**: it asks for a property that SQLite cannot give without a pause. It is replaced by what the sync does promise, and the test asserts: a writer with a
  *generous* timeout (30 s) is never refused; the sync is cancellable; a count never sees a short index; the peak of the `-wal` file of each measured sync (the log truncated before it) stays
  under half of the database; no transaction holds the lock for an absurd time (10 s). The starvation of a tight timeout stays a log line.
  **Rejected for now: a pause between the transactions of the sync** (an option `LuiSyncOptions.PauseBetweenTransactions`). A probe at 400,000 rows with a writer of a 2 s busy
  timeout: 16 writes in a sync of ten seconds and a longest wait of 1.81 s without a pause; 83 writes and 0.90 s with a pause of 120 ms after every transaction (longer than the
  longest pause of SQLite's busy handler, so that every polling writer lands in it) — and the sync **56 % longer**. The reasons against: the cost on every sync for an edge
  case (a very large sync beside a write-heavy application), a new public option to explain, and it helps only writers that poll — those of the application; another process
  with its own busy handler or none gets nothing from it. The way out for an application is its own: run the first sync before it accepts writes, a generous busy timeout
  (30 s) and a retry on `SQLITE_BUSY`, `BEGIN IMMEDIATE` for its write transactions — the README and `docs/api.md` say so. It is recorded so that a report from a real application can
  reopen it with the numbers above.
- **An open item for #275: the merge step.** In the second run the longest merge step held the lock for 1.81 s (batches 0.54 s, removal steps 0.42 s) with `DefaultMergePages`
  = 4,096; in the first run the merge had nothing left to do, because FTS5's own merge had run in the removal. Whether 4,096 pages is too many is measured again there; the
  lock bound of the test (10 s) is not the promise of a step of a batch's length.
- **Format 2 gains more columns; no release had format 2.** `lui_meta` has `sync_generation`, `sync_lease_owner` and `sync_lease_expires` beside `needs_initial_sync`; the open
  check refuses a file with indexes that misses any of them, naming the first one, and the first `Create` in the empty `lui_meta` of 0.3.0 adds them all. The table of a
  linked index has `gen`. The golden schema of the file changes with the DDL of `lui_meta` (the entry table of a linked index is no part of it).
- **A lease that is held for a crashed process delays the next sync by a minute at most**, and a sync that is stopped for longer than a minute (a progress handler that
  blocks) is stopped by the next sync that takes the lease over — the price of an expiry; the exception says who holds the lease and until when.
- **The cost of the batches** (the total time of a million-row sync against 0.3.0, the longest lock, the size of a step of the merge) is measured once, in the same session as
  the baseline, by #275; the numbers go to `docs/benchmarks.md` there.
