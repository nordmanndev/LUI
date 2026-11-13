# 0010 — The query budget of ADR-0020 does not hold at three million entries on FTS5 alone

> **Note (ADR-0057, 2026-11-12, issue #121):** the German phrase of the record was translated into English. The
> specification is German and stays German until it is deleted in pull request 8; the decision stands as it was
> written; the text below is history.

- **Status:** Accepted — owner's decision of 2026-11-04: option 2 (the budget per input length) now, option 1 (a real
  stock) when there is one; option 3 only when a real stock asks for it
- **Date:** 2026-11-02
- **Supersedes:** —
- **Affects:** `docs/benchmarks.md`, ADR-0020, the plan for PR-009

## Context

ADR-0020 sets the budget for a query without correction: **p50 ≤ 2 ms, p95 ≤ 5 ms
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
  or titles. ADR-0020's own numbers (Anhang A1, A5) were taken on a different
  synthetic corpus, "as an order of magnitude" (Anhang A).

## Follow-up measurement (same day, after review)

Question asked: *are the indexes and the ordering right, and why does the
specification's measurement not reproduce?* Checked, at 1,000,000 entries,
the same texts in four FTS5 indexes (warm, best of 7 runs, `LIMIT 501`,
`ORDER BY rowid`, milliseconds):

| Index | `gescher` | `verscha` | `resche` | `rausch` | `kuchen` | built in |
|---|---:|---:|---:|---:|---:|---:|
| plain, dense rowid 1…N, bulk-built | 6.9 | 5.4 | 5.4 | 4.0 | 2.6 | 8 s |
| packed rowid, bulk-built in rowid order | 8.5 | 7.4 | 6.5 | 4.5 | 2.8 | 11 s |
| packed rowid, same + `optimize` (1 segment) | 7.5 | 6.4 | 6.4 | 6.4 | 3.4 | +2 s |
| **packed rowid, row by row through the triggers (this PR)** | **25.4** | **19.6** | **20.5** | **12.5** | **7.0** | **82 s** |

- **The ordering and the indexes are right.** The plan is the specification's
  (`INDEX 64`, no `TEMP B-TREE`); the packed rowid costs about 20 % against a
  dense one when both are built the same way. Half of the weights being 0
  (Anhang A1) changes the file by 11 % and the latency not at all.
- **What differs is how the FTS5 index came to be.** Inserted row by row, in
  the random rowid order that the weight imposes, it ends in 24 segments
  (every committed write adds a small one; `automerge` only trims them) and
  is 3× slower to read and 8× slower to build than the same index loaded in
  rowid order. The specification's build figures (Anhang A6) are not the
  figures of a row-by-row build either.
- **SQLite settings do not move it.** Cold, single-shot, 2,000 inputs, 1 M
  entries, bulk-built and merged (`FtsLayoutExperiment`): memory map 2 GB or
  off, page cache 64 MB or 512 MB, rollback journal or WAL, database page
  size 4 KB / 16 KB / 64 KB, FTS5 `pgsz` 1000 / 4000 / 16000 / 64000 — all
  within 5 % of each other (p50 0.37–0.40 ms, p95 3.8–4.0 ms for the
  candidate statement), except 64 KB pages, which are slower (p50 0.51 ms).
  The defaults stay. **Read-only connections do not help either**: with
  `Mode=ReadOnly` or `PRAGMA query_only=ON` the same 2,000 queries at 1 M
  entries gave p50 1.04 / 1.01 ms and p95 4.27 / 4.55 ms, against 1.03 ms and
  4.29 ms for an ordinary connection — the difference is the noise of the run.
  (`immutable=1` is not an option for a database that is being written.)

## What the structure decides (bulk-built and merged, cold, single-shot)

Candidate statement over the same inputs and texts; "join" is the lookup of
the text in the content table, which the query needs for the ranking:

| Entries | statement | p50 | p95 | file |
|---:|---|---:|---:|---:|
| 1 M | FTS5 only | 0.39 ms | 4.05 ms | 148 MB |
| 1 M | + join, unique index on `s_rowid` (as built) | 1.09 ms | 4.43 ms | 196 MB |
| 1 M | + join, **covering** unique index `(s_rowid, text)` | **0.80 ms** | 4.39 ms | 212 MB |
| 3 M | FTS5 only | 0.85 ms | 13.9 ms | 562 MB |
| 3 M | + join, unique index on `s_rowid` (as built) | 1.75 ms | 13.6 ms | 723 MB |
| 3 M | + join, **covering** unique index `(s_rowid, text)` | **1.32 ms** | 13.5 ms | 785 MB |

Against the row-by-row index of this pull request at 3 M (p50 3.57 ms, p95
26.5 ms, build 334 s), loading in rowid order and merging gives **p50 1.3–1.8 ms
(target ≤ 2 ✓), p95 13.5 ms (target ≤ 5 ✗), build about 100 s for the search
index and the content table together (target ≤ 3 min ✓)**. At 1 M the budget
holds. What remains at 3 M is the tail of the trigram-dense corpus (the
`gescher` family), which is FTS5's own cost; the covering index takes 0.4 ms
off every query for 60 MB per 3 M entries.

