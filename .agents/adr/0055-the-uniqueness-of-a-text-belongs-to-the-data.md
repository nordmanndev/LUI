# 0055 — The uniqueness of a text belongs to the data

- **Status:** Accepted
- **Date:** 2026-11-12
- **Builds on:** [0008](./0008-the-query-path.md) (the query path: `DistinctText` works on the candidates),
  [0012](./0012-typo-tolerance.md) (the correction and the candidates of the input and of its corrections),
  [0013](./0013-the-api-shape-before-the-first-release.md) (the shape of the public API before the first release),
  [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze begins with the tag `v0.1.0`),
  [0032](./0032-the-allocation-of-a-query.md) (the allocation of a query — the measurement of the option)
- **Affects:** `src/Nordmann.LUI/LuiQueryOptions.cs`, `src/Nordmann.LUI/LuiQuery.cs`,
  `src/Nordmann.LUI/Querying/CandidateReader.cs`, `src/Nordmann.LUI/Querying/Scored.cs`,
  `src/Nordmann.LUI/Querying/TypoCorrection.cs`, `samples/Lui.Sample/Api/SuggestParameters.cs`,
  `samples/Lui.Sample/wwwroot/js/`, `docs/api.md`, `docs/tutorial/01-erster-index.md`, `README.md`,
  `docs/testing.md`, `CHANGELOG.md`
- **Issue:** #119, step 2 of #117. The owner's decision of 2026-11-11, in the plan
  [`.agents/plan/2026-11-11-the-public-surface-and-the-measurement.md`](../plan/2026-11-11-the-public-surface-and-the-measurement.md)
  (D5).

## Context

`DistinctText` showed one hit per normalised text: the system tag "vegan" and the ordinary tag "Vegan" were one hit
instead of two. ADR-0008 decided that the option works on the candidates in the order of the rowid — the first candidate
of a text is the one with the highest priority and then the highest weight, so keeping the first is the rule of §9.2
and it costs no second pass.

The option had two prices, and the second one was the worse:

- **It kept the folded text of every candidate.** To know that two texts are the same, the search form of every
  candidate had to be kept — a string per candidate. ADR-0032 measured it: **+28 KB** per query with one word, **+84 KB**
  with several — the largest option cost in the library, against a budget of 60 KB for the whole query.
- **A duplicate used up a place of the candidate budget.** The budget counts the rows that were read, not the
  candidates that survived: 500 rows of which 400 are duplicates leave 100 candidates to rank. The option therefore made
  the search **shallower**, not only the answer shorter — the opposite of what ADR-0008 wrote ("the duplicates are
  dropped *before* the candidate budget is filled"). The code counted every row, and the code was right about the cost.

What the option did was solve a problem of the data at the price of every query. The problem of the data is this: two
entries hold the same text in two spellings, and the application wants one of them. The application knows which one — it
wrote both. The library does not: it cannot know whether the system tag or the ordinary tag is the one the user should
see.

## Decision

**The uniqueness of a text belongs to the data. `DistinctText` is removed.**

- **`LuiQueryOptions.DistinctText` and `LuiQuery.DistinctText(bool)` are gone.** Both of them: the reflection test
  `PublicApiShapeTests` compares the options of a query with its methods, so one of the two alone would have broken the
  guard. It is a breaking change, and it is still free — the freeze of ADR-0026 begins with the tag `v0.1.0`, and there
  is no such tag yet.
- **The implementation goes with it.** The query path keeps no folded text of a candidate any more: the `HashSet` of the
  search forms in `CandidateReader` and the `Key` of `Scored` are gone, and the union of the input and its corrections
  in `TypoCorrection` drops no duplicate. A query returns one hit per entry — the entries are the rows of the data.
- **The option was off by default, so the behaviour of a query that did not ask for it does not change.** Only a
  caller who asked for the option sees a difference, and that caller has a migration note in the `CHANGELOG`.
- **Where the uniqueness is stated depends on the kind of the index:**
  - **`Embedded`** — the entries live in the table of the library, and their normalised text is the generated column
    `s_norm` of that table. A **unique constraint on the normalised text** is what makes a text unique; the library
    does not create one, because only the application knows whether two entries may share a text.
  - **`Linked`** — the table is the application's own, and so is the constraint: a unique constraint over the normalised
    text of its own column, in its own migration.
- **The sample, the tutorial, the README and `docs/api.md` follow.** The checkbox of the sample's panel and its
  `distinct` parameter are gone; the tutorial's first example does not ask for one any more; the page of the API says
  where the uniqueness belongs.
- **The five tests are restated, not deleted.** The case — one text in two spellings — is the point, so it stays as a
  statement about the data: two spellings are two hits, the data decides how many entries there are, the priority still
  leads the hits, and the correction of a typo still reaches over the two spellings.

## What was measured

**Nothing was measured in this pull request.** The decision rests on the measurement of ADR-0032, which measured the
option on the query of the time: the folded text of every candidate costs **+28 KB** per query with one word and
**+84 KB** with several, against a budget of 60 KB for the whole query. That number is the reason; this pull request
only removes what it bought.

The allocation of a query without the option is the base of ADR-0032 — 26.6 KB for a single-word query and 105.7 KB
for a multi-word one — and it did not change here: the guard `AllocationPoolTests` holds it.

## Consequences

- **`LuiQueryOptions.DistinctText` and `LuiQuery.DistinctText(bool)` do not exist any more.** An application that used
  them does not compile, and the `CHANGELOG` carries the migration note.
- **An application that wants one hit per text has to keep the text unique in its data.** Without the constraint, a
  text in two spellings is two hits — and the library does not hide the second one.
- **The query path is smaller.** The search form of a candidate is not kept in a string any more; it is written into a
  buffer of the ranking, as it always was for the similarity.
- **The candidate budget is the budget of the rows again.** A duplicate is a candidate now, like every other entry: it
  is ranked, and it takes a place of the budget — which is what a query that asks for 500 candidates means.
- **The sample's page has one knob less**, and the sample's API one parameter less.
- **The CHANGELOG carries the migration note**, because a breaking change needs one (ADR-0026).

## Alternatives

- **The option stays, but it is cheaper.** Rejected: the cost is not the bookkeeping, it is the *decision*. A duplicate
  that is dropped in the query path is a duplicate the application never got to write; the library cannot know which of
  the two entries is the one the user should see. Making the option cheaper would not make it correct — it would only
  make the wrong answer cheaper.
- **The option becomes a default of the index, like the typo tolerance.** Rejected: it moves the same decision into
  another place and makes it invisible. An application that wants two spellings of a text in its list would have to
  know about a builder setting to get them.
- **The library creates the unique constraint on the normalised text in the `Embedded` case.** Rejected: the library
  cannot know whether two entries may share a text — the system tag "vegan" beside the ordinary tag "Vegan" is a case
  that the application may well want. A constraint that the library creates would refuse a write the application meant
  to make.
- **The option is deprecated instead of removed.** Rejected: there is no tag `v0.1.0` yet, so a deprecation would only
  carry the cost of the option further — and the option is the most expensive one in the library.
