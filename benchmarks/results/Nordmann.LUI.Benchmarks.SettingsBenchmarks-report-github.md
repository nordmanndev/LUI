```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 12 (bookworm)
Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-13, the measurements per case: 27–77
  [Machine]  : Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
  [Journal]  : wal
```
| Method | Corpus | Entries | Input | Candidates | Order | Similarity | Correction | Source | Mean | Error | StdDev | Median | Gen0 | Allocated | Hits |
|------- |------- |-------- |------ |----------- |----------- |------------ |----------- |--------- |-----------:|---------:|---------:|-----------:|-------:|----------:|---:|
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Embedded | 811.6 μs | 16.10 μs | 27.35 μs | 813.0 μs | 1.9531 | 42.03 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | True | Embedded | 827.9 μs | 16.45 μs | 42.16 μs | 814.4 μs | - | 42.03 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | Levenshtein | False | Embedded | 847.7 μs | 16.89 μs | 42.69 μs | 834.2 μs | - | 42.03 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | Levenshtein | True | Embedded | 840.5 μs | 16.72 μs | 42.86 μs | 830.2 μs | - | 42.03 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | JaroWinkler | False | Embedded | 773.0 μs | 14.11 μs | 19.77 μs | 772.9 μs | 1.9531 | 41.29 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | JaroWinkler | True | Embedded | 757.3 μs | 15.07 μs | 26.39 μs | 756.3 μs | 1.9531 | 41.29 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | Levenshtein | False | Embedded | 767.1 μs | 15.31 μs | 33.60 μs | 753.6 μs | 1.9531 | 41.29 KB | 500 |
| Search | words | 200000 | haus | 500 | WeightOnly | Levenshtein | True | Embedded | 765.1 μs | 15.30 μs | 27.59 μs | 759.9 μs | 1.9531 | 41.29 KB | 500 |
| Search | words | 200000 | haus | 2000 | Start/Sim | JaroWinkler | False | Embedded | 1,712.7 μs | 33.40 μs | 58.49 μs | 1,702.5 μs | 7.8125 | 185.22 KB | 1090 |
| Search | words | 200000 | haus | 2000 | Start/Sim | JaroWinkler | True | Embedded | 1,686.9 μs | 33.40 μs | 56.71 μs | 1,683.0 μs | 7.8125 | 185.22 KB | 1090 |
| Search | words | 200000 | haus | 2000 | Start/Sim | Levenshtein | False | Embedded | 1,807.9 μs | 35.87 μs | 87.31 μs | 1,774.8 μs | 7.8125 | 185.22 KB | 1090 |
| Search | words | 200000 | haus | 2000 | Start/Sim | Levenshtein | True | Embedded | 1,810.0 μs | 36.14 μs | 83.76 μs | 1,787.6 μs | 7.8125 | 185.22 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | JaroWinkler | False | Embedded | 1,587.5 μs | 31.42 μs | 78.83 μs | 1,564.5 μs | 7.8125 | 184.36 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | JaroWinkler | True | Embedded | 1,594.7 μs | 31.51 μs | 55.19 μs | 1,591.9 μs | 7.8125 | 184.36 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | Levenshtein | False | Embedded | 1,581.9 μs | 31.31 μs | 70.68 μs | 1,568.9 μs | 7.8125 | 184.36 KB | 1090 |
| Search | words | 200000 | haus | 2000 | WeightOnly | Levenshtein | True | Embedded | 1,602.5 μs | 31.76 μs | 56.46 μs | 1,594.0 μs | 7.8125 | 184.36 KB | 1090 |
