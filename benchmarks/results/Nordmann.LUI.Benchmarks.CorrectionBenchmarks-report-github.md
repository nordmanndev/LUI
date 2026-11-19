```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-19, the measurements per case: 13–76
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
  [Disk]     : ext4, local — /tmp/lui-bench.leE5Mc/tmp
```
| Method | Corpus | Entries | Input | Candidates | Order | Similarity | Correction | Source | Mean | Error | StdDev | Median | Allocated | Hits |
|------- |------- |-------- |--------- |----------- |---------- |------------ |----------- |--------- |-----------:|---------:|---------:|-----------:|----------:|---:|
| Search | words | 200000 | bahmhof | 500 | Start/Sim | JaroWinkler | True | Embedded | 276.8 μs | 5.51 μs | 9.05 μs | 276.3 μs | 25.53 KB | 99 |
| Search | words | 200000 | fusbal | 500 | Start/Sim | JaroWinkler | True | Embedded | 561.9 μs | 9.36 μs | 7.81 μs | 560.8 μs | 27.98 KB | 118 |
| Search | words | 200000 | kartofel | 500 | Start/Sim | JaroWinkler | True | Embedded | 591.6 μs | 11.83 μs | 30.11 μs | 579.0 μs | 23.71 KB | 72 |
| Search | words | 200000 | strasee | 500 | Start/Sim | JaroWinkler | True | Embedded | 1,320.7 μs | 26.40 μs | 52.73 μs | 1,324.7 μs | 42.86 KB | 402 |
