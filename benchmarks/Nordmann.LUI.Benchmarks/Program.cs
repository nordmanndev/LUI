using BenchmarkDotNet.Running;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// The benchmark host. The measurements themselves arrive with the feature they
/// measure (PR-009); what exists here from the first commit is the harness —
/// BenchmarkDotNet, central package versions and the analyzer, so that the
/// performance budgets land on a working bench instead of a new one.
/// </summary>
public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args);
}
