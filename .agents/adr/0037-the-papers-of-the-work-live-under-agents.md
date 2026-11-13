# 0037 — The papers of the work live under `.agents/`, the documentation of the library under `docs/`

- **Status:** Accepted
- **Date:** 2026-11-06
- **Builds on:** [0036](./0036-the-readme-the-specification-and-the-tutorial.md) (the entrance, the contract, the way),
  [0019](./0019-the-test-tree-three-layers-one-map.md) (the map of the tests is a guard), [0022](./0022-the-benchmark-results-in-the-readme.md)
  (the benchmark tables and their guard)
- **Affects:**
  - `.agents/spec/`, `.agents/adr/`, `.agents/plan/`, `.agents/README.md` (new: the index of the papers)
  - `docs/README.md` (the map of the documentation — the papers are no longer in it), `docs/testing.md`,
    `docs/normalization.md`, `docs/domain/lui.md`, `docs/architecture/alignment.md`, `docs/benchmarks.md`
  - `README.md`, `AGENTS.md`, `CHANGELOG.md`, `.github/ISSUE_TEMPLATE/adr.md`, `.github/ISSUE_TEMPLATE/task.md`,
    `samples/Lui.Sample/wwwroot/index.html`
- **Issue:** #61

## Context

The `docs/` directory of the repository held two kinds of documents side by side:

| | What it is | Who reads it |
|---|---|---|
| `docs/tutorial/`, `docs/normalization.md`, `docs/architecture/alignment.md`, `docs/domain/lui.md`, `docs/testing.md`, `docs/benchmarks.md` | the **documentation of the library** — the way, the rules, the numbers | a reader who wants to use LUI, and a contributor |
| `docs/adr/` (36 records), `docs/plan/` (4 plans), `docs/spec/.md` | the **papers of the work** — the decisions, the delivery plans, the contract | the agent that works in the repository, and a reviewer |

The README linked both kinds. The consequence was visible to everybody who came to the repository to *use* the library:
the entrance pointed at a specification, at a folder of 36 ADRs and at four delivery plans — the papers of a process, not
the documentation of a package. The owner said it plainly: the official documentation should not talk about the internal
papers, and the internal papers do not belong under `docs/`.

The specification is a working document: it stays until the owner deletes it. But it is a paper of the work like the
others, and a document under `docs/` that nothing links to is a document nobody reads — the rule
[0036](./0036-the-readme-the-specification-and-the-tutorial.md) wrote down.

## Decision

1. **Two trees, two jobs.**
   - `docs/` holds the **documentation of the library**: the map, the tutorial, the API, the benchmarks, the
     normalisation, the test map, the alignment, the domain, the images.
   - `.agents/` holds the **papers of the work**: the specification, the ADRs, the delivery plans, the state of the
     session. It gets an index of its own, [`.agents/README.md`](../README.md).
2. **The README and the documentation link to neither the ADRs nor the plans nor the specification.** A paper of the
   work is reached through the index of its own tree — `.agents/README.md` — and not through the entrance of the
   library.
3. **A number is a pointer, not a link.** The ADR numbers in the comments of the code, in the tests, in the CHANGELOG
   and in the test map stay as they are: `ADR-0035` names a decision, and a reader who wants to see it opens the index
   of the decisions. Only the *links* to the papers go.
4. **The specification is moved as well.** It is a working document of the work, it belongs to the same tree as the
   decisions, and the owner deletes it at the end of the release; `docs/spec/` would leave a paper behind under a
   documentation tree that is published.
5. **The contract of the repository points at the new place.** `AGENTS.md` is the working contract for the agent that
   works here — it is not the documentation of the library — and it names the papers where they now are.
6. **The sample's page links the tutorial.** The specification was the only paper on the page of the sample; the
   tutorial is the way a reader of the sample needs.

## Alternatives

| | For | Against |
|---|---|---|
| **A — everything stays under `docs/`** | one tree; the links of today keep working | the entrance of a package points at the papers of a process; the owner asked for the other way |
| **B — only the ADRs move, the plans and the specification stay** | the specification is the contract of the repository, the ADRs are the history | the contract is a working document of the work; it belongs with the decisions, and the rule "a document nobody links to is a document nobody reads" applies to it too |
| **C — the papers move, and the README keeps a link to the decisions** | the history stays one click away | a link is a promise to read; the owner asked for the papers out of the official documentation |
| **D — the papers move under `.agents/`** (this decision) | the documentation of the library and the papers of the work are told apart; both have an index | the paths change; every link has to be followed — done in the same pull request, and the link check holds it up |

## Consequences

- **The documentation of the library is smaller and is about the library.** The map of `docs/README.md` lists seven
  documents; a reader of the package finds no ADR, no plan and no specification in it.
- **The papers keep their history.** The ADRs, the plans and the specification are moved with `git mv`, so the blame
  and the history of every line stay; only the path changes.
- **Every link in the repository was followed.** The links of the README, of the map, of the tutorial, of the test map,
  of the normalisation, of the domain, of the alignment, of the CHANGELOG, of the contract and of the issue templates
  point at the new place; the link check of the pull request proves it.
- **The public API does not change.** Nothing in this decision touches a type, a member or a signature.
- **The papers are not published with the package.** Only the README and the licence travel into the `.nupkg`.

**Verified by:** the link check of the pull request (every relative link of the repository resolves), the
architecture tests, and `grep` over the tree: outside `.agents/`, no document of `docs/` names an ADR, a plan or the
specification as a link.
