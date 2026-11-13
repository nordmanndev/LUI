# 0049 — The tests are sorted by subject, a folder is a namespace, and every test class carries one suffix

- **Status:** Accepted
- **Date:** 2026-11-09
- **Builds on:** [0019](./0019-the-test-tree-three-layers-one-map.md) (the test tree and its map)
- **Affects:**
  - `tests/Nordmann.LUI.Unit.Tests/`, `tests/Nordmann.LUI.Integration.Tests/` (folders by subject; files split and merged)
  - `tests/Nordmann.LUI.Architecture.Tests/` (the classes renamed; `NamespaceConventionTests` and `TestLandscapeTests` extended)
  - `docs/testing.md` (the map), `tools/mutation-check.sh`, `.github/workflows/ci.yml` and `performance.yml` (the filters)
- **Issues:** #77 (epic #72) — the owner's decision of 2026-11-08; #89 (two gaps of the map guard, found in the review of
  pull request 88)

## Context

The integration project was one folder of 48 files: tests, measurements, attributes and helpers side by side. The names
followed four patterns (`…Tests`, `Snake_Case`, no suffix, a mix). Three files were named after *when* their tests were
written, not after *what* they prove (`ReviewFollowUpTests`, `TypoReviewTests`, `ApiShapeTests`). A reader looking for the
rules of `Upsert` had to read four files. `LinkedIndexTests` held 42 tests in 907 lines.

