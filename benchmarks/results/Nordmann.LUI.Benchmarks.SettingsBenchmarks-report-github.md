```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-19, the measurements per case: 12–100
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
```
| Method | Corpus | Entries | Input | Candidates | Order | Similarity | Correction | Source | Mean | Error | StdDev | Median | Allocated | Hits |
|------- |------- |-------- |------ |----------- |----------- |------------ |----------- |--------- |-----------:|---------:|----------:|-----------:|----------:|---:|
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Embedded | 581.9 μs | 11.43 μs | 22.83 μs | 580.7 μs | 41.49 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | True | Embedded | 572.1 μs | 11.20 μs | 29.30 μs | 562.8 μs | 41.49 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | Levenshtein | False | Embedded | 622.3 μs | 11.40 μs | 26.19 μs | 615.3 μs | 41.49 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | Levenshtein | True | Embedded | 615.0 μs | 12.22 μs | 16.31 μs | 614.9 μs | 41.49 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | JaroWinkler | False | Embedded | 473.2 μs | 8.36 μs | 11.16 μs | 472.6 μs | 40.75 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | JaroWinkler | True | Embedded | 526.2 μs | 10.33 μs | 19.66 μs | 520.8 μs | 40.75 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | Levenshtein | False | Embedded | 494.6 μs | 9.69 μs | 13.90 μs | 492.3 μs | 40.75 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | Levenshtein | True | Embedded | 533.7 μs | 10.54 μs | 27.59 μs | 524.2 μs | 40.75 KB | 500 |
| Search | words | 200000 | haus | 2000 | Start/Sim | JaroWinkler | False | Embedded | 1,440.0 μs | 34.64 μs | 102.13 μs | 1,422.8 μs | 184.68 KB | 1090 |
| Search | words | 200000 | haus | 2000 | Start/Sim | JaroWinkler | True | Embedded | 1,533.9 μs | 42.42 μs | 125.08 μs | 1,576.0 μs | 184.68 KB | 1090 |
| Search | words | 200000 | haus | 2000 | Start/Sim | Levenshtein | False | Embedded | 1,545.7 μs | 31.21 μs | 92.02 μs | 1,570.3 μs | 184.68 KB | 1090 |
| Search | words | 200000 | haus | 2000 | Start/Sim | Levenshtein | True | Embedded | 1,562.6 μs | 53.37 μs | 157.36 μs | 1,532.1 μs | 184.68 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | JaroWinkler | False | Embedded | 1,127.2 μs | 20.94 μs | 20.57 μs | 1,129.3 μs | 183.82 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | JaroWinkler | True | Embedded | 1,371.2 μs | 47.21 μs | 139.20 μs | 1,333.2 μs | 183.82 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | Levenshtein | False | Embedded | 1,307.2 μs | 12.74 μs | 9.95 μs | 1,305.6 μs | 183.82 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | Levenshtein | True | Embedded | 1,433.8 μs | 44.20 μs | 130.32 μs | 1,449.7 μs | 183.82 KB | 1090 |
