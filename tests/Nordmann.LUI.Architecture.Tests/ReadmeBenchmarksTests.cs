using System.Text;
using System.Text.RegularExpressions;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The BenchmarkDotNet results of the documentation (ADR-0022, issue #36): a performance number is a claim, and a claim
/// needs a run behind it. BenchmarkDotNet's own reports (<c>*-report-github.md</c>) are committed in
/// <c>benchmarks/results/</c>; the README shows them, between two markers, in a form this test renders.
/// When the two differ, the document was edited by hand or the reports were replaced without it — either way the test
/// fails.
/// <para>
/// The raw tables live in the README since issue #61 (ADR-0039): the entrance of the release shows the measurement
/// itself — the machine, the corpus and the tables of the run — and the record, <c>docs/benchmarks.md</c>, keeps the
/// method and the budgets beside it. The rule of ADR-0022 stands: the tables are rendered from the committed reports,
/// never written by hand.
/// </para>
/// <para>
/// <c>LUI_UPDATE_README=1</c> writes the rendered section into the README instead of failing:
/// <c>tools/benchmarks.sh</c> runs the benchmarks, copies the reports and does that. The rendering lives here, once —
/// the guard and the generator cannot disagree.
/// </para>
/// </summary>
public sealed class ReadmeBenchmarksTests
{
    private const string Begin = "<!-- benchmarks:begin -->";
    private const string End = "<!-- benchmarks:end -->";
    private const string UpdateVariable = "LUI_UPDATE_README";

    /// <summary>The benchmark classes in the order of the README, each with its heading and what it measures.</summary>
    private static readonly (string Class, string Heading, string Measures)[] Sections =
    [
        ("QueryBenchmarks", "A query", "one query through the whole pipeline: the prefix path for one and two characters, FTS5 from three"),
        ("SettingsBenchmarks", "The settings of a query", "the budget of the candidates, the order of the ranking, the measure of the similarity and the correction on and off — each of them is measured, not described"),
        ("CorrectionBenchmarks", "A query with a typo correction", "a single-word index with typo tolerance, the dictionary built; the input has one typo"),
        ("WriteBenchmarks", "A single write", "one write in a transaction of its own"),
        ("TextBenchmarks", "The search form and the similarity", "per text, no database: the normalisation of §6 and two similarity measures against `apfelku`; BenchmarkDotNet shortens a long text — `Gedec(...)useln [35]` is „Gedeckter Apfelkuchen mit Streuseln\", `Größe(...)nahme [23]` „Größenänderungsmaßnahme\""),
    ];

    /// <summary>
    /// The columns of BenchmarkDotNet's table that the README shows besides the parameters: the statistics and the
    /// number of hits the query found (issue #124) — a line without it can measure nothing and look healthy.
    /// </summary>
    private static readonly string[] Measured = ["Mean", "Error", "Allocated", "Hits"];

    /// <summary>The first column of every table.</summary>
    private static readonly string[] MethodColumn = ["Method"];

    /// <summary>
    /// The attribute of a benchmark method in any form — <c>[Benchmark]</c>, <c>[Benchmark(Baseline = true)]</c>,
    /// <c>[Benchmark, …]</c>, <c>[…, Benchmark]</c> — and not <c>[BenchmarkCategory]</c> (review of pull request 37, M3).
    /// </summary>
    private static readonly Regex BenchmarkAttribute = new(
        @"[\[,]\s*Benchmark\s*[\](,]",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    /// <summary>Line and block comments: a <c>[Benchmark]</c> in a comment is no benchmark.</summary>
    private static readonly Regex Comment = new(
        @"//[^\n]*|/\*.*?\*/",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Singleline,
        TimeSpan.FromSeconds(1));

    /// <summary>The document the rendered section lives in: the README of the release shows the tables of the run (ADR-0039).</summary>
    private static string DocumentPath => Path.Combine(Repository.RootPath, "README.md");

    private static string ResultsDirectory => Path.Combine(Repository.RootPath, "benchmarks", "results");

    private static string BenchmarksDirectory => Path.Combine(Repository.RootPath, "benchmarks", "Nordmann.LUI.Benchmarks");

    [Fact]
    public void Every_benchmark_class_has_a_committed_report_and_a_section_and_nothing_else_has()
    {
        var classes = Repository
            .SourceFiles(BenchmarksDirectory)
            .Where(file => BenchmarkAttribute.IsMatch(StripComments(File.ReadAllText(file))))
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var reports = Directory
            .EnumerateFiles(ResultsDirectory, "*-report-github.md")
            .Select(ClassOf)
            .Order(StringComparer.Ordinal)
            .ToArray();

        classes.ShouldNotBeEmpty();
        reports.ShouldBe(classes, "benchmarks/results/ holds BenchmarkDotNet's report of every benchmark class — run tools/benchmarks.sh");
        Sections.Select(section => section.Class).Order(StringComparer.Ordinal).ToArray().ShouldBe(classes, "every benchmark class has a section in the README");
    }

    [Theory]
    [InlineData("[Benchmark] public int A() => 1;", true)]
    [InlineData("[Benchmark(Baseline = true)] public int A() => 1;", true)]
    [InlineData("[Benchmark(Description = \"x\")] public int A() => 1;", true)]
    [InlineData("[MemoryDiagnoser, Benchmark] public int A() => 1;", true)]
    [InlineData("[BenchmarkCategory(\"x\")] public int A() => 1;", false)]
    [InlineData("// [Benchmark] in a comment", false)]
    [InlineData("/* [Benchmark] */ public int A() => 1;", false)]
    public void A_benchmark_is_found_by_its_attribute_in_every_form_and_not_in_a_comment(string source, bool isBenchmark)
    {
        // review of pull request 37, M3: a literal "[Benchmark]" missed [Benchmark(Baseline = true)]
        BenchmarkAttribute.IsMatch(StripComments(source)).ShouldBe(isBenchmark, source);
    }

    [Fact]
    public void The_reports_come_from_one_run_on_one_machine()
    {
        // issue #124: jeder Bericht traegt seinen eigenen Lauf (Datum, Messungen je Fall) — der Vergleich laeuft
        // ueber die MASCHINE und die Laufzeit, nicht ueber den ganzen Block: der Block ist je Klasse verschieden.
        var machines = Sections
            .Select(section => (section.Class, Environment: Report.Read(ReportPath(section.Class)).Environment))
            .ToArray();

        machines
            .Select(item => item.Environment)
            .Where(environment => environment.Contains("[Machine]", StringComparison.Ordinal))
            .Select(environment => environment
                .Split('\n')
                .First(line => line.Contains("[Machine]", StringComparison.Ordinal))
                .Trim())
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(1, "the document compares the benchmarks with each other; they have to be measured on one machine, with one runtime: "
                + string.Join("; ", machines.Select(item => $"{item.Class}: {item.Environment.Split('\n')[0]}")));
    }

    [Fact]
    public void A_line_with_zero_hits_is_shown_as_zero_and_not_as_an_empty_cell()
    {
        // issue #124: a line without the number of hits can measure nothing and look healthy — the input "ka" found
        // nothing in the report of the day. Zero is a measurement; a dash is "this line measures no query".
        var report = new Report(
            "the machine",
            ["Input"],
            [new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Method"] = "Search",
                ["Input"] = "ka",
                ["Mean"] = "16.46 μs",
                ["Error"] = "0.275 μs",
                ["Allocated"] = "4.94 KB",
                ["Hits"] = "0"
            }]);

        var rendered = RenderSection(report, "QueryBenchmarks", "A query", "a query through the whole pipeline");

        rendered.Contains("| 0 |", StringComparison.Ordinal)
            .ShouldBeTrue("the zero is a measurement, and the report shows it");
        rendered.Contains("|  |", StringComparison.Ordinal)
            .ShouldBeFalse("an empty cell is not a measurement");
    }

    [Fact]
    public void Every_report_names_the_date_the_measurements_the_machine_and_the_journal_mode()
    {
        foreach (var (name, _, _) in Sections)
        {
            var path = ReportPath(name);
            var environment = Report.Read(path).Environment;

            environment.ShouldMatch("\\d{4}-\\d{2}-\\d{2}", $"the report of {name} names the date of the run");
            environment.Contains("the measurements per case", StringComparison.Ordinal)
                .ShouldBeTrue($"the report of {name} names the number of measurements");
            environment.Contains("[Machine]", StringComparison.Ordinal)
                .ShouldBeTrue($"the report of {name} names the machine it was measured on");
            environment.Contains("[Journal]", StringComparison.Ordinal)
                .ShouldBeTrue($"the report of {name} names the journal mode of the database");
        }
    }

    [Fact]
    public void Every_line_of_every_report_names_its_hits_or_says_that_it_measures_no_query()
    {
        foreach (var (name, _, _) in Sections)
        {
            var report = Report.Read(ReportPath(name));
            report.Rows.ShouldNotBeEmpty();

            foreach (var row in report.Rows)
            {
                row.ContainsKey("Hits").ShouldBeTrue($"the report of {name} has the column of the hits");
                var hits = row["Hits"];
                hits.ShouldNotBeNullOrWhiteSpace($"a line of the report of {name} without the number of hits");
                (string.Equals(hits, BenchmarkHits.NoQuery, StringComparison.Ordinal)
                    || int.TryParse(hits, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                    .ShouldBeTrue($"the hits of a line are a number or the dash of a line that measures no query — not '{hits}'");
            }
        }
    }

    [Fact]
    public void The_benchmark_document_shows_the_committed_reports()
    {
        var document = File.ReadAllText(DocumentPath);
        var begin = document.IndexOf(Begin, StringComparison.Ordinal);
        var end = document.IndexOf(End, StringComparison.Ordinal);
        begin.ShouldBeGreaterThanOrEqualTo(0, $"the README marks the generated section with '{Begin}'");
        end.ShouldBeGreaterThan(begin, $"the README ends the generated section with '{End}'");

        // the line endings of the checkout are not a difference of the content: a runner that
        // checks the tree out with CRLF (Windows) must not fail the guard of the tables
        var shown = document[(begin + Begin.Length)..end].Replace("\r\n", "\n", StringComparison.Ordinal);
        var rendered = Render().Replace("\r\n", "\n", StringComparison.Ordinal);

        if (string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "1", StringComparison.Ordinal))
        {
            File.WriteAllText(DocumentPath, document[..(begin + Begin.Length)] + rendered + document[end..]);
            return;
        }

        shown.ShouldBe(
            rendered,
            $"the benchmark tables of the README are generated from benchmarks/results/ — run '{UpdateVariable}=1 dotnet test "
            + "tests/Nordmann.LUI.Architecture.Tests --filter ReadmeBenchmarksTests', or tools/benchmarks.sh for a new run");
    }

    /// <summary>The section between the markers: the machine once, then a table per benchmark class.</summary>
    private static string Render()
    {
        var text = new StringBuilder();
        text.Append("\n<!-- generated by ReadmeBenchmarksTests from benchmarks/results/ — do not edit by hand -->\n\n");

        var first = Report.Read(ReportPath(Sections[0].Class));
        text.Append("```text\n").Append(first.Environment).Append("\n```\n");

        foreach (var (name, heading, measures) in Sections)
        {
            text.Append(RenderSection(Report.Read(ReportPath(name)), name, heading, measures));
        }

        return text.Append('\n').ToString();
    }

    /// <summary>
    /// The section of one benchmark class: its heading, what it measures and its table. The table is rendered from the
    /// committed report — never written by hand (ADR-0022).
    /// </summary>
    /// <param name="report">The report of the class.</param>
    /// <param name="name">The name of the class.</param>
    /// <param name="heading">The heading of the section.</param>
    /// <param name="measures">What the class measures.</param>
    /// <returns>The rendered section.</returns>
    private static string RenderSection(Report report, string name, string heading, string measures)
    {
        var text = new StringBuilder();
        text.Append('\n').Append("#### ").Append(heading).Append("\n\n");
        text.Append(char.ToUpperInvariant(measures[0])).Append(measures[1..]);

        var constant = report.Parameters
            .Where(parameter => report.Rows.Select(row => row[parameter]).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToArray();
        if (constant.Length > 0)
        {
            text.Append(" — ").Append(string.Join(", ", constant.Select(parameter => $"{parameter} = {report.Rows[0][parameter]}")));
        }

        text.Append(". `").Append(name).Append("`.\n\n");

        var columns = MethodColumn.Concat(report.Parameters.Except(constant, StringComparer.Ordinal)).Concat(Measured).ToArray();
        text.Append("| ").Append(string.Join(" | ", columns)).Append(" |\n");
        text.Append('|').Append(string.Join('|', columns.Select(column => Measured.Contains(column, StringComparer.Ordinal) ? "---:" : "---"))).Append("|\n");
        foreach (var row in report.Rows)
        {
            text.Append("| ").Append(string.Join(" | ", columns.Select(column => row[column]))).Append(" |\n");
        }

        return text.ToString();
    }

    private static string StripComments(string source) => Comment.Replace(source, string.Empty);

    private static string ReportPath(string benchmarkClass) =>
        Path.Combine(ResultsDirectory, $"Nordmann.LUI.Benchmarks.{benchmarkClass}-report-github.md");

    private static string ClassOf(string reportPath)
    {
        var name = Path.GetFileName(reportPath);
        return name["Nordmann.LUI.Benchmarks.".Length..name.IndexOf("-report-github.md", StringComparison.Ordinal)];
    }

    /// <summary>
    /// BenchmarkDotNet's GitHub report: a code block with the machine and the runtime, then a table — a row per case, the
    /// columns the method, the parameters, the statistics. A group's first row is set in bold; the bold is dropped.
    /// </summary>
    private sealed record Report(string Environment, IReadOnlyList<string> Parameters, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows)
    {
        /// <summary>The columns between the method and the statistics that are not parameters.</summary>
        private static readonly string[] Statistics = ["Mean", "Error", "StdDev", "Median", "Ratio", "RatioSD", "Gen0", "Gen1", "Gen2", "Allocated", "Alloc Ratio"];

        internal static Report Read(string path)
        {
            File.Exists(path).ShouldBeTrue($"{path} is missing — run tools/benchmarks.sh");
            var lines = File.ReadAllLines(path);

            var fences = lines.Select((line, index) => (line, index)).Where(item => item.line.StartsWith("```", StringComparison.Ordinal)).Select(item => item.index).ToArray();
            fences.Length.ShouldBeGreaterThanOrEqualTo(2, $"{path}: BenchmarkDotNet's report begins with the machine in a code block");
            var environment = string.Join('\n', lines[(fences[0] + 1)..fences[1]].Select(line => line.TrimEnd()).Where(line => line.Length > 0));

            var table = lines.Skip(fences[1] + 1).Where(line => line.StartsWith('|')).ToArray();
            table.Length.ShouldBeGreaterThan(2, $"{path}: the report has a table with at least one row");
            var header = Cells(table[0]);
            var parameters = header.Skip(1).TakeWhile(column => !Statistics.Contains(column, StringComparer.Ordinal)).ToArray();
            foreach (var column in Measured)
            {
                header.ShouldContain(name => string.Equals(name, column, StringComparison.Ordinal), $"{path}: the report has the column '{column}' — [MemoryDiagnoser] gives 'Allocated'");
            }

            var rows = table
                .Skip(2)
                .Select(line => (IReadOnlyDictionary<string, string>)header
                    .Zip(Cells(line))
                    .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal))
                .ToArray();
            return new Report(environment, parameters, rows);
        }

        private static string[] Cells(string line) =>
            [.. line.Trim().Trim('|').Split('|').Select(cell => cell.Replace("**", string.Empty, StringComparison.Ordinal).Trim())];
    }
}
