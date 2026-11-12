# Plan — Issue #61: the README for developers, the tutorial, the map of the documentation

> **Note (ADR-0057, 2026-11-12, issue #121):** this paper was written in German and has been translated into English.
> The translation keeps what the paper said at its time — the decisions, the numbers, the names, the branches, the issue
> numbers and the dates. The text below is the paper of its time.

State: 2026-11-06 · Branch: `docs/readme-and-tutorial` (from `main` = `111eefb`) · Issue:
[#61](https://github.com/nordmanndev/LUI/issues/61)

## The goal

The README is an architecture document today. It becomes the entrance for developers who want to
use LUI — and the gap it has had from the start is filled: **a way to learn it**.

The three models the owner named deliver the shape: *Best-README-Template* (badges, a table of
contents, a clear structure), *awesome-readme* (short, one example, nothing superfluous) and the .NET
blog about NuGet READMEs (what, which problem, highlights, installation, a running example, the most
important types, where to go next).

## The diagnosis, checked against `main`

| | Finding |
|---|---|
| Scope | 571 lines, 18 sections |
| The five concept sections (182–311) | a duplicate of `.agents/spec/LUI-Spezifikation.md` — two places for one truth |
| Performance (from 387) | the argument for trust stands after two thirds of the file |
| BenchmarkDotNet tables (414–494) | 80 lines of raw data in the wrong document |
| Table of contents, badges | none |
| `normalization.md`, `architecture/alignment.md`, `domain/lui.md` | **null** references from the README — orphaned |

The API is final since `main` = `111eefb`: the index knows no types (ADR-0035), the serialiser
belongs to the catalog, the type stands where the payload is used. The README is written against this
API, not against a later one.

## The work

### 1. The tutorial (`docs/tutorial/`)

The specification is a contract, not a wayfinder. The tutorial is the way: from the empty database
to the working autocomplete search, in steps, with runnable code after every step.

- `docs/tutorial/README.md` — the wayfinder: the steps, what you can do afterwards, how long it
  takes.
- `docs/tutorial/01-first-index.md` — the catalog, the first index, the first entries, the first
  query.
- `docs/tutorial/02-your-own-data.md` — an index over your own table (`Linked`), the reconciliation,
  the triggers.
- `docs/tutorial/03-the-payload.md` — the payload: the serialiser of the catalog, `GetPayload<T>()`.
- `docs/tutorial/04-many-entries.md` — `Sync`, the limits, what it costs (with the measured
  numbers).
- `docs/tutorial/05-in-the-application.md` — `AddLui` in ASP.NET Core, the catalog out of the
  container, the read-only access.

Rules: every code example **must be compilable** (the package smoke test or a test of its own takes
that over); "why so?" as a side note instead of a chapter; nothing that the specification does not
say.

### 2. The new README

At most ~250 lines, in this order:

1. Title, tagline, **badges** (build, version, NuGet, licence, .NET).
2. **What LUI is** — three sentences.
3. **The problem** — two sentences: 98 % of the inputs are reads; a second store for the same data
   is the most expensive part.
4. **Highlights** — five to seven outcome-focused bullets.
5. **Table of contents**.
6. **Install** — the two `dotnet add package` lines.
7. **Quick start** — **one** example, ~20 lines, with the output.
8. **Performance** — the budget table (target / measured / limit), a sentence about the machine, a
   link to `docs/benchmarks.md`.
9. **The two index kinds and the two content sources** — **one** table instead of two sections.
10. **A payload** — the serialiser of the catalog, `GetPayload<T>()` (a section of its own, so that
    the quick start stays short).
11. **In an ASP.NET Core application** — `AddLui` and the catalog.
12. **Reading only** — `Mode=ReadOnly`.
13. **The most important types** — a table: `LuiCatalog`, `LuiEntry`, `LuiKey`, `LuiQueryOptions`,
    `LuiCatalogOptions`.
14. **The sample application** — the two screenshots and the way to the sample.
15. **The documentation** — the map: tutorial, specification, benchmarks, normalisation, ADRs, test
    map.
16. **Status, contributing, licence** — the part of the repository, at the back.

What goes out: the five concept sections (they stand in the specification in full), the
BenchmarkDotNet tables (they belong in `docs/benchmarks.md`), the long "status" block (to the
back).

**"What this is not" stays** — `AGENTS.md` line 16 demands it.

### 3. The map of the documentation

- `docs/README.md` — new: what lies where, what is the contract, what is the history, who reads
  what.
- `.agents/adr/README.md` — a column "topic" (API / storage / measurement / runtime / probe), so
  that a developer finds "what was decided" without opening 36 files.
- The orphaned documents (`normalization.md`, `architecture/alignment.md`, `domain/lui.md`) are
  linked from the README.
- `.agents/plan/` — a `README.md` that explains the delivery plans as the history of the repository.

### 4. The guards and the paper

- `Readme_Benchmarks` — the guard compares the benchmark sections **in the README** with the
  BenchmarkDotNet reports. If the tables move, the guard moves with them: it checks the new place
  (`docs/benchmarks.md`) and renders there.
- **ADR-0022** ("The benchmark results in the README") is restated: the raw data lies in
  `docs/benchmarks.md`, the budgets in the README.
- A new ADR for the new order (the README is the entrance, the specification the contract, the
  tutorial the way) — in the house style of the others.
- The package smoke test executes every C# block of the README — that has to keep holding after the
  new order (and for the blocks that moved it holds: the concepts stand in the specification now).

### 5. Build, test, commit, push, fill the pull request

- `dotnet build -c Release`, `dotnet test`, `dotnet pack -c Release`, `npm test` — exit 0, the real
  output in the report and in the pull request.
- One commit on the branch, in English, in the style of the repository. `main` stays untouched, no
  force push.
- The pull request against `main` gets a new body: what, why (issue #61), the real verification
  output, the changed tests, the CHANGELOG note.
- Do not wait for the CI, do not merge anything.

### 6. The verification of the pull request

Read-only, with its own evidence and not with the tests of the developer:

- does the branch build and do the tests pass ✓
- is the README shorter, does it have a table of contents and badges, does the performance stand in
  the first third, are the concepts out ✓
- are the orphaned documents linked ✓
- does the guard still work (the benchmark comparison passes) ✓
- do the code examples agree with the code (the README shows no API that does not exist) ✓
- is `main` untouched, no force push, no build rubbish, no secrets in the diff ✓

Report to `/workspace/verify-61-readme.md`, the first line PASS or FAIL, the last line of the
summary `VERDICT: PASS` or `VERDICT: FAIL`.

## The assumptions that are checked

a) The README is at most ~250 lines, has a table of contents and badges.
b) The five concept sections stand in the specification and no longer in the README.
c) The performance budget table stands before the first third of the README.
d) The BenchmarkDotNet raw data stands in `docs/benchmarks.md`, and the guard `Readme_Benchmarks`
   checks there — the test is green.
e) `docs/normalization.md`, `docs/architecture/alignment.md` and `docs/domain/lui.md` are reachable
   from the README; `docs/README.md` exists.
f) The tutorial exists, and every one of its code examples compiles.
g) Every C# block of the README compiles and runs in the package smoke test.
h) The README shows no API that the code does not have (AGENTS.md).
i) `main` is untouched, no force push, no build rubbish, no secrets.

## What is open

Nothing. The owner released the plan on 2026-11-06: first the tutorial, then the README, then the
map, all in one pull request against issue #61.
