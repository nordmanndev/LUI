using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The budget table of the README is the promise a reader checks — and a promise that is copied by hand drifts
/// (issue #38). This test holds it up: the limits are read out of the **file** (ADR-0056) and compared with the
/// tables that show them — the one of the entrance and the two of the record.
/// <para>
/// A limit that changes in the file, or a limit that is changed by hand in one of the tables, fails the build. The
/// measured values are not checked here: they belong to a run, and a run is not a promise — they stand in the record
/// with the machine they were measured on.
/// </para>
/// </summary>
public sealed class ReadmeBudgetsTests
{
    /// <summary>The entrance: the budget table beside the performance of the README.</summary>
    private static string ReadmePath => Path.Combine(Repository.RootPath, "README.md");

    /// <summary>The record: the limits of the file in English, beside the measured numbers.</summary>
    private static string RecordPath => Path.Combine(Repository.RootPath, "docs", "benchmarks.md");

    [Fact]
    public void The_limits_of_the_readme_are_the_limits_of_the_file()
    {
        var expected = LimitsOfTheFile();

        var limits = LimitsOf(TableOf(ReadmePath, "Target", "Measured", "Limit").Select(row => row[2]));

        limits.ShouldBe(
            expected,
            "the limit column of the budget table of the README is the file of the budgets (issue #38, ADR-0056). "
            + "The file says: " + string.Join(", ", expected)
            + " — the README says: " + string.Join(", ", limits));
    }

    [Fact]
    public void The_limits_of_the_record_are_the_limits_of_the_file()
    {
        var expected = LimitsOfTheFile();

        var limits = LimitsOf(TableOf(RecordPath, "Measurement", "Scenario", "Target").Select(row => row[2]));

        limits.ShouldBe(
            expected,
            "the target table of the record is the file of the budgets (issue #38, ADR-0056). "
            + "The file says: " + string.Join(", ", expected)
            + " — the record says: " + string.Join(", ", limits));
    }

    [Fact]
    public void The_limits_of_the_status_table_of_the_record_are_the_limits_of_the_file()
    {
        var expected = LimitsOfTheFile();

        var limits = LimitsOf(TableOf(RecordPath, "Target", "Measured at the scenario", "Limit").Select(row => row[2]));

        limits.ShouldBe(
            expected,
            "the limit column of the status table of the record is the file of the budgets (issue #38, ADR-0056). "
            + "The file says: " + string.Join(", ", expected)
            + " — the record says: " + string.Join(", ", limits));
    }

    [Fact]
    public void Every_table_with_the_head_target_measured_limit_shows_limits_of_the_file_in_the_order_of_the_file()
    {
        // the README has one table of this head and the record has one of its own (the two scenarios of their own): a test that
        // rests on the first of them lets the second drift (issue #181: one number, two verdicts)
        var expected = LimitsOfTheFile();

        foreach (var path in new[] { ReadmePath, RecordPath })
        {
            var tables = TablesOf(path).Where(table => table[0].SequenceEqual(new[] { "Target", "Measured", "Limit" })).ToList();
            tables.ShouldNotBeEmpty(Path.GetFileName(path));

            foreach (var table in tables)
            {
                var rows = table.Skip(1).ToList();
                rows.ShouldAllBe(row => row.Length == 3, $"{Path.GetFileName(path)}: a row of the table '{string.Join(" | ", table[0])}' has not three cells");

                // a row whose limit does not parse (`<= 300 MB`, `max 300 MB`) would pass as an empty list
                foreach (var row in rows)
                {
                    LimitsOf([row[2]]).ShouldNotBeEmpty($"{Path.GetFileName(path)}: the row '{row[0]}' shows no limit of the form '≤ 300 MB'");
                }

                var limits = LimitsOf(rows.Select(row => row[2]));

                IsSubsequence(limits, expected).ShouldBeTrue(
                    $"{Path.GetFileName(path)}: the table '{string.Join(" | ", table[0])}' shows {string.Join(", ", limits)}; "
                    + $"the file of the budgets says {string.Join(", ", expected)} — every limit is one of them, in their order");
            }
        }
    }

