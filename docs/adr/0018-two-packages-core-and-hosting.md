# 0018 — Two packages: the core and the hosting, released together

> **Name note (ADR-0024, 2026-11-04):** since this record was written, `LuiRow` is `LuiPayloadRow`, `LuiSyncResult` is
> `LuiLinkedSyncReport`, `LuiPriorityRanking` is `LuiPriorityMode` (`Priorities` → `PriorityMode`), `LuiTimings.Candidates`
> is `CandidateRead`, the linked overload of `AddIndex` is `AddLinkedIndex`, and `Check()`, `WarmUpTypoDictionary` and
> `RebuildTypoDictionary` are on the write interfaces, not on `ILuiIndex`. The text below is history.

> **Freeze note (ADR-0026, 2026-11-04):** the sentence below, "in 0.x every minor version may break the API (SemVer)", is
> tightened: from the tag `v0.1.0` on the public API grows only additively; a breaking change needs a new minor version,
> a migration note and an ADR. The exact pin stays. The text below is history.

- **Status:** Accepted (owner's decision, 2026-11-03)
- **Date:** 2026-11-03
- **Supersedes:** the "one package" of [0001](./0001-single-package.md) for the dependency injection; the "in the package"
  of [0017](./0017-dependency-injection-and-hosted-services.md)
- **Affects:**
  - `src/Nordmann.LUI.Hosting/` (new), `src/Nordmann.LUI/Nordmann.LUI.csproj`, `Lui.sln`
  - `ILuiIndex.TypoTolerance` (new)
  - `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs`, `Public_API_Shape.cs`, `Namespace_Convention.cs`,
    `Internal_Layering.cs`, `tests/Shared/Repository.cs`
  - `tools/package-smoke-test.sh`, `tools/mutation-check.sh`, `.github/workflows/publish.yml`
  - spec §12.7 and §17, the README

## Context

PR-008 put the dependency injection into `Nordmann.LUI` (ADR-0017). With it, every consumer installed three
`Microsoft.Extensions.*` abstractions and five packages more — a console application, too, that opens a catalog with
`LuiCatalog.Open` and never sees a container. The owner asked whether a core library and a hosting library would be
better, and decided after the discussion:

- two packages;
- the second one is `Nordmann.LUI.Hosting`;
- one version for both;
- in PR-008, before 0.1.0, while the split breaks no one.

What argued for the split:

1. **Dependencies.** The core's dependencies are its contract (AGENTS.md). A console application should not carry the
   abstractions of a host it does not have.
2. **Versions.** A core pinned to `Microsoft.Extensions.*` 10.0.14 should not pull an application's own packages up.
   *(A first version of this record also said "a fix of the DI should not force a release of the core". With one
   version for both packages it does: a fix of either releases both, the other one unchanged in content. That is the
   price of the shared version, not an argument for the split — review of pull request 23, S3.)*
3. **A passive core.** The core runs no thread and no timer of its own: every call is synchronous and happens when the
   application makes it. Background work — the ensure on start, the warm-up, `ApplyPending` on a timer — belongs to whoever
   owns the lifetime: the host. As a package of its own, that line is visible.
4. **The convention of .NET libraries:**
   - Serilog / `Serilog.Extensions.Hosting`
   - Quartz / `Quartz.Extensions.Hosting`
   - Polly / `Microsoft.Extensions.Http.Polly`

What argued against: two packages to find, release and test, where one is simpler — convenience, not the contract.

## Decision

| Package | Content | Runtime dependencies |
|---|---|---|
| `Nordmann.LUI` | everything that does not need a host | `Microsoft.Data.Sqlite`, `symspell`, `F23.StringSimilarity` |
| `Nordmann.LUI.Hosting` | `AddLui`, `LuiServicesBuilder`, the hosted services `LuiStartup` and `LuiPendingApplier` | `Nordmann.LUI`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `.Hosting.Abstractions`, `.Logging.Abstractions` |

**One namespace for the consumer still.** The public types of the hosting package live in `Nordmann.LUI` too, so an
application needs one `using`. The hosted services are internal, in `Nordmann.LUI.Hosting`.

**The hosting knows only the public API of the core** — now by the compiler, not by an architecture test: it is another
assembly, and the core gives it no `InternalsVisibleTo`. A package that saw the internals of the other would tie their
versions together again. Three places used internals and now do not:

- **The `TimeProvider`.** The hosted service took it from the catalog. Now `AddLui` registers the `LuiCatalogOptions` as
  a singleton, and both the catalog and the service take them from there.
- **Whether an index corrects typos.** The warm-up asked the builder. Now every index says it: **`ILuiIndex.TypoTolerance`**
  is new, the default of the instance, which a query can override.
- **The rule of the index name.** The registration checked it with the core's internal identifier check. Now
  `LuiCatalog.Index(name)` checks it when the index is ensured — on start: an invalid name stops the start with the rule in
  its message. The registration still refuses an empty name and a name twice.

**Released together, one version.** `VersionPrefix` lives in `Directory.Build.props` only, no project sets a version (an
architecture test). The publish pipeline packs both from the same commit, and the smoke test checks that the hosting
package depends on the core of the same version.

**The core is pinned exactly** — owner's decision of 2026-11-04 (issue #24, review of pull request 23, S3):
`Nordmann.LUI.Hosting x.y.z` depends on `Nordmann.LUI [x.y.z]`. NuGet makes a project reference a lower bound
(`>= x.y.z`); in 0.x every minor version may break the API (SemVer), so an application that referenced a newer core
directly would have met a changed signature the hosting package calls at run time (`MissingMethodException`), not at
compile time. The two packages are released together anyway, so the pin costs nothing: a fix of either releases both.
The hosting project rewrites the version of its reference to the core into an exact range after NuGet read it
(`PinTheCoreExactly`, after `_GetProjectReferenceVersions`), and the package smoke test checks the nuspec for
`version="[x.y.z]"`. **What the pin does, measured:** an application that references `Nordmann.LUI 0.2.0` directly beside
`Nordmann.LUI.Hosting 0.1.0` still restores — NuGet takes the direct reference — but with the warning
`NU1608: Detected package version outside of dependency constraint: Nordmann.LUI.Hosting 0.1.0 requires Nordmann.LUI
(= 0.1.0) but version Nordmann.LUI 0.2.0 was resolved`; with `TreatWarningsAsErrors` it is an error. Without the pin
there was no word at all. The pin turns a silent mismatch into a named one at restore; it cannot forbid a direct
reference of the application.

**No timer in the core.** A console application keeps its linked index current without a host:

- `ApplyPending()` after its own writes, or a `PeriodicTimer` loop of five lines;
- `WarmUpTypoDictionary()` at its start;
- `Ensure()` where it opens the index.

A host-free `ApplyPendingEvery(…)` in the core would bring back exactly the background thread this split keeps out.

## Consequences

- The README installs `Nordmann.LUI`, and `Nordmann.LUI.Hosting` for an ASP.NET Core or worker application.
- The core package carries again exactly the dependencies of ADR-0004. The smoke test refuses a core nuspec with a
  `Microsoft.Extensions` dependency, and the architecture tests check the projects and the assembly the compiler bound.
- `ILuiIndex.TypoTolerance` is an addition to the public API. An application's own implementation of `ILuiIndex` (a test
  double) adds a property.
