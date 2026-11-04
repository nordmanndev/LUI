# 0017 — Dependency injection and hosted services; stale candidates past the budget

> **Name note (ADR-0023, 2026-11-04):** since this record was written, `Owned` is called `Embedded`: `ILuiOwnedIndex` is
> `ILuiEmbeddedIndex`, `LuiIndexBuilder` is `LuiEmbeddedIndexBuilder`, and `OwnedIndex`, `OwnedSource`, `OwnedSync` and
> `OwnedCheck` are `Embedded…`. The text below is history and stays as it was written.

> **Name note (ADR-0024, 2026-11-04):** since this record was written, `LuiRow` is `LuiPayloadRow`, `LuiSyncResult` is
> `LuiLinkedSyncReport`, `LuiPriorityRanking` is `LuiPriorityMode` (`Priorities` → `PriorityMode`), `LuiTimings.Candidates`
> is `CandidateRead`, the linked overload of `AddIndex` is `AddLinkedIndex`, and `Check()`, `WarmUpTypoDictionary` and
> `RebuildTypoDictionary` are on the write interfaces, not on `ILuiIndex`. The text below is history.

> **Note (ADR-0028, 2026-11-04):** `Highlight` is not coming back: LUI delivers no found positions, and issue #20 is closed.
> The text below is history.

- **Status:** Accepted
- **Date:** 2026-11-03
- **Where it lives:** in the second package `Nordmann.LUI.Hosting` — **[ADR-0018](./0018-two-packages-core-and-hosting.md)
  decides the packages and the dependencies**. A first version of this record put the DI into `Nordmann.LUI`; that is
  superseded, and this record keeps only what holds (review of pull request 23, S2).
- **Builds on:** [0013](./0013-the-api-shape-before-the-first-release.md) (the abstractions with the DI, the warm-up as a
  hosted service), [0015](./0015-the-linked-index-is-loosely-coupled.md) (the outbox on a timer; F1)
- **Affects:**
  - `src/Nordmann.LUI.Hosting/` (`LuiServiceCollectionExtensions`, `LuiServicesBuilder`, `Hosting/`)
  - `src/Nordmann.LUI/Querying/CandidateReader.cs`, `LuiResult.cs`
  - spec §7.3, §9.3, §12.5, §12.7, §20; `README.md`

## Context

Step 8 of the plan is the dependency injection of §12.7: `services.AddLui(…).AddIndex(…)`, keyed services, `Ensure()` on
start as an `IHostedService` that can be switched off. Two hosted services were left to this step:

- **ADR-0013:** one that warms the typo dictionary.
- **ADR-0015:** one that applies the outbox of a linked index on a timer.

The review of pull request 16 added **F1**: a linked row that changed and is not applied yet is read as a candidate of its
old text, found not to match, and still counted against the candidate budget — many of them among the best crowd out an
entry that does match.

