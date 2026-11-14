// Probe 12: der Preis der Anweisung — ein neues Kommando je Anfrage oder eines, das wiederverwendet wird?
//
// Die beiden Varianten wechseln sich ab (A B A B ...), damit ein Driften der Maschine
// beide gleich trifft. Beide laufen auf DERSELBEN gehaltenen Verbindung — es geht nur
// um das Kommando.

using System.Diagnostics;
using Microsoft.Data.Sqlite;

const int Rounds = 300;
const int Rows = 500;

var path = Path.Combine(Path.GetTempPath(), $"lui-prep-{Guid.NewGuid():N}.db");
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
}

const string Sql = "SELECT f.rowid, c.text FROM fts f JOIN t c ON c.id = f.rowid WHERE fts MATCH $q ORDER BY f.rowid LIMIT $n;";

static double Median(List<double> values)
{
    values.Sort();
    return values[values.Count / 2];
}

using var connection = new SqliteConnection(writeString);
connection.Open();

// das wiederverwendete Kommando, vorbereitet
using var reused = connection.CreateCommand();
reused.CommandText = Sql;
var reusedQ = reused.CreateParameter();
reusedQ.ParameterName = "$q";
reusedQ.Value = "eintrag";
reused.Parameters.Add(reusedQ);
var reusedN = reused.CreateParameter();
reusedN.ParameterName = "$n";
reusedN.Value = Rows;
reused.Parameters.Add(reusedN);
reused.Prepare();

var withNewCommand = new List<double>();
var withReusedCommand = new List<double>();

for (var round = 0; round < Rounds; round++)
{
    // A) ein neues Kommando je Anfrage (der Entwurf des Owners)
    var beganA = Stopwatch.GetTimestamp();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = Sql;
        command.Parameters.AddWithValue("$q", "eintrag");
        command.Parameters.AddWithValue("$n", Rows);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
        }
    }

    withNewCommand.Add(Stopwatch.GetElapsedTime(beganA).TotalMilliseconds);

    // B) das vorbereitete Kommando, wiederverwendet (die Sitzung)
    var beganB = Stopwatch.GetTimestamp();
    using (var reader = reused.ExecuteReader())
    {
        while (reader.Read())
        {
        }
    }

    withReusedCommand.Add(Stopwatch.GetElapsedTime(beganB).TotalMilliseconds);
}

Console.WriteLine("=== derselbe Prozess, dieselbe Verbindung, abwechselnd (300 Runden) ===");
Console.WriteLine($"  A) ein neues Kommando je Anfrage:   p50 {Median(withNewCommand):F4} ms");
Console.WriteLine($"  B) das vorbereitete, wiederverwendet: p50 {Median(withReusedCommand):F4} ms");
Console.WriteLine($"  -> der Preis des Vorbereitens:        {Median(withNewCommand) - Median(withReusedCommand):F4} ms je Anfrage");

// und dieselbe Frage mit Prepare() auf dem neuen Kommando
var withPrepare = new List<double>();
var withoutPrepare = new List<double>();
for (var round = 0; round < Rounds; round++)
{
    var beganA = Stopwatch.GetTimestamp();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = Sql;
        command.Parameters.AddWithValue("$q", "eintrag");
        command.Parameters.AddWithValue("$n", Rows);
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
        }
    }

    withPrepare.Add(Stopwatch.GetElapsedTime(beganA).TotalMilliseconds);

    var beganB = Stopwatch.GetTimestamp();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = Sql;
        command.Parameters.AddWithValue("$q", "eintrag");
        command.Parameters.AddWithValue("$n", Rows);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
        }
    }

    withoutPrepare.Add(Stopwatch.GetElapsedTime(beganB).TotalMilliseconds);
}

Console.WriteLine();
Console.WriteLine("=== und: hilft Prepare() auf dem neuen Kommando? ===");
Console.WriteLine($"  mit Prepare():    p50 {Median(withPrepare):F4} ms");
Console.WriteLine($"  ohne Prepare():   p50 {Median(withoutPrepare):F4} ms");