The map guard had two gaps (#89). It compared the **project** of a row, never its **layer**: a row of the sample could
say "Integration" and the build stayed green. And it knew a **fixed list** of test projects: a fifth one was invisible to
every rule of the map.

## Decision

**A folder is a subject, and a folder is a namespace** — in the tests as in the library. `NamespaceConventionTests` now
covers every test project and the shared infrastructure. The root is each project's `RootNamespace`; a file of global
usings declares none and is exempt.

| Project | Folders |
|---|---|
| Unit | `Text/`, `Similarity/`, `Keys/`, `Payload/`, `Querying/`, `Typos/`, `Package/`, `Support/` |
| Integration | `Catalog/`, `Embedded/`, `Linked/`, `Querying/`, `Typos/`, `Maintenance/`, `Concurrency/`, `Hosting/`, `Normalization/`, `Performance/`, `Support/` |
| Architecture | one folder: these rules read the tree, not a subject of the library |

**One naming rule.**
- `<Subject>Tests` for behaviour.
- `<Subject>Performance` for the measurements, which live in `Performance/`.
- The architecture tests take the same suffix. A filter, the test explorer and the mutation check now speak one language.

**A test lives with the behaviour it proves, not with the review that found it.**
- The three review files are dissolved. Each test moved into the file of its subject.
- Where a test came from stays as a comment above it (`// review of pull request 6, B1: a value-type payload`).
- The large files are split by behaviour:
  - `LinkedIndexTests` became `LinkedIndexTests` (the definition and the lifetime), `LinkedSyncTests`, `LinkedTriggerTests` and `LinkedQueryTests` (what comes back: the payload, the keys, the stale candidates).
  - The issue named "payload" for the fourth file. The payload is one part of what a query returns, and the stale candidates belong there too, so the file is `LinkedQueryTests`.
  - The four files share one table, spelled once (`LinkedTable`).
  - `EmbeddedIndexTests` keeps the definition. The writes, the payload and the batch moved into `EmbeddedWriteTests`, `EmbeddedPayloadTests` and `EmbeddedBatchTests`.

**The map guard checks the layer and finds the projects itself** (#89).
- A row whose layer is not the layer of its project fails.
- The test projects are read from the tree: every project that says `<IsTestProject>true</IsTestProject>`, and every
  project that references `Microsoft.NET.Test.Sdk`. The SDK sets the property itself, so a project that does not write it
  down is still a test project (review of pull request 90, M1).
- A test project with no layer fails. So does a layer whose project is gone.

### The renames

| Before | After |
|---|---|
| `Internal_Layering`, `Link_Check`, `Namespace_Convention`, `Public_API_Documentation`, `Public_API_Shape`, `Readme_Benchmarks`, `Readme_Budgets`, `Repository_Layout`, `Sample_Client`, `Test_Landscape`, `Workflow_Rules` | `InternalLayeringTests`, `LinkCheckTests`, `NamespaceConventionTests`, `PublicApiDocumentationTests`, `PublicApiShapeTests`, `ReadmeBenchmarksTests`, `ReadmeBudgetsTests`, `RepositoryLayoutTests`, `SampleClientTests`, `TestLandscapeTests`, `WorkflowRulesTests` |
| `Type_Names` (a helper) | `TypeNames` |
| `NormalizationParity` | `NormalizationParityTests` |
| `PerformanceBudgets` (the targets of ADR-0020, measured) | `BudgetPerformance` |
| `PerformanceBudget_Machine` (the rule of the machine, a test of behaviour) | `BudgetMachineTests` |
| `ReviewFollowUpTests` (15 tests) | `CatalogTests`, `EmbeddedWriteTests`, `EmbeddedPayloadTests`, `SingleWordQueryTests` |
| `TypoReviewTests` (11 tests) | `TypoDictionaryLifecycleTests`, `TypoToleranceTests` |
| `ApiShapeTests` (23 tests) | `CatalogTests`, `IndexBuilderTests`, `EmbeddedWriteTests`, `EmbeddedBatchTests`, `SingleWordQueryTests`, `LinkedIndexTests`, `TypoDictionaryLifecycleTests` |

The ADRs before this one keep the old names: history stays as written, and this table is the key to it.

## What was measured

**No test is lost and none is changed.** The test names of every project were listed before (`main`, `7ea9e77`) and
after:

| Project | before | after | method names |
|---|---:|---:|---|
| Unit | 263 | 263 | identical |
| Integration | 421 | 421 | identical — the classes and namespaces are new |
| Architecture | 89 | 91 | identical, plus the two new tests of #89 and #77 |
| Sample | 46 | 46 | untouched |

**The bodies were compared as well.**
- Method: every test method of the four test projects, before and after, from its attribute to its closing brace,
  compared byte for byte.
- Result: **543** methods before, **545** after. **538** are identical.
- The two new methods are the guards of #89 and #77.
- Five methods differ, and none of them changes what it asserts:
  - `LinkedTable.Schema(…)` instead of `Schema(…)`: the namespace `Nordmann.LUI.Schema` wins over a `using static`.
  - `Nordmann.LUI.Querying.PreparedQuery` instead of `Querying.PreparedQuery`: the tests now have a `Querying` namespace of
    their own.
  - `TypeNames.Of` instead of `Type_Names.Of`, and `--filter ReadmeBenchmarksTests` in a failure message: two consequences
    of the renames.
  - `Every_file_the_map_lists_sits_in_the_layer_the_map_names`: the repair of #89, on purpose.

**The gaps of #89 were red first.** On `main`, a row of the sample rewritten to "Integration" and a fifth test project with
a test file left all six guards of the map green. Here they are the mutations `map-row-names-another-layer` and
`test-project-without-a-layer`, both caught. The new namespace rule has its own mutation, `test-file-in-another-namespace`.
A project that references the test SDK without writing `IsTestProject` down was green too (review of pull request 90, M1);
it is the mutation `test-sdk-project-without-a-layer`, caught.

## Consequences

- **A subject is a filter.** `--filter "FullyQualifiedName~Integration.Tests.Linked"` runs the tests of the linked index,
  and nothing else.
- **A new test file goes where its subject is.** A file named after a review, a date or a pull request has no folder to
  go to.
- **A new test project is a row of the list of the layers**, or the build fails.
- **The filters follow the names:**
  - the lane of the parity: `NormalizationParityTests.The_normalizer_…`;
  - the lane of the budgets: `BudgetPerformance|NormalizationParityTests`;
  - the mutation check.
- **One dead helper is gone.** `ApiShapeTests` held a serialiser (`ChipSerializer`) that no test used.

## Alternatives

- **Folders without namespaces.** Rejected: the library keeps folder = namespace (ADR-0014). A test tree that does not would
  need a second rule, and the filters would lose the subject.
- **Keep the review files and add a pointer per subject.** Rejected: a reader opens the file of the subject and does not
  find the test. The comment above each moved test keeps the history without the detour.