The step also named `Highlight` (§9.3). **By the owner's decision it is not part of it**: the JavaScript library of the
front end marks the match; highlighting moves to a later version (issue #20).

## Decision

**The registration.**

- `services.AddLui(connectionString, options?)` — or with delegates that read the container, for a connection string out of
  the configuration — registers the catalog as a singleton the container opens on first use and disposes with itself.
  Once per container.
- The settings are kept under a type of LUI's own, not as `LuiCatalogOptions`: an application that registers options for
  a catalog of its own does not change the container's (review of pull request 23, N4).
- `.AddIndex(name, index => …)` takes the description of `lui.Index(name)` from its kind on. The return type of the lambda
  chooses the overload: a `LuiIndexBuilder<TKey, TPayload>` is an owned index, a `LuiLinkedIndexBuilder<TPayload>` a linked
  one.
- Every index is a **keyed singleton under its name**, as its read interface (`ILuiIndex<TKey, TPayload>`, what a suggest
  endpoint needs) and as its write interface (`ILuiOwnedIndex` or `ILuiLinkedIndex`) — the same instance.
- **Refused at the registration:** an empty name, a name twice, and a non-positive interval.
- **Refused when the index is ensured — on start:** a name that is not an index name. The rule of the name is the core's
  (`LuiCatalog.Index`); the hosting package sees no internals (ADR-0018).

**Two hosted services.**

- **`LuiStartup` ensures every index when the host starts.** A definition that does not match, or a table that does not
  fit, stops the start — not the first request. `EnsureOnStart(false)` switches it off.
- With `WarmUpTypoDictionariesOnStart()`, `LuiStartup` then builds the typo dictionaries in the background. Only the
  indexes whose instance has typo tolerance (`ILuiIndex.TypoTolerance`) are warmed; another one would cost memory for
  nothing, and builds its dictionary lazily when a query asks for a correction (`Query(text).TypoTolerance(true)`).
- **`LuiPendingApplier` applies the outbox of a linked index** on the interval its registration names
  (`applyPendingEvery`; none by default). The triggers are not created by it — that stays the application's call
  (ADR-0015).
- **A round that fails** does not end the loop; the next one tries again — a locked file or a renamed column must not
  end the application, and `Pending()` shows that the index falls behind. A failure that lasts is logged as one error;
  the rounds after it at debug level, and the round that succeeds again says how many failed (review of pull request 23,
  N3).
- Both use the `TimeProvider` of the catalog's settings.
- **Both resolve the catalog before they are built**, so the container disposes them before it: a host disposed without
  a stop cancels the warm-up instead of waiting for it, and a round of the outbox that meets a disposed catalog ends the
  loop quietly (review of `main` after pull request 23, findings 1 and 3). With it, the catalog is opened when the host
  starts, also with `EnsureOnStart(false)`.
- **The order of the hosted services is the order of their registration** (.NET). `LuiStartup` is registered by
  `AddLui`, so it starts before a hosted service the application adds later — a migration that creates the table of a
  linked index has to run before: register it before `AddLui`, run it before `host.Run()`, or switch the ensure on start
  off (finding 2).

**What the warm-up does not do** (review of pull request 23, S1). Queries **without** a correction do not wait for it. A
query that **needs** a correction waits until the dictionary is built — it takes the same lock as the build — and its
cancellation token does not interrupt that wait. Without the warm-up the first correction pays for the build itself, so
the warm-up makes nothing worse; it moves the cost to the start, and the window in which a correcting query waits is the
first seconds after the host reports ready. A query that skips the correction while a build runs, and says so, is a
change of the core and belongs to issue #19.

**F1: an outdated candidate does not count.** A row the check against its current text throws out no longer uses up the
budget of `Candidates`. The read goes on past it, up to another budget of such rows; when that is spent too, the answer is
`Truncated` — which then means "the index is behind", not only "there were more". Without such rows nothing changes: the
read stops one row after the budget, as before. Only a source whose texts can change under the index (`Linked`) reads the
longer limit; `Owned` is untouched.

## What was measured

| Test | Result |
|---|---|
| 1,200 rows, the 450 best changed and not applied (budget 500) | 20 hits, all of the rows that still match, the best of them first — before: none |
| the 1,150 best changed | empty and `Truncated`; with `Candidates = 1150`, 20 hits |
| no row changed, budget 10 of 30 | `CandidatesConsidered = 10`, `Truncated` — as before |

Measured by the reviewer of pull request 23, on 100,000 linked rows: `Search("vegan")` with no outdated row median
1.51 ms, with 500 outdated among the best 1.47 ms — the doubled limit costs nothing, the reader stops before it. 1,000
outdated: empty and `Truncated`. And, for S1: with the warm-up running on 300,000 random words, a query without a
correction took 2 ms, one with a correction 11.6 s; `StopAsync` in the middle of the warm-up 8 ms.

## Consequences

- One catalog per container. An application with two databases opens the second catalog itself (`LuiCatalog.Open`).
- An index ensured on start keeps its typo tolerance and similarity as described; they are not part of the definition, so
  the description in `AddIndex` is what every injected instance has.
- `ApplyPending` on a timer makes a linked index with triggers as current as the interval; a health check asks `Pending()`.
- The edge of F1 is hard: 500 outdated rows among the best of a budget of 500 still give hits, **501** give none —
  the read stops at the 501st (corrected after a review of `main`, finding 5; the reviewer of pull request 23 had
  measured it so). A count of
  the outdated candidates in the result would let a health check see why; it is not built (review of pull request 23,
  N1) — `Pending()` is the value to watch.
- The typo dictionary is still built anew at the end of a sync with changes (ADR-0016), and a correcting query waits for
  a build that runs; both are issue #19. *(Done by [ADR-0021](./0021-a-stale-typo-dictionary-and-a-query-that-does-not-wait.md):
  the sync marks it stale, a query does not wait for a build another thread runs and says `CorrectionSkipped`.)*

**Verified by:** `tests/Nordmann.LUI.Tests/DependencyInjectionTests.cs` (11 tests, against a real host):

- keyed services, the same instance for both interfaces, ensured on start;
- a mismatch stops the start;
- an invalid name stops the start;
- without ensure on start, an index is ensured on first resolution;
- a linked index whose outbox is applied on its interval (`FakeTimeProvider`);
- a failed round does not end the loop, with one error for a lasting failure and the recovery logged;
- the application's own `LuiCatalogOptions` do not change the container's catalog;
- the warm-up of the indexes with typo tolerance only;
- `ILuiIndex.TypoTolerance`;
- the catalog disposed with the host;
- the registration refusing what cannot work.

Seen red: without the ensure and the `ApplyPending` call, four of them fail.

Also verified by `LinkedIndexTests` (`Stale_candidates_do_not_use_up_the_budget_up_to_a_second_budget_of_them`,
`Without_stale_candidates_the_budget_is_what_it_was`), and by the package smoke test, which runs the README's host block.
The rule that the hosting knows only the public API is the compiler's (ADR-0018).
