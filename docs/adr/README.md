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

## Index

| # | Title | Status |
|---|---|---|
| [0001](./0001-single-package.md) | One NuGet package instead of three | Accepted |
| [0002](./0002-library-not-an-application.md) | LUI is a library, not an application — the standards delta | Accepted |
| [0003](./0003-defer-the-open-questions.md) | The nine open questions of the specification are deferred | Accepted |
| [0004](./0004-dependencies.md) | The dependency set — and the deviation from the closed library list | Accepted |
| [0005](./0005-measured-normalisation.md) | The normalisation rule is measured, not derived | Accepted |
| [0006](./0006-the-single-word-index.md) | The single-word index: one rowid, three indexes, one external-content FTS5 table | Accepted |
| [0007](./0007-the-catalog-checks-the-sqlite-it-runs-on.md) | The catalog checks the SQLite it runs on | Accepted |
| [0008](./0008-the-query-path.md) | The query path: groups, a lazy similarity, and what the result reserves | Accepted |
| [0009](./0009-writes-use-prepared-statements.md) | Writes use prepared statements on one writer connection | Accepted |
| [0010](./0010-the-query-budget-at-three-million-entries.md) | The query budget of §16 does not hold at three million entries on FTS5 alone | Accepted (owner, 2026-11-04: the budget per input length) |
| [0011](./0011-the-multi-word-index.md) | The multi-word index: one indexed column, one expression, a Dice that allocates nothing | Accepted |
| [0012](./0012-typo-tolerance.md) | Typo tolerance: SymSpell over the word beginnings, corrected when nothing stands at a word start | Accepted |
| [0013](./0013-the-api-shape-before-the-first-release.md) | The API shape before the first release: steps, one namespace, entries, named results | Accepted |
| [0014](./0014-seams-for-the-second-content-source.md) | Seams for the second content source: the kind and the source as strategies, one direction inside | Accepted |
| [0015](./0015-the-linked-index-is-loosely-coupled.md) | The linked index is loosely coupled: LUI never changes the application's table on its own | Accepted |
| [0016](./0016-sync-check-rebuild.md) | Sync, check and rebuild: batches that can be run again, a merge at the end, a catalog taken apart | Accepted |
| [0017](./0017-dependency-injection-and-hosted-services.md) | Dependency injection and hosted services, in the package; stale candidates past the budget | Accepted |
| [0018](./0018-two-packages-core-and-hosting.md) | Two packages: the core and the hosting, released together | Accepted |
| [0019](./0019-the-test-tree-three-layers-one-map.md) | The test tree: three layers, one shared infrastructure, one map that is a guard | Accepted |
| [0020](./0020-the-performance-budgets-measured.md) | The performance budgets of §16, measured: a corpus, a test per target, waivers with reasons | Accepted |
| [0021](./0021-a-stale-typo-dictionary-and-a-query-that-does-not-wait.md) | A stale typo dictionary, a query that does not wait, a rebuild in the background | Accepted |
