// Probe 11: zwei Nachprüfungen.
//
//   A) Was kostet das Parsen der FTS5-Anweisung — und zahlt der Provider sie nur einmal?
//   B) Wie viele physische Verbindungen hält der Pool, und wie viel Page-Cache gehört dazu?

using System.Diagnostics;
using Microsoft.Data.Sqlite;

const int Queries = 200;
const int Rows = 500;

var path = Path.Combine(Path.GetTempPath(), $"lui-pool-{Guid.NewGuid():N}.db");
var readOnlyString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
var writeString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();

using (var setup = new SqliteConnection(writeString))
{
    setup.Open();
    using var create = setup.CreateCommand();
    create.CommandText = "CREATE VIRTUAL TABLE fts USING fts5(text); CREATE TABLE t (id INTEGER PRIMARY KEY, text TEXT);";
    create.ExecuteNonQuery();
    using var insert = setup.CreateCommand();
    insert.CommandText = "INSERT INTO t (text) VALUES ($text);";
    var text = insert.CreateParameter();
    text.ParameterName = "$text";
    insert.Parameters.Add(text);
    insert.Prepare();
    for (var i = 1; i <= 20_000; i++)
    {
        text.Value = $"eintrag {i} straße haus berlin";
        insert.ExecuteNonQuery();
    }

    using var rebuild = setup.CreateCommand();
    rebuild.CommandText = "INSERT INTO fts(rowid, text) SELECT id, text FROM t;";
    rebuild.ExecuteNonQuery();
    using var wal = setup.CreateCommand();
    wal.CommandText = "PRAGMA journal_mode = WAL;";
    wal.ExecuteNonQuery();
}

static double Median(List<double> values)
{
    values.Sort();
    return values[values.Count / 2];
}

const string Sql = "SELECT f.rowid, c.text FROM fts f JOIN t c ON c.id = f.rowid WHERE fts MATCH $q ORDER BY f.rowid LIMIT $n;";

Console.WriteLine("=== A) der Parsepreis der FTS5-Anweisung (200 Ausfuehrungen je Variante) ===");

// dieselbe Anweisung, 200x
using (var connection = new SqliteConnection(writeString))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = Sql;
    var q = command.CreateParameter();
    q.ParameterName = "$q";
    q.Value = "eintrag";
    command.Parameters.Add(q);
    var n = command.CreateParameter();
    n.ParameterName = "$n";
    n.Value = Rows;
    command.Parameters.Add(n);

    var first = Stopwatch.GetTimestamp();
    using (var reader = command.ExecuteReader())
    {
        while (reader.Read())
        {
        }
    }

    Console.WriteLine($"  der erste Aufruf:                      {Stopwatch.GetElapsedTime(first).TotalMilliseconds:F4} ms");

    var same = new List<double>();
    for (var i = 0; i < Queries; i++)
    {
        var began = Stopwatch.GetTimestamp();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
        }

        same.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
    }

    Console.WriteLine($"  danach, dieselbe Anweisung (200x):       {Median(same):F4} ms");
}

// 200 verschiedene Anweisungen (je Text ein eigener Parse)
using (var connection = new SqliteConnection(writeString))
{
    connection.Open();
    var different = new List<double>();
    for (var i = 0; i < Queries; i++)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Sql.Replace("LIMIT $n;", $"LIMIT $n /* {i} */;");
        var q = command.CreateParameter();
        q.ParameterName = "$q";
        q.Value = "eintrag";
        command.Parameters.Add(q);
        var n = command.CreateParameter();
        n.ParameterName = "$n";
        n.Value = Rows;
        command.Parameters.Add(n);

        var began = Stopwatch.GetTimestamp();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
        }

        different.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
    }

    Console.WriteLine($"  danach, 200 verschiedene Anweisungen:    {Median(different):F4} ms");
    Console.WriteLine($"  -> der Parsepreis einer Anweisung:        {Median(different) - Median(same):F4} ms");
}

Console.WriteLine();
Console.WriteLine("=== B) der Pool: physische Verbindungen und ihre Page-Caches ===");

// 8 gleichzeitige Ausleihen aus dem Pool
var threads = new List<Thread>();
var caches = new System.Collections.Concurrent.ConcurrentBag<string>();
var before = GC.GetTotalMemory(false);
var workingSetBefore = Process.GetCurrentProcess().WorkingSet64;

for (var i = 0; i < 8; i++)
{
    var thread = new Thread(() =>
    {
        using var connection = new SqliteConnection(readOnlyString);
        connection.Open();
        using var pragmas = connection.CreateCommand();
        pragmas.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA cache_size = -524288;";
        pragmas.ExecuteNonQuery();
        using var command = connection.CreateCommand();
        command.CommandText = Sql;
        var q = command.CreateParameter();
        q.ParameterName = "$q";
        q.Value = "eintrag";
        command.Parameters.Add(q);
        var n = command.CreateParameter();
        n.ParameterName = "$n";
        n.Value = Rows;
        command.Parameters.Add(n);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
        }

        using var cache = connection.CreateCommand();
        cache.CommandText = "PRAGMA cache_size;";
        caches.Add(Convert.ToString(cache.ExecuteScalar()) ?? "?");
    });
    threads.Add(thread);
    thread.Start();
}

foreach (var thread in threads)
{
    thread.Join();
}

var workingSetAfter = Process.GetCurrentProcess().WorkingSet64;
Console.WriteLine($"  acht gleichzeitige Ausleihen: {caches.Count} Verbindungen, jede mit cache_size = {caches.First()} KiB");
Console.WriteLine($"  => die maximale Page-Cache-Menge des Pools: {caches.Count} x 512 MB = {caches.Count * 512} MB (die Obergrenze, keine Zusage)");
Console.WriteLine($"  der Arbeitsspeicher vorher/nachher: {workingSetBefore / 1024 / 1024} MB / {workingSetAfter / 1024 / 1024} MB");
