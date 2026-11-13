# 0028 — LUI has no Highlight: a correction is shown as a question

> **Note (ADR-0057, 2026-11-12, issue #121):** the German wording of the record — the owner's decision and two
> sentences of the specification — was translated into English. The specification is German and stays German until it
> is deleted in pull request 8; the decision stands as it was written; the text below is history.

> **Note (ADR-0061, 2026-11-13, issue #125):** the specification this record was written against is deleted.
> Every section of it has a home — the ADR or the page that carries the same rule; the mapping is in
> [ADR-0061](./0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md). The sentences below
> that name the specification are history; the rule they carry lives in the ADR named beside them.

- **Status:** Accepted (owner's decision, 2026-11-04: "build it that way, and I would strike number 20")
- **Date:** 2026-11-04
- **Issue:** #20 (closed, not planned); found by PR-011 (#51)
- **Amends:** ADR-0028 (now "falls away"), docs/api.md (the remark on the target API), docs/api.md (the listing of the query), the table of the steps;
  [0013](./0013-the-api-shape-before-the-first-release.md), [0017](./0017-dependency-injection-and-hosted-services.md) and
  [0008](./0008-the-query-path.md) (each says `Highlight` comes in a later version; they get a note)
- **Builds on:** [0027](./0027-the-sample-flexdatalist-modular-client-one-fetch-boundary.md) (the sample), [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (additive-only)

## Context

Spec ADR-0028 described a marking of the found position: `LuiHit.Highlight(Start, Length)` in the **original** text, computed by the
`Normalizer` from a map of positions (search form → original), switched off by default. The owner moved it out of 0.1.0 (#20,
ADR-0017: "the JavaScript library of the front end marks the match itself"). Building the sample (PR-011) put it to the test, and
the test said it is the wrong thing to build:

- **flexdatalist's highlight cannot serve a server-ranked list.** It lives in the same function as the client filter. The filter
  must be off (it drops what the server found by transliteration, the compact form or a correction — `mueller` → `Müller`,
  `kartofel` → `Kartoffelsuppe`), and then nothing is marked. Left on, a hit the library cannot match literally is silently lost,
  and a correction can never be shown. The sample marked the match itself for a while (a fold table, a span map, 600 lines
  with tests) and still could not mark what LUI matched through the compact form or the second correction variant.
- **SQLite's `highlight()` does not replace the map.** Measured on tables shaped like LUI's: it returns the **compact form**, not
  the original (`Käsekuchen` → `[Kaesekuchen]`, `Apfel Kuchen` → `Gedeckter[Apfelkuch]en`); it returns **`NULL`** for a linked
  index (contentless); it does not exist for the prefix path (under three characters) nor for a correction variant.
- **The map is a promise across every script.** It can be exact for the Latin alphabets (334 letters of 16 alphabets measured
  equal to the tokenizer's fold) because LUI already holds the tokenizer's table; for scripts without word boundaries, with stacked or
  composed marks, or with a locale-dependent case it is a research task, and a wrong mark is wrong in public.
- **What a user needs from a correction is to know that it happened.** The typo tolerance (ADR-0012) searches a corrected word
  besides the typed one, so a list can show hits the input does not contain, while the input still holds the typo. A mark does not
  explain that; "Did you mean “apfel”?" does — and `LuiResult.CorrectedQuery` already says what was searched.

## Decision

1. **LUI delivers no found positions.** There is no `LuiHit.Highlight`, no `.Highlight(…)` on the query, no property on
   `LuiQueryOptions`, and no map from the search form to the original in the `Normalizer`. None of them exists in the code (ADR-0013
   removed the field); the specification stops promising them: ADR-0028 becomes "falls away", the listing of docs/api.md loses its line.
2. **A correction is shown as a question.** An application reads `CorrectedQuery` and shows "Did you mean …?". The sample does
   (`suggest/did-you-mean.js`): one line above the list, and choosing it searches the corrected word as it is.
3. **An application that wants marks makes them itself**, with what it knows about its script and its input. The library does not
   hold that knowledge and does not promise it.
4. **Not decided, not planned:** a field on `LuiHit` that says a correction found the hit, which would let a page split "hits" from
   "did you mean" (today the union of the typed input and the corrections comes back undivided). It would be an addition
   (ADR-0026); it waits for someone who needs it.
5. Issue #20 is closed as *not planned*.

## Consequences

- **Nothing to build, nothing to freeze:** one feature less with a promise across scripts; the freeze (ADR-0026) has one thing less to
  hold.
- **The sample shrinks:** `fold.js`, `fold-table.js`, `highlight.js`, the generator and their tests are deleted (the sample keeps
  the escaped twin of every displayed property — flexdatalist writes the displayed values with `innerHTML`, so the twin is a duty of
  safety, not of marking).
- **The first user who wants marks in a browser** writes ~100 lines against `CorrectedQuery` and the hit text; the sample no
  longer shows how. That is the price, and it is accepted.
- **Reversible:** if a later release wants positions, it is an addition; this record would be superseded, not amended.
