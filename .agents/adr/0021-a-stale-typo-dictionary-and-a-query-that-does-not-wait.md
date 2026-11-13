# 0021 — A stale typo dictionary, a query that does not wait, a rebuild in the background

> **Name note (ADR-0023, 2026-11-04):** since this record was written, `Owned` is called `Embedded`: `ILuiOwnedIndex` is
> `ILuiEmbeddedIndex`, `LuiIndexBuilder` is `LuiEmbeddedIndexBuilder`, and `OwnedIndex`, `OwnedSource`, `OwnedSync` and
> `OwnedCheck` are `Embedded…`. The text below is history and stays as it was written.

> **Name note (ADR-0024, 2026-11-04):** since this record was written, `LuiRow` is `LuiPayloadRow`, `LuiSyncResult` is
> `LuiLinkedSyncReport`, `LuiPriorityRanking` is `LuiPriorityMode` (`Priorities` → `PriorityMode`), `LuiTimings.Candidates`
> is `CandidateRead`, the linked overload of `AddIndex` is `AddLinkedIndex`, and `Check()`, `WarmUpTypoDictionary` and
> `RebuildTypoDictionary` are on the write interfaces, not on `ILuiIndex`. The text below is history.

> **Superseded in part by [ADR-0025](./0025-the-typo-dictionary-only-grows.md) (2026-11-04):** the `Stale` state, the marks
> counter, `LuiSyncReport.TypoDictionaryStale`, `RebuildStaleTypoDictionariesEvery` and the hosted service `LuiTypoRebuilder`
> were removed before the first release — a built dictionary only grows, and nothing rebuilds it on its own. What stays: a
> query does not wait for a build, and `CorrectionSkipped`. The text below is history.

