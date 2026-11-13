# 0061 — The specification is deleted, and every one of its sections has a home

- **Status:** Accepted
- **Date:** 2026-11-13
- **Builds on:** [0002](./0002-library-not-an-application.md) (the contract of the library is the repository), [0057](./0057-the-repository-of-a-public-library-is-english.md) (the repository of a public library is English), [0059](./0059-the-corpora-are-four-fixed-files-in-the-repository.md) (the corpora are files)
- **Affects:**
  - `.agents/spec/LUI-Spezifikation.md` — **deleted**
  - `.agents/README.md` — the index of the papers, without the specification
  - `AGENTS.md` — the contract of the repository, without the specification
  - the whole tree — every reference to a section of the specification
  - `tests/Nordmann.LUI.Architecture.Tests/SpecificationIsGoneTests.cs` (new) — the guard that holds it gone
- **Issues:** #125 (epic #117)

## Context

The specification was the contract of the library: 1429 lines in German, 21 sections and two appendices, and the
reason of every rule that the ADRs do not carry. It was read by everybody and by nothing else.

Three things happened to it, and together they make it a document with a known end date.

1. **It was unlinked.** ADR-0057 made the repository English and moved the plan of the work into
   [`.agents/plan/`](../plan/README.md). The specification stayed German and stayed the contract — a contract that a
   reader of the English repository had to learn a second language to read.
2. **Its rules became ADRs.** Every decision of its section 3 became an ADR of its own; the budgets of its section 16
   became a file ([ADR-0056](./0056-the-budgets-live-in-a-file.md)); the API of its section 12 became
   [`docs/api.md`](../../docs/api.md). What the specification said and what the ADRs say is the same rule in two
   languages, and the ADRs are the ones that the index of the papers finds.
3. **Nothing reads it.** No guard reads the file, no build reads it, no test reads it. It is 424 references to itself
   in 175 files, and 129 files that name it — a web of pointers around a document that nothing loads.

Translating it would have produced a second document that says what the first one says, in a language nobody would
have read twice. The plan says it plainly (D12): the deletion is its own pull request, because a big deletion mixed
into the work is neither reviewable nor revertible on its own.

## Decision

**The file `.agents/spec/LUI-Spezifikation.md` is deleted, and every section of it gets a home.**

1. **The file is not translated.** It is deleted. A document with a known end date is not worth a second language.
2. **Every reference to a section is rehung, not dropped.** A `§x` becomes the ADR or the documentation page that
   carries the same rule today. Where no document carries the rule, the reference goes — the comment describes the
   behaviour in words anyway. The mapping is the table below; it is the machine form of the decision, and the sweep
   that wrote the tree followed it.
3. **A reference that names the document it belongs to is not a reference to the specification.**
   `docs/normalization.md` has a section 7 of its own, and the plan has sections of its own. Those references stay.
4. **The basic decisions of the section 3 each became an ADR.** A section that names ten decisions cannot be
   rehung onto one document; it is rehung onto ten.

### The mapping

| Section of the specification | Its subject | Its home today |
|---|---|---|
| §1 | the purpose and the limits of the library | ADR-0002 |
| §2 | the terms | `docs/api.md` |
| §3 | the basic decisions E1–E10 | ADR-0005, ADR-0006, ADR-0008, ADR-0012, ADR-0013, ADR-0014, ADR-0015, ADR-0016, ADR-0036 — one ADR per decision |
| §4 | the kinds of index | ADR-0014 |
| §5 | the sources of the content: embedded and linked | ADR-0014, ADR-0015 |
| §6 | the normalisation | ADR-0005, `docs/normalization.md` |
| §7 | the data model | ADR-0006 |
| §7.1 | the catalog | ADR-0006, ADR-0033 |
| §7.2 | the content table of an embedded index | ADR-0006 |
| §7.3 | the content table of a linked index | ADR-0015 |
| §7.4 | the search index of both sources | ADR-0011 |
| §7.5 | the settings of the database | ADR-0046 |
| §8 | the way of a query | ADR-0008 |
| §8.1 | the path of the prefix | ADR-0008 |
| §8.2 | the candidates out of FTS5 | ADR-0008 |
| §8.3 | the result | ADR-0008 |
| §9 | the ranking | ADR-0008 |
| §9.1 | the word start | ADR-0008 |
| §9.2 | the order of the ranking | ADR-0013, ADR-0055 |
| §9.3 | the marking of the hit — it fell away | ADR-0028 |
| §10 | the typo tolerance | ADR-0012 |
| §10.1 | the dictionary | ADR-0012 |
| §10.2 | when a query is corrected | ADR-0012 |
| §10.3 | how a query is corrected | ADR-0012 |
| §10.3a | the decisions of the implementation | ADR-0012 |
| §10.4 | the limits | ADR-0012 |
| §11 | the similarity, interchangeable | ADR-0030 |
| §12 | the public API | `docs/api.md` |
| §12.1 | opening a catalog | `docs/api.md` |
| §12.2 | building an index | `docs/api.md` |
| §12.3 | the keys and the payload | ADR-0035 |
| §12.4 | writing | ADR-0016 |
| §12.5 | querying | `docs/api.md` |
| §12.6 | checking and repairing | ADR-0016 |
| §12.7 | dependency injection | ADR-0017, ADR-0034 |
| §12.8 | the errors | ADR-0046 |
| §12.9 | opening an index without a write | ADR-0033 |
| §13 | synchronous, not asynchronous | ADR-0016 |
| §14 | the consistency and the integrity | ADR-0016, ADR-0046 |
| §15 | the concurrency | ADR-0015, ADR-0046 |
| §16 | the performance targets | ADR-0020, ADR-0056 |
| §17 | the packages, the namespaces, the dependencies | ADR-0018 |
| §17.1 | the packages | ADR-0001 |
| §17.2 | the namespaces | ADR-0014 |
| §17.3 | the dependencies | ADR-0004 |
| §17.4 | the taking over of the former LUI | ADR-0004 |
| §18 | the limits and the non-goals | ADR-0002 |
| §19 | the strategy of the tests | ADR-0019, `docs/testing.md` |
| §20 | the plan of the implementation | the plans of `.agents/plan/` |
| §21 | the open questions | ADR-0003 |
| Anhang A | the ground of the measurement | `docs/benchmarks.md` |
| Anhang B | the behaviour from the user's side | ADR-0028 |

## Consequences

- **The contract of the library is the set of the ADRs.** `AGENTS.md` says so now, and the papers of the work begin
  with the decisions and not with a specification.
- **A section of the specification is a section of a document that is read.** The rules live where the reader of the
  library already is: in the API page, in the benchmarks, in the testing map, in the ADRs.
- **A guard holds the deletion.** A test walks the tree and fails on a reference to a section of the specification, and
  on a file that names it. The papers of the work are the one place that may still name it, and only to say that it
  is gone.
- **The history keeps its shape.** The ADRs are dated records; a decision that was taken against the specification
  says so, and names the ADR that carries the rule now. The record of what happened is not rewritten — the pointer
  is.
- **The delivery plan of the repository carries the deletion itself.** The plan of 2026-11-11 names the file, because
  it is the plan that deletes it.
