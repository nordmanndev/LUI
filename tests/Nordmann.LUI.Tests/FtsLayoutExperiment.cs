#pragma warning disable MA0051, CA1861, MA0004, MA0002, CA1845, MA0074, MA0006, CA1305, CA2100, MA0011
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexing;
using Nordmann.LUI.Normalization;
using Xunit.Abstractions;

namespace Nordmann.LUI.Tests;

/// <summary>
/// An experiment, not a test of behaviour (ADR-0010, "Follow-up measurement"): the same
/// texts in FTS5 indexes that are built in bulk, in rowid order and merged, with one
/// SQLite setting varied at a time — and the statement of the candidate query run
/// cold and single-shot over a fixed sequence of inputs.
/// <code>LUI_PERF=1 LUI_PERF_ENTRIES=3000000 dotnet test --filter FtsLayoutExperiment</code>
/// Needs <c>tools/fetch-wordlist.sh</c>. Writes to <c>artifacts/perf/</c>.
/// </summary>
public sealed class FtsLayoutExperiment(ITestOutputHelper output)
{
    private static readonly string Dir = Path.Combine(Testing.Repository.RootPath, "artifacts", "perf") + Path.DirectorySeparatorChar;

    private static readonly int Entries = int.Parse(Environment.GetEnvironmentVariable("LUI_PERF_ENTRIES") ?? "1000000");


