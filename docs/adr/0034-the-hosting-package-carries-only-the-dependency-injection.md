# 0034 — The hosting package carries only the dependency injection

- **Status:** Accepted
- **Date:** 2026-11-05
- **Builds on:** [0018](./0018-two-packages-core-and-hosting.md) (the hosting package is the second package, released with the core),
  [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the container holds the catalog and nothing else)
- **Affects:**
  - `src/Nordmann.LUI.Hosting/Nordmann.LUI.Hosting.csproj` (the dependency set, the comment and the description of the package)
  - `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs` (the guard of the dependency set of the hosting package)
  - `tests/Nordmann.LUI.Integration.Tests/Nordmann.LUI.Integration.Tests.csproj` (the pins of the abstractions of the host)
  - `tools/package-smoke-test.sh` (the API the smoke application runs)
  - the README (the install line of the hosting package)
- **Issue:** #56

## Context

ADR-0018 gave LUI two packages: the core and the hosting. The hosting package existed for the container — `AddLui`, the
registrations of the indexes, the hosted services that created the indexes on start and applied the outbox of a linked
index on a timer. It carried three `Microsoft.Extensions` abstractions for that: the dependency injection, the host and
the logging.

ADR-0033 took the registrations and the hosted services away: the container holds **the catalog and nothing else** — an
index is not registered and not injected, the application asks the catalog for what it needs, and the file decides what
may be written. What is left of the hosting package is `AddLui` and the `IServiceCollection` it returns.

The two abstractions that the package does not use any more were still in its `.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
```

A consumer of `Nordmann.LUI.Hosting` paid for them: they are dependencies of the `.nuspec`, and every application that
installs the package restores them. The dependency set of a package **is** its contract (AGENTS.md, ADR-0004) — a package
that carries what it does not need makes its consumers carry it too.

The description of the package said the same: it promised "every index as a keyed service, the indexes ensured on start,
the outbox of a linked index applied on a timer". None of that exists any more (ADR-0033): there are no keyed services, no
indexes are ensured on start, and the outbox is the application's own business.

## Decision

**The hosting package carries the dependency injection and nothing else.**

- `Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Logging.Abstractions` are **removed** from
  `src/Nordmann.LUI.Hosting/Nordmann.LUI.Hosting.csproj`. The package references `Microsoft.Extensions.DependencyInjection.Abstractions`
  — the one abstraction it really uses — and the core.
- The versions stay in `Directory.Packages.props`: they are the versions of the host that the tests and the package smoke
  test use, and every pinned version has to be referenced by a project (the guard of `Repository_Layout`). The integration
  tests — the project that hosts the sample in-process — reference the two abstractions explicitly.
- The description of the package names what the package is: the catalog in the container, `AddLui`, and the two ways into
  an index — `catalog.Create` and `catalog.Index`.

## Consequences

- **The dependency set of `Nordmann.LUI.Hosting` is smaller**: a consumer restores the dependency injection only. An
  application that has a host brings its own abstractions anyway; one that has not needs none of them.
- **The guard of the layout changes with it**: `Repository_Layout.The_hosting_ships_the_core_and_the_three_abstractions` is
  now `The_hosting_ships_the_core_and_the_dependency_injection` — the rule did not change (the dependency set of a package
  is its contract), the list did.
- **The pins of `Directory.Packages.props` do not change** — the versions of the host stay where they are, and the
  integration tests keep them referenced. Nothing is lost by the hosting package dropping them: the host is the
  application's own (ADR-0018).
- The comment and the description of the `.csproj` no longer promise keyed services, indexes on start and an outbox on a
  timer: there are none (ADR-0033).
- The package smoke test still packs both packages, installs them into a console application and runs the README — whose
  blocks are the quick start of the new API.

## Alternatives

| The alternative | Why not |
|---|---|
| Keep the two references with `PrivateAssets="all"` | The build would still bind them, and the package would still carry what it does not use. The point is that it does not use them. |
| Remove the pins from `Directory.Packages.props` | They are the versions of the host, which the tests and the package smoke test use — and the central file is not touched by the dependency set of one package. |
| Keep the hosted services and the registrations | ADR-0033 took them away: the container describes no indexes. The owner decided that the application asks the catalog for what it needs. |
