using System.Globalization;

using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The index of the records of the decisions (<c>.agents/adr/README.md</c>) is written by hand, and a
/// hand-written index rots: a record that is not in it is invisible until a reader looks for it, and a
/// row that names a record that is not there points at nothing (issue #129).
/// <para>
/// Four promises: every record has a row, every row names a record that exists, the number of the row is
/// the number of the record, and the topic of a row is one of the topics of the index.
/// </para>
/// </summary>
public sealed class AdrIndexTests
{
    /// <summary>The index of the records, beside the records themselves.</summary>
    private static string IndexPath => Path.Combine(Repository.RootPath, ".agents", "adr", "README.md");

    /// <summary>The folder of the records.</summary>
    private static string RecordsDirectory => Path.Combine(Repository.RootPath, ".agents", "adr");

    [Fact]
    public void Every_record_of_the_decisions_has_a_row_in_the_index()
    {
        var index = File.ReadAllText(IndexPath);

        var missing = Records()
            .Where(record => !Rows().Any(row => string.Equals(row.Target, Path.GetFileName(record), StringComparison.Ordinal)))
            .Select(Relative)
            .ToArray();

        missing.ShouldBeEmpty(
            "every record of the decisions has a row in the index (issue #129). Missing: "
            + string.Join(", ", missing)
            + ". A new record needs its row — the index is how a reader finds it.");
    }

    [Fact]
    public void Every_row_of_the_index_points_at_a_record_that_exists()
    {
        var missing = Rows()
            .Where(row => !File.Exists(Path.Combine(RecordsDirectory, row.Target)))
            .Select(row => row.Number)
            .ToArray();

        missing.ShouldBeEmpty(
            "every row of the index points at a record that exists (issue #129). Missing: "
            + string.Join(", ", missing)
            + ". A row of a record that is gone needs to go with it.");
    }

    [Fact]
    public void The_number_of_a_row_is_the_number_of_the_record()
    {
        var wrong = Rows()
            .Where(row => !TargetCarriesTheNumber(row))
            .Select(row => $"{row.Number} → {row.Target}")
            .ToArray();

        wrong.ShouldBeEmpty(
            "the number of a row is the number of the record it names (issue #129). Wrong: "
            + string.Join(", ", wrong));
    }

    [Fact]
    public void The_topic_of_a_row_is_one_of_the_topics_of_the_index()
    {
        var topics = Topics();
        var unknown = Rows()
            .Select(row => row.Topic)
            .Where(topic => !topics.Contains(topic, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        unknown.ShouldBeEmpty(
            "the topic of a row is one of the topics of the index (issue #129). Unknown: "
            + string.Join(", ", unknown)
            + ". A topic that is not in the list is a decision a reader cannot find.");
    }

    /// <summary>The records of the decisions: the files whose name starts with their number.</summary>
    private static string[] Records() =>
        Directory
            .EnumerateFiles(RecordsDirectory, "*.md")
            .Where(file => FileNumber(file) > 0)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The rows of the index: the number, the record it names, the title, the topic and the status.</summary>
    private static Row[] Rows() =>
        File.ReadAllText(IndexPath)
            .Split('\n')
            .Where(line => line.StartsWith("| [", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length > 4)
            .Select(cells => new Row(
                RowNumber(cells[1]),
                Target(cells[1]),
                cells[2],
                cells[3],
                cells[4]))
            .ToArray();

    /// <summary>The topics of the index: the first column of its table of topics.</summary>
    private static string[] Topics() =>
        File.ReadAllText(IndexPath)
            .Split('\n')
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length > 1)
            .Select(cells => cells[1].Trim('`'))
            .Where(topic => topic.Length > 0)
            .ToArray();

    /// <summary>The number of a cell of the index: the digits between the brackets of the link.</summary>
    private static string RowNumber(string cell)
    {
        var open = cell.IndexOf('[', StringComparison.Ordinal);
        var close = cell.IndexOf(']', StringComparison.Ordinal);

        if (open < 0 || close < open)
        {
            return string.Empty;
        }

        return cell[(open + 1)..close];
    }

    /// <summary>The record a cell of the index names: the file name between the parentheses of the link.</summary>
    private static string Target(string cell)
    {
        var open = cell.IndexOf('(', StringComparison.Ordinal);
        var close = cell.IndexOf(')', StringComparison.Ordinal);

        if (open < 0 || close < open)
        {
            return string.Empty;
        }

        return cell[(open + 1)..close].TrimStart('.', '/');
    }

    /// <summary>Whether the record a row names carries the number of the row in its file name.</summary>
    private static bool TargetCarriesTheNumber(Row row) =>
        row.Target.StartsWith(row.Number + "-", StringComparison.Ordinal)
        || string.Equals(row.Target, row.Number, StringComparison.Ordinal);

    /// <summary>The number of a record: the four digits its file name starts with.</summary>
    private static int FileNumber(string file)
    {
        var name = Path.GetFileName(file);

        return name.Length >= 4 && int.TryParse(name[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;
    }

    private static string Relative(string file) =>
        Path.GetRelativePath(Repository.RootPath, file).Replace('\\', '/');

    /// <summary>A row of the index.</summary>
    private sealed record Row(string Number, string Target, string Title, string Topic, string Status);
}
