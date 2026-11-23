# The latest run of the lane

Written by the lane of the schedule ([ADR-0041](../.agents/adr/0041-the-budgets-of-16-run-on-a-schedule.md)); not by a person.
A number of this file is the number of **this** run — its date, its iteration and its disk stand below.

| | |
|---|---|
| Date | 2026-11-23 (UTC) |
| Iteration | run 10, attempt 1 |
| Disk | ext2/ext3, local |
| Machine of the scenario | other |
| Scenario | prefixLength 3000000 |
| Run | https://github.com/nordmanndev/LUI/actions/runs/45174027769 |
| Commit | 1b3497248371121c19f2c74ab3f3db4f90891c47 (refs/heads/main) |
| Budgets | passed |

```text
scenario: words.txt (685,739 entries, prefix 685,739), 3000000 entries of the corpus, batch 1,000,000, minimum weight 0, machine Other
  4 cores, .NET 10.0.15
typo dictionary of 685,739 words: 3.8 s, 194 MB
scenario: words.txt (685,739 entries, prefix 685,739), 3000000 entries of the corpus, batch 1,000,000, minimum weight 0, machine Other
  4 cores, .NET 10.0.15
typo dictionary of two errors, 685,739 words: 8.7 s, 388 MB
scenario: words.txt (1,908,798 entries, prefix 3,000,000), 3000000 entries of the corpus, batch 1,000,000, minimum weight 0, machine Other
  4 cores, .NET 10.0.15
build: 1,908,798 entries by Sync (batches of 1,000,000) in 96.1 s, file 415 MB
  alloc, budget of candidates spent (1,247 queries): p50 39 KB, p95 43 KB; the rest (753): p50 15 KB, p95 35 KB
query: p50 0.90 ms, p95 3.41 ms, max 10.7 ms; alloc p50 19 KB, p95 43 KB
  1 chars: p50 0.16 ms, p95 0.21 ms (251 queries)
  2 chars: p50 0.17 ms, p95 0.24 ms (256 queries)
  3 chars: p50 0.66 ms, p95 1.16 ms (248 queries)
  4 chars: p50 0.95 ms, p95 1.79 ms (254 queries)
  5 chars: p50 1.24 ms, p95 2.79 ms (248 queries)
  6 chars: p50 1.45 ms, p95 4.09 ms (250 queries)
  7 chars: p50 1.86 ms, p95 5.65 ms (259 queries)
  8 chars: p50 1.91 ms, p95 4.42 ms (234 queries)
typo dictionary of 1,908,798 entries: 7.4 s
  alloc, budget of candidates spent (328 queries): p50 54 KB, p95 110 KB; the rest (672): p50 22 KB, p95 54 KB
corrected query: p50 2.83 ms, p95 9.18 ms, max 24.7 ms; alloc p50 30 KB, p95 87 KB; 874 of 1,000 corrected
  4 chars: p50 1.12 ms, p95 4.40 ms (50 queries)
  5 chars: p50 2.97 ms, p95 6.94 ms (139 queries)
  6 chars: p50 2.58 ms, p95 7.57 ms (211 queries)
  7 chars: p50 2.58 ms, p95 9.58 ms (215 queries)
  8 chars: p50 3.18 ms, p95 10.12 ms (193 queries)
  9 chars: p50 3.29 ms, p95 10.27 ms (146 queries)
  10 chars: p50 2.80 ms, p95 8.45 ms (46 queries)
scenario: wiki-titles.tsv (1,000,000 entries, prefix 1,000,000), 3000000 entries of the corpus, batch 1,000,000, minimum weight 0, machine Other
  4 cores, .NET 10.0.15
titles: 1,000,000 by Sync in 69.8 s, file 243 MB
scenario: words.txt (1,908,798 entries, prefix 3,000,000), 3000000 entries of the corpus, batch 1,000,000, minimum weight 0, machine Other
  4 cores, .NET 10.0.15
build: baseline 96.0 s, with a log of 100,000 pages 94.0 s, baseline again 95.6 s
the log of the file: 229 MB without the lever, 424 MB with it
```
