// Probe 10: reicht der Entwurf des Owners?
//
//   A) Was spart das Connection Pooling des Providers wirklich?
//   B) Bereitet der Provider eine Anweisung je physischer Verbindung vor (einen Statement-Cache)?
//   C) Der Entwurf des Owners (read-only Verbindung je Anfrage, vorbereitete Anweisung)
//      gegen die Sitzung (gehaltene Verbindung, wiederholte Anweisung) — Latenz und Allokation.
//
// Variante 0: heute  — neue Verbindung je Anfrage, die Pragmas je Anfrage, neues Kommando
// Variante 1: der Owner — neue read-only Verbindung je Anfrage, die Pragmas, Prepare()
// Variante 2: der Owner ohne Prepare() — der Provider übernimmt das
// Variante 3: der Owner ohne Pragmas je Anfrage — der Pool behält sie
// Variante 4: die Sitzung — eine gehaltene read-only Verbindung, das Kommando wiederverwendet

using System.Diagnostics;
using Microsoft.Data.Sqlite;

const int Queries = 200;
const int Rows = 500;

var path = Path.Combine(Path.GetTempPath(), $"lui-owner-{Guid.NewGuid():N}.db");
var readOnlyString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
var writeString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();

using (var setup = new SqliteConnection(writeString))
{
    setup.Open();
    using var create = setup.CreateCommand();
    create.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, text TEXT);";
    create.ExecuteNonQuery();
    using var insert = setup.CreateCommand();
    insert.CommandText = "INSERT INTO t (text) SELECT 'eintrag ' || value FROM generate_series(1, 5000);";
    insert.ExecuteNonQuery();
    using var wal = setup.CreateCommand();
    wal.CommandText = "PRAGMA journal_mode = WAL;";
    wal.ExecuteNonQuery();
}

static double Median(List<double> values)
{
    values.Sort();
    return values[values.Count / 2];
}

static (double Latency, double Allocation) Measure(int queries, Func<(double, double)> run)
{
    var latencies = new List<double>();
    var allocations = new List<double>();
    for (var i = 0; i < queries; i++)
    {
        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var began = Stopwatch.GetTimestamp();
        run((0, 0));
        latencies.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
        allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocationBefore);
    }

    return (Median(latencies), Median(allocations));
}

const string Pragmas = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL; PRAGMA cache_size = -524288;";
const string Sql = "SELECT id, text FROM t WHERE id <= $n ORDER BY id;";

static long ReadRows(SqliteCommand command)
{
    var count = 0L;
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        count += reader.GetInt64(0);
    }

    return count;
}

Console.WriteLine("=== A) was das Connection Pooling einspart (200 Oeffnungen) ===");

var pooled = Measure(Queries, _ =>
{
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT 1;";
    command.ExecuteScalar();
    return (0d, 0d);
});
Console.WriteLine($"  gepoolte Verbindung je Anfrage:        {pooled.Latency:F4} ms, {pooled.Allocation:F0} B");

var held = Measure(Queries, _ =>
{
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
    connection.Open();
    for (var i = 0; i < Queries; i++)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        command.ExecuteScalar();
    }

    return (0d, 0d);
});
Console.WriteLine($"  eine gehaltene Verbindung (400 Anfragen):{held.Latency:F4} ms, {held.Allocation:F0} B");

var unpooled = Measure(50, _ =>
{
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Pooling = false,
    }.ToString());
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT 1;";
    command.ExecuteScalar();
    return (0d, 0d);
});
Console.WriteLine($"  ungepoolte Verbindung (physisch neu):   {unpooled.Latency:F4} ms, {unpooled.Allocation:F0} B");

Console.WriteLine();
Console.WriteLine("=== B) bereitet der Provider eine Anweisung je physischer Verbindung vor? ===");
using (var connection = new SqliteConnection(writeString))
{
    connection.Open();

    var first = Stopwatch.GetTimestamp();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT count(*) FROM t WHERE id > 1;";
        command.ExecuteScalar();
    }

    var firstMs = Stopwatch.GetElapsedTime(first).TotalMilliseconds;

    var same = Measure(200, _ =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM t WHERE id > 1;";
        command.ExecuteScalar();
        return (0d, 0d);
    });

    var different = Measure(200, i =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM t WHERE id > {i + 1};";
        command.ExecuteScalar();
        return (0d, 0d);
    });

    Console.WriteLine($"  der erste Aufruf:                       {firstMs:F4} ms");
    Console.WriteLine($"  danach, derselbe Text (200x):            {same.Latency:F4} ms");
    Console.WriteLine($"  danach, 200 verschiedene Texte:          {different.Latency:F4} ms");
    Console.WriteLine($"  -> die Differenz ist der Preis des Parsens: {different.Latency - same.Latency:F4} ms je Anweisung");
}