The library's own run on a merged database (1 M, p50 2.65 ms) is still about a
millisecond slower at the median than the harness (prepare per query, pooled
connection, ranking, the hits statement); it has to be measured again once the
bulk path exists.

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
   *start* of what the user wants (E5, ADR-0002). An index on a longer prefix of the
   normalised text, `s_p3 … s_p8` or one `s_p8` column with a range scan, answers
   the word-start candidates in `O(log n + k)` in rowid order — the same trick
   as the prefix path — and FTS5 is asked only for the rest. *Cost: up to six
   more indexes (size, write time) and a second candidate source with its own
   rules for the merge; it reaches the start of the **text**, not of every
   word (ADR-0003 O1).*
4. **Tune the build and the write.** Bulk-load in rowid order, merge the FTS5
   segments once at the end (`automerge`, `optimize`). That is the plan for
   `Sync`/`Rebuild` (PR-007) and the benchmarks (PR-009); it addresses the
   3-minute build and part of the write p95, not the query.

5. **Load in bulk and keep the index merged.** `Sync`/`Rebuild`/`Optimize`
   (PR-007) should fill the content table, then build the search index in rowid
   order and `optimize` it (about 10 s and 2 s per million entries here), and
   `automerge` can be tightened for the steady state. The follow-up
   measurement says this is worth 3× on the warm query and 8× on the build,
   and the cold figures above confirm it for the median and the build.
6. **Spare the index hop of the join.** A covering unique index
   `(s_rowid, text)` saves 0.3–0.4 ms per query in the experiment — on a table
   without generated columns. On the real table SQLite does not use it as a
   covering index (an index that contains a generated column never is; checked
   with `EXPLAIN QUERY PLAN`). **Built instead:** the candidate statement joins
   on `doc_id = (f.rowid & 4294967295)`, a seek by primary key in the table
   itself — the same saved hop, no extra storage. Measured on the row-by-row
   index: 1 M entries p50 2.02 → **1.39 ms**, p95 7.4 → **6.1 ms**; 200 k
   entries p50 0.86 → 0.82 ms, p95 2.2 → 1.8 ms.

## Measured again after the bulk path (PR-009, ADR-0020)

Option 5 is built (`Sync`, PR-007, ADR-0016) and measured in the scenario, on the generated corpus of ADR-0020 (the
word list, then compounds, three million entries, weights with a long tail):

| | row by row (PR-003) | `Sync`, merged (PR-009) | target |
|---|---:|---:|---:|
| query p50 | 3.57 ms | **1.61–1.71 ms** | ≤ 2 ms ✓ |
| query p95 | 26.5 ms | **9.6–10.3 ms** | ≤ 5 ms ✗ |
| build | 334 s | **172.5 s** (batches of 1,000,000) | ≤ 3 min ✓ |
| single write p95 | 2.6 ms | **0.51–0.57 ms** | ≤ 1 ms ✓ |

The p95 by the length of the input: **≤ 3.1 ms up to four characters**, 6.9 ms at five, 17.5 ms at eight. What is left
is FTS5's own cost of a long trigram expression over three million entries — option 2 (a budget per input length) and
option 3 (a word-start structure for 5–8 characters) are the choices that remain, and option 1 (a real stock) the way to
know whether the synthetic corpus is the adversary. The corrected query (p95 29 ms, ADR-0012) follows the same cost.

## Decision

**Decided by the owner on 2026-11-04, on the numbers of PR-009 (ADR-0020):**

- **Option 2 — the budget per input length.** ADR-0020 now reads: p50 ≤ 2 ms; p95 ≤ 5 ms for inputs of 1–4 characters
  (measured ≤ 3.1 ms), ≤ 20 ms for 5–8 characters (measured 17.5 ms at eight); the corrected query p95 ≤ 35 ms (measured
  28–29 ms). For the long inputs and the correction this is a **loosening** of the target, written down as such. The
  reason: a search field waits 100–300 ms between two keys; 20 ms are not felt there, and the median meets the old
  target.
- **Option 1 — a real stock.** When there is one, the scenario is measured on it; the synthetic corpus is trigram-dense
  on purpose and may be the adversary no application has.
- **Option 3 — a word-start structure for 5–8 characters — not now.** Up to six more indexes cost writes and file size;
  it is built when a real stock shows that the loosened budget is not enough.

What follows is the record of how the decision came about.

**Before the decision: Proposed — the owner decides.** Options 1 and 5 are cheap and should come first (the original probe of the specification would settle both at once); if
a real stock confirms the numbers, options 2 and 3 are the real choice. Until
then the pull request reports the numbers as measured and does not claim the
budget. The target in `docs/benchmarks.md` is **not** lowered quietly (its own
rule 5); it is marked as missed, with this record as the reason.

## Consequences

- PR-003 ships a correct single-word index whose measured behaviour is
  written down, not a promise the measurement contradicts.
- PR-004 (`MultiWord`) inherits the same candidate mechanism — and the same
  cost. It makes the decision more pressing: `MultiWord` ANDs several phrases.

## Note (2026-11-08, issue #76)

The experiment of this decision — `FtsLayoutExperiment` — was a spike, and it is gone from the tree with the restructure of the
tests (epic #72). Its numbers stay above; its code is in the history:

```bash
git show 0cbc02fb9ab700ee5d7585e590f9bfad60af3627:tests/Nordmann.LUI.Integration.Tests/FtsLayoutExperiment.cs
```

