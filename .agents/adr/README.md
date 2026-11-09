# Architecture Decision Records

This directory is LUI's changelog. Every decision that changes the public API,
the schema, the dependency set or the shape of the repository becomes an ADR
here — in the **same pull request** as the change it describes.

## Format

```text
# NNNN — Title

- **Status:** Accepted | Proposed | Superseded by NNNN | Rejected
- **Date:** YYYY-MM-DD
- **Supersedes:** NNNN | —
- **Affects:** paths, files, packages

## Context
## Decision
## Consequences
```

## Status lifecycle

| Status | Meaning |
|---|---|
| `Proposed` | written, not decided — a discussion |
| `Accepted` | decided and in force |
| `Superseded by NNNN` | replaced by a later ADR — kept for the reasoning |
| `Rejected` | considered and declined — kept so it is not re-proposed |

## The topics of the index

Every decision belongs to a topic — what it is about. The column is there so that a developer finds "what was
decided about the query path" without opening 36 files.

| Topic | What it decides |
|---|---|
| `API` | the shape of the public API: the names, the types, the overloads, the promise of compatibility |
| `Storage` | the schema, the tables, the indexes, the bulk path |
| `Normalisation` | the rule that folds a text into a searchable form |
| `Query path` | the way a query runs, the ranking, the similarity |
| `Typo tolerance` | the dictionary, the correction, when it triggers |
| `Measurement` | the budgets, the benchmarks, the allocation of a query |
| `Runtime` | the host, the container, the pools, the work in the background |
| `Probe` | the checks LUI runs on its environment before it starts |
| `Packages` | what ships, and what it depends on |
| `Architecture` | how the code is put together, and where it may grow |
| `Testing` | the test tree, the map, the guards |
| `Sample` | the sample application |
| `Documentation` | where a document belongs |
| `Scope` | what is deferred, and what is out of scope |

## Index

