using System.Globalization;

namespace Nordmann.LUI;

/// <summary>
/// What <see cref="LuiCatalog.Initialize"/> did (ADR-0076): one line per index, in the order of the names, and the total. It is what the call returns when no
/// index failed, and what <see cref="LuiInitializeException.Report"/> carries when one did. <see cref="ToString"/> is a readable summary for a log line.
/// </summary>
public sealed record LuiInitializeReport
{
    /// <summary>One line per index of the run, in the order of the names.</summary>
    public required IReadOnlyList<LuiInitializeIndexReport> Indexes { get; init; }

    /// <summary>The time the whole call took.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Whether an index failed.</summary>
    public bool Failed => Indexes.Any(line => line.Failed);

    /// <summary>Whether every index of the run is ready: none failed and none needs anything — <see cref="LuiIndexInfo.NeedsInitialization"/> is <see cref="LuiInitializationNeeds.None"/> for each.</summary>
    public bool Ready => Indexes.All(line => !line.Failed && line.Skipped == LuiInitializationNeeds.None);

    /// <summary>A readable summary: the indexes, what ran for each, what is left, and the time.</summary>
    /// <returns>The text, for a log.</returns>
    public override string ToString()
    {
        var indexes = Indexes.Count == 0 ? string.Empty : "; " + string.Join("; ", Indexes.Select(Describe));
        var state = Failed ? "FAILED" : Ready ? "ready" : "not ready";
        return string.Create(CultureInfo.InvariantCulture, $"Initialize: {Indexes.Count} indexes in {Elapsed.TotalSeconds:F2} s, {state}{indexes}");
    }

    private static string Describe(LuiInitializeIndexReport line)
    {
        var done = new List<string>();
        if (line.TriggersInstalled)
        {
            done.Add("triggers");
        }

        if (line.Sync is { } sync)
        {
            done.Add(string.Create(CultureInfo.InvariantCulture, $"sync of {sync.Indexed} entries in {sync.Batches} batches"));
        }

        if (line.PendingApplied > 0)
        {
            done.Add(string.Create(CultureInfo.InvariantCulture, $"{line.PendingApplied} rows of the outbox"));
        }

        if (line.TypoDictionaryBuilt)
        {
            done.Add("typo dictionary");
        }

        var text = $"{line.Name}: {(done.Count == 0 ? "nothing to do" : string.Join(", ", done))}";
        if (line.Failed)
        {
            text += $", FAILED at {line.FailedStep}";
        }

        return line.Skipped == LuiInitializationNeeds.None ? text : $"{text}, still needs {line.Skipped}";
    }
}
