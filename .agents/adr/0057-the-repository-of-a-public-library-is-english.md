# 0057 — The repository of a public library is English

> **Note (ADR-0061, 2026-11-13, issue #125):** the specification this record was written against is deleted.
> Every section of it has a home — the ADR or the page that carries the same rule; the mapping is in
> [ADR-0061](./0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md). The sentences below
> that name the specification are history; the rule they carry lives in the ADR named beside them.

- **Status:** Accepted
- **Date:** 2026-11-12
- **Builds on:** [0037](./0037-the-papers-of-the-work-live-under-agents.md) (the papers of the work live under `.agents/`,
  the documentation of the library under `docs/`), [0036](./0036-the-readme-the-specification-and-the-tutorial.md) (the entrance,
  the contract, the way), [0002](./0002-library-not-an-application.md) (a library, not an application — and it stands on the
  rules of this repository)
- **Affects:**
  - `.agents/plan/` (five papers: three translated, two restated), `.agents/adr/` (thirteen records: six restated, six
    with a translated quotation, one with a renamed link — this one added), `.agents/adr/README.md` (the index),
    `.agents/state/recently-completed.md`
  - `docs/tutorial/` (five files renamed, and their README), `docs/architecture/alignment.md`, `CHANGELOG.md`
  - **not** the live documents — `README.md`, `AGENTS.md`, `docs/README.md`, `docs/testing.md`, `Directory.Build.props`,
    `Directory.Packages.props`, `.editorconfig`, `.github/workflows/ci.yml`: #131 took the references to the external
    contract out of them, and this pull request does not undo that work.
- **Issue:** #121 (part of #117, step 4)

## Context

LUI is a **public NuGet library**. Its reader is anybody on nuget.org — and its reader does not know the house this
repository was born in: **the external contract** is not published with the package, and nothing in the repository needs
it. A decision that is written down in the terms of a contract nobody outside can read is a decision nobody outside can
use.

The repository had grown a second voice:

- the **working papers** under `.agents/plan/` were partly German — two papers wholly, a third in its two findings;
- the **five tutorials** carried German file names, although their text was English;
- the **ADRs** — the decisions of the library — were written in the terms of the external contract: the delta to it *is*
  the subject of ADR-0002.

Two things stay German on purpose:

- the **sample data** ("Gedeckter Apfelkuchen") — it shows what the normalisation is for;
- the **specification** — it is the contract of the work, it is deleted in pull
  request 8, and translating 1,407 lines of a document that is about to go would be work without a purpose.

## Decision

**The repository of a public library is English, and its decisions speak in its own terms.**

1. **Every document a reader of the package can reach is English.** The entrance, the documentation under `docs/`, the
   papers of the work under `.agents/`, the decisions, the CHANGELOG, the comments of the build files.
2. **The working papers are translated — in place.** The German text becomes English; a dated note at the top of each
   paper says what was translated and that the text below is the paper of its time. The translation keeps what the paper
   said at its time: the decisions, the numbers, the names, the branches, the issue numbers, the dates. A paper is
   history; it is not corrected by the translation.
3. **The tutorials carry English file names.** `01-first-index.md`, `02-your-own-data.md`, `03-the-payload.md`,
   `04-many-entries.md`, `05-in-the-application.md` — and every link that pointed at the old names follows them.
4. **The ADRs restate their decisions in the terms of this repository.** Six records named the external contract — 0002,
   0004, 0027, 0030, 0036, 0053 — and are restated in place: no decision is lost, the reason of every rule stays, and a
   dated note says what was restated and why. ADR-0002 is the hard one: its subject *is* the delta, so its decision
   becomes "LUI is a library, not an application — and it stands on the rules of this repository", its title changes with
   it, and the row of the index follows. Six further records — 0005, 0010, 0022, 0025, 0028, 0042 — carried a
   German quotation; the quotation is translated, and the dated note of the record says so. The thirteenth, ADR-0055,
   changed in one line: the link to the renamed tutorial follows the rename. It carried no quotation, so it needs no
   note of its own.
5. **The external contract goes out of the documents.** No document of the library points at it any more — not the
   entrance, not the map of the documentation, not the build files, not the decisions. The papers of the work describe
   what was removed, in the words of this repository; they do not name it.
6. **The history stays visible.** An ADR is not rewritten to look as if it had always been written this way: the dated
   note is what the reader finds first.

**This decision changes no public API.** Nothing here touches a type, a member or a signature — no migration note, no
row in `docs/api.md`.

## Consequences

- **The searches come back where they should.** The German function words over the markdown files of the repository find
  the specification and the sample data only; the search for the external contract finds the papers of the work and the
  decisions that say what was replaced — and nothing else.
- **The papers of the work keep their history.** Each of them carries a dated note; the text below the note is the paper
  of its time, in the language of the repository.
- **The tutorials have new names.** The links of the entrance, of the map, of the papers and of the tutorials follow
  them, and the link check holds them.
- **ADR-0002 has a new title**, and its row in the index of the decisions carries it.
- **The German quotation marks stay.** They are the typography of this repository, and they quote the German sample data
  ("Apfel Kuchen") in the code as well as in the documents.
- **The specification keeps its German text** until pull request 8 deletes it. The citations of its annexes ("the record")
  go with it; nothing else points at a German text.
- **A second voice in the repository is a bug now.** A German sentence in a document of the library is a defect that the
  search of the acceptance criteria catches.

## Alternatives

| | For | Against |
|---|---|---|
| **A — translate the specification too** | one language in the repository | 1,407 lines of a document that is deleted in pull request 8 — work without a purpose |
| **B — leave the working papers in German** | the papers stay exactly as they were written | a reader finds three documents in another language, and the search for German words does not come back empty |
| **C — keep the ADRs as they are** | the records stay untouched | the decisions of a public library are written in the terms of a contract its reader cannot know |
| **D — translate the sample data too** | one language in the data as well | the sample data shows what the normalisation is for — "Gedeckter Apfelkuchen" is the point of it, not an accident |
| **E — the repository is English, and its decisions speak in its own terms** (this decision) | every document of the library is in the language of its reader, and the history of the work stays visible in the notes | the papers of the work carry a translation — a note per paper, and a restatement per ADR |
