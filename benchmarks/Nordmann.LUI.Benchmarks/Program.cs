using BenchmarkDotNet.Running;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// The benchmark host. The measurements themselves arrive with the feature they
/// measure (PR-009); what exists here from the first commit is the harness —
/// BenchmarkDotNet, central package versions and the analyzer, so that the
/// performance budgets land on a working bench instead of a new one.
/// <para>
/// After the run the reports are rewritten: every line gets the number of hits of its
/// query, and the report gets the record of the run — the date, the measurements per
/// case, the machine and the journal mode (issue #124, ADR-0060). A report that names
/// a number without the place it was measured at is a rumour.
/// </para>
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var startedAt = DateTime.UtcNow;

        var summaries = BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args)
            .ToArray();

        ReportWriter.Write(summaries, startedAt);
        IndexFixture.DisposeAll();

        return 0;
    }
}
