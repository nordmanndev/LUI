```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-19, the measurements per case: 91–98
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
  [Disk]     : ext4, local — /tmp/lui-bench.leE5Mc/tmp
```
| Method | Entries | Mean | CI 99.9 % | StdDev | Median | Allocated | Hits |
|------------------ |-------- |---------:|---------:|---------:|---------:|----------:|---:|
| UpdateWeight | 200000 | 334.6 μs | 12.54 μs | 36.38 μs | 337.4 μs | 2.73 KB | — |
| UpsertTheSame | 200000 | 131.0 μs | 2.60 μs | 7.29 μs | 130.4 μs | 3.96 KB | — |
| UpsertAnotherText | 200000 | 306.9 μs | 23.53 μs | 68.63 μs | 325.1 μs | 3.96 KB | — |
