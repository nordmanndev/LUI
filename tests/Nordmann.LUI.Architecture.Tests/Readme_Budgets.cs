using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The budget table of the README is the promise a reader checks — and a promise that is copied by hand drifts
/// (issue #38). This test holds it up: the limits of the table are read out of the **contract** (§16 of the
/// specification) and compared with the two tables that show them — the one of the entrance and the one of the record.
/// <para>
/// A limit that changes in the contract, or a limit that is changed by hand in one of the two tables, fails the
/// build. The measured values are not checked here: they belong to a run, and a run is not a promise — they stand in
/// the record with the machine they were measured on.
/// </para>
/// </summary>
public sealed class Readme_Budgets
{
    /// <summary>The contract: the targets of §16, in the language of the specification.</summary>
    private static string SpecificationPath =>
        Path.Combine(Repository.RootPath, ".agents", "spec", "LUI-Spezifikation.md");

    /// <summary>The entrance: the budget table beside the performance of the README.</summary>
    private static string ReadmePath => Path.Combine(Repository.RootPath, "README.md");

    /// <summary>The record: the targets of §16 in English, beside the measured numbers.</summary>
    private static string RecordPath => Path.Combine(Repository.RootPath, "docs", "benchmarks.md");

    [Fact]
    public void The_limits_of_the_readme_are_the_targets_of_the_specification()
    {
        var expected = TargetsOfTheSpecification();

        var limits = LimitsOf(TableOf(ReadmePath, "Target", "Measured", "Limit").Select(row => row[2]));

        limits.ShouldBe(
            expected,
            "the limit column of the budget table of the README is the target table of §16 (issue #38). "
            + "The specification says: " + string.Join(", ", expected)
            + " — the README says: " + string.Join(", ", limits));
    }

    [Fact]
    public void The_limits_of_the_record_are_the_targets_of_the_specification()
    {
        var expected = TargetsOfTheSpecification();

        var limits = LimitsOf(TableOf(RecordPath, "Measurement", "Scenario", "Target").Select(row => row[2]));

        limits.ShouldBe(
            expected,
            "the target table of the record is the target table of §16 (issue #38). "
            + "The specification says: " + string.Join(", ", expected)
            + " — the record says: " + string.Join(", ", limits));
    }

    [Fact]
    public void The_limits_of_the_status_table_of_the_record_are_the_targets_of_the_specification()
    {
        var expected = TargetsOfTheSpecification();

        var limits = LimitsOf(TableOf(RecordPath, "Target", "Measured at the scenario", "Limit").Select(row => row[2]));

        limits.ShouldBe(
            expected,
            "the limit column of the status table of the record is the target table of §16 (issue #38). "
            + "The specification says: " + string.Join(", ", expected)
            + " — the record says: " + string.Join(", ", limits));
    }

    /// <summary>
    /// The limits of §16, in the order of the table: the specification states them as prose in its third column
    /// ("p50 ≤ 2 ms; p95 ≤ 5 ms bei 1–4 Zeichen, …"), and every "≤ number unit" is one limit.
    /// </summary>
    private static string[] TargetsOfTheSpecification()
    {
        File.Exists(SpecificationPath).ShouldBeTrue(
            "the contract of the repository is missing — the limits of §16 have to live somewhere; "
            + "the owner decides where (issue #38).");

        var table = TableOf(SpecificationPath, "Messung", "Szenario", "Ziel");

        return LimitsOf(table.Select(row => row[2]));
    }

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

        var table = tables.FirstOrDefault(candidate =>
            candidate.Count > 0 && candidate[0].SequenceEqual(header));

        table.ShouldNotBeNull(
            $"the table '{string.Join(" | ", header)}' is missing in {Path.GetFileName(path)}.");

        return table!.Skip(1).ToArray();
    }

    /// <summary>The remarks in brackets — "(ADR-0032)", "(too much — bug #30)" — are no limits.</summary>
    private static readonly Regex Remarks = new(@"\([^)]*\)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    /// <summary>A limit: "≤" and a number, with the unit if it has one.</summary>
    private static readonly Regex Limit = new(
        @"≤\s*(?<number>\d+(?:[.,]\d+)?)\s*(?<unit>ms|KB|MB|min|s)?",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));
}
