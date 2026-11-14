// Probe: Prepare() mit benannten Parametern und gesetzter Transaktion.

using Microsoft.Data.Sqlite;

var path = Path.Combine(Path.GetTempPath(), $"lui-prep-{Guid.NewGuid():N}.db");
using (var setup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
{
    setup.Open();
    using var create = setup.CreateCommand();
    create.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT);";
    create.ExecuteNonQuery();
    using var insert = setup.CreateCommand();
    insert.CommandText = "INSERT INTO t (name) VALUES ($name);";
    var parameter = insert.CreateParameter();
    parameter.ParameterName = "$name";
    parameter.Value = "Apfel";
    insert.Parameters.Add(parameter);
    insert.ExecuteNonQuery();
}

// 1) Prepare() mit benannten Parametern, die Transaktion ZUERST gesetzt
try
{
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
    connection.Open();
    using var transaction = connection.BeginTransaction(deferred: true);

    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT id, name FROM t WHERE name = $name;";
    var name = command.CreateParameter();
    name.ParameterName = "$name";
    name.Value = "Apfel";
    command.Parameters.Add(name);

    command.Prepare();

    var rows = 0;
    for (var i = 0; i < 3; i++)
    {
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows++;
        }
    }

    transaction.Commit();
    Console.WriteLine($"  Prepare() + Transaktion zuerst + benannte Parameter: OK, {rows} Zeilen ueber 3 Ausfuehrungen gelesen");
}
catch (Exception exception)
{
    Console.WriteLine($"  Prepare() + Transaktion zuerst: FEHLER {exception.GetType().Name}: {exception.Message}");
}

// 2) dieselbe Anweisung ohne Prepare() (Microsoft.Data.Sqlite cached sie je Verbindung selbst)
try
{
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
    connection.Open();
    using var transaction = connection.BeginTransaction(deferred: true);

    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT id, name FROM t WHERE name = $name;";
    var name = command.CreateParameter();
    name.ParameterName = "$name";
    name.Value = "Apfel";
    command.Parameters.Add(name);

    var rows = 0;
    for (var i = 0; i < 3; i++)
    {
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows++;
        }
    }

    transaction.Commit();
    Console.WriteLine($"  ohne Prepare(), dieselbe Anweisung dreimal:            OK, {rows} Zeilen gelesen");
}
catch (Exception exception)
{
    Console.WriteLine($"  ohne Prepare(): FEHLER {exception.GetType().Name}: {exception.Message}");
}

// 3) Prepare() und dann die Transaktion setzen (die Reihenfolge, die vorher scheiterte)
try
{
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT id, name FROM t WHERE name = $name;";
    var name = command.CreateParameter();
    name.ParameterName = "$name";
    name.Value = "Apfel";
    command.Parameters.Add(name);
    command.Prepare();

    using var transaction = connection.BeginTransaction(deferred: true);
    command.Transaction = transaction;
    using var reader = command.ExecuteReader();
    var rows = 0;
    while (reader.Read())
    {
        rows++;
    }

    transaction.Commit();
    Console.WriteLine($"  Prepare() vor der Transaktion:                        OK, {rows} Zeilen gelesen");
}
catch (Exception exception)
{
    Console.WriteLine($"  Prepare() vor der Transaktion: FEHLER {exception.GetType().Name}: {exception.Message}");
}
