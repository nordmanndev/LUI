# The latest run of the lane

Written by the lane of the schedule ([ADR-0041](../.agents/adr/0041-the-budgets-of-16-run-on-a-schedule.md)); not by a person.
A number of this file is the number of **this** run — its date, its iteration and its disk stand below.

| | |
|---|---|
| Date | 2026-11-19 (UTC) |
| Iteration | run 9, attempt 1 |
| Disk | ext2/ext3, local |
| Machine of the scenario | other |
| Scenario | prefixLength 3000000 |
| Run | https://github.com/nordmanndev/LUI/actions/runs/44520325667 |
| Commit | b42b01319f7f8fb9841da1c81a28331d66469f1d (refs/heads/feat/177-the-lane-writes-its-numbers) |
| Budgets | passed |

```text
scenario: words.txt (685,739 entries, prefix 685,739), 3000000 entries of the corpus, batch 1,000,000, minimum weight 0, machine Other
  4 cores, .NET 10.0.15
typo dictionary of 685,739 words: 3.7 s, 194 MB
scenario: words.txt (1,908,798 entries, prefix 3,000,000), 3000000 entries of the corpus, batch 1,000,000, minimum weight 0, machine Other
  4 cores, .NET 10.0.15
build: 1,908,798 entries by Sync (batches of 1,000,000) in 67.0 s, file 415 MB
  alloc, budget of candidates spent (1,247 queries): p50 41 KB, p95 45 KB; the rest (753): p50 16 KB, p95 37 KB
query: p50 0.87 ms, p95 3.10 ms, max 10.3 ms; alloc p50 20 KB, p95 44 KB
  1 chars: p50 0.16 ms, p95 0.21 ms (251 queries)
  2 chars: p50 0.16 ms, p95 0.23 ms (256 queries)
  3 chars: p50 0.63 ms, p95 0.99 ms (248 queries)
  4 chars: p50 0.97 ms, p95 1.73 ms (254 queries)
  5 chars: p50 1.23 ms, p95 2.62 ms (248 queries)
  6 chars: p50 1.41 ms, p95 3.58 ms (250 queries)
  7 chars: p50 1.77 ms, p95 4.52 ms (259 queries)
  8 chars: p50 1.81 ms, p95 4.09 ms (234 queries)
typo dictionary of 1,908,798 entries: 7.1 s
  alloc, budget of candidates spent (328 queries): p50 56 KB, p95 112 KB; the rest (672): p50 23 KB, p95 55 KB
corrected query: p50 2.70 ms, p95 8.37 ms, max 23.1 ms; alloc p50 31 KB, p95 88 KB; 874 of 1,000 corrected
  4 chars: p50 1.17 ms, p95 4.42 ms (50 queries)
  5 chars: p50 3.07 ms, p95 6.77 ms (139 queries)
  6 chars: p50 2.54 ms, p95 7.46 ms (211 queries)
  7 chars: p50 2.49 ms, p95 8.99 ms (215 queries)
  8 chars: p50 2.99 ms, p95 9.56 ms (193 queries)
  9 chars: p50 3.11 ms, p95 9.46 ms (146 queries)
  10 chars: p50 2.63 ms, p95 7.52 ms (46 queries)
scenario: wiki-titles.tsv (1,000,000 entries, prefix 1,000,000), 3000000 entries of the corpus, batch 1,000,000, minimum weight 0, machine Other
  4 cores, .NET 10.0.15
titles: 1,000,000 by Sync in 51.5 s, file 243 MB
scenario: words.txt (1,908,798 entries, prefix 3,000,000), 3000000 entries of the corpus, batch 1,000,000, minimum weight 0, machine Other
  4 cores, .NET 10.0.15
build: baseline 66.1 s, with a log of 100,000 pages 67.3 s, baseline again 67.3 s
the log of the file: 229 MB without the lever, 424 MB with it
```
