```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 12 (bookworm)
Intel Xeon 6982P-C 3.80GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method            | Entries | Mean       | Error    | StdDev   | Allocated |
|------------------ |-------- |-----------:|---------:|---------:|----------:|
| UpdateWeight      | 200000  | 1,868.1 μs | 36.99 μs | 80.42 μs |     436 B |
| UpsertTheSame     | 200000  |   688.0 μs |  3.93 μs |  3.07 μs |     820 B |
| UpsertAnotherText | 200000  | 1,496.3 μs | 29.42 μs | 58.75 μs |     820 B |
