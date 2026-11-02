# 0011 — The multi-word index: the compact column, one expression, a Dice that allocates nothing

- **Status:** Accepted (the proposal at the end is **Proposed**)
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `src/Nordmann.LUI/Indexing/OwnedSchema.cs`, `src/Nordmann.LUI/Querying/PreparedQuery.cs`,
  `src/Nordmann.LUI/LuiIndexBuilder.cs`, `src/Nordmann.LUI/Similarity/Similarity.cs`

## Context

Step 4 of the plan: `MultiWord` for names and titles. The input is split into words that are ANDed; the order of
the words and the spaces do not matter (§4). The specification fixes the schema (§7.2: the generated column
`s_compact`; §7.4: the search index over `s_norm` and `s_compact`), the candidate expression (§8.2) and the
ranking (§9). What it leaves open is where the rules meet real input, and what they cost.

## Decision

**The schema is the specification's.** A multi-word index adds `s_compact` (the transliterated text with the
separators taken out, `SqlNormalizer.Compact`, held to the C# `Normalizer` by the parity test), indexes
`s_norm` and `s_compact` in one FTS5 table, and the three triggers carry both columns. The kind is part of the
stored definition: the same name as a single-word index is a `LuiDefinitionMismatchException`.

**The words of the input are the runs between separators** of the search form (`apfel-zimt kuchen` is three
words), the same separator set the compact form uses. Words of three or more code points go into the AND;
shorter ones stay out of the expression and count in the ranking through the compact form (§8.2) — `apfelkuchen mi`
finds every `Apfelkuchen…` and puts the title that continues with `mi` first.

**The expression is §8.2's, with one simplification that gives the same hits.**

| Input | Expression |
|---|---|
| several words | `(s_norm : ("apfel" AND "kuch")) OR (s_compact : "apfelkuch")` |
| no word of three characters | `s_compact : "abcd"` |
| **one word, no separator** | **`s_compact : "apfelkuch"`** (§8.2 would give `(s_norm : …) OR (s_compact : …)`) |

For one word, everything `s_norm` holds `s_compact` holds too: the separators are only taken out *between* the
letters, a word has none, so a substring stays a substring — and `s_compact` holds more (`apfelkuch` in
`Apfel Kuchen`). A test checks it on 1,500 random titles and over 150 inputs: the two arms and the compact column
return the same rows. FTS5 walks one posting list instead of two.

**The prefix path decides on letters, not on characters.** For a multi-word index "fewer than three" counts the
compact form (`a b` is two letters), and the key of the prefix columns is the first two code points of the
typed text. The prefix path still finds the beginning of the *title*, not of every word (§21 O1).

**N-gram Dice is our own.** `F23.SorensenDice` builds a dictionary per comparison — about 6.5 KB, as §11 says —
and a query compares up to 500 candidates. Measured at 200,000 titles: **3,651 KB** allocated at the 95th
percentile (target ≤ 250 KB). `Similarity.NgramDice` is now a sorted array of bigrams on the stack, the same
definition (the *sets* of bigrams, runs of white space as one space): **263 KB** at p95, p50 1.83 → 1.58 ms. A test
compares it with `SorensenDice(2)` on 10,000 random pairs (whitespace, umlauts, tabs) and on a long text, to 1e-12,
and checks that a call allocates nothing. The other three measures stay on `F23.StringSimilarity` (ADR-0004).

## Measurements

Titles of two to four words from the German word list (average 36 characters), weights as in
`docs/benchmarks.md`, inputs: two thirds the beginning of a title or a piece of it (1–8 characters), one third two
word beginnings of 3–5 letters in either order. Row-by-row build through the triggers, as in ADR-0010:

| Entries | build | file | query p50 / p95 | alloc p50 / p95 | write p50 / p95 |
|---:|---:|---:|---:|---:|---:|
| 200,000 | 33 s | 126 MB | 1.64 / 4.9 ms | 46 / 263 KB | 0.31 / 1.9 ms |
| 1,000,000 | 180 s | 619 MB | 4.16 / 21.0 ms | 83 / 273 KB | 0.35 / 4.8 ms |

Against §16: the query holds at 200 k and misses at 1 M (the slowest queries are two-word inputs of frequent trigrams,
e.g. `Rent Schwe`, 54 ms for 38 candidates — FTS5 walking posting lists, the cause of ADR-0010); the file is **2×
the target of 300 MB per million** (the titles are long: 36 characters against 25 for a recipe title, and
the search index holds every title twice); the build of 3 M would take about nine minutes against three.

## Proposed — the owner decides: index `s_compact` only

The compact column holds everything a multi-word query needs: `s_compact : ("apfel" AND "kuch")` finds every title
whose letters, separators aside, hold both words. The specification's second column (`s_norm`) buys only one
distinction — a word that must not run across a word boundary in the title — and the ranking already makes that
distinction (word start on the compact form). Measured on 500,000 titles, built in rowid order and merged, the
candidate statement over the same 2,000 inputs (`MultiWordLayoutExperiment`):

| FTS5 index | build | file | p50 | p95 | rows per query |
|---|---:|---:|---:|---:|---:|
| `s_norm` + `s_compact`, §8.2 | 21.3 s | 241 MB | 1.26 ms | 5.99 ms | 190 |
| same, one word only in `s_compact` (built here) | 19.7 s | 241 MB | 1.20 ms | 5.65 ms | 190 |
| **`s_compact` only, `s_compact : ("w1" AND "w2")`** | **12.9 s** | **155 MB** | **0.83 ms** | **4.09 ms** | 191 |

−36 % file, −40 % build, −30 % latency for one more candidate in 190: a title whose two query words each run across
a title boundary but not next to each other (`elk ort` in `Apfel Kuchen Torte`). It changes §7.4 and §8.2, so it is
not done here. If you take it, `s_norm` stays a column of the content table (the prefix columns and the ranking
use it) and leaves the search index; `Ensure()` would see a different definition.

## Consequences

- `MultiWord` ships as specified, measured, with its misses written down: the 1 M query p95 and the file size
  (ADR-0010 for the first; this ADR for the second).
- The bulk build of PR-007 (rowid order, one merge) is the lever for the build time and the query tail
  here too; the 500 k experiment above is already built that way.
- `ITextSimilarity` implementations may be called from many threads; `NgramDice` keeps no state.
