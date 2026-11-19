```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-19, the measurements per case: 14–100
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
  [Disk]     : ext4, local — /tmp/lui-bench.leE5Mc/tmp
```
| Method | Corpus | Entries | Input | Candidates | Order | Similarity | Correction | Source | Mean | Error | StdDev | Median | Allocated | Hits |
|------- |------- |-------- |------ |----------- |----------- |------------ |----------- |--------- |-----------:|---------:|----------:|-----------:|----------:|---:|
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Embedded | 587.7 μs | 11.69 μs | 22.80 μs | 579.7 μs | 41.49 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | True | Embedded | 585.8 μs | 11.57 μs | 27.26 μs | 581.8 μs | 41.49 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | Levenshtein | False | Embedded | 624.6 μs | 12.39 μs | 24.75 μs | 616.6 μs | 41.49 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | Levenshtein | True | Embedded | 621.1 μs | 12.39 μs | 30.15 μs | 617.7 μs | 41.49 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | JaroWinkler | False | Embedded | 489.1 μs | 9.74 μs | 24.96 μs | 479.7 μs | 40.75 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | JaroWinkler | True | Embedded | 518.2 μs | 9.33 μs | 9.16 μs | 520.8 μs | 40.75 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | Levenshtein | False | Embedded | 488.0 μs | 9.72 μs | 21.94 μs | 481.7 μs | 40.75 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | Levenshtein | True | Embedded | 541.4 μs | 12.10 μs | 34.92 μs | 531.5 μs | 40.75 KB | 500 |
| Search | words | 200000 | haus | 2000 | Start/Sim | JaroWinkler | False | Embedded | 1,497.6 μs | 33.96 μs | 99.60 μs | 1,492.9 μs | 184.68 KB | 1090 |
| Search | words | 200000 | haus | 2000 | Start/Sim | JaroWinkler | True | Embedded | 1,573.6 μs | 47.77 μs | 140.86 μs | 1,662.8 μs | 184.68 KB | 1090 |
| Search | words | 200000 | haus | 2000 | Start/Sim | Levenshtein | False | Embedded | 1,621.1 μs | 17.95 μs | 15.91 μs | 1,623.9 μs | 184.68 KB | 1090 |
| Search | words | 200000 | haus | 2000 | Start/Sim | Levenshtein | True | Embedded | 1,630.3 μs | 44.72 μs | 131.86 μs | 1,706.2 μs | 184.68 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | JaroWinkler | False | Embedded | 1,212.6 μs | 31.49 μs | 92.84 μs | 1,201.4 μs | 183.82 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | JaroWinkler | True | Embedded | 1,401.5 μs | 42.56 μs | 125.49 μs | 1,381.1 μs | 183.82 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | Levenshtein | False | Embedded | 1,236.7 μs | 31.28 μs | 91.73 μs | 1,215.8 μs | 183.82 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | Levenshtein | True | Embedded | 1,435.7 μs | 41.06 μs | 121.07 μs | 1,428.0 μs | 183.82 KB | 1090 |
