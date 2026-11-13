# 0030 — The four similarity measures are LUI's own: the runtime dependency on F23 ends

> **Contract note (ADR-0056, 2026-11-12):** this record was written against a contract outside the repository — a
> private one, that no reader of LUI can know and does not have to. The repository states its own rules since: the
> rules that contract carried for LUI stand in [`AGENTS.md`](../../AGENTS.md) and in the documents of
> [`docs/`](../../docs/README.md). The text below is history and stays as it was written.
> **Note (ADR-0057, 2026-11-12, issue #121):** this record named an external contract that a reader of a public
> library cannot know. The one sentence that named it is restated in the terms of this repository. The decision
> stands as it was written; the text below is history.

- **Status:** Accepted
- **Date:** 2026-11-05
- **Builds on:** [0004](./0004-dependencies.md) (the dependency set),
  [0011](./0011-the-multi-word-index.md) (the measures), [0018](./0018-two-packages-core-and-hosting.md)
  (the runtime of the two packages), [0032](./0032-the-allocation-of-a-query.md) (the allocation of a query),
  [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md) (the freeze of the public API)
- **Affects:**
  - `src/Nordmann.LUI/Similarity/DamerauMeasure.cs` (new), `src/Nordmann.LUI/Similarity/LevenshteinMeasure.cs` (new),
    `src/Nordmann.LUI/Similarity/JaroWinklerMeasure.cs` (ADR-0032)
  - `src/Nordmann.LUI/LuiSimilarity.cs`, `src/Nordmann.LUI/Nordmann.LUI.csproj` (the package reference),
    `Directory.Packages.props`
  - `tests/Nordmann.LUI.Unit.Tests/DamerauEquivalenceTests.cs` (new),
    `tests/Nordmann.LUI.Unit.Tests/LevenshteinEquivalenceTests.cs` (new),
    `tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs` (the dependencies),
    `tests/Nordmann.LUI.Unit.Tests/Nordmann.LUI.Unit.Tests.csproj` (the package as a test dependency)
  - spec ADR-0030, spec ADR-0004, ADR-0004, ADR-0018, ADR-0032, `docs/testing.md`, CHANGELOG
- **Issue:** #30 (the owner's note on the issue)

## Context

ADR-0004 gave LUI two packages that no ADR of this repository named yet: `symspell` for the typo tolerance
and `F23.StringSimilarity` for the four similarity measures of ADR-0030. The owner's note on issue #30: *"own
Jaro-Winkler/Damerau/Levenshtein ⇒ F23 falls away ⇒ runtime dependencies 3 → 2 ⇒ that needs an ADR."*

Two of the four measures were already LUI's own: the N-gram Dice (ADR-0011) and, since ADR-0032, the Jaro-Winkler. The
remaining two — Damerau-Levenshtein and Levenshtein — were still the library's, and the library **allocates per
comparison**: a dictionary and a matrix of distances for the Damerau-Levenshtein (about 5 KB), two arrays for the
Levenshtein (about 350 B). A query compares up to 500 candidates (ADR-0020), so the measure is on the hot path of every
ranking.

## Decision

**The definitions move into the library; the numbers do not change.**

`DamerauMeasure` and `LevenshteinMeasure` are LUI's own, in `src/Nordmann.LUI/Similarity/`. They are **bit-exact** to
`F23.StringSimilarity.Damerau` and `F23.StringSimilarity.NormalizedLevenshtein` of the **version 7.0.1** that the
library shipped — the packed version, not the branch `main` (the two differ: `THREE` is an `int` in 7.0.1 and a
`double` on `main`).

What "the same number" means here is exact, and the proofs are tests without a tolerance:

- `DamerauEquivalenceTests` — known pairs, 10,000 random pairs, every length against every length up to 12, a long
  text; **and zero allocation**.
- `LevenshteinEquivalenceTests` — the same.

The quirks of the definitions are part of them and are kept:

- the Damerau-Levenshtein is the **unrestricted** distance (a substring may be edited more than once), not the optimal
  string alignment;
- the distance is an **integer**; the division by the longer text and the subtraction from one follow the library's
  order — the order decides where the rounding happens.

**The runtime dependency ends.** `F23.StringSimilarity` is no longer a dependency of `Nordmann.LUI`: the runtime
dependencies of the core are `Microsoft.Data.Sqlite` and `symspell` (spec ADR-0004). The package stays a **test**
dependency — the tests compare against it, and the comparison is the proof. The architecture test
`The_library_ships_only_the_documented_runtime_dependencies` names the two.

**The allocation of a measure is zero.** The measures use the stack for short texts and the array pool for long ones;
they allocate nothing per comparison.

## What was measured

The same machine, the old and the new implementation alternately, the median of three runs of 20,000 comparisons of a
15-character text against a 38-character one (`GC.GetAllocatedBytesForCurrentThread()` for the allocation):

| Measure | F23 7.0.1 | LUI's own |
|---|---:|---:|
| Jaro-Winkler | 328 B | **0 B** |
| N-gram Dice (own since ADR-0011) | 11.2 KB | **0 B** |
| Damerau-Levenshtein | 5.0 KB | **0 B** |
| Levenshtein | 416 B | **0 B** |

The **times** of this table are not published: the sandbox box they were measured on fluctuates by a factor of two
between two runs of the *same* code, so a number from it would be a claim without a tree. What the runs show is that
the own measures are **as fast or faster** than the library's — the N-gram Dice by a factor of three, the Damerau-
Levenshtein and the Levenshtein by the margin the missing allocation buys. The times of ADR-0030 belong to the measurement
machine and are to be measured there anew.

## Consequences

- **The core ships two runtime dependencies instead of three** (ADR-0004). One package less to keep current, one less
  licence in the dependency list, one less assembly in the output of an application that uses LUI.
- **The numbers do not change**: a ranking that used `LuiSimilarity.DamerauLevenshtein` or `LuiSimilarity.Levenshtein`
  gives the same hits in the same order as before. The public API is unchanged — the measures are the same four, with
  the same names and the same semantics; `ISpanSimilarity` (ADR-0032) stays internal.
- **`F23.StringSimilarity` remains a test dependency.** The parity tests need the library they replace; that is the
  point of a parity test.
- **`symspell` stays.** It is the typo tolerance (ADR-0012), not a similarity measure; building it is the owner's open
  decision after 0.1.0.
- **The own measures are code to maintain.** Four definitions instead of none, each with a proof against the library
  it replaces. The proof is a test that fails when the definition drifts — which is the price of not depending on
  somebody else's implementation.

**Verified by:** `tests/Nordmann.LUI.Unit.Tests/DamerauEquivalenceTests.cs`,
`tests/Nordmann.LUI.Unit.Tests/LevenshteinEquivalenceTests.cs`,
`tests/Nordmann.LUI.Unit.Tests/JaroWinklerEquivalenceTests.cs` (all without a tolerance),
`tests/Nordmann.LUI.Architecture.Tests/Repository_Layout.cs` (the runtime dependencies),
and the public API diff against the base of the branch (byte-identical).
