// Probe: die drei Punkte des Owners, gemessen in .NET mit der echten Bibliothek.
//
// 1) Der Preis des jetzigen Lesepfads: je Anfrage eine neue Verbindung (Microsoft.Data.Sqlite poolt, aber die
//    PRAGMAs laufen bei jedem Open) + ein neu gebautes Kommando mit zwei neuen Parametern.
// 2) Der Preis von PRAGMA mmap_size bei jedem Verbindungsaufbau (SQLite hebt die alte Abbildung auf und bildet neu ab).
// 3) Kann eine ReadOnly-Verbindung eine WAL-Datei in einem schreibgeschuetzten Verzeichnis lesen?
//
// Alles gegen dieselbe Datei, alles auf einem lokalen Datentraeger (/tmp).

using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Nordmann.LUI;
using Nordmann.LUI.Testing;

const int Entries = 1_000_000;
const int Queries = 200;

var database = new TestDatabase();
var catalog = catalog0(database);
var index = catalog.Create("bench", builder => builder.SingleWord().TypoTolerance());

Console.WriteLine($"baue den Index ({Entries} Eintraege) …");
var corpus = Corpus.From(CorpusFile.Open("words.txt", Entries, 0));
var started = Stopwatch.StartNew();
index.Sync(corpus.Entries(), new LuiSyncOptions { BatchSize = 100_000 });
Console.WriteLine($"  fertig in {started.Elapsed.TotalSeconds:F1} s");

var file = database.Connect().DataSource;
Console.WriteLine($"  die Datei: {file} ({new FileInfo(file).Length / 1048576.0:F0} MB)");

// ------------------------------------------------------------------------------------------------------------------
// 1) der jetzige Lesepfad gegen eine gehaltene Verbindung mit vorbereiteter Anweisung
// ------------------------------------------------------------------------------------------------------------------

Console.WriteLine("\n=== 1) der Lesepfad: heute gegen eine gehaltene read-only Verbindung ===");

// warm
for (var i = 0; i < 20; i++)
{
    index.Query("b").Candidates(500).Execute();
}

var today = new List<double>();
for (var i = 0; i < Queries; i++)
{
    var began = Stopwatch.GetTimestamp();
    index.Query("b").Candidates(500).Execute();
    today.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
}

// die gehaltene read-only Verbindung mit einer vorbereiteten Anweisung
var readOnly = new SqliteConnectionBuilder(new SqliteConnectionStringBuilder(file) { Mode = SqliteOpenMode.ReadOnly }.ToString());
using (var held = readOnly.Open())
{
    using var pragma = held.CreateCommand();
    pragma.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA cache_size = -524288;";
    pragma.ExecuteNonQuery();

    using var candidates = held.CreateCommand();
    candidates.CommandText =
        "SELECT f.rowid, c.text FROM bench_fts f JOIN bench c ON c.doc_id = (f.rowid & 4294967295) "
        + "WHERE bench_fts MATCH ? ORDER BY f.rowid LIMIT ?";
    candidates.Parameters.Add(new SqliteParameter { Value = "b" });
    candidates.Parameters.Add(new SqliteParameter { Value = 501 });
    candidates.Prepare();

    using var transaction = held.BeginTransaction(deferred: true);
    var heldSamples = new List<double>();
    for (var i = 0; i < Queries; i++)
    {
        var began = Stopwatch.GetTimestamp();
        using var reader = candidates.ExecuteReader();
        var rows = 0;
        while (reader.Read())
        {
            rows++;
        }

        heldSamples.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
    }

    Console.WriteLine($"  heute (neue Verbindung, neues Kommando): p50 {Median(today):F3} ms");
    Console.WriteLine($"  gehaltene read-only Verbindung, vorbereitet: p50 {Median(heldSamples):F3} ms");
    Console.WriteLine($"  -> {(Median(today) - Median(heldSamples)):F3} ms Unterschied je Anfrage");
}

// ------------------------------------------------------------------------------------------------------------------
// 2) der Preis von PRAGMA mmap_size bei jedem Verbindungsaufbau
// ------------------------------------------------------------------------------------------------------------------

Console.WriteLine("\n=== 2) der Preis des pragma mmap_size bei jedem Verbindungsaufbau ===");
foreach (var mmap in new long?[] { 2048L * 1024 * 1024, null })
{
    var samples = new List<double>();
    for (var i = 0; i < 30; i++)
    {
        var began = Stopwatch.GetTimestamp();
        using var connection = new SqliteConnection(file);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = mmap is null
            ? "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL; PRAGMA cache_size = -524288;"
            : $"PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL; PRAGMA cache_size = -524288; PRAGMA mmap_size = {mmap};";
        command.ExecuteNonQuery();
        samples.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
    }

    Console.WriteLine($"  {(mmap is null ? "ohne mmap_size " : "mit mmap_size  ")}: p50 {Median(samples):F3} ms je Verbindungsaufbau");
}

// ------------------------------------------------------------------------------------------------------------------
// 3) eine read-only Verbindung auf einer WAL-Datei in einem schreibgeschuetzten Verzeichnis
// ------------------------------------------------------------------------------------------------------------------

Console.WriteLine("\n=== 3) read-only Verbindung, WAL-Datei, schreibgeschuetztes Verzeichnis ===");
var locked = "/tmp/lui-locked";
if (Directory.Exists(locked))
{
    Directory.Delete(locked, true);
}

Directory.CreateDirectory(locked);
var copy = Path.Combine(locked, "bench.db");
File.Copy(file, copy);
foreach (var suffix in new[] { "-wal", "-shm" })
{
    if (File.Exists(copy + suffix))
    {
        File.Copy(copy + suffix, copy + suffix);
    }
}

// die Datei in WAL-Modus bringen und beschreiben
using (var writer = new SqliteConnection(copy))
{
    writer.Open();
    using var wal = writer.CreateCommand();
    wal.CommandText = "PRAGMA journal_mode = WAL;";
    wal.ExecuteNonQuery();
    using var insert = writer.CreateCommand();
    insert.CommandText = "INSERT INTO bench(key, text, weight, priority) VALUES ('probe', 'probe', 1, 3);";
    insert.ExecuteNonQuery();
}

File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
Directory.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);

try
{
    var builder = new SqliteConnectionStringBuilder(copy) { Mode = SqliteOpenMode.ReadOnly };
    using var ro = new SqliteConnection(builder.ToString());
    ro.Open();
    using var read = ro.CreateCommand();
    read.CommandText = "SELECT count(*) FROM bench;";
    var count = read.ExecuteScalar();
    Console.WriteLine($"  read-only, Verzeichnis schreibgeschuetzt: {count} Zeilen gelesen  OK");
}
catch (Exception exception)
{
    Console.WriteLine($"  read-only, Verzeichnis schreibgeschuetzt: FEHLER {exception.GetType().Name}: {exception.Message}");
}
finally
{
    Directory.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}

static double Median(List<double> values)
{
    var sorted = values.Order().ToArray();
    return sorted[sorted.Length / 2];
}

static LuiCatalog catalog0(TestDatabase database) =>
    database.Open(new LuiCatalogOptions { CacheSizeMegabytes = 512, MemoryMapMegabytes = 2048 });
