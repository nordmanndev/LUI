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