- `Directory.Packages.props` pins `Microsoft.Extensions.Hosting` 10.0.14 as a **test** package: the tests of the hosting
  and the package smoke test need a real host (`Host.CreateApplicationBuilder`). It is not shipped — an application
  brings its own host (review of `main` after pull request 23, finding 6).
- The tests of the hosting stay in `Nordmann.LUI.Tests`, which references both projects. Like every test, they see the
  internals of the core. The hosting package does not.

**Verified by:**

- `Repository_Layout`:
  - `The_two_packages_are_the_core_and_the_hosting_and_share_one_version`
  - `The_hosting_references_the_core_and_nothing_else_of_the_repository`
  - `The_library_ships_only_the_documented_runtime_dependencies` (the core: three)
  - `The_hosting_ships_the_core_and_the_three_abstractions`
  - `The_core_assembly_knows_no_microsoft_extensions`
  - `The_library_ships_the_readme_and_the_mit_license` (both)
- `Public_API_Shape` and `Namespace_Convention` over both packages.
- The mutation `core-references-the-hosting-abstractions`.
- `DependencyInjectionTests`:
  - `A_name_that_is_not_an_index_name_stops_the_start`
  - `An_index_says_whether_it_corrects_typos`
- The package smoke test: it packs both, checks both nuspecs, installs both and runs the README.
