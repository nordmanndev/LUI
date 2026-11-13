# 0011 — The multi-word index: one indexed column, one expression, a Dice that allocates nothing

> **Name note (ADR-0023, 2026-11-04):** since this record was written, `Owned` is called `Embedded`: `ILuiOwnedIndex` is
> `ILuiEmbeddedIndex`, `LuiIndexBuilder` is `LuiEmbeddedIndexBuilder`, and `OwnedIndex`, `OwnedSource`, `OwnedSync` and
> `OwnedCheck` are `Embedded…`. The text below is history and stays as it was written.

> **Note (ADR-0061, 2026-11-13, issue #125):** the specification this record was written against is deleted.
> Every section of it has a home — the ADR or the page that carries the same rule; the mapping is in
> [ADR-0061](./0061-the-specification-is-deleted-and-every-section-of-it-has-a-home.md). The sentences below
> that name the specification are history; the rule they carry lives in the ADR named beside them.

- **Status:** Accepted — changes ADR-0011 and ADR-0008 of the specification (corrected in the same pull request). The file size
  target of ADR-0020 is stated anew by the owner (2026-11-04, ADR-0020): ≤ 500 MB for one million titles of 36 characters on
  average (measured 445–457 MB)
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `src/Nordmann.LUI/Indexing/OwnedSchema.cs`, `src/Nordmann.LUI/Querying/PreparedQuery.cs`,
  `src/Nordmann.LUI/LuiIndexBuilder.cs`, `src/Nordmann.LUI/Similarity/Similarity.cs`

## Context

Step 4 of the plan: `MultiWord` for names and titles. The input is split into words that are ANDed; the order of
the words and the spaces do not matter (ADR-0014). The specification fixes the schema (ADR-0006: the generated column
`s_compact`; ADR-0011: the search index over `s_norm` and `s_compact`), the candidate expression (ADR-0008) and the
ranking (ADR-0008). What it leaves open is where the rules meet real input, and what they cost.

## Decision

**The search index holds `s_compact` and nothing else.** A multi-word index adds the generated column `s_compact` (the
transliterated text with the separators taken out, `SqlNormalizer.Compact`, held to the C# `Normalizer` by the parity
test) next to `s_norm`; the FTS5 table indexes `s_compact` alone, and the three triggers carry that one column.
`s_norm` stays a column of the table — the prefix columns and the ranking use it. The kind is part of the stored
definition: the same name as a single-word index is a `LuiDefinitionMismatchException`.

The specification (Entwurf 2) indexed `s_norm` **and** `s_compact` and searched
`(s_norm : (w1 AND w2)) OR (s_compact : "w1w2")`. Measured on 500,000 titles (built in rowid order and merged, the
candidate statement over the same 2,000 inputs, `MultiWordLayoutExperiment`):

| FTS5 index | build | file | p50 | p95 | rows per query |
|---|---:|---:|---:|---:|---:|
| `s_norm` + `s_compact`, ADR-0008 as drafted | 21.3 s | 241 MB | 1.26 ms | 5.99 ms | 190 |
| **`s_compact` only, `s_compact : ("w1" AND "w2")`** | **12.9 s** | **155 MB** | **0.83 ms** | **4.09 ms** | 191 |

−36 % file, −40 % build, −30 % latency, for one more candidate in 190. Why nothing is lost: a title that holds a word
in `s_norm` holds it in `s_compact` too (the separators are taken out between the letters, never inside a word), and a
title that holds `w1w2` holds both words. So `s_compact : ("w1" AND "w2")` finds everything both arms found. What it adds:
titles whose words run across a boundary of the title — `elk ort` in „Apfel Kuchen Torte" — which the compact arm of the
draft already found for a single word. They have no word start and the ranking puts them behind every title that has one.
A test checks it on 1,500 random titles and 600 inputs: the answer equals "every title whose compact text holds every
word" and contains the draft's set.

**The expression** (the specification's ADR-0011 and ADR-0008 are corrected with it):

| Input | Expression |
|---|---|
| several words | `s_compact : ("apfel" AND "kuch")` |
| one word | `s_compact : "apfelkuch"` |
| no word of three characters | `s_compact : "abcd"` (the whole input, compact) |

A change of the separators alone („Apfel Kuchen" to „Apfel-Kuchen") does not change `s_compact` and leaves the search index
untouched (the `WHEN` clause of the update trigger).

**The words of the input are the runs between separators** of the search form (`apfel-zimt kuchen` is three
words), the same separator set the compact form uses. Words of three or more code points go into the AND;
shorter ones stay out of the expression and count in the ranking through the compact form (ADR-0008) — `apfelkuchen mi`
finds every `Apfelkuchen…` and puts the title that continues with `mi` first.

**The prefix path reads the compact text.** For a multi-word index "fewer than three" counts the letters of the compact
form, and the prefix columns `s_p1`/`s_p2` are built from `s_compact`, the key from the compact input: `ab`, `a b`,
`a-b`, `a.b` and `-a b` give the same answer, and `xy` finds „x-y Test" (review of pull request 8, M1; the first version
built the columns from `s_norm` and the key from the typed text, and answered `a-b` and `xy` with nothing). The prefix
path still finds the beginning of the *title*, not of every word (ADR-0003 O1), and not the beginnings of several short
words (ADR-0003 O10: `an mü` is searched as `anmu`). An input of separators alone is empty.

**At most eight words go into the expression** — the eight longest of three or more code points, in the order they were
typed; the others count in the ranking through the compact form. Every word is a phrase with posting lists of its own,
and a hundred words must not be a hundred lists to walk (review M3; ADR-0008 says so now).

**`s_norm` is a `VIRTUAL` column of a multi-word index.** After the changes above nothing reads it but `s_compact`,
which derives from it. A stored copy of every title was a tenth of the file (200,000 titles: 99 → 91 MB; build and query
unchanged). It answers O2 for this index kind; a single-word index still stores it, FTS5 and the prefix columns use it.

**N-gram Dice is our own.** `F23.SorensenDice` builds a dictionary per comparison — about 6.5 KB, as ADR-0030 says —
and a query compares up to 500 candidates. Measured at 200,000 titles: **3,651 KB** allocated at the 95th
percentile (target ≤ 250 KB). `Similarity.NgramDice` is now a sorted array of bigrams on the stack, the same
definition (the *sets* of bigrams, runs of white space as one space): **263 KB** at p95, p50 1.83 → 1.58 ms. A test
compares it with `SorensenDice(2)` on 10,000 random pairs (whitespace, umlauts, tabs) and on a long text, to 1e-12,
and checks that a call allocates nothing. The other three measures stay on `F23.StringSimilarity` (ADR-0004).

## Measurements

Titles of two to four words from the German word list (average 36 characters), weights as in
`docs/benchmarks.md`, inputs: two thirds the beginning of a title or a piece of it (1–8 characters), one third two
word beginnings of 3–5 letters in either order. Row-by-row build through the triggers, as in ADR-0010. The rows
without „virtual" are the stages of this pull request before the review (two indexed columns, then one):

| Entries | index | build | file | query p50 / p95 | alloc p50 / p95 | write p50 / p95 |
|---:|---|---:|---:|---:|---:|---:|
| 200,000 | two columns | 33 s | 126 MB | 1.64 / 4.9 ms | 46 / 263 KB | 0.31 / 1.9 ms |
| 200,000 | `s_compact` only | 28 s | 99 MB | 1.29 / 3.4 ms | 47 / 263 KB | 0.23 / 1.3 ms |
| 200,000 | **`s_compact` only, `s_norm` virtual** | **30 s** | **91 MB** | **1.26 / 3.4 ms** | 47 / 263 KB | 0.24 / 1.5 ms |
| 1,000,000 | two columns | 180 s | 619 MB | 4.16 / 21.0 ms | 83 / 273 KB | 0.35 / 4.8 ms |
| 1,000,000 | `s_compact` only | 155 s | 483 MB | 3.13 / 14.8 ms | 83 / 273 KB | 0.32 / 6.2 ms |
| 1,000,000 | **`s_compact` only, `s_norm` virtual** | **157 s** | **445 MB** | **3.16 / 14.1 ms** | 83 / 273 KB | 0.32 / 5.7 ms |

Against ADR-0020: the query holds at 200 k and misses at 1 M (the slowest queries are two-word inputs of frequent trigrams,
e.g. `Rent Schwe`, 54 ms for 38 candidates — FTS5 walking posting lists, the cause of ADR-0010); the file is still **1.5×
the target of 300 MB per million** (the titles are long: 36 characters against 25 for a recipe title, and the content
table holds the text and `s_compact` besides the search index); the build of 3 M would take about eight minutes
against three. The bulk build of PR-007 (rowid order, one merge) is the lever for the build and the tail, as for the
single-word index.

## Consequences

- `MultiWord` ships measured, with its misses written down: the 1 M query p95 and the file size (ADR-0010 for the
  first; this ADR for the second).
- The content table keeps `s_norm` although the search index does not use it: the prefix columns and the ranking read
  it. If the file size ever matters more than the generated column, `s_norm` can become a virtual column.
- `ITextSimilarity` implementations may be called from many threads; `NgramDice` keeps no state.

## Note (2026-11-08, issue #76)

The experiment of this decision — `MultiWordLayoutExperiment` — was a spike, and it is gone from the tree with the restructure of the
tests (epic #72). Its numbers stay above; its code is in the history:

```bash
git show 0cbc02fb9ab700ee5d7585e590f9bfad60af3627:tests/Nordmann.LUI.Integration.Tests/MultiWordLayoutExperiment.cs
```

