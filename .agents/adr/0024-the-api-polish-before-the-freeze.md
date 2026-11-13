# 0024 — The API polish before the freeze: `AddLinkedIndex`, `entries`, `CandidateRead`, `LuiPayloadRow`, `LuiException`, `LuiLinkedSyncReport`, a read interface that only reads

> **Note (ADR-0062, 2026-11-13, issue #145):** the naming of A.3 lived in the hosting package, and the builder of the
> indexes went with the entry point (ADR-0033). The route is named in the call today as well — in the catalog:
> `Create` creates an embedded index, `CreateLinked` a linked one. The text below is history and stays as it was written.

- **Status:** Accepted (owner's decisions, 2026-11-04, after an independent architecture review)
- **Date:** 2026-11-04
- **Issue:** #42, step 3 of epic #39 (the public API before the freeze of 0.1.0)
- **Builds on:** [0013](./0013-the-api-shape-before-the-first-release.md) (the API shape), [0017](./0017-dependency-injection-and-hosted-services.md)
  and [0018](./0018-two-packages-core-and-hosting.md) (the hosting package), [0023](./0023-owned-becomes-embedded.md) (`Embedded`)
- **Affects:**
  - `ILuiIndex`, `ILuiEmbeddedIndex`, `ILuiLinkedIndex`, `ILuiSimilarity`
  - `LuiQuery`, `LuiQueryOptions`, `LuiTimings`, `LuiPriorityMode` (was `LuiPriorityRanking`), `LuiPayloadRow` (was `LuiRow`),
    `LuiLinkedSyncReport` (was `LuiSyncResult`), `LuiException` (new)
  - `LuiServicesBuilder.AddIndex` / `AddLinkedIndex` (hosting package)
  - `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs`, `tools/mutation-check.sh`, ADR-0030–ADR-0016, README, CHANGELOG

## Context

An independent architecture review of `main` (2026-11-04) found that the API has never been published, so every change that
breaks a name costs nothing today and a minor version with a migration note after 0.1.0. The owner went through the review
point by point and decided what is done before the freeze (epic #39). This record is the group of changes to the public
API that are small on their own. The rename of `Owned` is [0023](./0023-owned-becomes-embedded.md); the freeze is a record of
its own.

## Decision

| # | Before | After | Why |
|---|---|---|---|
| A.3 | `AddIndex` twice, one overload chosen by the return type of the lambda | `AddIndex` (embedded) and **`AddLinkedIndex`** (linked, with `applyPendingEvery`) | The route is named in the call, not guessed by the compiler; `applyPendingEvery` exists on the side that has an outbox. A linked description passed to `AddIndex` does not compile any more. |
| A.4 | `LuiPriorityRanking`, `.Priorities(…)`, `LuiQueryOptions.Priorities` | **`LuiPriorityMode`**, `.PriorityMode(…)`, `PriorityMode` | `LuiPriorityRanking` and `LuiRankingOrder` differed by one word and steer different things. `LuiPriority` itself is **not** renamed (owner). |
| A.5 | `LuiTimings.Candidates` | **`CandidateRead`** | `LuiQueryOptions.Candidates` is a budget (`int`), `LuiResult.CandidatesConsidered` a count, `LuiTimings.Candidates` a duration — three meanings of one word in one query. |
| A.6 | `Sync(source, …)`, `Check(source)` | **`entries`** | The parameter is the set of entries. "Source" stays the word for where the rows come from (`lui_meta.source`, `ContentSource`). |
| A.7 | `LuiRow` | **`LuiPayloadRow`** that **throws** when it is read after the mapping | It is the payload columns of one hit, not "a row". A row that was kept read the next hit through the reader, without a word. |
| A.8 | three exceptions, each an `InvalidOperationException` | all three derive from the new **`LuiException`** (abstract, an `InvalidOperationException`) | A caller had no type for "a condition LUI reports"; `catch (LuiException)` is one. |
| B.1 | `LuiSyncResult` | **`LuiLinkedSyncReport`** | One vocabulary — a *report* —, two types: the sync of a linked index rebuilds from a table (counts of rows indexed, without text, out of range), the sync of an embedded index compares with a source (added, changed, removed). One type would have half of its fields always 0. |
| B.3 | `Check()`, `WarmUpTypoDictionary()`, `RebuildTypoDictionary()` on `ILuiIndex` | on **`ILuiEmbeddedIndex`** and **`ILuiLinkedIndex`** | The interface a suggest endpoint gets injected only reads. `TypoTolerance` and `TypoDictionaryState` stay: the hosting package sees the public API only (ADR-0018). |
| B.4 | `ILuiSimilarity.Name` | removed | Nothing in the library read it; every measure of your own had to invent a name. |
| B.2 | — | a **guard**: every property of `LuiQueryOptions` has its method on `LuiQuery`, and every setting method of `LuiQuery` is a property | The settings of a query exist twice, and nothing said that the two sets are the same. No `With…` methods: a record has `with`. |
| D.4 | — | README and ADR-0017, ADR-0034: `AddLui` opens the database when the host starts, also with `EnsureOnStart(false)` | ADR-0017 said it; the README, which people read, did not. |

### Notes on the decisions

- **`LuiPayloadRow` stays a class and is not a snapshot.** A `ref struct` cannot be the argument of a `Func<…>`, which
  `WithPayload` takes. A copy of the values per hit would allocate per hit and meet issue #30. The row is created per hit
  anyway; it forgets its reader when the mapping returns (`Invalidate()` in a `finally`), which costs no allocation.
- **`LuiException` is abstract with protected constructors.** The library throws the three; an abstract class can become
  concrete later without breaking anyone, a concrete one cannot become abstract. It is the base of the exceptions about an
  index or its environment. Other conditions stay what they were: `DropIndex` of a linked index with triggers throws a plain
  `InvalidOperationException` — a call that is not valid, not a condition of the index.
- **The maintenance members are declared in both write interfaces**, not in a third interface: the freeze wants few public
  types (`ILuiIndex` and the two write interfaces are all there is).
- **The hosting package changes in one place:** `AddLinkedIndex`. Its helpers take the state and the action as arguments,
  because the state is on the read side and the rebuild on the write side.

## Consequences

- **Breaking, and free:** nothing is published. The compiler finds every call site; a linked description handed to
  `AddIndex` is a compile error.
- **The API ends here for the freeze:** what the review proposed and the owner declined is listed by name in the record of
  the freeze (#43) — `LuiServicesBuilder` keeps its name, `LuiPriority` keeps `High` and `Elevated`, no merged sync type, no
  `With…` methods, no keyed registrations.
- **`ILuiIndex` is a smaller promise.** An application's own implementation of it (a test double) has fewer members to
  provide than in the earlier drafts; an application that called `WarmUpTypoDictionary()` on a read interface now holds the
  write interface for it (which the hosting package registers under the same name).

**Verified by:**

- `Public_API_Shape`:
  - `Every_exception_the_library_exposes_is_a_LuiException`
  - `Every_option_of_a_query_can_be_set_on_the_query_and_every_setting_of_the_query_is_an_option`
  - `The_read_interface_only_reads_and_the_write_interfaces_carry_the_maintenance`
- `LinkedIndexTests.A_payload_row_that_is_kept_throws_instead_of_reading_another_hit`;
- `ApiShapeTests.The_exceptions_of_the_library_can_be_caught_as_one_and_still_as_an_InvalidOperationException`;
- `DependencyInjectionTests` (the linked registrations through `AddLinkedIndex`, the warm-up and the rebuild through the write
  interfaces);
- the mutations `an-option-without-a-method-on-the-query`, `a-member-joins-the-read-interface`,
  `an-event-joins-the-read-interface` (an event is no property and no method without a special name; review of pull
  request 47, M5) and `a-write-interface-loses-a-maintenance-member` (`tools/mutation-check.sh`), each caught by test and
  not refused by the build;
- the package smoke test (the README is compiled and run with the new names).
