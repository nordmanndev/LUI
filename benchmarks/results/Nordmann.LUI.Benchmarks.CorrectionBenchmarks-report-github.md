```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 12 (bookworm)
Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-13, the measurements per case: 36–72
  [Machine]  : Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
  [Journal]  : wal
```
| Method | Corpus | Entries | Input | Candidates | Order | Similarity | Correction | Source | Mean | Error | StdDev | Median | Gen0 | Allocated | Hits |
|------- |------- |-------- |--------- |----------- |---------- |------------ |----------- |--------- |----------:|---------:|----------:|----------:|-------:|----------:|---:|
| Search | words | 200000 | bahmhof | 500 | Start/Sim | JaroWinkler | True | Embedded | 57.91 μs | 1.156 μs | 2.857 μs | 56.86 μs | 0.2441 | 5.52 KB | 0 |
| Search | words | 200000 | fusbal | 500 | Start/Sim | JaroWinkler | True | Embedded | 125.30 μs | 2.447 μs | 4.088 μs | 124.44 μs | 0.2441 | 5.52 KB | 0 |
| Search | words | 200000 | kartofel | 500 | Start/Sim | JaroWinkler | True | Embedded | 281.90 μs | 5.553 μs | 11.833 μs | 280.21 μs | - | 5.53 KB | 0 |
| Search | words | 200000 | strasee | 500 | Start/Sim | JaroWinkler | True | Embedded | 329.98 μs | 6.509 μs | 12.695 μs | 326.94 μs | - | 5.52 KB | 0 |
