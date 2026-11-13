namespace Nordmann.LUI.Testing;

/// <summary>
/// The cell of a report line that measures no query (issue #124): the report says that it measured none. A line that
/// measured **zero** hits says zero — the difference between a measurement and no measurement is the difference between
/// the two cells, and a report that hides it cannot be read.
/// </summary>
public static class BenchmarkHits
{
    /// <summary>The cell of a line that measures no query: a write, a text — anything that is not a query.</summary>
    public const string NoQuery = "—";
}
