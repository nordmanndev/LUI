```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 12 (bookworm)
Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-13, the measurements per case: 23–95
  [Machine]  : Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
  [Journal]  : wal
```
| Method | Corpus | Entries | Input | Candidates | Order | Similarity | Correction | Source | Mean | Error | StdDev | Median | Gen0 | Allocated | Hits |
|------- |------- |-------- |--------- |----------- |---------- |------------ |----------- |--------- |---------:|---------:|---------:|---------:|-------:|----------:|---:|
| Search | titles | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Embedded | 131.5 μs | 2.61 μs | 5.79 μs | 130.2 μs | - | 5.78 KB | 0 |
| Search | titles | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Linked | 133.5 μs | 2.65 μs | 6.30 μs | 132.3 μs | - | 5.83 KB | 0 |
| Search | titles | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Embedded | 495.0 μs | 9.86 μs | 24.55 μs | 491.8 μs | 1.9531 | 43.62 KB | 500 |
| Search | titles | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Linked | 540.3 μs | 10.79 μs | 30.96 μs | 540.5 μs | 1.9531 | 43.76 KB | 500 |
| Search | titles | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Embedded | 736.8 μs | 14.45 μs | 30.17 μs | 738.5 μs | 1.9531 | 51.57 KB | 500 |
| Search | titles | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Linked | 842.5 μs | 16.80 μs | 43.96 μs | 824.8 μs | 3.9063 | 114.3 KB | 500 |
| Search | titles | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Embedded | 525.9 μs | 10.45 μs | 27.90 μs | 518.3 μs | 1.9531 | 43.04 KB | 500 |
| Search | titles | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Linked | 565.3 μs | 11.22 μs | 25.09 μs | 558.1 μs | 1.9531 | 43.18 KB | 500 |
| Search | words | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Embedded | 191.2 μs | 3.78 μs | 8.14 μs | 190.0 μs | - | 7.07 KB | 1 |
| Search | words | 200000 | apfelkuc | 500 | Start/Sim | JaroWinkler | False | Linked | 184.7 μs | 3.69 μs | 4.66 μs | 184.5 μs | - | 7.11 KB | 1 |
| Search | words | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Embedded | 476.4 μs | 9.48 μs | 24.63 μs | 472.6 μs | 1.9531 | 40.02 KB | 500 |
| Search | words | 200000 | b | 500 | Start/Sim | JaroWinkler | False | Linked | 490.5 μs | 9.52 μs | 26.38 μs | 483.4 μs | 1.9531 | 40.16 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Embedded | 809.7 μs | 15.95 μs | 34.33 μs | 803.3 μs | 1.9531 | 42.03 KB | 500 |
| Search | words | 200000 | haus | 500 | Start/Sim | JaroWinkler | False | Linked | 808.9 μs | 15.86 μs | 30.94 μs | 803.7 μs | 1.9531 | 42.13 KB | 500 |
| Search | words | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Embedded | 517.5 μs | 10.29 μs | 26.37 μs | 508.1 μs | 1.9531 | 40.09 KB | 500 |
| Search | words | 200000 | ka | 500 | Start/Sim | JaroWinkler | False | Linked | 519.4 μs | 10.36 μs | 22.75 μs | 514.6 μs | 1.9531 | 40.22 KB | 500 |
