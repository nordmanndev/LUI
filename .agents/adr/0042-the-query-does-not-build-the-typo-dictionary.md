# 0042 — The query does not build the typo dictionary: the application asks for it

> **Note (ADR-0057, 2026-11-12, issue #121):** the two German quotations of ADR-0012 were translated into English. The
> specification is German and stays German until it is deleted in pull request 8; the decision stands as it was
> written; the text below is history.

> **Note (ADR-0061, 2026-11-13, issue #125):** the specification this record was written against is deleted.
> Every section of it has a home — the ADR or the page that carries the same rule; the mapping is in
> [ADR-0061](./0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md). The sentences below
> that name the specification are history; the rule they carry lives in the ADR named beside them.

- **Status:** Accepted
- **Date:** 2026-11-07
- **Builds on:** [0021](./0021-a-stale-typo-dictionary-and-a-query-that-does-not-wait.md) (the dictionary in the life of an
  index), [0025](./0025-the-typo-dictionary-only-grows.md) (the dictionary only grows),
  [0033](./0033-the-read-path-opens-an-index-without-writing.md) (the read path opens an index without writing)
- **Affects:**
  - `src/Nordmann.LUI/Querying/TypoCorrection.cs` (the rule), `src/Nordmann.LUI/Typos/TypoDictionary.cs` (the path that is
    gone)
  - `samples/Lui.Sample/Data/SampleSeed.cs`, `samples/Lui.Sample/Indexes/BulkSampleIndex.cs` (the application asks for it)
  - ADR-0012 (the contract that is read), `README.md`, `CHANGELOG.md`
  - `tests/Nordmann.LUI.Integration.Tests/TypoDictionaryLifecycleTests.cs` (the contract of the tests) and the tests that
    measured the build inside the query
- **Issue:** #35

## Context

The typo dictionary was built **by the first query that needed a correction** — inside the read transaction of that
query, in the snapshot that pins the candidates and the hits. The build reads every entry over a second connection, and
at three million entries it takes seconds and 138 MB.

Three things follow from that, and all three are costs of the *query*:

- **The WAL grows while the build runs.** A checkpoint cannot move past the snapshot of the query, and the WAL file
  grows for as long as the build takes.
- **With the WAL switched off, a writer that waits and the query that builds hold each other up.** The writer takes
  PENDING — which forbids new SHARED locks — so the build cannot read on its second connection; the writer, in turn,
  waits for the SHARED lock of the query. Both wait for the busy timeout (issue #35).
- **The autocomplete pays for it.** The input is corrected at every keystroke, and a query that is cancelled at the
  next keystroke would start the build again — a build bound to the request would never finish (review of pull request
  34, I3).

The contract of ADR-0012 said so: *"the build on the first access"*. It is the one promise
of the library that the application cannot see, cannot pay for and cannot avoid.

## Decision

1. **A query never builds the dictionary.** It asks whether one is there; if there is none, it answers with what the
   input found and sets `CorrectionSkipped` — the answer it already gave while another build was running (ADR-0021).
2. **The build belongs to the application.** `WarmUpTypoDictionary()` builds it, `RebuildTypoDictionary()` builds it
   anew, and the writes learn the new beginnings. The application decides when it pays the 138 MB and the seconds.
3. **A failed build says so where it was asked for.** The warm-up throws. Before, the query swallowed the failure and
   answered without the correction — the error appeared nowhere.
4. **The state of the dictionary stays** `NotBuilt`, `Building`, `Current`. A query that comes while a build runs
   answers without the correction, as before.
5. **The public API does not change.** `WarmUpTypoDictionary()` and `RebuildTypoDictionary()` exist already, on the
   embedded and on the linked index; no member disappears, so `docs/api.md` stays as it is.
6. **The specification follows.** ADR-0012 says *"the build on explicit request"* and names the rule: a query asks,
   the application builds.

## Alternatives

| | For | Against |
|---|---|---|
| **A — the query builds outside its snapshot** (direction 1 of issue #35): it ends the snapshot, builds, opens a new one and reads the candidates again | the correction still happens without the application | the query still waits for seconds, and the WAL still grows during the build; the autocomplete waits at every keystroke |
| **B — the query hands the build to the next call** (direction 2 of issue #35) | the application need not know the dictionary at all | the *next* query pays for it — the seconds move, they do not go; a build per process, begun by whoever asks first |
| **C — the query builds as it does now** | nothing changes for the application | the WAL grows, the writer and the query can hold each other up, and the cost is invisible |
| **D — the application asks for the build; the query never does** (this decision) | the cost is where it is asked for; no second connection in the read path | the application has to call the warm-up — a change of behaviour, written down in the CHANGELOG |

## Consequences

- **The behaviour changes.** An application that relied on the lazy build has to call `WarmUpTypoDictionary()` after it
  has loaded its entries. The sample does it — after the seed and after a fill.
- **The deadlock of issue #35 is gone.** The read path opens no second connection, so there is no lock that can wait for
  another one. The acceptance of the issue — with the WAL switched off, a writer that waits does not deadlock a query —
  is a test now.
- **The WAL does not grow because of a build inside a query.**
- **The first query after the start answers without a correction** until the application asks for the build. That is the
  honest answer: the dictionary is not there yet.
- **The numbers of the record stay the same work.** The measurement of the build moves from the query to the warm-up;
  the seconds and the megabytes are the same.

**Verified by:** `TypoDictionaryLifecycleTests` — the two new tests (a correcting query leaves the dictionary unbuilt and
sets `CorrectionSkipped`; a writer that waits does not deadlock a query with the WAL switched off), the three tests of the
old contract brought to the new one, and the green CI run.
