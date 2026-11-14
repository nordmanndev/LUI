// Probe: was passiert, wenn ein Katalog einen Embedded- und einen Linked-Index mischt?
//
// Fragen:
//   1) In wie vielen Dateien landen die Tabellen?
//   2) Was macht LUI mit dem Journal-Modus der Datei der Anwendung?
//   3) Funktionieren beide Indizes in einem Katalog?

using Microsoft.Data.Sqlite;
using Nordmann.LUI;

var file = Path.Combine(Path.GetTempPath(), $"lui-mix-{Guid.NewGuid():N}.db");

// 1) die Datei der Anwendung mit ihrer Tabelle
using (var app = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ToString()))
{
    app.Open();
    using var create = app.CreateCommand();
    create.CommandText = "CREATE TABLE notes (id INTEGER PRIMARY KEY, text TEXT); INSERT INTO notes (text) VALUES ('Berlin');";
    create.ExecuteNonQuery();
}

static string Journal(SqliteConnection connection)
{
    using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA journal_mode;";
    return Convert.ToString(command.ExecuteScalar()) ?? "?";
}

using (var app = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ToString()))
{
    app.Open();
    Console.WriteLine($"  der Journal-Modus der Anwendungsdatei, bevor LUI sie oeffnet: {Journal(app)}");
}

// 2) der Katalog auf der Datei der Anwendung: ein Embedded- und ein Linked-Index
using (var catalog = LuiCatalog.Open($"Data Source={file}"))
{
    var embedded = catalog.Create("tags", index => index.SingleWord());
    embedded.Upsert("hauptstadt", 10);
    embedded.Upsert("hauptbahnhof", 5);

    var linked = catalog.CreateLinked("notes", index => index.SingleWord().LinkTo("notes", "id", "text"));
    linked.Sync();

    Console.WriteLine("  beide Indizes im selben Katalog: angelegt.");

    var embeddedHits = embedded.Search("haupt").ToList();
    Console.WriteLine($"  der Embedded-Index findet: {string.Join(", ", embeddedHits.Select(h => h.Key))}");
    var linkedHits = linked.Search("Berlin").ToList();
    Console.WriteLine($"  der Linked-Index findet:   {string.Join(", ", linkedHits.Select(h => h.Key))}");
}

// 3) was liegt in der Datei?
using (var app = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ToString()))
{
    app.Open();
    using var tables = app.CreateCommand();
    tables.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name;";
    var names = new List<string>();
    using (var reader = tables.ExecuteReader())
    {
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }
    }

    Console.WriteLine($"  der Journal-Modus nach dem Oeffnen durch LUI: {Journal(app)}");
    Console.WriteLine($"  die Tabellen in EINER Datei ({names.Count}): {string.Join(", ", names)}");
}

// 4) der Kampf um den Modus: die Anwendung will zurück auf rollback
using (var app = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ToString()))
{
    app.Open();
    using var rollback = app.CreateCommand();
    rollback.CommandText = "PRAGMA journal_mode = DELETE;";
    rollback.ExecuteNonQuery();
    Console.WriteLine($"  die Anwendung setzt den Modus zurueck:      {Journal(app)}");
}

using (var catalog = LuiCatalog.Open($"Data Source={file}"))
{
    _ = catalog.Index("tags");
}

using (var app = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ToString()))
{
    app.Open();
    Console.WriteLine($"  nach dem naechsten Oeffnen durch LUI:        {Journal(app)}");
}