Console.WriteLine();
Console.WriteLine($"=== C) die Leser-Anweisung ({Rows} Zeilen), 200 Anfragen ===");

// Variante 0: heute
var variant0 = Measure(Queries, _ =>
{
    using var connection = new SqliteConnection(writeString);
    connection.Open();
    using var pragmas = connection.CreateCommand();
    pragmas.CommandText = Pragmas;
    pragmas.ExecuteNonQuery();
    using var command = connection.CreateCommand();
    command.CommandText = Sql;
    command.Parameters.AddWithValue("$n", Rows);
    ReadRows(command);
    return (0d, 0d);
});
Console.WriteLine($"  0 heute (schreibbare Verbindung):        {variant0.Latency:F4} ms, {variant0.Allocation:F0} B");

// Variante 1: der Owner — read-only, Prepare()
var variant1 = Measure(Queries, _ =>
{
    using var connection = new SqliteConnection(readOnlyString);
    connection.Open();
    using var pragmas = connection.CreateCommand();
    pragmas.CommandText = Pragmas;
    pragmas.ExecuteNonQuery();
    using var command = connection.CreateCommand();
    command.CommandText = Sql;
    command.Parameters.AddWithValue("$n", Rows);
    command.Prepare();
    ReadRows(command);
    return (0d, 0d);
});
Console.WriteLine($"  1 der Owner (read-only + Prepare()):      {variant1.Latency:F4} ms, {variant1.Allocation:F0} B");

// Variante 2: der Owner ohne Prepare()
var variant2 = Measure(Queries, _ =>
{
    using var connection = new SqliteConnection(readOnlyString);
    connection.Open();
    using var pragmas = connection.CreateCommand();
    pragmas.CommandText = Pragmas;
    pragmas.ExecuteNonQuery();
    using var command = connection.CreateCommand();
    command.CommandText = Sql;
    command.Parameters.AddWithValue("$n", Rows);
    ReadRows(command);
    return (0d, 0d);
});
Console.WriteLine($"  2 der Owner ohne Prepare():               {variant2.Latency:F4} ms, {variant2.Allocation:F0} B");

// Variante 3: der Owner ohne die Pragmas je Anfrage (der Pool behält sie)
using (var once = new SqliteConnection(readOnlyString))
{
    once.Open();
    using var pragmas = once.CreateCommand();
    pragmas.CommandText = Pragmas;
    pragmas.ExecuteNonQuery();
}

var variant3 = Measure(Queries, _ =>
{
    using var connection = new SqliteConnection(readOnlyString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = Sql;
    command.Parameters.AddWithValue("$n", Rows);
    ReadRows(command);
    return (0d, 0d);
});
Console.WriteLine($"  3 der Owner ohne die Pragmas je Anfrage:   {variant3.Latency:F4} ms, {variant3.Allocation:F0} B");

// Variante 4: die Sitzung — eine gehaltene read-only Verbindung, das Kommando wiederverwendet
var variant4 = Measure(Queries, _ =>
{
    using var connection = new SqliteConnection(readOnlyString);
    connection.Open();
    using var pragmas = connection.CreateCommand();
    pragmas.CommandText = Pragmas;
    pragmas.ExecuteNonQuery();
    using var command = connection.CreateCommand();
    command.CommandText = Sql;
    command.Parameters.AddWithValue("$n", Rows);
    command.Prepare();
    for (var i = 0; i < Queries; i++)
    {
        command.Parameters["$n"].Value = Rows;
        ReadRows(command);
    }

    return (0d, 0d);
});
Console.WriteLine($"  4 die Sitzung (gehalten + wiederverwendet):{variant4.Latency:F4} ms, {variant4.Allocation:F0} B");
