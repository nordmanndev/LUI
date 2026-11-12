using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Performance.Tests;

/// <summary>
/// What a <c>Linked</c> index costs on a table of the application (ADR-0015): the sync of the whole table, the
/// queries through the whole pipeline, the refresh of single rows — against an <c>Embedded</c> index of the same entries.
/// <code>dotnet test tests/Nordmann.LUI.Performance.Tests --filter LinkedPerformance --logger "console;verbosity=detailed"</code>
/// </summary>
[Trait("Category", "Timing")]
public sealed class LinkedPerformance(ITestOutputHelper output)
{
    /// <summary>The scenario of this run (ADR-0058): the corpus, the prefix length and the settings.</summary>
    private static PerformanceScenario Scenario => PerformanceScenario.Current;
    private const int Queries = 2_000;

    [Fact]
    public void Sync_queries_and_refreshes_against_embedded()
    {
        var entries = Scenario.PrefixLength ?? 200_000;
        var corpus = CorpusOf("query-p50");
        var words = corpus.Texts;
        Report($"  {entries:N0} entries");

        var random = new Random(42);
        var rows = Enumerable.Range(1, entries)
            .Select(i => (Id: (long)i, Text: words[(i - 1) % words.Count], Weight: corpus.Weights[(i - 1) % corpus.Weights.Count]))
            .ToList();

        using var database = new TestDatabase();
        using var catalog = database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512 });
        CreateTable(database, rows);

        var linked = catalog.Create("tags", index => index.SingleWord().LinkTo("tag", "id", "name", weight: "usage_count"));
        var clock = Stopwatch.StartNew();
        linked.Sync();
        Report($"linked: sync {clock.Elapsed.TotalSeconds:F1} s");

        var embedded = catalog.Create("embedded", index => index.SingleWord());
        clock.Restart();
        embedded.UpsertMany(rows.Select(row => new LuiEntry(row.Id, row.Text) { Weight = row.Weight }));
        Report($"embedded:  UpsertMany {clock.Elapsed.TotalSeconds:F1} s");

        var inputs = Enumerable.Range(0, Queries).Select(_ =>
        {
            var text = rows[random.Next(rows.Count)].Text;
            var length = Math.Min(text.Length, random.Next(1, 9));
            return text[..length];
        }).ToList();

        // warm both, then measure them alternating
        foreach (var input in inputs.Take(200))
        {
            linked.Search(input);
            embedded.Search(input);
        }

        var linkedTimes = new List<double>();
        var embeddedTimes = new List<double>();
        foreach (var input in inputs)
        {
            var started = Stopwatch.GetTimestamp();
            linked.Search(input);
            linkedTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            started = Stopwatch.GetTimestamp();
            embedded.Search(input);
            embeddedTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        Report($"query: linked p50 {Percentile(linkedTimes, 50):F2} / p95 {Percentile(linkedTimes, 95):F2} ms, "
            + $"embedded p50 {Percentile(embeddedTimes, 50):F2} / p95 {Percentile(embeddedTimes, 95):F2} ms");

        MeasureRefreshes(database, linked, entries, random);
    }

    private void MeasureRefreshes(TestDatabase database, ILuiLinkedIndex linked, int entries, Random random)
    {
        var refreshes = new List<double>();
        using (var connection = database.Connect())
        {
            for (var i = 0; i < 500; i++)
            {
                var id = random.Next(1, entries);
                using var update = connection.CreateCommand();
                update.CommandText = "UPDATE tag SET usage_count = usage_count + 1 WHERE id = $id;";
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();

                var started = Stopwatch.GetTimestamp();
                linked.Refresh(id);
                refreshes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }

        Report($"refresh: p50 {Percentile(refreshes, 50):F2} / p95 {Percentile(refreshes, 95):F2} ms (own transaction)");
    }

    private static void CreateTable(TestDatabase database, List<(long Id, string Text, int Weight)> rows)
    {
        using var connection = database.Connect();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE tag (id INTEGER PRIMARY KEY, name TEXT NOT NULL, usage_count INTEGER NOT NULL);";
            create.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO tag VALUES ($id, $name, $weight);";
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var name = insert.Parameters.Add("$name", SqliteType.Text);
        var weight = insert.Parameters.Add("$weight", SqliteType.Integer);
        foreach (var row in rows)
        {
            id.Value = row.Id;
            name.Value = row.Text;
            weight.Value = row.Weight;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static double Percentile(List<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * percentile / 100.0) - 1)];
    }

    /// <summary>
    /// Das Korpus der Messung: die Datei, die das Budget ihres Ziels nennt (ADR-0056). Der
    /// Schnitt, den die Messung nimmt, ist der des Budgets, beschnitten um die Zahl der
    /// Eintraege des Laufs. Welches Korpus es war, steht im Bericht — zwei Messungen auf
    /// zwei Korpora sind kein Vergleich.
    /// </summary>
    /// <param name="budgetId">Die Kennung des Budgets, dessen Ziel gemessen wird.</param>
    /// <returns>Das Korpus des Laufs.</returns>
    private Corpus CorpusOf(string budgetId)
    {
        var corpus = Scenario.CorpusOf(budgetId, out var name);
        Report(Scenario.Describe(name, corpus.Count, LuiBudgets.By(budgetId).Scenario.PrefixLength));
        Report($"  {Environment.ProcessorCount} cores, .NET {Environment.Version}");
        return corpus;
    }

    private void Report(string line)
    {
        output.WriteLine(line);
        Console.Error.WriteLine("[perf] " + line);
    }
}
