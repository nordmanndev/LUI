# 0016 — Sync, check and rebuild: batches that can be run again, a merge at the end, a catalog taken apart

- **Status:** Accepted (the default batch size and the shadow build of a linked sync are open for the owner, see the end)
- **Date:** 2026-11-03
- **Affects:** `ILuiIndex.Check()`, `ILuiOwnedIndex.Sync/Check/Rebuild/Optimize`, `ILuiLinkedIndex` (`Check()` through
  `ILuiIndex`), `LuiSyncOptions`, `LuiSyncReport`, `LuiIntegrityReport`; `src/Nordmann.LUI/Storage/`,
  `Indexes/IndexStore.cs`, `Indexes/OwnedSync.cs`, `Indexes/OwnedCheck.cs`, `Indexes/IndexCheck.cs`;
  `tests/Nordmann.LUI.Architecture.Tests/Internal_Layering.cs`, `tools/mutation-check.sh`;
  specification §12, §14, §17.2, §20
- **Builds on:** [0010](./0010-the-query-budget-at-three-million-entries.md) (option 5, the bulk path),
  [0014](./0014-seams-for-the-second-content-source.md) (D4, D6), [0015](./0015-the-linked-index-is-loosely-coupled.md)

## Context

Step 7 of the plan: an `Owned` index is brought in step with a source (`Sync`), compared with it (`Check(source)`),
checked in itself (`Check()`) and repaired (`Rebuild()`); a `Linked` index gets its `Check()`. The specification asks for
batches in their own transactions that can be cancelled and run again (§14), and the plan's "done when" is a sync of a
million entries, cancelled and run again. ADR-0010 left one lever for the build and the query tail with this step: load
in the order of the rowid, merge once. And the review of the API (DevEx D4, D6) asked that the catalog be taken apart
before the operations that need its connections arrive.

## Decision

**The catalog is a facade** (D4, D6). Its connections, its writer, its lock and the count of running operations are
`Storage.Database` — a layer of its own at the bottom, it knows no other. The stored definitions and the statements
that create and drop an index are `Indexes.IndexStore`; the builders describe an index and hand it over. The typo
dictionaries are `Typos.TypoDictionaries`. No layer below the public API knows `LuiCatalog` any more: the named
exception of `Internal_Layering` is gone, and the mutation check proves the rule (`query-path-uses-the-catalog`,
`storage-knows-the-schema`).

**`Sync(source, options?, cancellationToken)` of an `Owned` index** compares and writes in batches
(`LuiSyncOptions.BatchSize`, default 10,000, 1 … 1,000,000), each in a transaction of its own on the catalog's writer:

- an entry that is missing is added, one that differs (text, weight, priority or payload) is written, one that is the
  same is not touched; a key that comes twice keeps its last entry;
- **what is removed:** at the start the sync notes the keys of the index in a table of the writer's connection (one
  statement); each key of the source is crossed off; what is left when the whole source is read is removed — **the keys
  that were in the index when the sync began and that the source does not have**. A key the application adds while the
  sync runs stays; a key that was there and is not in the source goes, even when the application removed and wrote it
  again meanwhile. By key and not by document id, because SQLite gives a new row the highest id plus one — a removed
  last row's id comes back, and the outcome would depend on it;
- a cancelled or failing sync keeps the batches it committed and removes nothing. Run again, it finds them unchanged
  and writes only the rest — the "Wiederaufsetzen" of §14;
- each batch is written **in the order of the search index** (priority, then weight descending — a new entry's
  document id grows with the order of the inserts) with FTS5's `automerge` off, set back to its default before the
  commit; the search index is merged (`optimize`) once at the end, when anything changed;
- `IProgress<LuiSyncReport>` hears after every batch; an `IAsyncEnumerable` overload takes an asynchronous source
  (§13) — the call still blocks, the writes stay synchronous.

