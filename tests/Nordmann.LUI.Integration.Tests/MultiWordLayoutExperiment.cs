#pragma warning disable MA0051, CA1861, MA0004, MA0002, CA1845, MA0074, MA0006, CA1305, CA2100, MA0011, CA1307, CA1866
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Querying;
using Xunit.Abstractions;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// An experiment, not a test of behaviour (ADR-0011): the same multi-word titles in an FTS5 index over
/// <c>s_norm</c> and <c>s_compact</c> (the specification) and over <c>s_compact</c> alone, built in rowid
/// order and merged; the candidate statement run single-shot over the same inputs.
/// <code>LUI_PERF=1 dotnet test --filter MultiWordLayoutExperiment</code>
/// Needs <c>tools/fetch-wordlist.sh</c>; writes to <c>artifacts/perf/</c>.
/// </summary>
public sealed class MultiWordLayoutExperiment(ITestOutputHelper output)
{
    private static readonly string Dir = Path.Combine(Repository.RootPath, "artifacts", "perf") + Path.DirectorySeparatorChar;
    private const int N = 500_000;

    [PerformanceFact]
    public void Two_columns_or_one()
    {
        Directory.CreateDirectory(Dir);
        var words = File.ReadLines(TestCorpus.FullWordListPath).Where(w => w.Length >= 3).ToList();
        var random = new Random(42);
        var titles = new List<string>(N);
        var src = Dir + "src.db";
        foreach (var s in new[] { "", "-wal", "-shm" }) File.Delete(src + s);
        using (var c = new SqliteConnection("Data Source=" + src + ";Pooling=False"))
        {
            c.Open();
            Exec(c, "CREATE TABLE t(s_rowid INTEGER PRIMARY KEY, s_norm TEXT, s_compact TEXT);");
            using var tx = c.BeginTransaction();
            using var ins = c.CreateCommand();
            ins.CommandText = "INSERT INTO t VALUES ($r, $n, $c)";
            var pr = ins.Parameters.Add("$r", SqliteType.Integer); var pn = ins.Parameters.Add("$n", SqliteType.Text); var pc = ins.Parameters.Add("$c", SqliteType.Text);
            for (var i = 1; i <= N; i++)
            {
                var title = string.Join(' ', Enumerable.Range(0, random.Next(2, 5)).Select(_ => { var w = words[random.Next(words.Count)]; return char.ToUpperInvariant(w[0]) + w[1..]; }));
                titles.Add(title);
                var weight = (int)Math.Min(536_870_911, 1 + (long)(random.NextDouble() * random.NextDouble() * random.NextDouble() * 1_000_000));
                pr.Value = RowId.Encode(LuiPriority.Normal, weight, i); pn.Value = Normalizer.ToSearchForm(title); pc.Value = Normalizer.ToCompactForm(title); ins.ExecuteNonQuery();
            }
            tx.Commit();
        }

        var inputs = new List<string>();
        var r2 = new Random(7);
        while (inputs.Count < 2000)
        {
            var text = titles[r2.Next(titles.Count)];
            if (r2.Next(3) == 0) { var parts = text.Split(' ').Select(w => w[..Math.Min(w.Length, r2.Next(3, 6))]).Take(2).ToArray(); if (r2.Next(2) == 0) Array.Reverse(parts); inputs.Add(string.Join(' ', parts)); continue; }
            var length = Math.Min(text.Length, r2.Next(3, 9));
            var start = r2.Next(3) == 0 && text.Length > length ? r2.Next(text.Length - length + 1) : 0;
            var input = text.Substring(start, length).Trim();
            if (input.Length >= 3) inputs.Add(input);
        }

        foreach (var variant in new[] { "A two columns, spec expression", "A2 two columns, one word only in s_compact", "B one column s_compact, AND of words" })
        {
            var path = Dir + "v.db";
            foreach (var s in new[] { "", "-wal", "-shm" }) File.Delete(path + s);
            var cols = variant.StartsWith("B") ? "s_compact" : "s_norm, s_compact";
            var build = Stopwatch.StartNew();
            using (var c = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                c.Open();
                Exec(c, "PRAGMA journal_mode=WAL; PRAGMA cache_size=-524288;");
                Exec(c, $"CREATE VIRTUAL TABLE f USING fts5({cols}, tokenize='trigram remove_diacritics 1');");
                Exec(c, $"ATTACH DATABASE '{src}' AS src;");
                Exec(c, $"INSERT INTO f(rowid, {cols}) SELECT s_rowid, {cols} FROM src.t ORDER BY s_rowid;");
                Exec(c, "DETACH DATABASE src;");
                Exec(c, "INSERT INTO f(f) VALUES('optimize');");
                Exec(c, "PRAGMA wal_checkpoint(TRUNCATE);");
            }
            var seconds = build.Elapsed.TotalSeconds;
            var size = new FileInfo(path).Length / 1048576;

            using var q = new SqliteConnection("Data Source=" + path + ";Pooling=False");
            q.Open();
            Exec(q, "PRAGMA cache_size=-524288; PRAGMA mmap_size=2147483648;");
            var lat = new List<double>();
            var rows = 0L;
            foreach (var pass in new[] { 0, 1 })
            {
                foreach (var input in inputs)
                {
                    var pq = PreparedQuery.Create(input, NormalizationOptions.Default, MultiWordRules.Instance);
                    if (pq.IsPrefixPath) continue;
                    string expr;
                    if (variant.StartsWith("A2") && pq.Words.Count == 1 && pq.Words[0] == pq.Compact) expr = "s_compact : " + IndexKindRules.Quote(pq.Compact);
                    else if (variant.StartsWith("B"))
                    {
                        var longWords = pq.Words.Where(w => w.Length >= 3).ToArray();
                        expr = longWords.Length == 0 ? "s_compact : " + IndexKindRules.Quote(pq.Compact) : "s_compact : (" + string.Join(" AND ", longWords.Select(IndexKindRules.Quote)) + ")";
                    }
                    else expr = pq.Expression;
                    using var cmd = q.CreateCommand();
                    cmd.CommandText = "SELECT rowid FROM f WHERE f MATCH $e ORDER BY rowid LIMIT 501";
                    cmd.Parameters.AddWithValue("$e", expr);
                    var sw = Stopwatch.GetTimestamp();
                    using var rd = cmd.ExecuteReader(); long n = 0; while (rd.Read()) n++;
                    if (pass == 1) { lat.Add(Stopwatch.GetElapsedTime(sw).TotalMilliseconds); rows += n; }
                }
            }
            lat.Sort();
            output.WriteLine($"{variant,-52} build {seconds,5:F1}s  fts file {size,4} MB  p50 {lat[lat.Count / 2],5:F2} p95 {lat[(int)(lat.Count * 0.95)],6:F2} ms  avg rows {rows / (double)lat.Count:F0}");
        }
    }

    private static void Exec(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 900; cmd.ExecuteNonQuery(); }
}
