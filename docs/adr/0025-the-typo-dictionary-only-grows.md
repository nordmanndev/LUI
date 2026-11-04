# 0025 — The typo dictionary only grows: no `Stale`, no background rebuild, one dictionary per index

- **Status:** Accepted (owner's decision, 2026-11-04: option G of #48, "wenn du G als gut empfindest und die Komplexität dadurch reduziert wird, finde ich, ist das ein guter Weg")
- **Date:** 2026-11-04
- **Issue:** #48
- **Amends:** [0021](./0021-a-stale-typo-dictionary-and-a-query-that-does-not-wait.md) — the part that made a built dictionary
  *stale* and had a hosted service build it anew
- **Affects:**
  - `LuiTypoDictionaryState` (`Stale` removed), `LuiSyncReport.TypoDictionaryStale` (removed)
  - `LuiServicesBuilder.RebuildStaleTypoDictionariesEvery` and the hosted service `LuiTypoRebuilder` (removed)
  - `Typos/TypoDictionary.cs` (the marks counter, `MarkStale()` and the built-at count removed; the build uses SymSpell's bulk path)
  - `Indexes/EmbeddedSync.cs`, `Hosting/LuiRegistrations.cs`, `Hosting/IndexRegistration.cs`, `Hosting/LinkedIndexRegistration.cs`
  - spec §10.1, §12.4, §12.7, README, CHANGELOG

## Context

ADR-0021 made a `Sync` that changed or removed entries mark the dictionary *stale* instead of building it anew, and put a
hosted service (`LuiTypoRebuilder`) in the host to build stale dictionaries on an interval. The state needed a counter, not
a flag (a sync during a build must not be forgotten), and the service needed its own loop, its own interval and its own tests.

Issue #48 looked at what that machinery buys. What was found:

- SymSpell can be added to at any time (`CreateDictionaryEntry`); it cannot remove. The earlier claim that it is "built once
  and then read" was wrong.
- A word that lingers in the dictionary costs **almost nothing**: a correction towards it becomes a variant of the query,
  the variant finds no candidate in the index, and it falls away. The index is the filter.
- What "stale" protects against is therefore memory (a removed word occupies a few entries) and the exactness of the
  frequencies that order the three suggestions. Neither is worth a state, a counter and a hosted service.
- The dictionary of **beginnings** (three to ten letters) recovers 91 % of the intended beginnings at one edit, against 35 % for
  whole words at one edit and 54 % at three edits (which costs 457 MB and 15 s). Its size follows the vocabulary, not the
  beginnings (132 MB for whole words, 150 MB for beginnings). So the dictionary stays as it is.

## Decision

**The typo dictionary of an index only grows (option G of #48).**

1. It is built once — on the first correction, by `WarmUpTypoDictionary()`, or by a start with warm-up — and it learns the
   words of every write through LUI at once (`Upsert`, `UpsertMany`, `Sync`, `Refresh`).
2. **Nothing rebuilds it on its own.** A removed or changed-away word lingers until `RebuildTypoDictionary()` or a restart.
   `RebuildTypoDictionary()` stays, as the application's lever for a quiet phase.
3. `LuiTypoDictionaryState` is `NotBuilt`, `Building`, `Current`. `Stale`, the marks counter, `MarkStale()`,
   `LuiSyncReport.TypoDictionaryStale`, `LuiTypoRebuilder` and `RebuildStaleTypoDictionariesEvery` are gone.
4. **Kept from ADR-0021:** a query does not wait for a build (`CorrectionSkipped`); the build a query starts takes no token of
   the query; the first build queues the words written meanwhile and replays them; the lock; the warm-up on start;
   `WarmUpTypoDictionary()` and `RebuildTypoDictionary()` end their wait for a build another thread runs with their token.
5. **The build uses SymSpell's bulk path** (`SuggestionStage` + `CommitStaged`), which collects the deletes of all words
   first and commits them once.
6. **Every index has a dictionary of its own** (the owner's requirement): a search among tags is never corrected towards names,
   nor the other way round. The registry of a catalog is keyed by the name of the index; this was already so (review of
   pull request 10, S1) and was not pinned by a test. It is now: `TypoDictionaryStateTests` (the registry) and
   `TypoDictionaryLifecycleTests` (two real indexes, one query each), and a mutation in `tools/mutation-check.sh`.
7. **Not changed here:** `compactLevel` of SymSpell (default 5). A lower level measured 150 → 93 MB and the lookup 8 → ~100 µs;
   it is a trade between memory and latency, and a follow-up if the memory matters.
8. **The `Sync()` of a linked index** (a full refill of the index from the table, called by the application) still builds a
   built dictionary anew at its end: it is an explicit "start over", and the words of the table may have changed without LUI
   seeing them. It is not a background mechanism.

### Measured

685,829 words, 300,000 entries, the typo dictionary of beginnings (`SyncPerformance`, gated):

| | ADR-0021 | this record |
|---|---|---|
| build (warm-up), first | 2.8 s | **2.3 s** |
| build anew (`RebuildTypoDictionary`) | 2.1 s | **1.6 s** |
| delta sync of 100 changes and 100 removals | 2.6 s | 2.2 s (the sync does not touch the dictionary in either; the difference is noise) |

## Consequences

- **Less code:** one state, one counter, one hosted service, one option, one report field and their tests are gone.
- **A host that never restarts and removes many words** keeps them in memory. The cost is small, bounded by the vocabulary the
  index ever had; an application that cares calls `RebuildTypoDictionary()` in a quiet phase.
- **The order of the three suggestions** follows the frequencies at the time of the build (new words add their weight only when
  their beginning is new, ADR-0013); `RebuildTypoDictionary()` makes it exact.
- **A rollback of the application's transaction** leaves the beginnings of its words in the dictionary (ADR-0013); unchanged.
- **API:** `LuiTypoDictionaryState.Stale`, `LuiSyncReport.TypoDictionaryStale` and `LuiServicesBuilder.RebuildStaleTypoDictionariesEvery`
  were never released (0.1.0 is the first release), so nothing breaks.
- **Rejected:** a rebuild on a threshold (a counter again), a rebuild by a timer without a state (the same service), and a
  dictionary of whole words (the numbers above).
