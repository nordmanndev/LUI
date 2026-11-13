# 0045 — The warm-up tells the logger of the host what it did and what failed

- **Status:** Accepted
- **Date:** 2026-11-08
- **Builds on:** [0034](./0034-the-hosting-package-carries-only-the-dependency-injection.md) (the dependency set of a
  package is its contract), [0043](./0043-the-warm-up-is-a-service-of-the-host.md) (the warm-up is a service of the host)
- **Affects:**
  - `src/Nordmann.LUI.Hosting/Hosting/LuiWarmupService.cs` (the three messages)
  - `src/Nordmann.LUI.Hosting/LuiServiceCollectionExtensions.cs` (the logger comes from the container)
  - `src/Nordmann.LUI.Hosting/Nordmann.LUI.Hosting.csproj` (the dependency set)
  - `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs` (the guard of the dependency set)
  - `tests/Nordmann.LUI.Integration.Tests/LuiWarmupTests.cs`, `RecordingLoggerProvider.cs` (the failed round is heard)
  - ADR-0017, ADR-0034 and ADR-0018, `docs/api.md`, the README, the tutorial, the CHANGELOG
- **Issue:** #68 — the review of pull request 69 (point A5), the owner's decision of 2026-11-08

## Context

The first version of the warm-up (ADR-0043) swallowed a round of the outbox that failed: `catch (Exception) { }`. The
reason it gave was ADR-0034 — "the package carries no logging" — and the backlog: the rows stay in the outbox, and the
application can watch `Pending()`.

That is a failure nobody hears of. A round that fails fails **every** round — a table renamed by a migration, a column
that is gone, a file that is read-only — and the only sign is a number that grows in a health check the application
may not have. A hosted service that fails silently is the one thing an operator of a host cannot find.

The host has the answer already: `ILogger`. Every .NET host carries a logger, and `Microsoft.Extensions.Logging.Abstractions`
is **already in the closure of the hosting package** — `Microsoft.Extensions.Hosting.Abstractions` depends on it
(ADR-0043). It costs the consumer no new package.

## Decision

**The warm-up tells the logger of the host what it did and what failed.**

- The service takes an `ILogger<LuiWarmupService>` from the container; without a logger in the container it takes the
  `NullLogger` and writes nothing.
- **Three messages**, with fixed event ids and source-generated (`[LoggerMessage]`, no allocation when the level is off):

  | Id | Level | When |
  |---|---|---|
  | 1 | `Debug` | rows of the outbox were applied — at the start, and in a round that brought rows in |
  | 2 | `Information` | the typo dictionaries were built at the start, with the indexes and the time it took |
  | 3 | `Warning` | a round of the outbox failed — with the exception; the rows stay in the outbox, the next round tries again |

- A failed round still does not take the host with it — it is **heard**, not swallowed.
- `Microsoft.Extensions.Logging.Abstractions` is **named** in the project of the hosting package: the rule of ADR-0034
  is that a package carries what it uses, and the package uses it now. The closure of the package does not change.
- The core stays without `Microsoft.Extensions` (ADR-0018): the logging is the host's, and the core has no host.

## Consequences

- An operator sees a failing outbox in the log of the host — once per round, as a `Warning`, with the exception that
  names the cause.
- The guard of the dependency set lists three abstractions:
  `Repository_Layout.The_hosting_ships_the_core_the_dependency_injection_and_the_abstractions_of_the_host`.
- `LuiWarmupTests.A_round_that_fails_is_told_to_the_logger_of_the_host` renames the table of a linked index under a
  running host, and asks the logger for the warning and its exception — and the outbox for the row that waits.
- A failure at the **start** of the host is not logged by the service: it is thrown, and the host reports it (an unknown
  index name, a file of another version — the start fails instead of running half warmed).

## Alternatives

| The alternative | Why not |
|---|---|
| Swallow the failure and leave it to `Pending()` | The first version. A failure nobody hears of is a failure nobody fixes — and not every application has a health check. |
| An event or a callback on the options (`OnRoundFailed`) | A second way to do what the logger of the host does already, and one more member of a public type before the freeze. |
| Throw from the timer | An exception in a timer callback ends the process. |
| `ILogger` in the core | The core knows no `Microsoft.Extensions` (ADR-0018) — and the core starts no service that could fail on its own. |
