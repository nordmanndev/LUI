# 0038 — The public API has a page of its own, and a test holds it up

> **Note (ADR-0061, 2026-11-13, issue #125):** the specification this record was written against is deleted.
> Every section of it has a home — the ADR or the page that carries the same rule; the mapping is in
> [ADR-0061](./0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md). The sentences below
> that name the specification are history; the rule they carry lives in the ADR named beside them.

- **Status:** Accepted
- **Date:** 2026-11-06
- **Builds on:** [0013](./0013-the-api-shape-before-the-first-release.md) (the shape of the API),
  [0024](./0024-the-api-polish-before-the-freeze.md) (the polish before the freeze),
  [0035](./0035-the-index-knows-no-types.md) (the index knows no types),
  [0036](./0036-the-readme-the-specification-and-the-tutorial.md) (the entrance, the contract, the way)
- **Affects:**
  - `docs/api.md` (new), `README.md` (the section "The API"), `docs/README.md` (the map)
  - `tests/Nordmann.LUI.Architecture.Tests/Public_API_Documentation.cs` (new), `docs/testing.md`
- **Issue:** #61

## Context

The public API of the package was documented — but nowhere.

| | Where it was |
|---|---|
| the **README** | eight types and the quick start: what a reader needs in the first five minutes |
| the **specification** | the API in prose, with the paragraphs of a contract — and the specification is a paper of the work, under `.agents/` since [0037](./0037-the-papers-of-the-work-live-under-agents.md) |
| the **XML comments** | every public member of the assembly, in the source |

Three places, none of them complete for the reader of the package: the README names eight of the thirty-nine public
types, the specification describes the API of a version of it that has moved twice since, and the XML comments are in
the source, where a consumer of the package does not look. The owner asked for **the complete public API** to be
described — and "complete" is a word that needs a test behind it, or it rots with the next change of the API.

## Decision

1. **`docs/api.md` documents every public type of the package.** Thirty-eight in `Nordmann.LUI`, one in
   `Nordmann.LUI.Hosting`: the catalog and its settings, the three builders, the three index interfaces, the entry and
   the key, the query and its options, the result and the hit, the types of the bulk path, the payload, the similarity,
   the seven enumerations, the seven exceptions and the hosting extension. Every member gets a row and a sentence.
2. **A new architecture test holds the page up.** `Public_API_Documentation` reflects over the two assemblies and fails
   when
   - a public type is not named in the page — the API grew and the page did not, or
   - the page names a type that the code does not have — the API shrank and the page did not.
   The same test fails when the README names a type that the code does not have.
3. **The page is in `docs/`, not in the README.** The README stays the entrance: it names the eight types a reader
   needs first and links the page for the rest. Thirty-nine types with a sentence each would be an entrance nobody
   reads ([0036](./0036-the-readme-the-specification-and-the-tutorial.md)).
4. **The page is a description, not a contract.** It says what there is and what it is for; the behaviour is in the
   specification and the tutorial.

## Alternatives

| | For | Against |
|---|---|---|
| **A — the API stays in the README** | one place to look | the README would grow by two hundred lines; an entrance is not a reference |
| **B — the API is generated from the assemblies** | it cannot drift | the generated text has no sentences; a consumer wants to know what a thing is *for* |
| **C — the XML comments are the documentation** | they are already there | they are in the source, and they are not the documentation of the package |
| **D — a page of its own, held up by a test** (this decision) | complete, in one place, and it cannot drift silently | one more document, and one more test to keep |

## Consequences

- **The public API is documented for the first time.** A reader of the package can find every type and every member
  without reading the source.
- **The page cannot drift.** The test compares the page with the assemblies; a change of the API without a change of
  the page fails the build — the same rule the test map follows ([0019](./0019-the-test-tree-three-layers-one-map.md)).
- **The test reads the assemblies, not a hand-written list.** A list would rot the way the page would; the assemblies
  are the truth.
- **The public API does not change.** Nothing in this decision touches a type, a member or a signature; the CHANGELOG
  says so.

**Verified by:** `Public_API_Documentation` (the page against the two assemblies, the README against the
assemblies), `dotnet test`.