- **Status:** Accepted (owner's decision, 2026-11-04: "#19 before 0.1.0, as proposed")
- **Date:** 2026-11-04
- **Issue:** #19
- **Review:** pull request 34 (I1–I3, M1–M8) — worked in, see "Decision" and "Consequences"
- **Amends:** [0016](./0016-sync-check-rebuild.md) (a sync rebuilt a built dictionary at its end),
  [0017](./0017-dependency-injection-and-hosted-services.md) (a correcting query waited for a build another thread ran)
- **Affects:**
  - `LuiTypoDictionaryState` (new), `ILuiIndex.TypoDictionaryState` (new), `LuiResult.CorrectionSkipped` (new),
    `LuiSyncReport.TypoDictionaryStale` (new)
  - `LuiServicesBuilder.RebuildStaleTypoDictionariesEvery(TimeSpan?)` (new), the hosted service `LuiTypoRebuilder` (new)
  - `Typos/TypoDictionary.cs`, `Querying/QueryRunner.cs`, `Querying/TypoCorrection.cs`, `Indexes/OwnedSync.cs`
  - spec ADR-0012, ADR-0012, ADR-0016, ADR-0017, ADR-0034

## Context

Two costs of the typo dictionary were left open by PR-007 and PR-008:

1. **A delta sync paid a full rebuild.** SymSpell cannot forget a word, so `Sync` of an owned index rebuilt a built
   dictionary at its end whenever it changed or removed anything — the whole dictionary for one changed entry. The
   reviewer of pull request 18 measured 12.1 s against 2.3 s for one change among 300,000 words (S2).
2. **A correcting query waited for a build another thread ran.** The warm-up of the hosting package builds in the
   background; a query that needed a correction meanwhile waited on the build gate — seconds for a large index — and
   its token did not end the wait (review of pull request 23, S1; ADR-0017).

## Decision

**A sync marks the dictionary stale instead of building it anew.** An owned sync calls `MarkStale()` after **every
commit** that changed or removed entries — a batch, a batch of the removal — not once at its end, which a cancelled sync
never reaches (review of pull request 34, M4). The new words of its batches are learned as before (`AddRange` after each
commit), so a new word is corrected at once; a removed or changed-away word lingers, and a correction to it finds
nothing — the behaviour of `Remove` since ADR-0012. `RebuildTypoDictionary()` brings it in step; the state is `Current`
again. The report of the sync says it: **`LuiSyncReport.TypoDictionaryStale`** (M6) — a console application learns it
where it happens, without asking the index.

**Stale is a count, not a flag** (review of pull request 34, I1, I2). A `volatile bool` written by the sync and cleared
by the build lost a mark in two windows — between the two reads of the mark's guard, and between the build's capture
and clear of the flag — and the state said `Current` for a dictionary that was behind, for good, because the background
service repairs only `Stale` ones. Now every mark counts one up (`Interlocked.Increment`, no guard), a build reads the
count **before** it reads the table, and the publish stores that count with the dictionary. The dictionary is stale
exactly when a mark came after the count it was built at:

- a sync that committed before the build read the table is in what it read — a mark of it that lands after the count is
  read gives one rebuild too many, never one too few;
- a sync that commits while the build reads leaves the published dictionary stale;
- a build that fails publishes nothing and changes no count: the old dictionary keeps its own, stale if a mark came
  after it — without a special case.

**The state is public:** `ILuiIndex.TypoDictionaryState` is one of

| State | Means |
|---|---|
| `NotBuilt` | not built yet — the first correcting query or `WarmUpTypoDictionary()` builds it |
| `Building` | a build runs |
| `Current` | built, in step with the index |
| `Stale` | built, a sync changed or removed entries since — new words are known, removed ones linger |

A build that fails leaves the old dictionary answering, in the state it had. A sync during the first build leaves the
dictionary `Stale` once it is published — the build may have read the table before the sync wrote it.

**A query does not wait for a build that runs.** `TypoDictionary.TryEnsureBuilt` takes the build gate only if it is
free. A query that wants a correction while another thread builds answers with what its input found, and says so:
**`LuiResult.CorrectionSkipped`** is `true`. When no build runs and the dictionary is not built, the query builds it
itself, as before — that is the first correcting query without a warm-up, and only that query waits. That build
(review of pull request 34, I3):

- **takes no token of the query.** The build is the index's, not the request's: an autocomplete cancels its request at
  the next keystroke, and a build bound to it would start anew with every typo and be thrown away each time — at three
  million entries (half a minute) it would never finish. The cancelled query ends after the build, with its
  `OperationCanceledException`, and leaves the dictionary built;
- **that fails is a correction not searched, not a failed query:** `CorrectionSkipped` is `true`, the dictionary stays
  `NotBuilt`, and the next query that needs a correction tries again — the retry needs no background service. Only a
  catalog that is gone (`ObjectDisposedException`) ends the query.

**A wait for the build gate looks at its token** (M3): `WarmUpTypoDictionary` and `RebuildTypoDictionary` wait for a build
another thread runs in steps of 50 ms and end with their token — the stop of a host is not held up by it. (ADR-0017 said
the token did not end that wait; it does now.)

**The host rebuilds a stale dictionary in the background.** `Nordmann.LUI.Hosting` gets a third hosted service,
`LuiTypoRebuilder`: every `RebuildStaleTypoDictionariesEvery` (default **one minute**; `null` switches it off) it
rebuilds the dictionaries that are `Stale`. One that is `NotBuilt` it leaves alone — building it is the warm-up's job, or
the next correcting query's, after a failure too. A failed rebuild is logged as an error, the next round tries again.
The indexes of a round are rebuilt **one after the other** (M2): with N stale dictionaries of T seconds each, a round
takes N × T, and the next one begins an interval after it — one build at a time keeps the memory of the process at one
dictionary more, not N. The core starts no thread of its own (ADR-0018): a
console application calls `RebuildTypoDictionary()` after its sync, or when `TypoDictionaryState` says `Stale`.

**The sync of a linked index is unchanged.** `ILuiLinkedIndex.Sync()` reads the whole table into the index anew; a
dictionary built anew with it is in proportion to the work. The shadow build of #31 may revisit it.

## What was measured

`SyncPerformance.A_delta_sync_with_a_built_typo_dictionary` (in the layer of the measurements, 300,000 entries): 300,000
single words of the German word list (`Corpus`, seed 42), first loaded by `Sync`, the dictionary warmed up, then a sync
with 100 changes and 100 removals (default batch of 10,000). Two runs each, the measurement machine of
`docs/benchmarks.md`. "Before" is the same test with the sync rebuilding the dictionary at its end, as on `main`.

| | Before (rebuild in the sync) | After (marked stale) |
|---|---:|---:|
| delta sync of 100 changes and 100 removals | 4.4 / 4.8 s | **2.6 / 2.6 s** (2.8 s with the marks per commit of the review) |
| `RebuildTypoDictionary()` on its own | — | 2.0 / 2.3 s |
| state after the sync | `Current` | `Stale` |

The difference is the rebuild — at 300,000 words about two seconds, at the whole word list 4–5 s (ADR-0012), at three
million entries proportionally more. The sync no longer pays it; the background service or the application does, when
it chooses. (The reviewer's 12.1 s were on random compounds, whose dictionary is larger.)

## Consequences

- **The correction of a stale dictionary is a little worse for a while:** a removed word still has its beginnings, so a
  typo near one may be corrected to it and find nothing, where the rebuilt dictionary would have offered the next best
  word. With the hosting package this lasts an interval plus a rebuild — **if the rebuild finishes between two syncs that
  change** (M7). An application whose syncs change entries more often than a rebuild takes (about 30 s at three million
  entries, ADR-0020) overtakes every rebuild: the dictionary stays `Stale`, and every round pays a full rebuild for
  nothing. Such an application sets a longer interval, or switches the service off (`null`) and calls
  `RebuildTypoDictionary()` itself when its syncs are quiet.
- **A rebuild the service and the application start at the same time is done twice** (M1): the service reads the state,
  then rebuilds — a rebuild of the application between the two makes its own redundant. It costs one build, it is rare
  (the application has no reason to rebuild on a schedule when the service does), and closing it would need a public
  "rebuild if stale" — not worth an API.
- **`CorrectionSkipped` is an honest answer, not an error:** an endpoint may show the uncorrected hits, or ask again. A
  query that does not need a correction (a word beginning among its candidates) never sets it.
- `ILuiIndex.TypoDictionaryState` is an addition to the interface; an application's own implementation (a test
  double) adds a property — the same note as for `TypoTolerance` in ADR-0018.
- The timings of a query (`TrackTimings`) show no correction time for a query that skipped it.
- **The first correcting query still builds inline, inside its read transaction** (M5, pre-existing in shape): in WAL —
  the default of `LuiCatalog.Open` — the snapshot holds the checkpoint back for the build; with WAL switched off a
  writer that waits could block the build's read until the busy timeout. A follow-up (#35) moves the build out of the
  query's transaction.

**Verified by:**

- `TypoDictionaryStateTests` (unit): the states, a second build that does not wait, a failed build that leaves the old
  dictionary as it was, a sync during the first build, a mark during a rebuild that succeeds or fails (I2), a query's
  build that fails and the next that succeeds (I3), a wait for the gate that ends with its token (M3);
- `TypoDictionaryLifecycleTests` (integration): a query during a blocked build skips the correction and says so, and
  corrects after it; a query whose build fails answers with `CorrectionSkipped` (I3); a query cancelled while it builds
  leaves the dictionary built (I3); a sync that changes or removes marks stale and says so in its report (M6), one that
  only adds does not, a rebuild makes current; a sync cancelled after a committed change leaves it stale (M4);
- `DependencyInjectionTests`: `A_typo_dictionary_a_sync_left_stale_is_built_anew_in_the_background` (with a
  `FakeTimeProvider`), `The_interval_of_the_rebuild_is_positive_or_off`;
- `SyncPerformance.A_delta_sync_with_a_built_typo_dictionary` (gated): the table above.
- The mutation `a-build-forgets-the-marks-during-it` (`tools/mutation-check.sh`): a publish that takes the count after the
  table read instead of before is caught by `TypoDictionaryStateTests`.
