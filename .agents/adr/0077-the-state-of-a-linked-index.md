# 0077 — The state of a linked index is a fact of the file: `NeedsInitialSync` is stored and read from the file, and `CreateTriggers()` is idempotent

- **Status:** Accepted (owner, 2026-11-20)
- **Date:** 2026-11-22
- **Builds on:** [0073](./0073-the-index-definition-and-the-typed-handles.md) (the typed handles; LUI format 2),
  [0075](./0075-the-catalog-holds-the-definitions-and-the-runtime-of-an-index.md) (the guard inside the snapshot of an operation; what another process can change without a change of the schema is read from the file),
  [0015](./0015-the-linked-index-is-loosely-coupled.md) (the linked index is loosely coupled: no trigger unless the application asks),
  [0016](./0016-sync-check-rebuild.md) (sync, check, rebuild),
  [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md) (one writer at a time),
  [0071](./0071-the-freeze-comes-with-1-0-0.md) (a break before 1.0.0 needs a reason, a migration note and a record)
- **Affects:**
  - `src/Nordmann.LUI/Schema/Meta.cs` — the column `needs_initial_sync` of `lui_meta`
  - `src/Nordmann.LUI/Indexes/IndexStore.cs` — the flag is written with a new row (1 for a linked index, 0 for an embedded one), read in a given snapshot and cleared in a given transaction
  - `src/Nordmann.LUI/Indexes/LinkedIndex.cs` — `NeedsInitialSync`, the flag cleared in the transaction of `Sync`, `CreateTriggers` as a state
  - `src/Nordmann.LUI/LuiLinkedIndexOfKey.cs`, `src/Nordmann.LUI/LuiLinkedIndexOfKeyAndPayload.cs`, `src/Nordmann.LUI/ILuiLinkedIndex.cs`, `src/Nordmann.LUI/LuiCatalog.cs` — the property and the words of the documentation
  - `tests/TestData/golden/embedded-schema.txt` — the golden DDL of `lui_meta`
  - `docs/api.md`, `docs/testing.md`, `CHANGELOG.md`, `.agents/adr/README.md`
  - `tests/Nordmann.LUI.Integration.Tests/Linked/LinkedStateTests.cs`, `tests/Nordmann.LUI.Integration.Tests/Linked/LinkedTriggerTests.cs`, `tests/Nordmann.LUI.Architecture.Tests/PublicApiShapeTests.cs`, `tools/mutation-check.sh` — the rules and the guards
- **Issue:** [#272](https://github.com/nordmanndev/LUI/issues/272), part of the epic of 0.4.0 ([#258](https://github.com/nordmanndev/LUI/issues/258)); #271 adds the batched sync to this record

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
   complete. Nothing else clears it — not a refresh, not the outbox, not `Check()`. Until #271 lands, "complete" means the single
   transaction of today committed.
   **The edge:** `Sync()` rebuilds a typo dictionary that is built *after* the commit of the index. A cancellation during that rebuild
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

## Alternatives

| | For | Against |
|---|---|---|
| **A — a column of `lui_meta`** (this decision) | one row per index, dropped with it; a plain `UPDATE` bumps no schema counter; written in the transaction of the sync | the golden DDL of `lui_meta` changes with format 2 |
| **B — a table of its own per linked index** | the golden stays | a second table per index to create and drop; the issue names `lui_meta` |
| **C — a field of the definition JSON** | no new column | the flag would take part in the comparison of definitions and change every stored definition; it changes without the definition changing |
| **D — a flag in memory (the map of 0075)** | no read | another process completes the sync without a change of the schema; the catalog would not see it |

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
  a loop over them, not a new mechanism.
