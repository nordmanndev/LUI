# 0017 — Dependency injection and hosted services, in the package; stale candidates past the budget

- **Status:** Accepted
- **Date:** 2026-11-03
- **Builds on:** [0001](./0001-single-package.md) (one package), [0013](./0013-the-api-shape-before-the-first-release.md)
  (the abstractions come back with the DI, this step decides which), [0015](./0015-the-linked-index-is-loosely-coupled.md)
  (the outbox, a hosted service on a timer; F1)
- **Affects:** `src/Nordmann.LUI/Nordmann.LUI.csproj`, `Directory.Packages.props` (a test package), `LuiServiceCollectionExtensions`,
  `LuiServicesBuilder`, `src/Nordmann.LUI/Hosting/`, `Querying/CandidateReader.cs`; spec §9.3, §12.5, §12.7, §17, §20;
  `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs`, `Internal_Layering.cs`, `tools/mutation-check.sh`,
  `tools/package-smoke-test.sh`, `README.md`

## Context

Step 8 of the plan is the dependency injection of §12.7: `services.AddLui(…).AddIndex(…)`, keyed services, `Ensure()` on
start as an `IHostedService` that can be switched off. Three things were left to this step:

- **ADR-0013:** which `Microsoft.Extensions.*` abstractions the package needs.
- **ADR-0013:** a hosted service that warms the typo dictionary.
- **ADR-0015:** a hosted service that applies the outbox of a linked index on a timer.

The review of pull request 16 added F1: a linked row that changed and is not applied yet is read as a candidate of its old
text, found not to match, and still counts against the candidate budget — many of them among the best crowd out an entry
that does match.

The step also named `Highlight` (§9.3). **By the owner's decision it is not part of it**: the JavaScript library of the
front end marks the match; highlighting moves to a later version (issue #20).

## Decision

**In the package, not beside it.** The DI is part of `Nordmann.LUI` (ADR-0001, spec §17.1). The package references three
abstractions again:

- `Microsoft.Extensions.DependencyInjection.Abstractions`: `IServiceCollection`, keyed services.
- `Microsoft.Extensions.Hosting.Abstractions`: `IHostedService`, `BackgroundService`.
- `Microsoft.Extensions.Logging.Abstractions`: the hosted services log.

All three are pinned at 10.0.14. They bring, transitively and nothing else:

- `Configuration.Abstractions`
- `Diagnostics.Abstractions`
- `FileProviders.Abstractions`
- `Options`
- `Primitives`

All of them are MIT and part of every ASP.NET Core and worker application anyway. The implementations (`Microsoft.Extensions.Hosting`, the
container) are the application's; the tests reference `Microsoft.Extensions.Hosting` for a real host.

**The registration.**

- `services.AddLui(connectionString, options?)` — or with delegates that read the container, for a connection string out of
  the configuration — registers the catalog as a singleton the container opens on first use and disposes with itself.
  Once per container.
- `.AddIndex(name, index => …)` takes the description of `lui.Index(name)` from its kind on. The return type of the lambda
  chooses the overload: a `LuiIndexBuilder<TKey, TPayload>` is an owned index, a `LuiLinkedIndexBuilder<TPayload>` a linked
  one.
- Every index is a **keyed singleton under its name**, as its read interface (`ILuiIndex<TKey, TPayload>`, what a suggest
  endpoint needs) and as its write interface (`ILuiOwnedIndex` or `ILuiLinkedIndex`) — the same instance.
- A name twice, an invalid name or a non-positive interval is refused at the registration.

**Two hosted services.**

- **`LuiStartup` ensures every index when the host starts.** A definition that does not match, or a table that does not
  fit, stops the start — not the first request. `EnsureOnStart(false)` switches it off.
- With `WarmUpTypoDictionariesOnStart()`, `LuiStartup` then builds the typo dictionaries in the background — only those of
  the indexes whose description asked for typo tolerance; another one would cost memory for nothing. Queries do not wait.
- **`LuiPendingApplier` applies the outbox of a linked index** on the interval its registration names
  (`applyPendingEvery`; none by default). The triggers are not created by it — that stays the application's call
  (ADR-0015). A round that fails is logged and the next one tries again: a locked file or a renamed column must not end
  the application, and `Pending()` shows that the index falls behind.
- Both use the catalog's `TimeProvider`.

**The hosting knows only the public API.** The registrations and the hosted services live in `Nordmann.LUI.Hosting`, beside
the facade, and ask the indexes what an application could ask them. An architecture test and a mutation
(`hosting-knows-the-storage`) pin it.

**F1: an outdated candidate does not count.** A row the check against its current text throws out no longer uses up the
budget of `Candidates`. The read goes on past it, up to another budget of such rows; when that is spent too, the answer is
`Truncated`. Without such rows nothing changes: the read stops one row after the budget, as before. Only a source whose
texts can change under the index (`Linked`) reads the longer limit; `Owned` is untouched.

## What was measured

Nothing on the query path of `Owned` moved: the limit of its statement is the one it was, and the golden, query-plan and
quality tests pass unchanged. For `Linked`, the statement's limit is twice the budget; it is read only as far as rows that
no longer match require.

| Test | Result |
|---|---|
| 1,200 rows, the 450 best changed and not applied (budget 500) | 20 hits, all of the rows that still match, the best of them first — before: none |
| the 1,150 best changed | empty and `Truncated`; with `Candidates = 1150`, 20 hits |
| no row changed, budget 10 of 30 | `CandidatesConsidered = 10`, `Truncated` — as before |

## Consequences

- An application without a host still installs the three abstractions and their five transitive packages — 380 KB of
  assemblies (measured: the eight `lib/net10.0` DLLs), all MIT. A second package (`Nordmann.LUI.Hosting`) would spare it; one package is the decision of ADR-0001.
- One catalog per container. An application with two databases registers the second catalog itself (`LuiCatalog.Open`).
- An index ensured on start keeps its typo tolerance and similarity as described; they are not part of the definition, so
  the description in `AddIndex` is what every injected instance has.
- `ApplyPending` on a timer makes a linked index with triggers as current as the interval; a health check asks `Pending()`.
- The typo dictionary is still built anew at the end of a sync with changes (ADR-0016); a lazy or background rebuild is
  issue #19.

**Verified by:** `tests/Nordmann.LUI.Tests/DependencyInjectionTests.cs` (8 tests, each against a real host):

- keyed services, the same instance for both interfaces, ensured on start;
- a mismatch stops the start;
- without ensure on start, an index is ensured on first resolution;
- a linked index whose outbox is applied on its interval (`FakeTimeProvider`);
- a failed round that does not end the loop;
- the warm-up of the indexes with typo tolerance only;
- the catalog disposed with the host;
- the registration refusing what cannot work.

Seen red: without the ensure and the `ApplyPending` call, four of them fail.

Also verified by `LinkedIndexTests` (`Stale_candidates_do_not_use_up_the_budget_up_to_a_second_budget_of_them`,
`Without_stale_candidates_the_budget_is_what_it_was`), the architecture tests
`The_hosting_knows_only_the_public_api` and `The_library_ships_only_the_documented_runtime_dependencies`, the mutation check,
and the package smoke test, which runs the README's host block with `Microsoft.Extensions.Hosting`.
