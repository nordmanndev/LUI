using System.Globalization;
using BenchmarkDotNet.Reports;
using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Benchmarks;

/// <summary>
/// The reports of a run, with what issue #124 asks of them: every line names the number of hits of its query, and the
/// report names the date of the run, the number of measurements per case, the machine and the journal mode. A line that
/// measures no query — a write, a text — says so with a dash; a line that measured zero hits says zero. The difference
/// between the two is the difference between a measurement and no measurement.
/// </summary>
internal static class ReportWriter
{
    /// <summary>The column of the hits.</summary>
    internal const string HitsColumn = "Hits";

    /// <summary>The journal modes of the databases the run measured on — they are all the same.</summary>
    private static readonly List<string> JournalModes = [];

    /// <summary>Rewrites the reports of the run: the hits of every line and the record of the run itself.</summary>
    /// <param name="summaries">The summaries of the run.</param>
    /// <param name="startedAt">When the run started.</param>
    internal static void Write(IReadOnlyList<Summary> summaries, DateTime startedAt)
    {
        var directory = summaries.Count > 0 ? summaries[0].ResultsDirectoryPath : string.Empty;
        foreach (var path in Directory
            .EnumerateFiles(directory, "*-report-github.md")
            .Order(StringComparer.Ordinal))
        {
            var lines = File.ReadAllLines(path);
            var table = ReadTable(lines);
            if (table is null)
            {
                continue;
            }

            var hits = Hits(table);
            var metadata = Metadata(summaries, ClassOf(path), lines, startedAt);
            File.WriteAllLines(path, Rebuild(lines, table, hits, metadata));
        }
    }

    /// <summary>
    /// The table of a report: its header, its separator and its rows.
    /// <para>
    /// BenchmarkDotNet writes the table of the cases and — after a second separator — the summary of them: the same
    /// cases, grouped, with the first row of every group in bold. The report keeps the summary, because that is the
    /// table the record of the repository shows; the table of the cases falls away with it.
    /// </para>
    /// </summary>
    private sealed record Table(string[] Header, string Separator, List<string[]> Rows, int First, int Last);

    /// <summary>Reads the table of a report, if it has one.</summary>
    private static Table? ReadTable(string[] lines)
    {
        var block = lines
            .Select((line, index) => (line, index))
            .Where(item => item.line.StartsWith('|'))
            .ToArray();
        if (block.Length < 3)
        {
            return null;
        }

        var second = -1;
        for (var i = 2; i < block.Length; i++)
        {
            if (IsSeparator(block[i].line))
            {
                second = i;
                break;
            }
        }

        var rows = second >= 0 ? block[(second + 1)..] : block[2..];

        return new Table(
            Cells(block[0].line),
            block[1].line,
            rows.Select(item => Cells(item.line)).ToList(),
            block[0].index,
            block[^1].index);
    }

    /// <summary>Whether a line of a table separates it from the rows: only pipes, dashes and colons.</summary>
    private static bool IsSeparator(string line) =>
        line.All(character => character is '|' or '-' or ':' or ' ');

    /// <summary>The cells of a line of a table: without the bold a group marks its first row with.</summary>
    private static string[] Cells(string line) =>
        [.. line.Trim().Trim('|').Split('|').Select(cell => cell.Replace("**", string.Empty, StringComparison.Ordinal).Trim())];

    /// <summary>The hits of every line: the number the query of the line found, or the dash for a line that measures no query.</summary>
    private static string[] Hits(Table table)
    {
        var isQuery = table.Header.Contains("Input", StringComparer.Ordinal)
            && table.Header.Contains("Corpus", StringComparer.Ordinal);

        return table.Rows.Select(row => isQuery ? Hits(table.Header, row) : BenchmarkHits.NoQuery).ToArray();
    }

