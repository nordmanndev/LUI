```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method | Entries | Input    | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------- |-------- |--------- |---------:|---------:|---------:|-------:|----------:|
| **Search** | **200000**  | **bahmhof**  | **274.8 μs** |  **8.99 μs** | **26.35 μs** | **0.4883** |  **99.78 KB** |
| **Search** | **200000**  | **fusbal**   | **476.6 μs** | **13.83 μs** | **40.57 μs** |      **-** |  **80.15 KB** |
| **Search** | **200000**  | **kartofel** | **113.5 μs** |  **3.29 μs** |  **9.70 μs** | **0.1221** |  **26.84 KB** |
| **Search** | **200000**  | **strasee**  | **123.7 μs** |  **3.58 μs** | **10.56 μs** | **0.1221** |  **26.79 KB** |
