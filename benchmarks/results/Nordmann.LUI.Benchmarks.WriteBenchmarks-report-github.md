```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-19, the measurements per case: 88–100
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
```
| Method | Entries | Mean | Error | StdDev | Median | Allocated | Hits |
|------------------ |-------- |---------:|---------:|---------:|---------:|----------:|---:|
| UpdateWeight | 200000 | 319.5 μs | 14.99 μs | 44.20 μs | 315.1 μs | 2.73 KB | — |
| UpsertTheSame | 200000 | 121.3 μs | 2.32 μs | 6.38 μs | 121.9 μs | 3.96 KB | — |
| UpsertAnotherText | 200000 | 288.4 μs | 26.24 μs | 75.72 μs | 312.5 μs | 3.96 KB | — |
