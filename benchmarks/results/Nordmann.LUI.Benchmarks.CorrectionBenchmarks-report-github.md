```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 12 (bookworm)
Intel Xeon 6982P-C 3.80GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method | Entries | Input    | Mean      | Error    | StdDev   | Allocated |
|------- |-------- |--------- |----------:|---------:|---------:|----------:|
| **Search** | **200000**  | **bahmhof**  | **201.66 μs** | **3.842 μs** | **3.946 μs** |  **23.61 KB** |
| **Search** | **200000**  | **fusbal**   | **371.00 μs** | **7.259 μs** | **7.130 μs** |  **21.33 KB** |
| **Search** | **200000**  | **kartofel** |  **85.87 μs** | **1.370 μs** | **1.144 μs** |   **7.22 KB** |
| **Search** | **200000**  | **strasee**  |  **94.57 μs** | **1.089 μs** | **0.909 μs** |   **7.17 KB** |
