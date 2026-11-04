```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method            | Entries | Mean      | Error     | StdDev    | Allocated |
|------------------ |-------- |----------:|----------:|----------:|----------:|
| UpdateWeight      | 200000  | 343.48 μs | 11.133 μs | 32.475 μs |     436 B |
| UpsertTheSame     | 200000  |  92.93 μs |  1.835 μs |  4.252 μs |     812 B |
| UpsertAnotherText | 200000  | 228.23 μs | 18.042 μs | 52.913 μs |     812 B |
