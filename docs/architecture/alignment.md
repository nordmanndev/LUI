# What applies to a library

> The engineering hygiene of a library: the rules this repository follows, the
> rules that describe the shape of an *application* and therefore do not apply
> here, and what LUI adds on top. The short form is ADR-0002; this file is the
> long form, with the pointers.

LUI is a **library**: a NuGet package that an application builds against. The
question this file answers is not "which rules does LUI follow?" but "which
rules describe *engineering hygiene* — and therefore apply everywhere — and
which describe *application shape* — and therefore do not".

## Applies — enforced by a test

| Rule | Where it comes from | How LUI implements it | The test that guards it |
|---|---|---|---|
| The build settings | `Directory.Build.props` | net10.0, nullable, `TreatWarningsAsErrors`, `EnableNETAnalyzers`, `AnalysisLevel=latest-recommended`, `NuGetAudit` | the build fails on any warning |
| Central package management | `Directory.Packages.props` | the only place in the tree with a version | `No_PackageReference_carries_a_Version`, `Every_PackageReference_has_a_Central_PackageVersion`, `Every_Central_PackageVersion_is_referenced_somewhere` |
| `Meziantou.Analyzer` from day one | the analyzer package | analyzer package, `TreatWarningsAsErrors` | the build itself |
| The test pyramid | ADR-0019 | unit · architecture · integration, three projects, plus the shared `Nordmann.LUI.Testing` | the test run itself; `TestLandscapeTests` (the map of the tests agrees with the tree) |
| NetArchTest for architectural rules | `Nordmann.LUI.Architecture.Tests` | the rules of the shape of the library | `PublicApiShapeTests` |
| ADR discipline | `.agents/adr/` | one ADR per decision, in the papers of the work | — (review) |
| The test map is a guard | `AGENTS.md`, ADR-0019 | `docs/testing.md` says which test holds which rule up | `TestLandscapeTests` |
| `CHANGELOG.md` on public API change | `AGENTS.md` | `CHANGELOG.md` | — (review) |
| A closed dependency list | ADR-0004 | every dependency outside the documented ones gets an ADR | `The_library_references_only_the_documented_dependencies` |
| The shape of the README | the package smoke test | "what this is / what this is not / install / quick start / layout" | the package smoke test |
| No push to `main` | `AGENTS.md` | branches and pull requests | — (process) |

## Does not apply — with the reason

| The shape of an application | Why it does not apply to a library | Where it would have hurt |
|---|---|---|
| DDD aggregates, CQS, Mediator dispatch | LUI has no domain, no commands, no handlers. Its API is `LuiCatalog → ILuiIndex → Query`. | a source generator in every consumer's build, for no behaviour |
| OrmLite persistence | LUI *is* the persistence layer, over raw `Microsoft.Data.Sqlite`. The specification's §8 is explicit SQL. | a second ORM hiding the SQL the design depends on |
| Outbox + `BackgroundService` | LUI has no events and no process. | a queue for a library that has one long operation with a `CancellationToken` |
| ULID identifiers | `TKey` is the *application's* key (`long` or `string`, §12.3). | imposing an ID type on consumers |
| Serilog → Seq | A library must not force a logging stack on consumers. | a transitive logging dependency in every app |
| Multi-tenant by default | One catalog = one database. Tenancy is the application's. | the library deciding a tenancy model for its consumer |
| A frontend framework for the sample | That governs the frontends of *projects*. The sample is a library demo with one inline page, plain ES modules and its own CSS. | a demo whose subject is the LUI API buried under a frontend stack |
| `lib/http.js` as the only fetch boundary | The library ships no browser code. The sample's page has exactly one fetch boundary. | — |

## What LUI adds on top

| Rule | Why |
|---|---|
| The behaviour is written down in the repository | a reader who has only the repository knows everything a change needs to know: the decisions live in the ADRs, the budgets in one file (ADR-0056) |
| Every decision of the specification's §3 (E1–E10) becomes an ADR here | the specification asks for it: *"Jede Entscheidung bekommt in der Umsetzung ein ADR."* |
| The performance budgets are tests, not prose | Anhang A's numbers are measurements on a machine that is not the consumer's machine |
| The dependency set is asserted | a library's dependency list is a public contract (ADR-0004) |
| The SQL ↔ C# normalisation parity is a test over the whole word list | the rule exists twice (§6.2); a drift between the two sides is a silent search miss |
| The public API grows only additively from `v0.1.0` on | a library's signatures are its contract with every application built against it; a break costs a minor version, a migration note and an ADR (ADR-0026) |
