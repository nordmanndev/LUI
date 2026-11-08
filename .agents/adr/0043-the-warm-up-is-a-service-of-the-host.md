# 0043 — The warm-up is a service of the host

- **Status:** Accepted
- **Date:** 2026-11-07
- **Builds on:** [0017](./0017-dependency-injection-and-hosted-services.md) (the dependency injection and the hosted services),
  [0018](./0018-two-packages-core-and-hosting.md) (the core and the hosting package),
  [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the container holds the catalog and no index),
  [0034](./0034-the-hosting-package-carries-only-the-dependency-injection.md) (the hosting package carries the dependency injection),
  [0042](./0042-the-query-does-not-build-the-typo-dictionary.md) (the query does not build the typo dictionary — the application does)
- **Affects:**
  - `src/Nordmann.LUI.Hosting/LuiServiceCollectionExtensions.cs` (the new registration)
  - `src/Nordmann.LUI.Hosting/Hosting/LuiWarmupService.cs` (the service)
  - `src/Nordmann.LUI.Hosting/Nordmann.LUI.Hosting.csproj` (the dependency set)
  - `src/Nordmann.LUI/LuiCatalog.cs` (the two new methods)
  - `src/Nordmann.LUI/Indexes/IndexStore.cs` (the list of the indexes of a catalog)
  - `tests/Nordmann.LUI.Architecture.Tests/Public_API_Shape.cs` (the guard of the registrations of the container)
  - `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs` (the guard of the dependency set of the hosting package)
  - `tests/Nordmann.LUI.Integration.Tests/LuiWarmupTests.cs` (the four cases)
  - `docs/api.md`, `docs/testing.md`, the CHANGELOG, the README, the tutorial
- **Issue:** #68

## Context

ADR-0042 took the building of the typo dictionary out of the query: a query asks the index whether a dictionary is
there and answers without a correction if there is none (ADR-0021). The dictionary is built by the application — with
`WarmUpTypoDictionary()`, with `RebuildTypoDictionary()` or with a write. Three things an application can forget:

- the **warm-up** of the dictionaries at the start — without it the first queries answer without corrections;
- the **outbox** of a linked index — the rows the triggers noted wait until `ApplyPending()` (ADR-0015);
- the **rebuild** in a quiet phase — a dictionary only grows (ADR-0025), and what was removed lingers until the rebuild.

The first two happen at a moment the application knows: the start of the process and a moment after a change. The
third is a decision of the application (when is quiet?).

ADR-0017 gave the hosting package the dependency injection **and the hosted services**; ADR-0033 and ADR-0034 took
them away: the container holds **the catalog and nothing else**, and the package carries **the dependency injection and
nothing else** — a package that carries what it does not use makes its consumers carry it too.

ADR-0034's rule was about the *dependency set*: the package must carry what it uses. It never said that a host may not
run a service of LUI.

## Decision

**The warm-up is a service of the host — and the host asks for it.**

- `AddLuiWarmup()` in `Nordmann.LUI.Hosting` registers the warm-up: a hosted service that
  1. **applies the outbox of every linked index once at the start of the host**,
  2. **warms the typo dictionaries of every index of the catalog** — after the outbox, so that the dictionary knows
     the words of the rows that came in, and
  3. **applies the outbox on an interval** (30 s by default).
- **Off by default.** Without the call the host starts nothing of LUI: no dictionary is built, no outbox is applied.
- **The library starts no thread of its own.** The service is an `IHostedService` — the host runs it, the host stops it,
  the host disposes it. The timer of the outbox belongs to the service and dies with it.
- **The catalog gains two methods**, both additive (ADR-0026):
  - `LuiCatalog.WarmUpTypoDictionaries()` — builds the dictionary of every index of the catalog (the index that the
    application opens later sees the same dictionary: it belongs to the catalog, not to the instance);
  - `LuiCatalog.ApplyPending()` — applies the outbox of every linked index and returns the number of rows brought in.
