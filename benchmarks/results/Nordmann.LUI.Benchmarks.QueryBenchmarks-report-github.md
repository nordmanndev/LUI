```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-19, the measurements per case: 14–85
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
```
| Method | Corpus | Entries | Input | Candidates | Order | Similarity | Correction | Source | Mean | Error | StdDev | Allocated | Hits |
|------- |------- |-------- |--------- |----------- |---------- |------------ |----------- |--------- |----------:|----------:|----------:|----------:|---:|
| Search | titles | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Embedded | 82.58 μs | 1.641 μs | 3.567 μs | 5.2 KB | 0 |
| Search | titles | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Linked | 81.96 μs | 1.400 μs | 1.667 μs | 5.24 KB | 0 |
| Search | titles | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Embedded | 139.52 μs | 2.655 μs | 2.354 μs | 14.88 KB | 500 |
| Search | titles | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Linked | 338.98 μs | 6.389 μs | 8.529 μs | 43.22 KB | 500 |
| Search | titles | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Embedded | 471.70 μs | 8.726 μs | 7.735 μs | 51.03 KB | 500 |
| Search | titles | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Linked | 561.43 μs | 11.126 μs | 16.991 μs | 113.76 KB | 500 |
| Search | titles | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Embedded | 145.83 μs | 2.827 μs | 7.643 μs | 14.73 KB | 500 |
| Search | titles | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Linked | 377.23 μs | 7.463 μs | 19.921 μs | 42.64 KB | 500 |
| Search | words | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Embedded | 120.07 μs | 2.379 μs | 6.391 μs | 6.53 KB | 1 |
| Search | words | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Linked | 114.99 μs | 2.145 μs | 3.076 μs | 6.57 KB | 1 |
| Search | words | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Embedded | 140.72 μs | 2.746 μs | 4.589 μs | 14.63 KB | 500 |
| Search | words | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Linked | 305.48 μs | 4.567 μs | 5.775 μs | 39.62 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Embedded | 598.66 μs | 11.937 μs | 27.902 μs | 41.49 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Linked | 564.52 μs | 11.252 μs | 24.698 μs | 41.59 KB | 500 |
| Search | words | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Embedded | 143.50 μs | 2.856 μs | 5.502 μs | 14.57 KB | 500 |
| Search | words | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Linked | 341.88 μs | 6.738 μs | 14.791 μs | 39.68 KB | 500 |
