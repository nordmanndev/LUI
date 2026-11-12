# 0036 — The README is the entrance, the specification the contract, the tutorial the way

> **Contract note (ADR-0056, 2026-11-12):** this record was written against a contract outside the repository — a
> private one, that no reader of LUI can know and does not have to. The repository states its own rules since: the
> rules that contract carried for LUI stand in [`AGENTS.md`](../../AGENTS.md) and in the documents of
> [`docs/`](../../docs/README.md). The entrance, the record and the tutorial keep their places. The text below is
> history and stays as it was written.

> **Note (ADR-0039, 2026-11-06, issue #61):** the entrance grows. Decision 1 — "at most 250 lines" — and decision 4 —
> "the raw tables move to the record" — are superseded: the entrance of the release carries the tables of the
> BenchmarkDotNet measurement, the example of the bulk path, the matrix of the kinds and the decision guide of the
> sources. The rule that stays is the one that mattered: the entrance may not say what the specification says. The
> decisions 2, 3, 5, 6, 7, 8 and 9 stand as they are written.

> **Note (ADR-0057, 2026-11-12, issue #121):** this record named an external contract that a reader of a public
> library cannot know, and its table of the diagnosis was in German. The sentences that named the contract are
> restated in the terms of this repository, and the table is translated. The decisions stand as they are written;
> the text below is history.

- **Status:** Accepted
- **Date:** 2026-11-06
- **Builds on:** [0022](./0022-the-benchmark-results-in-the-readme.md) (the benchmark tables and their guard),
  [0019](./0019-the-test-tree-three-layers-one-map.md) (the map of the tests is a guard), [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md)
  (the promise of the API), [0002](./0002-library-not-an-application.md) (a library, not an application)
- **Affects:**
  - `README.md` (rewritten: 571 → 241 lines), `docs/tutorial/` (new), `docs/benchmarks.md` (the rendered tables)
  - `tests/Nordmann.LUI.Architecture.Tests/Readme_Benchmarks.cs` (the target of the guard), `tools/package-smoke-test.sh`,
    `tools/benchmarks.sh`, `tools/mutation-check.sh`
  - `.agents/adr/0022-the-benchmark-results-in-the-readme.md` (restated), `.agents/adr/README.md`, `docs/testing.md`, `CHANGELOG.md`
- **Issue:** #61

## Context

The README of this repository grew into an architecture document. On `main` (111eefb) it had 571 lines and 18
sections, and it said the same things in two places:

| | Finding |
|---|---|
| Scope | 571 lines, 18 sections |
| The five concept sections (182–311) | a duplicate of `.agents/spec/LUI-Spezifikation.md` — two places for one truth |
| Performance (from 387) | the argument for trust stands after two thirds of the file |
| BenchmarkDotNet tables (414–494) | 80 lines of raw data in the wrong document |
| Table of contents, badges | none |
| `normalization.md`, `architecture/alignment.md`, `domain/lui.md` | **null** references from the README — orphaned |

Two things were missing besides. A reader who wanted to use LUI found a contract, not a way: the specification
defines the kinds of index, the query path, the ranking and the typo tolerance, but it does not take anybody by the
hand from an empty database to a working autocomplete. And a reader who wanted to know whether to trust LUI had to
read past two thirds of the file to find the numbers, and past a table of raw benchmark output to find them.

The API was settled at the same time ([ADR-0035](./0035-the-index-knows-no-types.md)): the index knows no types, the
serialiser belongs to the catalog, the type of a payload stands where the payload is used. A README that repeats the
concepts of the specification repeats them against an API that moves — and the ones that were written against the old
one were wrong.

## Decision

**Three documents, three jobs.**

1. **The README is the entrance** — at most 250 lines, in this order: title, tagline and badges (build, version,
   NuGet, licence, .NET); what LUI is (three sentences); the problem (two sentences: 98 % of the input is a read, and
   the second store for the same data is the expensive part); the highlights (outcome-focused bullets); a linked table
   of contents; the two `dotnet add package` lines; **one** quick start with its output; the budget table of
   [spec §16](./../spec/LUI-Spezifikation.md) with the machine and a link to the record; one table for the two index
   kinds and the two content sources; the payload; `AddLui` in an ASP.NET Core application; reading only
   (`Mode=ReadOnly`); the five most important types; the sample with its two screenshots; the map of the documentation;
   and at the end status, contributing and licence.
2. **The specification is the contract** — unchanged, and still the one the implementation follows. The five concept
   sections leave the README: the kinds of index, the sources of the content, the query path, the ranking and the
   priority, the typo tolerance, the query options — the specification has all of it, in full, and a second copy is a
   second truth that can rot.
3. **The tutorial is the way** — `docs/tutorial/`: a wayfinder and five steps, from the empty database to the working
   autocomplete, every code example compilable. It is the document for the reader who wants to *do* something; it does
   not restate the contract, it uses it.

4. **The budgets stay in the README, the raw tables move to the record.** The budget table is the promise a reader
   checks — nine rows, target, measured value, limit. The raw tables of a BenchmarkDotNet run are 80 lines of numbers
   with a machine, a corpus and a method; they belong to `docs/benchmarks.md`, which keeps them and their guard
   ([ADR-0022](./0022-the-benchmark-results-in-the-readme.md)).
5. **"What this is not" stays.** AGENTS.md asks for it, and it is the shortest honest sentence in the file: not a
   full-text search engine, not a key–value store, not a server.
6. **Every C# block of the README is executed.** The package smoke test compiles and runs them, in order, against the
   packed `.nupkg` (AGENTS.md). The blocks are few on purpose — one quick start, the payload, the host, reading only —
   and every one of them is a promise that can be checked.
7. **The map of the documentation is in the README**, near its end: the tutorial, the specification, the record, the
   normalisation, the ADRs, the test map — and the corners that are read less often, the alignment with the rules
   of the repository
   and the domain of the library.
8. **A document nobody links to is a document nobody reads.** `docs/normalization.md`, `docs/architecture/alignment.md`
   and `docs/domain/lui.md` had no link from anywhere; the README links all three.
9. **The README shows no API the code does not have.** It is written against the API of `main` (111eefb): no type
   parameter on an index, `LuiKey` for the key, the serialiser of the catalog, `hit.GetPayload<T>()` where the payload
   is used.

## Alternatives

| | For | Against |
|---|---|---|
| **A — keep the README as it is** | nothing to maintain; every concept is one click away | 571 lines with two truths for one rule; the performance argument after two thirds; no way to learn; three orphaned documents |
| **B — the README in German, like the specification** | one language in the repository | the specification is a contract, the README is an entrance; a German entrance turns away the reader the entrance is for |
| **C — a README without code, only links** | nothing to keep in step; no risk of a stale example | a reader cannot see what the thing looks like; the smoke test would have nothing to run |
| **D — the tutorial as the only entrance** | one way to learn, in one voice | the reader who only wants to know what LUI is has to read a tutorial; the entrance is also the contract's first page in the search results |
| **E — the README is the entrance, the specification the contract, the tutorial the way** (this decision) | each document has one job; the entrance is short and complete; the contract is in one place; the way exists | three documents to keep in step — the links between them are the price, and they are checked (the link check of the tutorial, the smoke test of the README) |

## Consequences

- **The README is 241 lines** and answers, in one page: what LUI is, what it is not, why, how to install it, how to
  write the first query, what it costs, what the important types are, and where everything else is.
- **The public API does not change.** Nothing in this decision touches a type, a member or a signature; the CHANGELOG
  says so.
- **The specification stays the contract.** It is not shortened — the concepts that left the README are in it already,
  and its paragraphs are numbered the way the tests name them.
- **The guard follows the tables** ([ADR-0022](./0022-the-benchmark-results-in-the-readme.md)): `Readme_Benchmarks`
  renders into `docs/benchmarks.md` and fails when the file and the committed reports differ. It did not lose its teeth:
  the mutation of a number by hand still fails the build, from the other side of the move.
- **The package smoke test still runs every C# block of the README** — four executed blocks instead of eleven, all of
  them executed. The expectations of the smoke test follow the blocks; the quick start of the README still has to print
  what its comments say.
- **The map of the documentation is a list of links**, and it grows with the documents: `docs/README.md` (the map of
  the documents themselves) is written by the same issue.
- **The documents of the corners are reachable** from the entrance: the normalisation, the alignment with the
  rules of the repository, the domain of the library.
- **A rule for the next README**: it may not grow past the length of a sitting, and it may not say what the
  specification says.

**Verified by:** `tools/package-smoke-test.sh` (every C# block of the README, executed against the packed `.nupkg`),
`Readme_Benchmarks` (the tables of the record against the committed reports), `Test_Landscape` (the map of the tests),
the link check of the tutorial, and the line count of the README (241).
