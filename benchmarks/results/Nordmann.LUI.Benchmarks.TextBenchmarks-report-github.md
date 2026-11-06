```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 12 (bookworm)
Intel Xeon 6982P-C 3.80GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method      | Text                 | Mean      | Error    | StdDev   | Gen0   | Allocated |
|------------ |--------------------- |----------:|---------:|---------:|-------:|----------:|
| **SearchForm**  | **Apfelkuchen**          |  **43.72 ns** | **0.408 ns** | **0.318 ns** | **0.0005** |     **144 B** |
| JaroWinkler | Apfelkuchen          |  41.64 ns | 0.861 ns | 1.719 ns |      - |         - |
| NgramDice   | Apfelkuchen          |  95.53 ns | 1.229 ns | 1.416 ns |      - |         - |
| **SearchForm**  | **Gedec(...)useln [35]** |  **97.19 ns** | **1.462 ns** | **1.564 ns** | **0.0008** |     **240 B** |
| JaroWinkler | Gedec(...)useln [35] |  78.58 ns | 1.509 ns | 1.260 ns |      - |         - |
| NgramDice   | Gedec(...)useln [35] | 251.99 ns | 5.046 ns | 4.720 ns |      - |         - |
| **SearchForm**  | **Größe(...)nahme [23]** | **100.69 ns** | **2.073 ns** | **3.842 ns** | **0.0012** |     **320 B** |
| JaroWinkler | Größe(...)nahme [23] |  70.50 ns | 1.447 ns | 1.931 ns |      - |         - |
| NgramDice   | Größe(...)nahme [23] | 182.70 ns | 3.655 ns | 6.005 ns |      - |         - |
