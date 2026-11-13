```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 12 (bookworm)
Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-13, the measurements per case: 13–33
  [Machine]  : Intel Xeon Platinum 2.50GHz, 1 CPU, 2 logical and 2 physical cores
  [Journal]  : wal
```
| Method | Text | Mean | Error | StdDev | Gen0 | Allocated | Hits |
|------------ |--------------------- |----------:|---------:|---------:|-------:|----------:|---:|
| SearchForm | Apfelkuchen | 50.28 ns | 1.072 ns | 1.571 ns | 0.0083 | 144 B | — |
| JaroWinkler | Apfelkuchen | 56.23 ns | 1.155 ns | 1.832 ns | - | - | — |
| NgramDice | Apfelkuchen | 140.19 ns | 2.634 ns | 4.177 ns | - | - | — |
| SearchForm | Gedec(...)useln [35] | 113.71 ns | 2.311 ns | 3.163 ns | 0.0138 | 240 B | — |
| JaroWinkler | Gedec(...)useln [35] | 92.13 ns | 1.582 ns | 1.480 ns | - | - | — |
| NgramDice | Gedec(...)useln [35] | 330.39 ns | 6.620 ns | 8.608 ns | - | - | — |
| SearchForm | Größe(...)nahme [23] | 113.51 ns | 2.237 ns | 3.061 ns | 0.0185 | 320 B | — |
| JaroWinkler | Größe(...)nahme [23] | 93.69 ns | 1.479 ns | 1.235 ns | - | - | — |
| NgramDice | Größe(...)nahme [23] | 227.96 ns | 4.578 ns | 6.566 ns | - | - | — |