    [PerformanceFact]
    public void The_same_texts_with_one_setting_varied()
    {
        Directory.CreateDirectory(Dir);
        var texts = MakeSource();
        var inputs = Inputs(texts);

        // name, db page size, fts5 pgsz, journal, mmap MB, cache MB
        var variants = new (string Name, int PageSize, int Pgsz, string Journal, int Mmap, int Cache, string Join)[]
        {
            ("fts only (no join)", 4096, 0, "WAL", 2048, 512, "none"),
            ("join content, plain unique index on s_rowid", 4096, 0, "WAL", 2048, 512, "plain"),
            ("join content, covering unique index (s_rowid, text)", 4096, 0, "WAL", 2048, 512, "covering"),
            ("join content, plain, mmap off, cache 2G", 4096, 0, "WAL", 0, 2048, "plain"),
        };

        foreach (var v in variants)
        {
            var path = Dir + "v.db";
            foreach (var s in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(path + s);
            var build = Stopwatch.StartNew();
            using (var c = Open(path, v.PageSize, v.Journal, 512, 0))
            {
                Exec(c, "CREATE VIRTUAL TABLE f USING fts5(s_norm, tokenize='trigram remove_diacritics 1');");
                if (v.Pgsz > 0) Exec(c, $"INSERT INTO f(f, rank) VALUES('pgsz', {v.Pgsz});");
                Exec(c, $"ATTACH DATABASE '{Dir}src.db' AS src;");
                Exec(c, "INSERT INTO f(rowid, s_norm) SELECT s_rowid, s_norm FROM src.t ORDER BY s_rowid;");
                if (v.Join != "none")
                {
                    Exec(c, "CREATE TABLE c(doc_id INTEGER PRIMARY KEY, text TEXT, s_rowid INTEGER);");
                    Exec(c, "INSERT INTO c(doc_id, text, s_rowid) SELECT s_rowid & 4294967295, s_norm, s_rowid FROM src.t ORDER BY s_rowid & 4294967295;");
                    Exec(c, v.Join == "plain" ? "CREATE UNIQUE INDEX c_rowid ON c(s_rowid);" : "CREATE UNIQUE INDEX c_rowid ON c(s_rowid, text);");
                }
                Exec(c, "DETACH DATABASE src;");
                Exec(c, "INSERT INTO f(f) VALUES('optimize');");
                Exec(c, "PRAGMA wal_checkpoint(TRUNCATE);");
            }
            var built = build.Elapsed.TotalSeconds;

            using var q = Open(path, v.PageSize, v.Journal, v.Cache, v.Mmap);
            var size = new FileInfo(path).Length / 1048576;
            var join = v.Join != "none";
            foreach (var input in inputs.Take(500)) Run(q, input, join);
            var lat = new List<double>(); var lat3 = new List<double>();
            foreach (var input in inputs)
            {
                var sw = Stopwatch.GetTimestamp();
                Run(q, input, join);
                var ms = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
                lat.Add(ms); if (input.Length >= 3) lat3.Add(ms);
            }
            output.WriteLine($"{v.Name,-60} build {built,5:F1}s size {size,4}MB  p50 {P(lat, 50),5:F2} p95 {P(lat, 95),6:F2} | >=3ch p50 {P(lat3, 50),5:F2} p95 {P(lat3, 95),6:F2}");
        }
    }

    private static double P(List<double> v, double p) { var s = v.Order().ToList(); return s[Math.Min(s.Count - 1, (int)Math.Ceiling(s.Count * p / 100) - 1)]; }

    private static void Run(SqliteConnection c, string input, bool join)
    {
        using var cmd = c.CreateCommand();
        if (input.Length < 3) { cmd.CommandText = "SELECT rowid FROM f WHERE 0"; cmd.ExecuteNonQuery(); return; }
        cmd.CommandText = join ? "SELECT f.rowid, c.text FROM f JOIN c ON c.s_rowid = f.rowid WHERE f MATCH $e ORDER BY f.rowid LIMIT 501" : "SELECT rowid FROM f WHERE f MATCH $e ORDER BY rowid LIMIT 501";
        cmd.Parameters.AddWithValue("$e", "s_norm : \"" + input.Replace("\"", "\"\"") + "\"");
        using var r = cmd.ExecuteReader();
        while (r.Read()) { if (join) _ = r.GetString(1); }
    }

    private static SqliteConnection Open(string path, int pageSize, string journal, int cacheMb, int mmapMb)
    {
        var c = new SqliteConnection("Data Source=" + path + ";Pooling=False");
        c.Open();
        Exec(c, $"PRAGMA page_size={pageSize};");
        Exec(c, $"PRAGMA journal_mode={journal};");
        Exec(c, $"PRAGMA cache_size=-{cacheMb * 1024}; PRAGMA mmap_size={(long)mmapMb * 1048576};");
        return c;
    }

    private static List<string> MakeSource()
    {
        var src = Dir + "src.db";
        var words = File.ReadLines(Path.Combine(Testing.Repository.RootPath, "artifacts", "wordlist", "german-words.txt")).Where(w => w.Length >= 3).ToList();
        var random = new Random(42);
        var texts = new List<string>(Entries);
        var exists = File.Exists(src);
        using var c = new SqliteConnection("Data Source=" + src + ";Pooling=False");
        c.Open();
        if (!exists) Exec(c, "CREATE TABLE t(s_rowid INTEGER PRIMARY KEY, s_norm TEXT);");
        using var tx = c.BeginTransaction();
        using var ins = c.CreateCommand();
        ins.CommandText = "INSERT INTO t(s_rowid, s_norm) VALUES ($r, $s)";
        var pr = ins.Parameters.Add("$r", SqliteType.Integer); var ps = ins.Parameters.Add("$s", SqliteType.Text);
        for (var i = 1; i <= Entries; i++)
        {
            var text = i <= words.Count ? words[i - 1] : char.ToUpperInvariant(words[random.Next(words.Count)][0]) + words[random.Next(words.Count)][1..] + words[random.Next(words.Count)].ToLowerInvariant();
            texts.Add(text);
            var weight = (int)Math.Min(536_870_911, 1 + (long)(random.NextDouble() * random.NextDouble() * random.NextDouble() * Entries));
            if (!exists) { pr.Value = RowId.Encode(LuiPriority.Normal, weight, i); ps.Value = Normalizer.ToSearchForm(text); ins.ExecuteNonQuery(); }
        }
        tx.Commit();
        return texts;
    }

    private static List<string> Inputs(List<string> texts)
    {
        var random = new Random(7); var inputs = new List<string>();
        while (inputs.Count < 2000)
        {
            var text = texts[random.Next(texts.Count)];
            var length = Math.Min(text.Length, random.Next(1, 9));
            var start = random.Next(3) == 0 && text.Length > length ? random.Next(text.Length - length + 1) : 0;
            var input = Normalizer.ToSearchForm(text.Substring(start, length).Trim());
            if (input.Length > 0) inputs.Add(input);
        }
        return inputs;
    }

    private static void Exec(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 900; cmd.ExecuteNonQuery(); }
}
