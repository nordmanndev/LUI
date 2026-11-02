# AGENTS.md — LUI

> **The working contract for this repository.** It inherits the
> [`nordmanndev/standards`](https://github.com/nordmanndev/standards) contract
> at commit `c63d696` (v2.5.5); this file states only the delta.
> Where the two disagree, **the standards win** — unless the delta below says
> otherwise, explicitly.

LUI is a **NuGet library**. It is not an application, it has no domain, no
tenants and no HTTP surface. The standards were written for applications, so
this repository follows them where they fit a library and states the
difference where they do not.

## Read these next, in order

1. [`README.md`](./README.md) — what LUI is, and what it is not
2. [`docs/spec/LUI-Spezifikation.md`](./docs/spec/LUI-Spezifikation.md) — **the
   contract**. The implementation follows it; when code and spec disagree, the
   spec wins and the difference becomes an ADR
3. [`docs/plan/2026-11-02-lui-repo-plan.md`](./docs/plan/2026-11-02-lui-repo-plan.md)
   — the delivery plan, one pull request per step
4. [`docs/normalization.md`](./docs/normalization.md) — the normalisation
   rule, and what was *measured* about the half of it that lives inside SQLite
5. [`docs/architecture/alignment.md`](./docs/architecture/alignment.md) — which
   standards rules apply here, and which do not
6. [`docs/adr/`](./docs/adr/) — the decisions, and why

## Project-level deltas

The standards' five non-negotiable rules, and how they read here:

| Standards rule | In LUI |
|---|---|
| DDD + CQS + multi-tenant + SQLite | **SQLite yes.** DDD, CQS and multi-tenancy do not apply — a library has no aggregates and no tenants. LUI opens *one* database per catalog; the application owns its tenancy. |
| Test pyramid: unit + architecture + integration | **Applies in full.** xUnit + Shouldly + Alba + NetArchTest, three test projects. |
| Optimistic concurrency on every aggregate | **Does not apply.** LUI has no aggregates. The concurrency that matters here is SQLite's: one writer per file, WAL for parallel readers. |
| Outbox dispatch atomicity | **Does not apply.** LUI has no events. Its one atomicity rule is the opposite direction: content and search index change in **one SQLite transaction** (external content + triggers). |
| Static types for tag/header names | **Does not apply.** LUI speaks no protocol tags; it speaks SQLite column names, and those are validated against a pattern before they reach SQL. |
| Browser HTTP boundary is single-direction | **Does not apply.** LUI ships no browser code. The sample's page is plain ES modules with one fetch boundary. |
| `Alpine.start()` lives in exactly one file | **Does not apply** to the library. The sample uses no framework at all — see ADR-0002. |

## Always / Never / Ask

- ✅ Always keep `docs/adr/` current: a decision that changes the public API,
  the schema or the dependency set gets an ADR **in the same pull request**.
- ✅ Always update `CHANGELOG.md` when the public API changes.
- ✅ Always measure: a performance claim without a measurement is a rumour.
  The budgets live in [`docs/benchmarks.md`](./docs/benchmarks.md).
- ✅ Always run `dotnet test` **and** `dotnet pack` before opening a pull request.
- ❌ Never edit `Directory.Build.props` or `Directory.Packages.props` without an
  ADR (the standards' rule; here it bites hardest, because the dependency set
  *is* the package's contract).
- ❌ Never let the README show an API the code does not have. Every C# block
  in the README is compiled and executed by the package smoke test.
- ❌ Never add a NuGet dependency to `Nordmann.LUI` without an ADR — even a
  transitive one.
- ❌ Never touch `lui_*` tables by hand. They belong to the library; the
  application writes through the index API, or (in `Linked` mode) through its
  own table, whose triggers keep the index in step.
- ❓ Ask before: changing the schema of a `Linked` index's host table, raising
  the candidate budget above 500, changing the normalisation rule, or
  publishing a release.

## The workflow

Work is **issue-driven**, one pull request per issue, the standards' way.
The steps of the delivery plan are the first issues; each one carries its own
acceptance criteria and its own ADR(s).

Every pull request has:

1. a **Verification** section with the *test output*, not the words "I ran the
   tests";
2. the tests that changed — a new rule without a new test does not exist;
3. a `CHANGELOG.md` entry if the public API moved.

## The one rule that is specific to this repository

**The specification is the contract, and the specification is in this
repository.** [`docs/spec/LUI-Spezifikation.md`](./docs/spec/LUI-Spezifikation.md)
is the source of truth for behaviour, schema and API shape. Code that
contradicts it is a bug in the code — unless an ADR says the spec is wrong,
in which case the ADR is the newer truth and the spec gets corrected in the
same pull request.

## Session start

Read `.agents/state/active-task.md` and `recently-completed.md` if they exist,
and this file. The scratchpad goes to `.agents/scratchpad/<session-id>.md`.