**`Check(source)`** reads the source once, in a snapshot of its own on a connection of its own, and notes what each
key found in a table of the connection's temporary database (the last entry of a key wins, as in a sync): counts and up
to five keys for missing, extra and different entries. It changes nothing.

**`Check()`** is on `ILuiIndex` — every index has it. It finds: a definition in `lui_meta` that is gone or another
than the instance's (the index was dropped or ensured anew); for `Owned`, triggers that are missing or not the ones of
this version, and FTS5's `integrity-check` **against the content table** (`rank = 1`), which also finds a row the
triggers did not carry; for `Linked`, a table that does not fit any more (a finding, not an exception), stale
triggers, the integrity check, and the entries against the rows — missing, extra, and another weight, priority or
beginning (the stored rowid and prefix keys against the ones the row gives). The integrity check is an `INSERT`: it runs
on the writer, under the write lock, and the transaction is rolled back. Each finding is a sentence that names the
repair: `Sync(source)`, `Rebuild()`, `Sync()`, `ApplyPending()`, `CreateTriggers()`.

**`Rebuild()`** of an `Owned` index creates the three triggers anew and the search index from the content table (FTS5
`rebuild` reads it in the order of the rowid), then merges — one transaction. **`Optimize()`** merges.

## What was measured

All on the measurement machine of `docs/benchmarks.md` (4 cores), the whole German word list, entries in random
order, cache 512 MB.

**The order and the merge, before anything was built** (`OwnedBulkExperiment`, gated; batches of 10,000 unless said):

| 1,000,000 entries | load | query p50/p95 before the merge | after |
|---|---:|---:|---:|
| row order (what `UpsertMany` does per batch) | 73.4 s | 1.85 / 5.73 ms | 1.26 / 2.91 ms |
| each batch sorted by its rowid | 64.2 s | 1.36 / 4.47 ms | 1.07 / 3.04 ms |
| sorted, `automerge` off | 61.9 s | 1.74 / 5.83 ms | 1.16 / 3.19 ms |
| sorted, `automerge` off, batches of 25,000 / 50,000 / 100,000 | 54.1 / 48.4 / **42.9 s** | — | 1.04 / 1.05 / 1.00 ms p50 |
| content table without triggers, then FTS5 `rebuild` | 48.3 s (29.4 + 18.9) | 1.71 / 5.22 ms | 1.28 / 3.24 ms |
| sorted, `automerge` off, `wal_autocheckpoint` 100,000 pages | 54.6 s | — | 1.06 / 3.03 ms |

Three things follow. **The merge at the end is the lever for the query**: p95 5.7 → 3.0 ms, whatever the order. **The
order of the rows matters less than the number of commits**: every commit writes the pages it touched into the WAL and
starts a new segment; ten times larger batches are 30 % faster. A larger checkpoint interval gives 12 % for a WAL of up
to 400 MB — not taken. And the "content first, then `rebuild`" path is not faster than large sorted batches, needs the
triggers gone across transactions — an aborted sync would leave the index without them — and cannot be run again; it
stays the repair (`Rebuild()`), not the load.

**The implementation** (`SyncPerformance`, gated, 1,000,000 entries):

| | batch 10,000 | batch 100,000 |
|---|---:|---:|
| sync into an empty index | 72.7 s | 53.6 s |
| the same sync again (nothing changes) | 8.3 s | 8.4 s |
| a sync that changes half of the entries, cancelled halfway | 38.9 s | 23.9 s |
| … run again (250,000 changes left) | 45.4 s | 29.6 s |
| a sync of 100 changes and 100 removals (with the merge) | 8.8 s | 9.4 s |
| `Check(source)` / `Check()` / `Rebuild()` | 10.1 / 2.4 / 17.1 s | 10.4 / 1.9 / 16.2 s |
| `UpsertMany` of the same entries (one transaction, no merge) | 56.0 s | 55.3 s |
| query p50/p95 after the sync · after `UpsertMany` | 1.14/3.55 · 1.61/5.35 ms | 1.22/3.88 · 1.62/5.18 ms |

