# 0076 — Maintenance works by name, and the start is one call: the catalog describes, counts, checks, optimizes and rebuilds an index by its name, and `Initialize()` brings every index to ready — without a definition or a payload type

- **Status:** Accepted (owner, 2026-11-20)
- **Date:** 2026-11-22
- **Builds on:** [0073](./0073-the-index-definition-and-the-typed-handles.md) (an index is opened only with its definition; the key types),
  [0075](./0075-the-catalog-holds-the-definitions-and-the-runtime-of-an-index.md) (the definitions in memory, the guard inside the snapshot of an operation, the missing name),
  [0072](./0072-the-serializer-is-bound-to-the-payload-type-through-a-registry.md) (the payload type and its format are stored),
  [0074](./0074-the-hit-carries-its-payload.md) (`CheckPayloads()` is the check that reads payloads),
  [0016](./0016-sync-check-rebuild.md) (sync, check, rebuild),
  [0068](./0068-three-ways-to-open-a-catalogue.md) (a catalog that may not be written refuses what writes),
  [0077](./0077-the-state-of-a-linked-index.md) (the flag of the first sync, the lease, triggers first — `Initialize()` is a loop over them),
  [0042](./0042-the-query-does-not-build-the-typo-dictionary.md) (a query never builds the dictionary — the application does),
  [0043](./0043-the-warm-up-is-a-service-of-the-host.md) (the warm-up as a service: superseded by this record from #268 on),
  [0018](./0018-two-packages-core-and-hosting.md) (the hosting package takes no web dependency), [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md) (one writer at a time: the outbox)
- **Affects:**
  - `src/Nordmann.LUI/LuiCatalog.cs` — `Indexes()`, `Count(name)`, `Check(name)`, `Optimize(name)`, `Rebuild(name)` and the lookup they share
  - `src/Nordmann.LUI/LuiIndexInfo.cs` (new, #273) — the record that describes an index by name
  - `src/Nordmann.LUI/Indexes/IndexRuntimes.cs` — the runtime of an index by its stored definition alone
  - `src/Nordmann.LUI/Typos/TypoDictionaries.cs` — the state of a dictionary, asked without creating one
  - `src/Nordmann.LUI/Querying/StoredColumns.cs` (new, #273) — the payload columns of a linked index, for the check of its structure
  - `src/Nordmann.LUI/Indexes/IndexStore.cs`, `src/Nordmann.LUI/Schema/ContentSources.cs`, `src/Nordmann.LUI/LuiDefinitionMismatchException.cs` — a row that cannot be read is a mismatch that names the index; `DropIndex` takes it
  - `src/Nordmann.LUI/LuiCatalog.cs`, `CatalogInitialization.cs` (new, #266) — `Initialize()`, `IsReadOnly` and the state read by `Indexes()`; `LuiInitializeOptions.cs`, `LuiInitializeProgress.cs`, `LuiInitializeStep.cs`, `LuiInitializeReport.cs`, `LuiInitializeIndexReport.cs`, `LuiInitializeException.cs`, `LuiInitializationNeeds.cs` (new, #266)
  - `src/Nordmann.LUI/Indexes/LinkedIndex.cs`, `LinkedSync.cs`, `LinkedInitialization.cs` (new, #266) — the state a linked index reads, and the first sync that reads its flag in the transaction that takes the lease
  - `src/Nordmann.LUI/LuiTypoDictionaryState.cs` — the comment of `NotBuilt` says that a query never builds the dictionary
  - `src/Nordmann.LUI.Hosting/LuiServiceCollectionExtensions.cs`, `src/Nordmann.LUI.Hosting/LuiOutboxOptions.cs`, `src/Nordmann.LUI.Hosting/Hosting/LuiOutboxService.cs` (new, #266) — `AddLuiOutbox`
  - `docs/api.md`, `docs/testing.md`, `CHANGELOG.md`, `README.md`, `.agents/adr/README.md`
  - `tests/Nordmann.LUI.Integration.Tests/Maintenance/MaintenanceByNameTests.cs`, `tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs`, `tools/mutation-check.sh` — the rules and the guards
  - `tests/Nordmann.LUI.Integration.Tests/Catalog/InitializeTests.cs`, `tests/Nordmann.LUI.Integration.Tests/Concurrency/TwoCatalogsInitializeTests.cs`, `tests/Nordmann.LUI.Integration.Tests/Hosting/OutboxTests.cs`, `tests/Nordmann.LUI.Integration.Tests/Hosting/HostPatternTests.cs` (#266)
- **Issues:** [#273](https://github.com/nordmanndev/LUI/issues/273) (maintenance by name) and [#266](https://github.com/nordmanndev/LUI/issues/266) (`Initialize()`, `AddLuiOutbox`, the host pattern — decisions 8–16), part of the epic of 0.4.0 ([#258](https://github.com/nordmanndev/LUI/issues/258))

## Context

Since #261 an index is opened with its definition, and an index with a payload type is opened with a handle that names the type
(ADR-0073). That is right for the application, which has its definitions. A tool, an admin endpoint or a health check — and later the
client of #212 — has none of them: it has a catalog and a name, and it must inspect and maintain **every** index of the file. If the only way
to count or check an index were a typed handle, such a tool would have to link every payload type of every application whose file it opens.

The owner decided on 2026-11-20: maintenance works by name, through the catalog, without definitions or payload types.

## Decision

1. **Five members of `LuiCatalog`**, for embedded and linked indexes alike, except where the table says otherwise:

   | member | embedded | linked |
   |---|---|---|
   | `IReadOnlyList<LuiIndexInfo> Indexes()` | ✓ | ✓ |
   | `long Count(string name)` | ✓ | ✓ |
   | `LuiIntegrityReport Check(string name)` | ✓ | ✓ |
   | `void Optimize(string name)` | ✓ | ✓ |
   | `void Rebuild(string name)` | ✓ | throws `InvalidOperationException` |

   `DropIndex(name, dropTriggers)` and `WarmUpTypoDictionaries(names)` are by name already and stay as they are. **None of the five returns a
   handle or can write an entry**, so "one index, one kind of definition" (ADR-0073) is untouched: a typed handle is still the only way
   to write an entry or to read a payload.
2. **The runtime is the stored definition's, and nothing of the application is in it.** An operation by name builds the internal runtime —
   the index, its source, its guard — from the stored entry of `lui_meta`, with no payload reader and no map: it **reads no payload and calls
   no map**. `Check(name)` is the structure only (the definition, the triggers, FTS5's integrity check, the counts against the table, and — for a linked
   index — that the table still has the **payload columns** of the stored definition: they are stored, so the check needs no map and
   no registry); whether
   the stored payloads can be read is `CheckPayloads()` of a typed handle (ADR-0074), which needs the type. The runtime is built for the call
   and let go; nothing of it is kept, so the handle cache of ADR-0075 — which needs a definition object — is never reached.
3. **The guards of ADR-0075 hold for every operation by name, with the same machinery.** The runtime has the `IndexGuard` of a handle: the
   schema counter is read inside the snapshot of a read and inside the write transaction of a write, so the check and the work are one step; an
   index that is gone is a `LuiIndexNotFoundException` that names it. The one difference is that a call by name **declares no definition**: an
   index that another process dropped and created again with another definition is not a `LuiDefinitionMismatchException` for the caller, it
   is the index as it is stored now. When the guard says that the definition it was built from is not the stored one, the call looks again from
   the definitions as the file holds them (`StoredIndexes.Refresh`) and is made again; it stops when the file stops moving, and then the
   exception of the guard is the answer. No exception is thrown from a snapshot that has not been compared with the file.
4. **A missing name** reloads the stored definitions once and is remembered as missing until the SQLite schema counter changes
   (`StoredIndexes.Find`, ADR-0075), so a name that a user types on an admin endpoint does not cause a reload per request. A name that is no
   index name at all (`^[a-z][a-z0-9_]{0,40}$`) is an `ArgumentException`, as everywhere.
5. **`Indexes()` is a list of records, read per field.** `LuiIndexInfo` is a sealed record with named properties: `Name`, `Source`
   (`LuiIndexSource.Embedded` or `Linked`; it is called `Source` because `Kind` already means single word or multi word), `Kind`, `PayloadType`
   and `PayloadFormat` (stored for an embedded index with a payload type, `null` otherwise — a linked index stores neither, ADR-0072), `KeyType`
   (`LuiKeyType`, the stored key type, ADR-0073), `TypoDistance`, `TypoDictionaryState` and `NeedsInitialization` (#266, decision 13).
   - The **definition fields** come from the definitions in the memory of the catalog: **no statement reads them**. The one statement of the
     call asks whether the schema counter of the file moved, and only a move reads `lui_meta` again — so an index that another process created
     or dropped is in the list, and an index is never listed from a snapshot that the file has left.
   - The **state field** `TypoDictionaryState` is read at the call. The dictionary lives in the memory of the process, one per index and
     definition, so it is read from the dictionaries of this catalog — **without creating one**; a dictionary that was built for another
     definition of the same name is not the dictionary of the stored one, and the state is `NotBuilt`. It says what *this* catalog has built.
   - The list is in the order of the names. An index whose row this version cannot read (another LUI format, another folding, a key type it
     does not know, a kind it does not know, a definition of `null` or of malformed JSON) is a `LuiDefinitionMismatchException` that names
     it — the error of the parser is its inner exception — as `WarmUpTypoDictionaries()` already does, **for `Indexes()` and for every operation by
     name on that index**. `Indexes()` throws for one damaged row and does not list the others around it: a list with a hole would hide the one
     row the operator has to `DropIndex`, which is the way out and reads such a row leniently, as what its `source` column says: a damaged **linked** row is dropped with the linked statements (its outbox, search index and entries) and with the refusal of its triggers unless `dropTriggers: true`, an embedded one with the embedded statements. A row whose `source` is neither `Embedded` nor `Linked` — the column is `NOT NULL`, so it is a value this version does not know — is **refused** with a `LuiDefinitionMismatchException` that names the index, and no statement runs: nothing is guessed, as a source this version does not know was always refused (review of pull request 14, N2). An embedded drop is also refused without `dropTriggers` when a trigger named like the index's own sits on a table that is not its own: the triggers of an embedded index and of a linked one have the same names, and the application's are not taken without the flag. The other indexes stay usable by name.
6. **`Rebuild(name)` is for an embedded index only.** For a linked index it throws an `InvalidOperationException` before it touches the file:
   *"'chefs' is a linked index; rebuild it from its table with Sync() on its handle."* A linked index has no content of its own to rebuild
   the search index from: its content is the application's table, and `Sync()` is the call that reads it. The refusal is thrown only from a
   definition that has been compared with the file, like every other.
7. **A catalog that may not be written** lists and counts. What writes — `Optimize` and `Rebuild` — is a `LuiReadOnlyException` that names
   the index (ADR-0068); so is `Check(name)`, whose integrity check of FTS5 is an insert that is rolled back, as `Check()` of a handle always was. For the same
   reason `Check(name)` **holds the write lock of the file for the whole integrity check**, which reads the whole index: it is cheap in what it
   reads, not in the lock it takes (#275 measures it), and a host should not call it from a probe of a second on a large index.

Which check for what:

| check | cost | use |
|---|---|---|
| `Check()` of a handle, `catalog.Check(name)` | structure only; holds the write lock while FTS5 checks the index, refused on a read-only catalog | health checks on a schedule |
| `catalog.Indexes()` → `NeedsInitialization` (#266) | cheap: state only | readiness, every probe |
| `CheckPayloads()` (ADR-0074) | reads every stored payload | maintenance, after a change of the payload type |

## Alternatives rejected

- **Handles by name** (`catalog.Index("chefs")` for a typed index, with the payload type as a generic argument at the call): it would make
  a second kind of definition for the same index — one in the application, one by name — and the tool would still need every payload type.
  The by-name operations return no handle; that is what keeps ADR-0073 intact.
- **Cache the runtime by name.** It would need an own invalidation next to the one of the handle cache (a drop, a recreate, a failed guard),
  to save the construction of an object that the operation by name — a count, a check, an optimize — costs much more than. The runtime is built
  for the call; the work is not.
- **`Indexes()` without the statement for the counter** (the snapshot as it is): free, and wrong as soon as another process creates an index —
  a tool that lists the indexes of a file that several processes write would list a stale file. One statement per call is the price of
  a list that is true, and no definition field costs a statement.
- **A count and the state of the triggers in `LuiIndexInfo`:** they cost a statement per index, and the list is meant for frequent calls.
  `Count(name)` and `Check(name)` are asked by name when they are wanted; `NeedsInitialization` (#266) is the one state field of a linked index that is read — the triggers, the flag and the lease in one small snapshot.

## Consequences

- A tool that opens a file it does not know can list, count, check and maintain every index of it. A health endpoint calls `Check(name)` and
  reads no payload.
- `LuiIndexInfo` is public API: a new member of it is a break for a caller that constructs one only if it is a `required` property; a
  caller reads it. The guard `PublicApiShapeTests` lists the members without a filter.
- Each operation by name builds a runtime — the prepared SQL of the index — for the call. The cost is measured with the rest of the
  release (#275); no benchmark is part of this change.
- `catalog.Initialize()`, the registration of the outbox and the host pattern are decisions 8–16 below.

## The start of a process: `Initialize()`, `AddLuiOutbox`, the host pattern (#266)

**Context.** An application has four start steps to know and to order — the triggers and the first sync of a linked index, the outbox, the typo dictionaries — and every
forgotten one fails silently: without its dictionary a typo-tolerant index never corrects (a query never builds it, ADR-0042). `AddLuiWarmup` mixed a start step (the
dictionaries) with an endless loop (the outbox) under a name that sounds optional. The owner decided on 2026-11-20: **explicit, not magic — one synchronous call with good
defaults**; the hosting package keeps only what needs a background loop.

8. **`catalog.Initialize(LuiInitializeOptions? options = null, CancellationToken cancellationToken = default)` returns a `LuiInitializeReport`.** LUI never initializes on its
   own; the application decides **when** and **on which thread**. There is no `InitializeAsync` — the work is synchronous SQLite, and a `Task.Run` around it would only pretend —
   and no hosted service of LUI that calls it. Every option switches one step off (`InstallTriggers`, `InitialSync`, `ApplyPending`, `WarmUpTypoDictionaries`); `Indexes` limits
   the run by definition (only the names are used; a definition of an index that does not exist is a `LuiIndexNotFoundException` before any step; `null` is every index of the
   file, read from the stored definitions, so a tool without definitions can initialize everything); `Sync` carries the options of the syncs; `Progress` is told right before each step
   and after each batch of a first sync.
9. **The order is LUI's, step by step across the selected indexes, and each step runs only where the state of the index needs it, read first.** (1) The triggers of every linked
   index that has none or stale ones — `CreateTriggers()`, idempotent — **before** the sync, so that a row written in between is noted in the outbox (ADR-0077); (2) the first sync of
   every linked index that still needs one, one after the other; (3) the outbox of every linked index that has rows in it, once; (4) the typo dictionaries, last. A second call right
   after the first announces no step. **Every step goes through the operations by name of decision 3** (`Maintain`: the runtime of the stored definition, the guard of ADR-0075 inside
   the snapshot or the transaction of the work) — there is no parallel path, so an index that was dropped or created again under the call is the proper exception, and a snapshot that
   the file has left throws nothing.
10. **The typo dictionaries are per process, last, one after the other, and only for an index with no file step left.** They live in memory, so every process builds its own: expected
   work, not a repetition. They come after the syncs and the outbox so that they know those words, and one at a time so that the peak memory is that of the largest dictionary and not
   the sum. An index that still has a file step — no triggers, stale triggers, a first sync due or running elsewhere — gets none from this call: it would miss the words of a sync still
   to come; the reason stays in `NeedsInitialization`, and the next call builds it. This holds also for a step that the options switched off: the rule is about the state, not about the
   intention. Each dictionary takes seconds and some hundred MB at the size of the German word list; with several typo-tolerant indexes the time adds up (the docs say so).
11. **Several processes: the first sync is guarded by the lease of ADR-0077 — and the flag is read in the transaction that takes it.** A lease that another process holds is
   `InitialSyncRunningElsewhere`: the call skips that sync and the steps after it for that index, does not wait and does not fail. Reading `NeedsInitialSync` first and syncing after
   would let two catalogs that both read "due" run the sync one after the other; `LinkedSync` therefore reads the flag in the same write transaction that takes the lease and, when the
   sync was completed meanwhile, takes nothing and returns no report. Of two processes that start together exactly one runs the first sync. The triggers are idempotent (ADR-0077) and the outbox is
   applied by one writer at a time (ADR-0046).
12. **A failing index does not stop the others, and the failure is never swallowed.** Its failing step is recorded (`LuiInitializeStep`, with `ReadState` for a state that cannot be read), its
   later steps are skipped, the other indexes go on, and at the end the call throws a `LuiInitializeException` (a `LuiException`) with the complete `Report` and the `Errors` by index name; its
   message names every failed index and its step, and the first error is its inner exception. **Cancellation** is checked between the steps and inside the syncs, throws an
   `OperationCanceledException` and is no failure of an index (the errors of indexes that had failed by then are not carried — the next call finds them again, and `NeedsInitialization` names what they need): what was finished stays, and the next call continues by state. **A catalog opened with `OpenReadOnly`** builds the dictionaries only; the
   file steps cannot be done and are left to the writing process, and the call does not fail for what it cannot do.
13. **`LuiIndexInfo.NeedsInitialization` is a `[Flags]` `LuiInitializationNeeds`, read from the same state.** `None` is ready. `Triggers` (none on the table), `StaleTriggers`, `InitialSync` (due, and no
   sync holds the lease), `InitialSyncRunningElsewhere` (due, and a sync holds an unexpired lease — **instead of** `InitialSync`, not beside it, so that the one flag says what this call would do),
   `TypoDictionary` (an index that forgives typos, not built in this process — also while it builds). The lease says something only about a sync that is due: a re-sync of an index that is synced is no
   need. A linked index costs one small read of the file in `Indexes()` (the triggers, the flag, the lease, in one snapshot with the guard); an embedded index costs none. A readiness check is "every index
   `None`"; on a read-only catalog it asks for `TypoDictionary` only. **A linked index that an application keeps without triggers by choice is never ready by this check** — it names `Triggers` for ever, and
   `Initialize()` builds no dictionary for it; the docs say so, and such an application leaves the flag out of its check.
14. **`AddLuiOutbox(TimeSpan)` and `AddLuiOutbox(Action<LuiOutboxOptions>)` register the only background loop LUI needs.** `LuiOutboxService` is a `BackgroundService`: it calls `catalog.ApplyPending()`
   every `Interval` (30 s) and does **nothing at the start**. A failing round never stops the host: it is logged through the host's `ILogger` (a warning, with the exception), the next round waits
   `Interval` × 2ⁿ for the n-th failure in a row, cut at `MaxBackoff` (5 min; a value below the interval is the interval), and the first round that works goes back to `Interval`. The round is a task of the
   service — the pattern of issue #185 — so the host that stops the service waits for it. Every process with the registration runs its own loop; that is safe (one writer at a time). The loop waits
   on the `TimeProvider` of the container, so a test moves a fake clock instead of waiting for one. On a catalog opened with `OpenReadOnly` the host does not start (`InvalidOperationException`: "a read-only
   catalog cannot apply the outbox"); the hosting package learns it from the new `LuiCatalog.IsReadOnly`, which is the way the catalog was opened and no second switch (ADR-0068) — the core grants the hosting
   assembly no access to its internals (the `InternalsVisibleTo` list is the test projects).
15. **The host pattern: the factory of `AddLui` only opens the catalog.** Creating the indexes and initializing them are explicit steps after `Build()`, so that every configuration error (an invalid
   definition, a mismatch, an unregistered payload type, a missing table of a linked index) surfaces at a line of `Program.cs`, not inside a service resolution — and a failing factory, which the container does not
   cache, never leaves an open catalog behind. The documented default for a container is a background service **of the application** that calls `Initialize()`: the host is up at once, the readiness check says "not
   ready" until it is done, and a long first sync does not fail a liveness probe and restart the pod; the short form for a small application or a tool is `catalog.Initialize();` before `app.Run()`. **By the size of the table** (ADR-0077): "not ready" is a fact about the search and stops no write of the application — the triggers come first, so no row is lost — but the first sync of a very large table beside a busy application can starve the application's writers: a small or medium table is initialized in the background, a very large one before the application accepts writes (or with a generous busy timeout and a retry on `SQLITE_BUSY`). The outbox loop competes with that sync for the write lock (a longer interval, or register it after the first sync). **The initializer repeats `Initialize()` until the report says `Ready`**: the call is idempotent, and it can end "not ready" without a failure — another process runs the first sync, or the lease of a killed predecessor runs out only after a minute (ADR-0077) — skipping that index and its dictionary without waiting; a pattern that calls it once would stay "not ready" for good (found in the review of #294). The repeat is **inside** the loop and covers a `LuiInitializeException` too (a busy file while another replica runs its batches is transient): it is logged as a warning, the pause grows from 5 s to a minute, a permanent failure is logged at every attempt, and nothing escapes `ExecuteAsync` — an exception that escapes would stop the host (round 2 of the review). The loop is for the writing process with the defaults: an index kept without triggers never reaches `Ready` and a read-only reader does not loop on it (its readiness is the dictionaries; start readers after the writer is ready, #287). The outbox loop's `Interval` and `MaxBackoff` are at most 24 days (a longer wait faults `Task.Delay` and would stop the host); a `MaxBackoff` below `Interval` is the interval, by decision, not an error. While an
   index is not initialized, the search answers without corrections, and a linked index shows the batches synced so far (ADR-0077). LUI offers no `AddLuiIndex<T>` and no hosted initializer.
16. **This record supersedes ADR-0043** (the warm-up as a service of the host): `AddLuiWarmup` is replaced by `Initialize()` and `AddLuiOutbox`, and #268 removed it (with `LuiWarmupOptions`); ADR-0043 stays as the record of 0.3.0.

### Alternatives rejected (#266)

- **`InitializeAsync`, or a `Task.Run` inside `Initialize()`.** The work is synchronous SQLite on connections of the pool; a wrapper would pretend that something is asynchronous (the API stays synchronous — the owner, 2026-11-20).
- **A hosted service of LUI that calls `Initialize()`.** The host would decide for the application when and on which thread a long first sync runs, and a failing initialization would take the start down with it — or be
  swallowed by it. The application's own service is about thirty-five lines with its retry (the host pattern), and it is where its logging, its readiness and its failure handling belong.
- **Initialize in the factory of `AddLui`.** A configuration error would surface in a service resolution, and a failing factory would open and leave a catalog that the container does not cache.
- **Build the dictionaries in parallel.** The peak memory would be the sum of the dictionaries, not the largest.
- **Read `NeedsInitialSync` first and sync after, without the lease's transaction.** Two processes that start together could both sync (one after the other).
- **A dictionary for an index with a file step left.** It would miss the words of the sync still to come and have to be rebuilt at its end.
- **Fail on a read-only catalog.** A reader process is a normal citizen of the file; it builds what is its own — the dictionaries — and reports the rest.
- **A probe write in the hosting package instead of a public `IsReadOnly`** (`DropIndex` of a name that does not exist, translated by SQLite's refusal): rejected — it writes the application's file at the start of a host
  only to find out how the catalog was opened.

### Consequences (#266)

- Every new public type is in `docs/api.md` and in the guard `PublicApiShapeTests` with its members unfiltered; `LuiInitializeStep` and `LuiInitializeIndexReport` are the two types the issue implies but does not name
  (the step of a progress, and a line of the report). `LuiIndexInfo` gains a `required` property: a caller that constructs one sets it.
- `Indexes()` is no longer free of reads for a catalog with linked indexes: one connection and one small snapshot per linked index. A health check that polls it every second has it in its budget; #275 measures it with the
  rest of the release.
- Performance of the first syncs is that of ADR-0077, and the dictionaries' that of ADR-0021; nothing is measured here — `Initialize()` is the loop over them.
