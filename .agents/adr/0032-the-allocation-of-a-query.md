# 0032 — The allocation of a query: what it costs, and what it may cost

> **Name note (ADR-0031, 2026-11-05):** since this record was written, the rule of the pool has changed: when there is
> no place left, **the smallest buffer goes** and the new one takes its place — before, the new buffer was dropped and
> the next query allocated everything anew. The pool has always had twelve places. The rule in the text below is
> history and stays as it was written; the figures in "Consequences" are corrected, because they counted a pool of four
> buffers that never existed.

- **Status:** Accepted
- **Date:** 2026-11-05
- **Builds on:** [0020](./0020-the-performance-budgets-measured.md) (the budgets of ADR-0020, measured),
  [0011](./0011-the-multi-word-index.md) (the measures), [0026](./0026-the-public-api-from-0-1-0-on-grows-only-additively.md)
  (the freeze of the public API)
- **Followed by:** [0030](./0030-the-similarity-measures-are-luis-own.md) (the other three measures become LUI's own
  too; the runtime dependency on F23 ends), [0031](./0031-the-pool-displaces-instead-of-discarding.md) (the pool
  displaces instead of discarding)
- **Affects:**
  - `src/Nordmann.LUI/Similarity/JaroWinklerMeasure.cs` (new), `src/Nordmann.LUI/ISpanSimilarity.cs` (new),
    `src/Nordmann.LUI/Querying/ScoredBuffer.cs` (new)
  - `src/Nordmann.LUI/LuiSimilarity.cs`, `src/Nordmann.LUI/Querying/Ranker.cs`, `src/Nordmann.LUI/Querying/CandidateReader.cs`,
    `src/Nordmann.LUI/Querying/HitReader.cs`, `src/Nordmann.LUI/Querying/QueryRunner.cs`,
    `src/Nordmann.LUI/Querying/TypoCorrection.cs`, `src/Nordmann.LUI/Normalization/Normalizer.cs` (the doc comment)
  - `tests/Nordmann.LUI.Unit.Tests/JaroWinklerEquivalenceTests.cs` (new),
    `tests/Nordmann.LUI.Unit.Tests/SearchFormParityTests.cs` (new)
  - ADR-0020, `docs/testing.md`, CHANGELOG
- **Issue:** #30

## Context

ADR-0020 sets a budget for the allocation of a query: p50 ≤ 250 KB, p95 ≤ 350 KB. It was measured at p50 83 KB and p95 298 KB
(ADR-0020) — the owner finds 250–300 KB per query too much, and issue #30 asks for the **causes measured** and the
allocation **clearly lower**, with the new target in ADR-0020 and in ADR-0020.

Until now nobody knew where the allocation was. The three plausible suspects — a string per candidate, the growth of the
`Scored[]`, the similarity measure — were not measured, and the similarity measure of the library allocates in a way that
was documented as deliberate (ADR-0011: the measure of F23 allocates about 200 B per comparison, the library builds its own
instead of the bigram dictionary).

## Decision

**Measure first, then optimize.** The shares come from **differences between measured runs**, never from estimates: the
similarity's share is the difference between the default order and the weight order, the candidate's share is the slope of
the allocation over the candidate budget, the hit phase's share is the difference between 20 hits and one hit. A probe
(`GC.GetAllocatedBytesForCurrentThread()`, the median of 31 rounds over 200,000 entries of the German word list) measured
every variant.

**Three changes, all inside the library.** The public API is untouched — the diff of the exported types against the base
of the branch is empty, and ADR-0026 freezes the API from 0.1.0 on.

### 1. LUI's own Jaro-Winkler, with the same number

> **Superseded in part by ADR-0030:** the Jaro-Winkler was the first of the four measures to become LUI's own; the
> other three followed, and the runtime dependency on F23 ended with them. The reasoning of this record — measure the
> shares, then optimize, never without the proof — stayed.

