```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-19, the measurements per case: 30–64
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
```
| Method | Corpus | Entries | Input | Candidates | Order | Similarity | Correction | Source | Mean | Error | StdDev | Allocated | Hits |
|------- |------- |-------- |--------- |----------- |---------- |------------ |----------- |--------- |-----------:|---------:|---------:|----------:|---:|
| Search | words | 200000 | bahmhof | 500 | Start/Sim | JaroWinkler | True | Embedded | 265.9 μs | 4.45 μs | 7.43 μs | 25.53 KB | 99 |
| Search | words | 200000 | fusbal | 500 | Start/Sim | JaroWinkler | True | Embedded | 563.6 μs | 9.87 μs | 14.77 μs | 27.98 KB | 118 |
| Search | words | 200000 | kartofel | 500 | Start/Sim | JaroWinkler | True | Embedded | 582.3 μs | 11.63 μs | 26.95 μs | 23.71 KB | 72 |
| Search | words | 200000 | strasee | 500 | Start/Sim | JaroWinkler | True | Embedded | 1,315.1 μs | 24.58 μs | 42.41 μs | 42.9 KB | 402 |
