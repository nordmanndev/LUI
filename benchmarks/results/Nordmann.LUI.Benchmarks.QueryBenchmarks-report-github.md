```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-19, the measurements per case: 13–80
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
  [Disk]     : ext4, local — /tmp/lui-bench.leE5Mc/tmp
```
| Method | Corpus | Entries | Input | Candidates | Order | Similarity | Correction | Source | Mean | CI 99.9 % | StdDev | Median | Allocated | Hits |
|------- |------- |-------- |--------- |----------- |---------- |------------ |----------- |--------- |----------:|----------:|----------:|----------:|----------:|---:|
| Search | titles | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Embedded | 80.49 μs | 1.589 μs | 1.561 μs | 80.20 μs | 5.2 KB | 0 |
| Search | titles | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Linked | 81.04 μs | 1.598 μs | 2.241 μs | 80.19 μs | 5.24 KB | 0 |
| Search | titles | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Embedded | 138.73 μs | 2.344 μs | 3.580 μs | 139.02 μs | 14.88 KB | 500 |
| Search | titles | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Linked | 346.38 μs | 6.175 μs | 7.351 μs | 345.38 μs | 43.22 KB | 500 |
| Search | titles | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Embedded | 489.59 μs | 8.890 μs | 14.607 μs | 488.48 μs | 51.03 KB | 500 |
| Search | titles | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Linked | 564.32 μs | 8.047 μs | 7.527 μs | 565.03 μs | 113.76 KB | 500 |
| Search | titles | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Embedded | 139.17 μs | 0.329 μs | 0.292 μs | 139.26 μs | 14.73 KB | 500 |
| Search | titles | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Linked | 366.86 μs | 7.253 μs | 15.613 μs | 363.08 μs | 42.64 KB | 500 |
| Search | words | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Embedded | 116.70 μs | 2.260 μs | 5.834 μs | 114.62 μs | 6.53 KB | 1 |
| Search | words | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Linked | 116.57 μs | 2.299 μs | 2.989 μs | 115.83 μs | 6.57 KB | 1 |
| Search | words | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Embedded | 143.17 μs | 2.768 μs | 7.243 μs | 141.13 μs | 14.63 KB | 500 |
| Search | words | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Linked | 312.43 μs | 5.459 μs | 7.830 μs | 309.12 μs | 39.62 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Embedded | 597.45 μs | 11.865 μs | 26.780 μs | 592.04 μs | 41.49 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Linked | 537.39 μs | 2.658 μs | 2.220 μs | 536.79 μs | 41.59 KB | 500 |
| Search | words | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Embedded | 138.76 μs | 1.600 μs | 1.336 μs | 138.39 μs | 14.57 KB | 500 |
| Search | words | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Linked | 345.86 μs | 6.887 μs | 15.544 μs | 344.34 μs | 39.68 KB | 500 |