    [Fact]
    public void A_table_that_shows_a_target_or_a_limit_has_one_of_the_known_heads()
    {
        // a head that is not known is an error: the next table with limits would slip through the tests above
        string[][] known =
        [
            ["Target", "Measured", "Limit"],
            ["Measurement", "Scenario", "Target"],
            ["Target", "Measured at the scenario", "Limit"],
            ["", "Result", "Target (ADR-0020)"],
            ["Kind of target", "On the machine of the record", "On the lane"], // which kind is asserted where: no number of a limit
            ["Entries", "p50", "p95", "target p95"], // the run of the first scale test: a dated record with the target of its day

            // the runs of 2026-11-04 with the targets of their day: a dated record, not a promise of today — its limits are the history
            ["Target", "Run 1", "Run 2", "Run 3 (batches of 1,000,000)", "Run 4 (as committed)", "Limit"],
        ];

        foreach (var path in new[] { ReadmePath, RecordPath })
        {
            foreach (var table in TablesOf(path))
            {
                var header = table[0];
                var shows = header.Any(cell => cell.Contains("Target", StringComparison.OrdinalIgnoreCase) || cell.Contains("Limit", StringComparison.OrdinalIgnoreCase)
                    || cell.Contains("Budget", StringComparison.OrdinalIgnoreCase) || cell.Contains("Maximum", StringComparison.OrdinalIgnoreCase));

                if (shows)
                {
                    known.Any(head => head.SequenceEqual(header)).ShouldBeTrue(
                        $"{Path.GetFileName(path)}: a table with the head '{string.Join(" | ", header)}' shows targets or limits, and its head is not one the guard knows");
                }
            }
        }
    }

    private static bool IsSubsequence(string[] part, string[] whole)
    {
        var at = 0;
        foreach (var item in part)
        {
            while (at < whole.Length && !string.Equals(whole[at], item, StringComparison.Ordinal))
            {
                at++;
            }

            if (at == whole.Length)
            {
                return false;
            }

            at++;
        }

        return true;
    }

    /// <summary>
    /// The limits of the file, in the order of the file: every budget carries its limit and its unit, and the
    /// measured value it is compared with lives in the record, not here.
    /// </summary>
    private static string[] LimitsOfTheFile() =>
        LuiBudgets.All
            .Select(budget => Canonical(budget.Limit.ToString("0.##", CultureInfo.InvariantCulture), budget.Unit))
            .ToArray();

    /// <summary>
    /// The limits of a table: the third column of every row, read as "≤ number unit". A number without a unit takes
    /// the unit of the next limit of the same cell — "≤ 250 / ≤ 350 KB" is two limits in kilobytes.
    /// </summary>
    private static string[] LimitsOf(IEnumerable<string> cells) =>
        cells.SelectMany(LimitsOfCell).ToArray();

    private static string[] LimitsOfCell(string cell)
    {
        var text = Remarks.Replace(cell, string.Empty);
        var matches = Limit.Matches(text).ToArray();
        var limits = new string[matches.Length];

        for (var index = 0; index < matches.Length; index++)
        {
            var unit = matches[index].Groups["unit"].Value;
            if (unit.Length == 0)
            {
                unit = matches
                    .Skip(index + 1)
                    .Concat(matches.Take(index).Reverse())
                    .Select(match => match.Groups["unit"].Value)
                    .FirstOrDefault(candidate => candidate.Length > 0) ?? string.Empty;
            }

            limits[index] = Canonical(matches[index].Groups["number"].Value, unit);
        }

        return limits;
    }

    /// <summary>
    /// A limit in one form: the number and the unit, the minute folded into the second — "4 min" and "240 s" are
    /// the same promise, and both are written in the tables.
    /// </summary>
    private static string Canonical(string number, string unit)
    {
        var value = double.Parse(number.Replace(',', '.'), CultureInfo.InvariantCulture);

        return string.Equals(unit, "min", StringComparison.Ordinal)
            ? (value * 60).ToString("0.##", CultureInfo.InvariantCulture) + " s"
            : number.Replace(',', '.') + " " + unit;
    }

    /// <summary>
    /// A table of a markdown file: the rows of the table whose header matches, the header itself left out.
    /// The cells are without their padding.
    /// </summary>
    private static string[][] TableOf(string path, params string[] header)
    {
        var table = TablesOf(path).FirstOrDefault(candidate => candidate.Count > 0 && candidate[0].SequenceEqual(header));

        table.ShouldNotBeNull(
            $"the table '{string.Join(" | ", header)}' is missing in {Path.GetFileName(path)}.");

        return table!.Skip(1).ToArray();
    }

    /// <summary>Every table of a markdown file — the header first, the separator row left out, the cells without their padding.</summary>
    private static List<List<string[]>> TablesOf(string path)
    {
        var tables = new List<List<string[]>>();
        List<string[]>? current = null;

        foreach (var line in File.ReadAllLines(path))
        {
            if (!line.TrimStart().StartsWith('|'))
            {
                current = null;
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

            // the separator row of a table holds no values
            if (cells.All(cell => cell.Length == 0 || cell.All(character => character == '-' || character == ':')))
            {
                continue;
            }

            if (current is null)
            {
                current = [];
                tables.Add(current);
            }

            current.Add(cells);
        }

        return tables;
    }

    /// <summary>The remarks in brackets — "(ADR-0032)", "(too much — bug #30)" — are no limits.</summary>
    private static readonly Regex Remarks = new(@"\([^)]*\)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    /// <summary>A limit: "≤" and a number, with the unit if it has one.</summary>
    private static readonly Regex Limit = new(
        @"≤\s*(?<number>\d+(?:[.,]\d+)?)\s*(?<unit>ms|KB|MB|min|s)?",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));
}