The sync costs about a second per 100,000 entries more than the same batches would without the comparison (the lookup
of each key); its answers are the merged index's — p95 a third lower than after `UpsertMany`. At 200,000 entries:
sync 9.1–12.0 s, `UpsertMany` 8.3–9.3 s, the same sync again 1.2–1.5 s, `Check()` 0.3 s, `Rebuild()` 2.4 s.

## Consequences

- **A sync in batches of 10,000 is slower than `UpsertMany` in one transaction** (73 s against 56 s for a million).
  That is the price of what the batches give: other writers wait at most about 0.7 s, and an aborted sync keeps its
  work. A first load into a file that no one else writes is a third faster with `BatchSize = 100_000` (about 5 s per
  batch — longer than the default busy timeout of 5 s of another writer); the default stays the specification's.
- The target of §16 for the build (3 M entries in ≤ 3 min) is not measured here; at a million the sync takes 54–73 s.
  PR-009 measures it at 3 M.
- The table of a sync lives in the temporary database of the writer's connection; SQLite keeps it in a file unless
  `temp_store` says otherwise (`MEMORY` gave 12.0 → 10.9 s at 200,000 — within the noise of the runs, not taken).
- A key upserted by the application *during* a sync stays, even when the source does not have it: the sync removes
  what was there when it began. "The source is the truth" holds for the state the sync started from.
- `Check()` of a large index holds the write lock for the integrity check (2 s for a million entries).
- A text of a linked row that changed behind its first two letters is not found by `Check()` — the contentless search
  index does not store the text to compare. A query never shows such a text (the candidates are read against the row,
  ADR-0015), and `Sync()` repairs it.
- ADR-0015 says the write of an older SQLite through the outbox triggers was "argued from the script, not measured";
  the reviewer of pull request 16 measured it with SQLite 3.40.1 (follow-up review). ADR-0015 is corrected.

## Open for the owner

1. **The default batch size.** 10,000 (the specification) keeps other writers waiting under a second; 50,000 would be
   about 20 % faster and hold the lock 2–3 s; 100,000 about 26 % faster and longer than a default busy timeout.
2. **A shadow build for `Linked.Sync()`** (review of pull request 16, S2). It is one transaction: 26.7 s for a million
   rows (ADR-0015), and other writers of the file wait that long. A build in chunks into shadow tables with a short swap
   would hold the lock per chunk only — but a `Refresh` or an `ApplyPending` that runs meanwhile writes the old tables,
   and has to reach the new ones too (noted in the outbox, applied after the swap). Not built here: it is a second write
   path for every refresh, and the owner should decide whether a 30-second lock per million rows is a problem worth it.

**Verified by:** `tests/Nordmann.LUI.Tests/SyncCheckRebuildTests.cs` (29 tests: a sync into an empty index, adds,
changes, keeps and removes, the same sync again, the payload compared, batches and progress, cancelled and run again,
a source that fails, a key added and a key written again while the sync runs, the empty text key, a key twice in a
batch and across batches, the same answers as single upserts, `automerge` back on,
an asynchronous source, a text key, the typo dictionary; `Check(source)` with counts and keys, healthy after a sync, the
last entry of a key, "…" after five keys; `Check()` healthy, a missing trigger and the row it did not carry, a search
index that lost an entry, a changed trigger, a dropped and an anew ensured index, `Rebuild()` repairing each, `Optimize()`;
a linked index healthy, rows it was not told of, the outbox, stale triggers, a renamed column), each guard seen red
(`RemoveNotFound`, the `automerge` reset and the integrity check taken out: six tests fail);
`tests/Nordmann.LUI.Architecture.Tests/Internal_Layering.cs` (seven layers, no catalog exception) and the two new
mutations of `tools/mutation-check.sh`; `OwnedBulkExperiment` and `SyncPerformance` (gated).