    /// <summary>The hits of one line: the query of its parameters, run on the index of its parameters.</summary>
    private static string Hits(string[] header, string[] row)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < header.Length && i < row.Length; i++)
        {
            parameters[header[i]] = row[i];
        }

        var settings = QuerySettings.From(parameters);
        var fixture = IndexFixture.Of(settings.Corpus, settings.Entries, settings.Source);
        JournalModes.Add(fixture.JournalMode);

        // die Treffer, die die Abfrage GEFUNDEN hat: `Hits` ist die Obergrenze der Antwort (20),
        // `CandidatesConsidered` die Zahl der Treffer, die rangiert wurden
        return settings.Run(fixture.Index).CandidatesConsidered.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The record of the run: the date, the measurements per case, the machine and the journal mode.</summary>
    private static string[] Metadata(IReadOnlyList<Summary> summaries, string benchmarkClass, string[] lines, DateTime startedAt)
    {
        var measurements = summaries
            .SelectMany(summary => summary.Reports)
            .Where(report => string.Equals(report.BenchmarkCase.Descriptor.Type.Name, benchmarkClass, StringComparison.Ordinal))
            .Select(report => report.ResultStatistics?.N ?? 0)
            .ToArray();

        return
        [
            $"  [Run]      : {startedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, the measurements per case: {Measurements(measurements)}",
            $"  [Machine]  : {Machine(lines)}",
            $"  [Journal]  : {JournalMode()}"
        ];
    }

    /// <summary>The number of measurements per case — the number, or the span the cases of the run differ in.</summary>
    private static string Measurements(int[] measurements)
    {
        if (measurements.Length == 0)
        {
            return "none";
        }

        var min = measurements.Min().ToString(CultureInfo.InvariantCulture);
        var max = measurements.Max().ToString(CultureInfo.InvariantCulture);
        return string.Equals(min, max, StringComparison.Ordinal) ? min : $"{min}–{max}";
    }

    /// <summary>The machine of the run: the processor line of the environment of the report.</summary>
    private static string Machine(string[] lines)
    {
        var environment = lines.Where(line => line.Contains("CPU", StringComparison.Ordinal)).ToArray();
        return environment.Length > 0 ? environment[0].Trim() : "unknown";
    }

    /// <summary>The journal mode of the database the measurements ran on.</summary>
    private static string JournalMode() => JournalModes.Count > 0 ? JournalModes[0] : "unknown";

    /// <summary>Rebuilds the report: the environment with the record of the run, and the table with the hits.</summary>
    private static string[] Rebuild(string[] lines, Table table, string[] hits, string[] metadata)
    {
        var output = new List<string>();
        var fences = lines
            .Select((line, index) => (line, index))
            .Where(item => item.line.StartsWith("```", StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();

        var environmentEnd = fences.Length > 1 ? fences[1] : -1;
        for (var index = 0; index < lines.Length; index++)
        {
            if (index == table.First)
            {
                output.AddRange(Rows(table, hits));
            }

            // die alte Tabelle faellt weg: der Bericht behaelt EINE Tabelle — die der Zusammenfassung, mit den Treffern
            if (index >= table.First && index <= table.Last)
            {
                continue;
            }

            if (index == environmentEnd)
            {
                output.AddRange(metadata);
            }

            output.Add(lines[index]);
        }

        return [.. output];
    }

    /// <summary>The table of the report with the hits of every line.</summary>
    private static IEnumerable<string> Rows(Table table, string[] hits)
    {
        yield return "| " + string.Join(" | ", table.Header.Append(HitsColumn)) + " |";
        yield return table.Separator[..^1] + "|---:|";
        for (var i = 0; i < table.Rows.Count; i++)
        {
            yield return "| " + string.Join(" | ", table.Rows[i].Append(hits[i])) + " |";
        }
    }

    /// <summary>The benchmark class a report belongs to: its name is in the name of the file.</summary>
    private static string ClassOf(string path)
    {
        var name = Path.GetFileName(path);
        const string prefix = "Nordmann.LUI.Benchmarks.";
        const string suffix = "-report-github.md";
        return name[prefix.Length..^suffix.Length];
    }
}
