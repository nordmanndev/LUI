# The delivery plans

A **plan** says what the repository will become and in which order. An **ADR** says what was decided and why. Both
live in this repository: the plans here, the decisions in [`../adr/`](../adr/README.md). A plan is written before the
work and stays as it was written — even where a later decision changed the names it uses.

| Plan | Date | What it brought |
|---|---|---|
| [LUI — repo, package and sample plan](./2026-11-02-lui-repo-plan.md) | 2026-11-02 | the founding plan: the library, the package and the sample in twelve pull requests, one per issue, each with its ADR, its tests and its measurement. The sections "what actually happened" (§6a–§6g) record where the plan and the world differed — the measurement moved three decisions before the first line of the library was written. |
| [Issue #56 — the read path and the catalog as the entry point](./2026-11-05-issue-56-the-catalog-is-the-entry-point.md) | 2026-11-05 | the shape of the API as it is now: the catalog is the entry point, `Create` writes and `Index` does not, one interface per capability, and the file decides what may be written. |
| [Issue #61 — the README for developers](./2026-11-06-issue-61-the-readme-for-developers.md) | 2026-11-06 | the entrance: the README for developers, the tutorial in five steps, and the map of the documentation. The public API does not change. |
| [Issue #64 — the production readiness](./2026-11-06-issue-64-production-readiness.md) | 2026-11-06 | the round of the review of 2026-11-06: the guards that run by themselves, the numbers that agree with the specification, the query that does not build the dictionary, the hardened concurrency and the typed view. |
| [The public surface and the measurement](./2026-11-11-the-public-surface-and-the-measurement.md) | 2026-11-11 | the public API before the freeze of 0.1.0 and the measurement that goes with it: the typo distance as an enumeration, `DistinctText` removed, the budgets in a file, the corpora as three fixed files, the specification unlinked and then deleted — seven pull requests on the branch `dev`. |

## The difference

- A **plan** is a decision about the work: the steps, the order, the acceptance bar. It is written before the work
  and is not rewritten afterwards — the sections "what actually happened" are the record of the difference.
- An **ADR** is a decision about the code: context, decision, consequences. It is written in the same pull request as
  the change it describes. A changed decision comes as a new ADR; a record that a new ADR supersedes is restated in
  place and marked as such in the [index](../adr/README.md) — ADR-0022 was restated that way for
  [ADR-0036](../adr/0036-the-readme-the-specification-and-the-tutorial.md).

A plan points at the ADRs it produced; an ADR rarely names the plan that led to it. The decision outlives the plan.

## How to read them

- **The first plan** is the history of the whole repository: the twelve pull requests, what each one was to prove, and
  the acceptance bar of the project. Read it to know how LUI was built.
- **The second plan** is the history of one API: what the read path became, and the seven assumptions the work is
  measured against.
- **The third plan** is the history of the entrance: what the README may say, what leaves it, and what the guard does
  after the move.
- **The fourth plan** is the history of the round of the review: what the review found, what was decided by the owner,
  and the seven tasks with their acceptance bar.
- **The fifth plan** is the history of the public surface and the measurement: what the API looks like before the freeze
  of 0.1.0, what the corpora and the budgets of the measurement are, and in which order the seven pull requests come.

Every decision the plans produced is an ADR in [`../adr/`](../adr/README.md) — indexed there by its topic.
