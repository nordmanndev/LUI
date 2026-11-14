// Probe: was tun die Microsoft-Parameter, und was legt LUI danach darüber?
//
// Drei Fragen:
//   A) Welche Pragmas setzt Microsoft.Data.Sqlite beim Oeffnen aus seinen Schluesselwortern?
//   B) Was gilt danach, wenn LUI seine eigene Pragma-Anweisung laufen laesst (die Reihenfolge)?
//   C) Welche Pragmas darf eine read-only-Verbindung ueberhaupt setzen?

using Microsoft.Data.Sqlite;

var path = Path.Combine(Path.GetTempPath(), $"lui-msparams-{Guid.NewGuid():N}.db");

static string Read(SqliteConnection connection, string pragma)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA {pragma};";
    return Convert.ToString(command.ExecuteScalar()) ?? "(null)";
}

static void Show(SqliteConnection connection, string label)
{
    Console.WriteLine($"  {label,-28} busy_timeout={Read(connection, "busy_timeout"),-6} synchronous={Read(connection, "synchronous"),-3} "
        + $"cache_size={Read(connection, "cache_size"),-8} mmap_size={Read(connection, "mmap_size"),-10} "
        + $"journal_mode={Read(connection, "journal_mode"),-8} foreign_keys={Read(connection, "foreign_keys"),-3} "
        + $"recursive_triggers={Read(connection, "recursive_triggers"),-3}");
}

Console.WriteLine("=== A) die Schluesselwoerter des Providers ===");
var builder = new SqliteConnectionStringBuilder
{
    DataSource = path,
    Mode = SqliteOpenMode.ReadWriteCreate,
    Cache = SqliteCacheMode.Shared,
    ForeignKeys = true,
    RecursiveTriggers = true,
    DefaultTimeout = 42,
    Pooling = true,
};

using (var connection = new SqliteConnection(builder.ToString()))
{
    connection.Open();
    Show(connection, "nur die Schluesselwoerter:");
    Console.WriteLine($"  (Command Timeout = {builder.DefaultTimeout} s, ConnectionString: {builder.ConnectionString})");
}

Console.WriteLine();
Console.WriteLine("=== B) danach die Pragmas von LUI (die Reihenfolge im Code) ===");
using (var connection = new SqliteConnection(builder.ToString()))
{
    connection.Open();
    Show(connection, "nur die Schluesselwoerter:");

    using var pragmas = connection.CreateCommand();
    pragmas.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL; PRAGMA cache_size = -524288; PRAGMA mmap_size = 268435456;";
    pragmas.ExecuteNonQuery();
    Show(connection, "nach den Pragmas von LUI:");
}

Console.WriteLine();
Console.WriteLine("=== C) welche Pragmas darf eine read-only-Verbindung setzen? ===");
foreach (var pragma in new[]
{
    "busy_timeout = 5000",
    "synchronous = NORMAL",
    "cache_size = -524288",
    "mmap_size = 268435456",
    "journal_mode = WAL",
    "foreign_keys = ON",
})
{
    try
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        command.ExecuteNonQuery();
        Console.WriteLine($"  PRAGMA {pragma,-28} OK");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"  PRAGMA {pragma,-28} FEHLER: {exception.Message}");
    }
}