- **The dependency set of the hosting package changes**: `Microsoft.Extensions.Hosting.Abstractions` comes back, because
  the package now uses it (`IHostedService`). The rule of ADR-0034 holds — the package carries what it uses — and the
  list changes, the way it changed in ADR-0034 itself. The abstraction is two interfaces without an implementation;
  every application that has a host brings it anyway.
- **What the service is not**: it does not **rebuild** a dictionary (the application decides when a phase is quiet),
  it does not **register an index** in the container (ADR-0033 — the container still holds no index), and it does not
  **write into the table of the application** (ADR-0015 — the outbox is brought into the index, the table stays as it is).

## Consequences

- The three things an application can forget are now two: the application calls `AddLuiWarmup()` and the host does the
  rest. The rebuild stays with the application.
- **The cost is visible**: a dictionary costs 138 MB for 685,829 words (ADR-0021). A host with three indexes warms three
  dictionaries. That is why the service is opt-in — the application knows its indexes, the host does not ask.
- The guard of the registrations changes: `Public_API_Shape.The_container_holds_the_catalog_and_nothing_else` becomes
  `The_container_holds_the_catalog_and_the_warm_up_but_no_index`. The claim of ADR-0033 stands — **the container holds
  no index** — and the list of the registrations grows by one.
- The guard of the dependency set changes with it:
  `Repository_Layout.The_hosting_ships_the_core_and_the_dependency_injection` becomes
  `The_hosting_ships_the_core_the_dependency_injection_and_the_abstractions_of_the_host` (with ADR-0045). The rule does not change (the
  dependency set of a package is its contract), the list does.
- **The warm-up at the start blocks the start of the host**: the host is ready when the dictionaries are built. The
  build is CPU-bound and runs on a thread of its own — the pool of the application belongs to the application.
- **A round of the outbox that fails does not take the host with it**: the rows stay in the outbox, the next round tries
  again, and the application sees the backlog with `Pending()`. *Superseded in this point by
  [ADR-0045](./0045-the-warm-up-tells-the-logger-of-the-host.md): the failure is told to the logger of the host.*
- A file that may not be written is warmed like any other: the dictionary lives in the memory, the build reads.

## Alternatives

| The alternative | Why not |
|---|---|
| The application warms the dictionaries itself, without a service | That is what ADR-0042 left to the application — and what an application forgets. The service is the opt-in answer, not the replacement of the choice. |
| The library starts a thread of its own | A library that starts a thread outlives the object that owns it. The host knows when to start and when to stop — the library does not. |
| The service also rebuilds the dictionaries | A rebuild replaces a dictionary that only grows (ADR-0025) — it belongs to a moment the application chooses, not to a timer. |
| The warm-up lives in the core | The core knows no `Microsoft.Extensions` (the guard `The_core_assembly_knows_no_microsoft_extensions`) — a hosted service is a type of a host. |
| A third package for the warm-up | ADR-0018: two packages, one version. A package for one registration is a package for nothing. |
| The service registers the indexes in the container | ADR-0033: the container describes no indexes. The warm-up needs the names of the indexes, not their services — it asks the catalog. |

## Review of pull request 69

The review found three things in the first version, and they were changed before the freeze of 0.1.0 (ADR-0026):

- **The settings are an options type**, not an optional parameter: `AddLuiWarmup(Action<LuiWarmupOptions>? configure)`
  instead of `AddLuiWarmup(TimeSpan? outboxInterval)`. After the freeze a new setting would have been a new overload;
  now it is a new property of `LuiWarmupOptions`.
- **The application names the dictionaries.** A dictionary costs 138 MB for 685,829 words, and the definition of an
  index does not say whether its queries correct typos — the service cannot know. `TypoDictionaryIndexes` names the
  indexes (empty: every index, as before), and `WarmUpTypoDictionaries = false` leaves only the outbox to the service.
  The catalog gained the overload `WarmUpTypoDictionaries(IEnumerable<string> names, …)`, which checks every name
  before the first build.
- **A failed round is told to the logger of the host** — ADR-0045.
- `AddLuiWarmup` is once per container, like `AddLui`: a second call would have started two services on one catalog.

