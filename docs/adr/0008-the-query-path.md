# 0008 — The query path: groups, a lazy similarity, and what the result reserves

- **Status:** Accepted
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `src/Nordmann.LUI/Querying/`, `src/Nordmann.LUI/LuiQuery.cs`,
  `src/Nordmann.LUI/LuiQueryOptions.cs`, `src/Nordmann.LUI/Similarity/`

## Context

§8 and §9 describe the query: normalise, read the candidates in the order of
the rowid, rank them, read key and payload of the hits. The specification
measures the hit rates (Anhang A2) and the latency budget (§16: p50 ≤ 2 ms,
p95 ≤ 5 ms, ≤ 250 KB per query). The similarity is the expensive stage —
4.5 µs and 336 B per comparison for Jaro-Winkler (§11) — and 500 candidates
times 4.5 µs is 2.25 ms, more than the whole budget.

## Decision

**The ranking works in groups, and the similarity is computed lazily.** The
cheap stages — priority and word start — put every candidate into one of eight
groups. The groups are visited in rank order; the similarity is computed for
a group only when the hits are not yet full, and each group is sorted by the
remaining stages. A candidate that cannot reach the hits never has its
similarity computed. `WeightOnly` with the priority first is the order the
candidates arrive in, and computes no similarity at all.

**A one- or two-character input is not ranked by similarity.** The prefix
path (§8.1) returns the best entries whose text *begins* with the input; all of
them satisfy the word-start rule, and the similarity of one letter to a word
carries nothing. Anhang B says what the user should see — „the most popular
first" — so the order is the order of the rowid. The hits still carry their
similarity (computed for the ≤ 20 of them), so a caller can show it.

**The similarity is measured from the word where the match begins** (§9.2):
the search form of the text from that word on, or the whole text when there is
no word start. The measure compares the normalised input with that text.

**`DistinctText` works on the candidates, in the order of the rowid.** The
first candidate of a normalised text is the one with the highest priority and
then the highest weight, so keeping the first is exactly §9.2 — and it costs
no second pass. The consequence is that duplicates are dropped *before* the
candidate budget is filled, which is what a caller who hides duplicates wants.

**`Truncated` means the statement had a row beyond `Candidates`.** The
statement asks for `Candidates + 1` rows; the extra row is read and thrown
away.

**The options are bounded where an unbounded value would hurt.** `MaxResults`
is 1 … 1000 (the hits are read with one `IN (…)` statement; SQLite allows
32,766 parameters, but a page of a thousand suggestions is not an
autocomplete), `Candidates` is 1 … 100,000. The default of 500 is the
specification's; raising *the default* needs a decision (AGENTS.md), raising it
for one query does not.

**Two fields of the result are reserved.** `LuiResult.CorrectedQuery` (typo
tolerance, PR-005) and `LuiHit.Highlight` (PR-008) are in the types of §8.3
and are `null` until their steps land. Adding a positional member to a record
later would break every deconstruction; a documented `null` breaks none.

**The type is called `Similarity`, in the namespace `Nordmann.LUI.Similarity`.**
The specification writes `.Similarity(Similarity.NgramDice)` (§11, §12.5), and
the analyzer rule MA0049 forbids a type named like its namespace. The rule is
suppressed on that one declaration, with the reason next to it; the API the
specification promises is worth more than the rule.

## Consequences

- At 200,000 entries a query is p50 0.86 ms, p95 2.2 ms and 58 KB (p50) —
  see `docs/benchmarks.md` for the 3,000,000-entry figures and the machine.
- The ranking can be tested by its groups: the golden queries and the
  four-orders test pin the order, not the arithmetic.
- A custom `ITextSimilarity` is called only for candidates that can still
  reach the hits, and from many threads at once — it must be thread-safe.
  The four shipped measures are stateless.