`F23.StringSimilarity.JaroWinkler` builds four arrays per comparison (about 200 B). A query compares up to 500 candidates,
so the default measure of the library allocated **55.89 KB per query** — 36 % of the total of a single-word query.

`JaroWinklerMeasure` is LUI's own: on the stack for up to 256 characters, otherwise from the array pool, without an
intermediate allocation. It is **bit-exact** to `F23.StringSimilarity.JaroWinkler` 7.0.1 — including the quirks that a
"better" implementation would get wrong: the measure is computed in single precision, the common prefix is **not** capped at
four characters, and its weight shrinks for long texts (`min(0.1, 1 / max.Length)`). `JaroWinklerEquivalenceTests` proves
the equality against the library: known pairs, 10,000 random pairs, the lengths 0 … 12 — **without a tolerance**. A
difference in the eighth decimal could change the order of the hits, so a tolerance would prove nothing.

### 2. The similarity is measured on spans

`Ranker.Measure` normalised the text of every candidate into a **string** (`Normalizer.ToSearchForm`: a `StringBuilder`
and a `String`, 144–320 B) and cut the part the match begins at out of it — 44.09 KB per query, 29 % of the total.

The ranking asks a measure of LUI through the internal `ISpanSimilarity` (a `ReadOnlySpan<char>` in, a number out). The
search form is written into a **buffer** (`Normalizer.TryWriteSearchForm`, which was there and unused) and measured there:
neither the form nor the part the match begins at becomes a string. The public `ILuiSimilarity` stays as it is; a measure
of an application that only speaks strings is measured the old way — the difference is 40.05 KB per query (measured), and
that is the price of a measure from outside.

`SearchFormParityTests` proves that the buffer path and the string path fold a text into the same form — the ranking may
not change.

### 3. The union of the corrected candidates works in the buffers

A corrected query reads up to three variants more candidates and **unites** them. The union built **four lists** of the
size of the candidates — about 200 KB per corrected query with the budget of 500. This is where the p95 of 298 KB of
issue #30 came from: from the **corrected** queries, not from the ordinary ones.

The union works in the pooled buffers now: one buffer for all the candidates, one for the ones that remain, sorting and
deduplication **in place**. What is left of the correction is the texts of the additional candidates — the database has to
hand those out.

### 4. The buffers of the candidates are reused

A query that reads 500 candidates needs three arrays of 500 entries (60 KB) and threw them away. `ScoredBuffer` keeps them
per thread: the first query of a thread allocates, the others do not. The arrays are cleared before they are returned, so
they do not hold the texts of the last query, and a buffer is used only when its length is exactly the length wanted — the
length of the array of the hits is the number of the hits.

## What was measured

200,000 entries, the whole German word list (685,829 words), the median of 31 rounds, `GC.GetAllocatedBytesForCurrentThread()`,
the same probe before and after the change.

| | Before | After | Difference |
|---|---:|---:|---:|
| **Single word, default** (Jaro-Winkler, 500 candidates, 20 hits) | 153.32 KB | **26.55 KB** | −83 % |
| **Multi word, default** (the same) | 185.24 KB | **66.53 KB** | −64 % |
| the similarity, single word | 55.89 KB | 0.86 KB | −98 % |
| per candidate (the slope over the candidate budget) | 301.8 B | **40.1 B** | −87 % |
| per candidate without the similarity | 95.5 B | 39.9 B | −58 % |
| `DistinctText` (on top) | +27.69 KB | +28.05 KB | — |
| the prefix path, one character | 64.35 KB | 36.38 KB | −43 % |
| FTS5, eight characters | 26.95 KB | 6.82 KB | −75 % |

The shares of the **before** (153.32 KB, single word) — measured as differences, not estimated:

