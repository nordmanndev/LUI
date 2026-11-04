# Alignment with nordmanndev/standards

> Which standards rules apply to LUI, which do not, and why. The short form is
> [ADR-0002](../adr/0002-library-not-an-application.md); this file is the long
> form, with the pointers.

The standards were written for **applications**. LUI is a **library**. The
question this file answers is not "do we follow the standards?" but "which
parts of the standards describe *engineering hygiene* (and therefore apply
everywhere), and which parts describe *application shape* (and therefore do
not)".

Baseline: [`nordmanndev/standards`](https://github.com/nordmanndev/standards)
at `c63d696` (v2.5.5).

---

## Applies — enforced by a test

| Standards rule | Source | How LUI implements it | The test that guards it |
|---|---|---|---|
| `Directory.Build.props` block | `architecture/layering.md` | verbatim: net10.0, nullable, `TreatWarningsAsErrors`, `EnableNETAnalyzers`, `AnalysisLevel=latest-recommended`, `NuGetAudit` | build fails on any warning |
| Central package management | `architecture/layering.md` | `Directory.Packages.props` is the only place with a version | `No_PackageReference_carries_a_Version`, `Every_PackageReference_has_a_Central_PackageVersion`, `Every_Central_PackageVersion_is_referenced_somewhere` |
| `Meziantou.Analyzer` from day one | `architecture/stack.md` entry 9 | analyzer package, `TreatWarningsAsErrors` | the build itself |
| Test pyramid | `architecture/operations.md` | unit · architecture · integration, three projects, plus the shared `Nordmann.LUI.Testing` (ADR-0019) | the test run itself; `Test_Landscape` (the map of the tests agrees with the tree) |
| NetArchTest for architectural rules | `architecture/operations.md` | `Nordmann.LUI.Architecture.Tests` | `Public_API_Shape` |
| ADR discipline | `architecture/meta.md` | `docs/adr/`, one ADR per decision | — (review) |
| The test map is a guard | `AGENTS.md`, ADR-0019 | `docs/testing.md` says which test holds which rule up | `Test_Landscape` |
| `CHANGELOG.md` on public API change | `AGENTS.md` | `CHANGELOG.md` | — (review) |
| Closed library list | `architecture/stack.md`, ADR-0008 | every deviation gets an ADR | `The_library_references_only_the_documented_dependencies` |
| README shape | the standards' own README | "what this is / what this is not / install / quick start / layout" | the package smoke test |
| Never push to `main` | `AGENTS.md` | branches and pull requests | — (process) |

## Does not apply — with the reason

| Standards rule | Why it does not apply to a library | Where it would have hurt |
|---|---|---|
| DDD aggregates, CQS, Mediator dispatch | LUI has no domain, no commands, no handlers. Its API is `LuiCatalog → ILuiIndex → Query`. | a source generator in every consumer's build, for no behaviour |
| OrmLite persistence | LUI *is* the persistence layer, over raw `Microsoft.Data.Sqlite`. The specification's §8 is explicit SQL. | a second ORM hiding the SQL the design depends on |
| Outbox + `BackgroundService` | LUI has no events and no process. | a queue for a library that has one long operation with a `CancellationToken` |
| ULID identifiers | `TKey` is the *application's* key (`long` or `string`, §12.3). | imposing an ID type on consumers |
| Serilog → Seq | A library must not force a logging stack on consumers. | a transitive logging dependency in every app |
| Multi-tenant by default | One catalog = one database. Tenancy is the application's. | the library deciding a tenancy model for its consumer |
| Alpine.js + Tailwind frontend | That baseline governs *project* frontends. The sample is a library demo with one inline page, plain ES modules and its own CSS. | a demo whose subject is the LUI API buried under a frontend stack |
| `lib/http.js` as the only fetch boundary | The library ships no browser code. The sample's page has exactly one fetch boundary. | — |

## What LUI adds on top

| Rule | Why |
|---|---|
| The specification lives in the repository (`docs/spec/`) | the standards' ADR-0048 prefers carried knowledge over reference-by-URL; here the *contract* is the code's reference |
| Every ADR of the specification's §3 (E1–E10) becomes an ADR here | the specification asks for it: *"Jede Entscheidung bekommt in der Umsetzung ein ADR."* |
| The performance budgets are tests, not prose | Anhang A's numbers are measurements on a machine that is not the consumer's machine |
| The dependency set is asserted | a library's dependency list is a public contract (ADR-0004) |
| The SQL ↔ C# normalisation parity is a test over the whole word list | the rule exists twice (§6.2); a drift between the two sides is a silent search miss |
| The public API grows only additively from `v0.1.0` on | a library's signatures are its contract with every application built against it; a break costs a minor version, a migration note and an ADR (ADR-0026) |
