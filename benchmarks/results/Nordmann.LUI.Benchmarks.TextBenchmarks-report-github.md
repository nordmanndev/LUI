```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-19, the measurements per case: 13–57
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
  [Disk]     : ext4, local — /tmp/lui-bench.leE5Mc/tmp
```
| Method | Text | Mean | CI 99.9 % | StdDev | Gen0 | Allocated | Hits |
|------------ |--------------------- |----------:|---------:|---------:|-------:|----------:|---:|
| SearchForm | Apfelkuchen | 46.64 ns | 0.980 ns | 1.840 ns | 0.0005 | 144 B | — |
| JaroWinkler | Apfelkuchen | 46.61 ns | 0.959 ns | 0.801 ns | - | - | — |
| NgramDice | Apfelkuchen | 97.39 ns | 1.580 ns | 1.319 ns | - | - | — |
| SearchForm | Gedec(...)useln [35] | 110.36 ns | 2.260 ns | 3.899 ns | 0.0007 | 240 B | — |
| JaroWinkler | Gedec(...)useln [35] | 69.94 ns | 1.397 ns | 1.913 ns | - | - | — |
| NgramDice | Gedec(...)useln [35] | 240.97 ns | 4.597 ns | 9.992 ns | - | - | — |
| SearchForm | Größe(...)nahme [23] | 109.29 ns | 2.154 ns | 2.212 ns | 0.0012 | 320 B | — |
| JaroWinkler | Größe(...)nahme [23] | 71.28 ns | 1.460 ns | 2.439 ns | - | - | — |
| NgramDice | Größe(...)nahme [23] | 163.47 ns | 3.304 ns | 6.824 ns | - | - | — |