| Cause | Share | How it was measured |
|---|---:|---|
| the similarity (F23's Jaro-Winkler, four arrays per comparison) | 55.89 KB (36 %) | default − weight order |
| the second normalisation (the search form as a string, the substring) | 44.09 KB (29 %) | weight order − weight order without the similarity |
| the candidate texts and the two `Scored[]` | ~44 KB (29 %) | the slope over the candidate budget |
| the hit phase (20 hits, the keys) | 8.16 KB (5 %) | 20 hits − 1 hit |
| the ground load (the SQLite statement, the query, the result) | ~7 KB (5 %) | one candidate |

## Consequences

- **The target of ADR-0020 is lower.** The allocation of a query is now p50 ≤ 60 KB, p95 ≤ 100 KB — measured by the budget
  test at 200,000 entries: **p50 18.09 KB, p95 38.80 KB** (before: p50 83 KB, p95 298 KB at three million, where it was
  about 20 % higher than at 200,000). The corrected query: p50 22 KB, p95 46 KB (before: 73 KB, 215 KB). The test of ADR-0020 measures it at three million and asserts it; a target that is
  missed is not lowered quietly.
- **What remains, and why it remains:**
  - **The texts of the candidates: 32–64 B per candidate** (19–37 KB per query). `Microsoft.Data.Sqlite` hands out the text
    of a row as a `string`; the word start (ADR-0008) needs the folded text, and the folded text cannot be read from the
    database: the column `s_norm` holds the transliteration, the case fold and the removal of the diacritics happen in the
    tokenizer. A candidate needs its text — this is the floor.
  - **The hit phase: about 370 B per hit** (the key, the payload, the hit object) — the hits are what the caller asked for.
  - **The ground load: about 7 KB per query** — the SQLite statement, the parameters, the query, the result.
  - **The typo correction: the union cost about 200 KB per corrected query before, now nothing** — it reads up to three
    times more candidates and unites them; the union built four lists of the size of the candidates. It works in the
    pooled buffers now, with a length that does not change from query to query (a length that changes misses the pool
    every time). What is left is the texts of the additional candidates. The corrected query is at p50 22 KB / p95 46 KB
    (before: 73 KB / 215 KB).
  - **`DistinctText`: +28 KB (single word), +84 KB (multi word)** — the option keeps the folded text of every candidate as
    the key of the set. A hash would be cheaper and would drop a candidate on a collision; the option is off by default.
- **The public API is unchanged.** `ISpanSimilarity` is internal; the exported types against the base of the branch are
  byte-identical.
- **The retained memory:** the pool keeps **twelve** buffers per thread (`ScoredBuffer.Slots`). At the default budget
  of 500 a query without a typo correction holds three of them — the candidates (`Candidates + 1` = 501 entries), the
  groups (the same number) and the hits (`MaxResults`, 20 by default); a corrected query holds five, the union of the
  corrections (`4 × (Candidates + 1)` = 2.004 entries) being the longest. An entry of `Scored` is 40 B (a long, two
  strings, an int, a double, a byte): 40.880 B for the three buffers of the plain query, 141.080 B for the five of the
  corrected one, and 961.920 B in the worst case — a thread whose pool holds twelve buffers of the longest length. The
  buffers are given back to the pool, not to the heap. The search form is written into a buffer of 256 characters — on
  the stack for texts up to 85 characters, from the array pool for longer ones; the similarity is measured in a buffer
  of 256 characters from the array pool.
- **Open: the dependency on F23.** The library still uses `F23.StringSimilarity` for `Damerau` and `NormalizedLevenshtein`
  (the measures that are not the default). The owner wants LUI's own measures for them as well, which would take the
  runtime dependencies from three to two. That is not part of the allocation: the two measures are not on the default path
  and do not allocate on it. It is the next step of #30.

**Verified by:** `tests/Nordmann.LUI.Unit.Tests/JaroWinklerEquivalenceTests.cs` (the parity with F23 7.0.1),
`tests/Nordmann.LUI.Unit.Tests/SearchFormParityTests.cs` (the parity of the two normalisation paths),
`tests/Nordmann.LUI.Integration.Tests/PerformanceBudgets.cs` (the target of ADR-0020), and the probe of this record
(200,000 entries, 31 rounds, median).