| # | Title | Topic | Status |
|---|---|---|---|
| [0001](./0001-single-package.md) | One NuGet package instead of three | Packages | Accepted |
| [0002](./0002-library-not-an-application.md) | LUI is a library, not an application — the standards delta | Architecture | Accepted |
| [0003](./0003-defer-the-open-questions.md) | The nine open questions of the specification are deferred | Scope | Accepted |
| [0004](./0004-dependencies.md) | The dependency set — and the deviation from the closed library list | Packages | Accepted |
| [0005](./0005-measured-normalisation.md) | The normalisation rule is measured, not derived | Normalisation | Accepted |
| [0006](./0006-the-single-word-index.md) | The single-word index: one rowid, three indexes, one external-content FTS5 table | Storage | Accepted |
| [0007](./0007-the-catalog-checks-the-sqlite-it-runs-on.md) | The catalog checks the SQLite it runs on | Probe | Accepted |
| [0008](./0008-the-query-path.md) | The query path: groups, a lazy similarity, and what the result reserves | Query path | Accepted |
| [0009](./0009-writes-use-prepared-statements.md) | Writes use prepared statements on one writer connection | Storage | Accepted |
| [0010](./0010-the-query-budget-at-three-million-entries.md) | The query budget of §16 does not hold at three million entries on FTS5 alone | Measurement | Accepted (owner, 2026-11-04: the budget per input length) |
| [0011](./0011-the-multi-word-index.md) | The multi-word index: one indexed column, one expression, a Dice that allocates nothing | Storage | Accepted |
| [0012](./0012-typo-tolerance.md) | Typo tolerance: SymSpell over the word beginnings, corrected when nothing stands at a word start | Typo tolerance | Accepted |
| [0013](./0013-the-api-shape-before-the-first-release.md) | The API shape before the first release: steps, one namespace, entries, named results | API | Accepted |
| [0014](./0014-seams-for-the-second-content-source.md) | Seams for the second content source: the kind and the source as strategies, one direction inside | Architecture | Accepted |
| [0015](./0015-the-linked-index-is-loosely-coupled.md) | The linked index is loosely coupled: LUI never changes the application's table on its own | Storage | Accepted |
| [0016](./0016-sync-check-rebuild.md) | Sync, check and rebuild: batches that can be run again, a merge at the end, a catalog taken apart | Storage | Accepted |
| [0017](./0017-dependency-injection-and-hosted-services.md) | Dependency injection and hosted services, in the package; stale candidates past the budget | Runtime | Accepted |
| [0018](./0018-two-packages-core-and-hosting.md) | Two packages: the core and the hosting, released together | Packages | Accepted |
| [0019](./0019-the-test-tree-three-layers-one-map.md) | The test tree: three layers, one shared infrastructure, one map that is a guard | Testing | Accepted |
| [0020](./0020-the-performance-budgets-measured.md) | The performance budgets of §16, measured: a corpus, a test per target, waivers with reasons | Measurement | Accepted |
| [0021](./0021-a-stale-typo-dictionary-and-a-query-that-does-not-wait.md) | A stale typo dictionary, a query that does not wait, a rebuild in the background | Typo tolerance | Accepted |
| [0022](./0022-the-benchmark-results-in-the-readme.md) | The BenchmarkDotNet results: the raw data in the record, the budgets in the README — committed reports, a rendered section, a guard | Documentation | Accepted (restated 2026-11-06, issue #61, ADR-0039) |
| [0023](./0023-owned-becomes-embedded.md) | `Owned` becomes `Embedded`: the name of the content source in LUI's own table | API | Accepted |
| [0024](./0024-the-api-polish-before-the-freeze.md) | The API polish before the freeze: `AddLinkedIndex`, `entries`, `CandidateRead`, `LuiPayloadRow`, `LuiException`, a read interface that only reads | API | Accepted |
| [0025](./0025-the-typo-dictionary-only-grows.md) | The typo dictionary only grows: no `Stale`, no background rebuild, one dictionary per index | Typo tolerance | Accepted |
| [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) | The public API from 0.1.0 on grows only additively: a breaking change needs a minor version, a migration note and an ADR | API | Accepted |
| [0027](./0027-the-sample-flexdatalist-modular-client-one-fetch-boundary.md) | The sample: flexdatalist against the whole API, a modular client, one fetch boundary | Sample | Accepted |
| [0028](./0028-lui-has-no-highlight-a-correction-is-shown-as-a-question.md) | LUI has no Highlight: a correction is shown as a question (#20 struck) | API | Accepted |
| [0029](./0029-the-sample-fills-its-own-bulk-indexes-on-demand.md) | The sample fills its own bulk indexes on demand: a word list, Bogus names and tags, behind a button | Sample | Accepted |
| [0030](./0030-the-similarity-measures-are-luis-own.md) | The four similarity measures are LUI's own: the runtime dependency on F23 ends | Packages | Accepted |
| [0031](./0031-the-pool-displaces-instead-of-discarding.md) | The pool of the buffers displaces instead of discarding | Runtime | Accepted |
| [0032](./0032-the-allocation-of-a-query.md) | The allocation of a query: what it costs, and what it may cost | Measurement | Accepted |
| [0033](./0033-the-read-path-opens-an-index-without-writing.md) | The read path opens an index without writing: `Create()` and `Index()` in the catalog, the file that decides what may be written | API | Accepted |
| [0034](./0034-the-hosting-package-carries-only-the-dependency-injection.md) | The hosting package carries only the dependency injection: the abstractions of the host are gone | Packages | Accepted |
| [0035](./0035-the-index-knows-no-types.md) | The index knows no types: the serialiser belongs to the catalog, the type stands where the payload is used | API | Accepted |
| [0036](./0036-the-readme-the-specification-and-the-tutorial.md) | The README is the entrance, the specification the contract, the tutorial the way | Documentation | Accepted (decisions 1 and 4 superseded 2026-11-06, ADR-0039) |
| [0037](./0037-the-papers-of-the-work-live-under-agents.md) | The papers of the work live under `.agents/`, the documentation of the library under `docs/` | Documentation | Accepted |
| [0038](./0038-the-public-api-has-a-page-of-its-own.md) | The public API has a page of its own, and a test holds it up | Documentation | Accepted |
| [0039](./0039-the-entrance-shows-the-measurement.md) | The entrance shows the measurement, the bulk path, the kinds and the sources | Documentation | Accepted |
| [0040](./0040-the-budget-table-of-the-entrance-is-a-promise.md) | The budget table of the entrance is the promise of §16, and a test holds it up | Measurement | Accepted |
| [0041](./0041-the-budgets-of-16-run-on-a-schedule.md) | The budgets of §16 run on a schedule: the bytes are asserted, the times are reported | Measurement | Accepted |
| [0042](./0042-the-query-does-not-build-the-typo-dictionary.md) | The query does not build the typo dictionary: the application asks for it | Typo tolerance | Accepted |
| [0043](./0043-the-warm-up-is-a-service-of-the-host.md) | The warm-up is a service of the host: the host asks for it, the library starts nothing | Hosting | Accepted |
| [0044](./0044-the-typed-view-over-the-untyped-index.md) | The typed view over the untyped index: `index.As<T>()` gives the payload in the type of the application | API | Accepted |
| [0045](./0045-the-warm-up-tells-the-logger-of-the-host.md) | The warm-up tells the logger of the host what it did and what failed | Hosting | Accepted |
| [0046](./0046-the-busy-timeout-is-the-wait-for-a-lock.md) | The busy timeout of the catalog is the wait for a lock — not the 30 s of the provider | Storage | Accepted |
| [0047](./0047-the-pipelines-change-only-with-the-repository.md) | The pipelines change only with the repository: the SDK of `global.json`, named images, actions pinned by commit, timeouts, Dependabot | Build | Accepted |
| [0048](./0048-the-release-is-the-pull-request-gate-the-owner-and-a-key-of-one-hour.md) | The release: the gates of every pull request, the owner's approval, a key of one hour (Trusted Publishing) | Build | Accepted |
| [0049](./0049-the-tests-are-sorted-by-subject-and-carry-one-suffix.md) | The tests are sorted by subject, a folder is a namespace, and every test class carries one suffix | Testing | Accepted |
| [0050](./0050-the-tests-that-read-the-clock-carry-a-trait-and-argue-their-bounds.md) | The tests that read the clock carry a trait, and argue their bounds | Testing | Accepted |
