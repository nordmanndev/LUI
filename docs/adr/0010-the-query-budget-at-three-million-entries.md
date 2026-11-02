# 0010 — The query budget of §16 does not hold at three million entries on FTS5 alone

- **Status:** Proposed
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `docs/benchmarks.md`, `docs/spec/LUI-Spezifikation.md` §16, the plan for PR-009

## Context

§16 sets the budget for a query without correction: **p50 ≤ 2 ms, p95 ≤ 5 ms
at 3,000,000 entries, inputs of 1–8 characters**; the build of 3,000,000
entries in ≤ 3 minutes; a single write at p95 ≤ 1 ms. Step 3 of the plan lists
the budget in its "done when".

The single-word index of this pull request was measured against it
(`QueryPerformance`, corpus and machine in `docs/benchmarks.md`). The result
is not what the specification promises:

| entries | query p50 | query p95 | query p95 at 1–2 chars | at 6–8 chars | build | write p95 |
|---:|---:|---:|---:|---:|---:|---:|
| 200,000 | 0.86 ms | 2.2 ms | 1.5 ms | 2.0–2.2 ms | 14 s | 1.0 ms |
| 1,000,000 | 2.02 ms | 7.4 ms | 1.9 ms | 9.0–10.9 ms | 87 s | 5.7 ms |
| 3,000,000 | **3.57 ms** | **26.5 ms** | 2.1 ms | 26–49 ms | **334 s** | **2.6 ms** |

The allocation budget (≤ 250 KB) holds: p50 79 KB, p95 290 KB at 3 M.

## What the measurement says about the cause

It is not the library, and it is not the prefix path.

- **The prefix path and the short inputs hold the budget** at every size
  (1–2 characters: p95 ≈ 2 ms at 3 M). They are an index range scan.
- **The time is FTS5's, spent in the candidate statement.** The slowest queries
  at 3 M spend 99.5 % of their time in it (`endender`: 130.6 of 131.3 ms;
  ranking 0.4 ms, payload 0.3 ms). Removing the join with the content table
  saves about 1 ms of it; rewriting the phrase as an `AND` of trigrams changes
  nothing (`gescher` at 1 M: 15.9 / 15.6 / 15.7 ms for join / no join / `AND`).
- **A phrase of *n* characters is a positional intersection of *n − 2*
  trigram posting lists.** FTS5 walks them in rowid order and stops after
  500 matches. A phrase that has few matches but is made of frequent trigrams
  (`gescher` is `ges`, `esc`, `sch`, `che`, `her`) cannot stop early: it walks
  the lists to the end. The cost follows the *length of the shortest list*
  and not the number of hits — which is why the longer inputs are the slower
  ones here, and why a phrase with a rare trigram (`abendmah`) is fast.
- The corpus makes it worse: 3 M compounds of two random words from the
  German word list are far denser in `sch`/`ung`/`ter`… than a stock of tags
  or titles. §16's own numbers (Anhang A1, A5) were taken on a different
  synthetic corpus, "als Größenordnung" (Anhang A).

## Options

1. **Keep the budget, measure on a real stock.** The synthetic compound corpus
   may be an adversary no application has. A stock of real tags or titles and
   the queries real users typed is the honest test. *Cost: needs the owner's
   data; no code.*
2. **Restate the budget** as p50/p95 *per input length*, with the
   one-to-three-character figures it holds and a stated expectation for longer
   inputs that follows the trigram density of the stock. *Cost: the
   specification changes; a promise gets smaller.*
3. **Add a word-start structure for 3–8 characters.** The input is usually the
   *start* of what the user wants (E5, §1). An index on a longer prefix of the
   normalised text, `s_p3 … s_p8` or one `s_p8` column with a range scan, answers
   the word-start candidates in `O(log n + k)` in rowid order — the same trick
   as the prefix path — and FTS5 is asked only for the rest. *Cost: up to six
   more indexes (size, write time) and a second candidate source with its own
   rules for the merge; it reaches the start of the **text**, not of every
   word (§21 O1).*
4. **Tune the build and the write.** Bulk-load in rowid order, merge the FTS5
   segments once at the end (`automerge`, `optimize`). That is the plan for
   `Sync`/`Rebuild` (PR-007) and the benchmarks (PR-009); it addresses the
   3-minute build and part of the write p95, not the query.

## Decision

**Proposed — the owner decides.** Option 1 is cheap and should come first; if
a real stock confirms the numbers, options 2 and 3 are the real choice. Until
then the pull request reports the numbers as measured and does not claim the
budget. The target in `docs/benchmarks.md` is **not** lowered quietly (its own
rule 5); it is marked as missed, with this record as the reason.

## Consequences

- PR-003 ships a correct single-word index whose measured behaviour is
  written down, not a promise the measurement contradicts.
- PR-004 (`MultiWord`) inherits the same candidate mechanism — and the same
  cost. It makes the decision more pressing: `MultiWord` ANDs several phrases.
