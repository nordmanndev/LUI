```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


  [Run]      : 2026-11-19, the measurements per case: 13–100
  [Machine]  : Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
  [Journal]  : wal
```
| Method | Text | Mean | Error | StdDev | Median | Gen0 | Allocated | Hits |
|------------ |--------------------- |----------:|---------:|---------:|----------:|-------:|----------:|---:|
| SearchForm | Apfelkuchen | 45.57 ns | 0.906 ns | 1.910 ns | 45.18 ns | 0.0005 | 144 B | — |
| JaroWinkler | Apfelkuchen | 41.65 ns | 0.802 ns | 1.015 ns | 41.54 ns | - | - | — |
| NgramDice | Apfelkuchen | 97.93 ns | 1.921 ns | 2.135 ns | 98.51 ns | - | - | — |
| SearchForm | Gedec(...)useln [35] | 105.44 ns | 2.171 ns | 2.823 ns | 105.63 ns | 0.0008 | 240 B | — |
| JaroWinkler | Gedec(...)useln [35] | 72.27 ns | 1.456 ns | 2.042 ns | 71.52 ns | - | - | — |
| NgramDice | Gedec(...)useln [35] | 233.50 ns | 3.137 ns | 2.780 ns | 233.82 ns | - | - | — |
| SearchForm | Größe(...)nahme [23] | 111.81 ns | 2.313 ns | 6.821 ns | 109.97 ns | 0.0012 | 320 B | — |
| JaroWinkler | Größe(...)nahme [23] | 68.86 ns | 0.920 ns | 0.768 ns | 68.55 ns | - | - | — |
| NgramDice | Größe(...)nahme [23] | 161.88 ns | 2.108 ns | 1.760 ns | 162.31 ns | - | - | — |
