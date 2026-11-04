```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method      | Text                 | Mean      | Error    | StdDev    | Gen0   | Allocated |
|------------ |--------------------- |----------:|---------:|----------:|-------:|----------:|
| **SearchForm**  | **Apfelkuchen**          |  **57.31 ns** | **1.250 ns** |  **3.686 ns** | **0.0010** |     **144 B** |
| JaroWinkler | Apfelkuchen          | 106.18 ns | 2.163 ns |  5.428 ns | 0.0014 |     208 B |
| NgramDice   | Apfelkuchen          | 106.25 ns | 2.332 ns |  6.767 ns |      - |         - |
| **SearchForm**  | **Gedec(...)useln [35]** | **129.86 ns** | **3.891 ns** | **11.290 ns** | **0.0017** |     **240 B** |
| JaroWinkler | Gedec(...)useln [35] | 155.27 ns | 4.885 ns | 14.404 ns | 0.0017 |     232 B |
| NgramDice   | Gedec(...)useln [35] | 286.61 ns | 6.816 ns | 19.665 ns |      - |         - |
| **SearchForm**  | **Größe(...)nahme [23]** | **135.20 ns** | **4.214 ns** | **12.360 ns** | **0.0021** |     **320 B** |
| JaroWinkler | Größe(...)nahme [23] | 185.13 ns | 3.672 ns |  8.583 ns | 0.0014 |     200 B |
| NgramDice   | Größe(...)nahme [23] | 201.63 ns | 4.061 ns | 10.038 ns |      - |         - |
