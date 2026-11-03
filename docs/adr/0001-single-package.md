# 0001 — One NuGet package instead of three

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
- **Amended by:** [0013](./0013-the-api-shape-before-the-first-release.md) — the namespace layout below; [0014](./0014-seams-for-the-second-content-source.md) — the internal namespaces; [0018](./0018-two-packages-core-and-hosting.md) — the dependency injection is a second package, `Nordmann.LUI.Hosting`, released with the core
- **Affects:** `src/Nordmann.LUI/Nordmann.LUI.csproj`, `Directory.Packages.props`,
  `docs/spec/LUI-Spezifikation.md` §17.1, `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs`

## Context

The specification (§17.1) proposes three packages:

| Package | Content | Dependencies |
|---|---|---|
| `Nordmann.LUI.Abstractions` | contracts: `ILuiIndex`, `ITextSimilarity`, options, results | none |
| `Nordmann.LUI` | the SQLite implementation | Abstractions, `Microsoft.Data.Sqlite`, `symspell`, `F23.StringSimilarity` |
| `Nordmann.LUI.DependencyInjection` | `AddLui`, keyed services, `IHostedService` | `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions` |

The delivery requirement for this repository is **one** package.

## Decision

LUI ships **one** package: `Nordmann.LUI`, one assembly.

The namespace a consumer sees is **one**: every public type lives in `Nordmann.LUI` (ADR-0013 — this replaces
the layout of the specification that this ADR first kept). The other namespaces are internal and follow the
folders:

```text
Nordmann.LUI                    every public type: LuiCatalog, the builder steps, ILuiIndex, ILuiOwnedIndex,
                                LuiEntry, LuiQuery, LuiQueryOptions, LuiResult, LuiHit, LuiSimilarity, …
Nordmann.LUI.Indexes            an index: source + writes + query path         (internal, ADR-0014)
Nordmann.LUI.Querying           the query path as a pipeline                   (internal)
Nordmann.LUI.Writing            the write statements                           (internal)
Nordmann.LUI.Schema             definitions, kind rules, content sources, SQL  (internal)
Nordmann.LUI.Typos              the prefix dictionary                          (internal)
Nordmann.LUI.Normalization      Normalizer, SQL expressions, the fold table    (internal)
```

The dependency injection (PR-008) lands in the same assembly; whether it gets a namespace of its own is that
pull request's decision. *(PR-008, ADR-0018: by the owner's decision the dependency injection is a package of its own, `Nordmann.LUI.Hosting`,
with the same version; its public types stay in `Nordmann.LUI`.)*

## Consequences

**The cost, stated plainly.** Every consumer installs `Microsoft.Data.Sqlite`,
`symspell` and `F23.StringSimilarity` — and, once the dependency injection lands (PR-008), the
`Microsoft.Extensions.*` abstractions it needs — whether or not it uses the DI helpers or the typo tolerance. All
of them are permissively licensed (MIT / Apache-2.0), so the cost is a few
megabytes and a longer restore — not a licence question.

**Why this direction.** Splitting a package later is a *mechanical* refactor:
move the types, add a package reference, publish two packages. Starting with
three packages and merging later is an *API break* for every consumer. The
cheap direction is the one that can be undone quietly.

**What would reverse it.** A consumer who needs the contracts without SQLite —
a shared kernel between a writer and a reader that must not touch the
database — would ask for `Nordmann.LUI.Abstractions` again. The architecture
test `The_library_ships_only_the_documented_runtime_dependencies` is where that
decision would show up.

**Verified by:** `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs`
— `Only_the_library_project_is_packable` and
`The_library_ships_only_the_documented_runtime_dependencies`.
