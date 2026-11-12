# AGENTS.md — LUI

> **The working rules of this repository.** They stand here, in full: a reader of
> the repository needs nothing outside it to work on it.
>
> LUI is a **NuGet library**. It is not an application, it has no domain, no
> tenants and no HTTP surface. The rules below are the rules of *this*
> repository — of a library, and of this one.

## Read these next, in order

1. [`README.md`](./README.md) — what LUI is, and what it is not
2. [`.agents/README.md`](./.agents/README.md) — the index of the **papers of the
   work**: the specification, the decisions, the plans, the state
3. [`.agents/spec/LUI-Spezifikation.md`](./.agents/spec/LUI-Spezifikation.md) — the
   behaviour of the library as it is written down: the kinds of index, the
   sources of the content, the query path, the ranking, the typo tolerance, the
   API. The implementation follows it
4. [`.agents/plan/2026-11-02-lui-repo-plan.md`](./.agents/plan/2026-11-02-lui-repo-plan.md)
   — the delivery plan, one pull request per step
5. [`docs/normalization.md`](./docs/normalization.md) — the normalisation
   rule, and what was *measured* about the half of it that lives inside SQLite
6. [`docs/architecture/alignment.md`](./docs/architecture/alignment.md) — what
   applies to a library, and what does not
7. [`.agents/adr/`](./.agents/adr/) — the decisions, and why
8. [`docs/api.md`](./docs/api.md) — the public API, described member by member;
   a test holds it up against the assemblies
9. [`docs/testing.md`](./docs/testing.md) — the map of the tests: which test
   holds which rule up. It is a guard, not a document (ADR-0019)

## Always / Never / Ask

- ✅ Always keep `.agents/adr/` current: a decision that changes the public API,
  the schema or the dependency set gets an ADR **in the same pull request**.
- ✅ Always update `CHANGELOG.md` when the public API changes — with a
  **migration note**, not a line of prose.
- ✅ Always update [`docs/api.md`](./docs/api.md) when the public API changes: a
  public type that is not on the page fails the build
  (`PublicApiDocumentationTests`).
- ✅ Always measure: a performance claim without a measurement is a rumour. The
  **budgets live in one file** —
  [`tests/Nordmann.LUI.Testing/Budgets/budgets.json`](./tests/Nordmann.LUI.Testing/Budgets/budgets.json)
  (ADR-0056) — and the measured values live in the record,
  [`docs/benchmarks.md`](./docs/benchmarks.md), beside the machine they were
  measured on.
- ✅ Always run `dotnet test` **and** `dotnet pack` before opening a pull request.
- ✅ Always write a test **red first**: a test that was never seen failing
  proves nothing. The guards of the repository are no exception —
  `tools/mutation-check.sh` mutates them and expects them to bite.
- ❌ Never edit `Directory.Build.props` or `Directory.Packages.props` without an
  ADR — here it bites hardest, because the dependency set *is* the package's
  contract.
- ❌ Never let the README show an API the code does not have. Every C# block
  in the README is compiled and executed by the package smoke test.
- ❌ Never break the public API from the tag `v0.1.0` on without a new minor version, a migration note in `CHANGELOG.md`
  and an ADR (ADR-0026). That includes a new optional parameter on an existing method (it is a new overload), a new
  abstract member of an interface (it is a default member, or a break), a renamed enum value, a renumbered enum value and a
  new positional parameter of a record. Before the tag, a change of a name still needs its ADR.
- ❌ Never add a NuGet dependency to `Nordmann.LUI` or `Nordmann.LUI.Hosting`
  without an ADR — even a transitive one. The core takes no `Microsoft.Extensions.*`
  package: that is the hosting package's (ADR-0018).
- ❌ Never touch `lui_*` tables by hand. They belong to the library; the
  application writes through the index API, or (in `Linked` mode) to its own
  table and tells the index with `Refresh`/`Sync` — or through triggers it
  created on purpose.
- ❌ Never let the library change the schema of a `Linked` index's host table on
  its own: no column, no index, no trigger unless the application calls for one
  (owner's decision, ADR-0015).
- ❌ Never lower a promise quietly. A budget that is missed is measured again and
  decided again, with an ADR and a record — it is not lowered in passing
  (ADR-0020, ADR-0040).
- ❌ Never leave a test file out of the map of the tests
  ([`docs/testing.md`](./docs/testing.md)): an unlisted test file fails the
  build (`TestLandscapeTests`).
- ❌ Never commit anonymously: every commit carries the identity of its author —
  `Mavis <noreply@minimax.io>` for the work of the agent, the owner's own
  identity for the owner's commits.
- ❓ Ask before: changing the schema of a `Linked` index's host table, raising
  the candidate budget above 500, changing the normalisation rule, or
  publishing a release.

## The workflow

Work is **issue-driven**, one pull request per issue.

Every pull request has:

1. a **Verification** section with the *test output*, not the words "I ran the
   tests";
2. the tests that changed — a new rule without a new test does not exist;
3. a `CHANGELOG.md` entry if the public API moved;
4. a commit whose subject says what the change is, and whose body says **why**:
   `<type>(<scope>): <subject> (#<issue>, ADR-<number>)`.

The branches of the series target `dev`; `dev` → `main` is a release pull
request with its own review.

## The one rule that is specific to this repository

**The truth of this repository is in this repository.** The decisions live in
[`.agents/adr/`](./.agents/adr/) with their reason, the behaviour of the library
is written down in
[`.agents/spec/LUI-Spezifikation.md`](./.agents/spec/LUI-Spezifikation.md), and
the budgets live in one file
([`tests/Nordmann.LUI.Testing/Budgets/budgets.json`](./tests/Nordmann.LUI.Testing/Budgets/budgets.json),
ADR-0056). No document outside the repository is part of it — a reader who has
only the repository knows everything a change needs to know.

Where the code and the specification disagree, the difference becomes an ADR,
and the ADR is the newer truth: the specification is corrected in the same pull
request.

## Session start

Read `.agents/state/active-task.md` and `recently-completed.md` if they exist,
and this file. The scratchpad goes to `.agents/scratchpad/<session-id>.md`.
