```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 12 (bookworm)
Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-13, the measurements per case: 12–59
  [Machine]  : Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
  [Journal]  : wal
```
| Method | Entries | Mean | Error | StdDev | Allocated | Hits |
|------------------ |-------- |-----------:|---------:|---------:|----------:|---:|
| UpdateWeight | 200000 | 2,037.0 μs | 40.30 μs | 89.30 μs | 436 B | — |
| UpsertTheSame | 200000 | 699.1 μs | 10.20 μs | 7.96 μs | 820 B | — |
| UpsertAnotherText | 200000 | 2,148.9 μs | 42.38 μs | 81.65 μs | 820 B | — |
