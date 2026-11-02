# 0001 — One NuGet package instead of three

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
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

The namespace layout of the specification (§17.2) survives unchanged, because
the namespace is what a consumer actually sees:

```text
Nordmann.LUI                    LuiCatalog, LuiCatalogOptions, the builder
Nordmann.LUI.Indexing           schema, triggers, Ensure, Sync, Check
Nordmann.LUI.Querying           ILuiIndex, LuiQueryOptions, LuiResult, LuiHit
Nordmann.LUI.Normalization      Normalizer, SQL expressions, highlight mapping
Nordmann.LUI.Typos              the prefix dictionary
Nordmann.LUI.Similarity         ITextSimilarity and the four implementations
Nordmann.LUI.Payloads           serialization, column mapping
Nordmann.LUI.DependencyInjection  AddLui(), keyed services, ensure-on-start
```

The dependency-injection surface keeps its own namespace inside the same
assembly, so the *code* that uses `AddLui` reads the same whether the DI
helpers later move into their own package or not.

## Consequences

**The cost, stated plainly.** Every consumer installs `Microsoft.Data.Sqlite`,
`symspell`, `F23.StringSimilarity` and the `Microsoft.Extensions.*`
abstractions, whether or not it uses the DI helpers or the typo tolerance. All
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
