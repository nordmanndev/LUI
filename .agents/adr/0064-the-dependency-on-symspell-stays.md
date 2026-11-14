# 0064 — SymSpell stays, and the contract with it is measured

- **Status:** Accepted
- **Date:** 2026-11-14
- **Supersedes:** —
- **Affects:** `src/Nordmann.LUI/Typos/TypoDictionary.cs`, `tests/Nordmann.LUI.Integration.Tests/Typos/TypoContractTests.cs`, `docs/typo-dictionary.md`, `docs/benchmarks.md`

## Context

The core had three runtime dependencies: `Microsoft.Data.Sqlite`, `symspell` and `F23.StringSimilarity`. ADR-0030 took
`F23.StringSimilarity` away — the four similarity measures are LUI's own — so **`symspell` is the last foreign algorithm
in the core**.

Issue #44 asks the question: does LUI keep `symspell`, or does it implement the part it uses itself? What LUI uses is
small — the delete variants over the word beginnings, the counts of the words, the prefix length, the count threshold and
the order of the corrections — but SymSpell has subtleties, and what it costs is measured: 138 MB and 4.3 s for 685,739
words (ADR-0012, `docs/typo-dictionary.md`), with quality floors in `TypoQualityTests` and a budget in ADR-0020.

The dependency set of a package is its contract (ADR-0018), so the question is the owner's; the data to answer it is ours
to measure. The owner answered on 2026-11-14: **SymSpell stays** — and the contract with the package is measured and
pinned, so that a later decision has the numbers and does not start from nothing.

## Decision

**SymSpell stays.** The core keeps the dependency; LUI does not implement the delete variants itself.

The contract between LUI and the package is now measured, in two parts:

- **The rules.** `TypoContractTests` pins them on a corpus with known weights: a beginning is of three to ten letters,
  the count of a word is its weight plus one, the count threshold is one (a word with the weight zero is a word), the
  order of the corrections is the distance, then the length that was typed, then the count, then the ordinal, and the
  head of a lookup is the first ten letters.
- **The measurement.** The same test measures the contract over the whole word list: the time of the build, the memory of
  the dictionary, the number of the corrections and their **fingerprint**. The fingerprint is what an own implementation
  of the dictionary would have to reproduce; the numbers are what a decision about one is made with.

The harness rides on the lane of the parity over the whole word list — that is where the list is.

## Consequences

- The dependency set of the core does not change; ADR-0004 and ADR-0018 stand as they are.
- The rules of the contract are a test now. A change of them — the count threshold, the counts, the order, the length of
  the beginnings — breaks the harness, and the mutation `a-count-threshold-that-drops-the-light-words` proves that it
  bites.
- The cost of the dictionary stays a promise: ADR-0020 allows 200 MB for 700,000 words, and the harness measures it.
- A later decision — an own implementation, a new version of the package — starts from the fingerprint and the numbers of
  `docs/benchmarks.md`, and not from a fresh measurement. **The question is not closed, it is answered once**: the owner
  may want to look at it again when the package moves or when the core's dependency set is touched for another reason.
