using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Testing;

/// <summary>
/// A database file of its own for one test, deleted afterwards. A file and not
/// <c>:memory:</c>, because every connection of the pool must see the same
/// database — that is how the library works.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _path;
    private readonly bool _keep;
    private readonly List<LuiCatalog> _catalogs = [];

    public TestDatabase()
        : this(null)
    {
    }

    /// <summary>
    /// A database at a fixed path that is kept when the test ends — for the
    /// measurements, whose corpus takes minutes to build.
    /// </summary>
    public TestDatabase(string? keepAt)
    {
        _keep = keepAt is not null;
        _path = keepAt ?? Path.Combine(Path.GetTempPath(), $"lui-test-{Guid.NewGuid():N}.db");
        if (keepAt is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keepAt))!);
        }
    }

    public string FilePath => _path;

    public string ConnectionString => $"Data Source={_path}";

    public LuiCatalog Open(LuiCatalogOptions? options = null)
    {
        var catalog = LuiCatalog.Open(ConnectionString, options);
        _catalogs.Add(catalog);
        return catalog;
    }

    /// <summary>A plain connection, for looking at what the library left in the file.</summary>
    public SqliteConnection Connect()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// The size of the database file in megabytes, after a checkpoint has moved the write-ahead log into it — what an
    /// application has on its disk (LUI-Spezifikation §16, "Dateigröße").
    /// </summary>
    public double FileMegabytes()
    {
        using (var connection = Connect())
        using (var checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }

        return new FileInfo(_path).Length / 1_048_576.0;
    }

    public T Scalar<T>(string sql)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        foreach (var catalog in _catalogs)
        {
            catalog.Dispose();
        }

        using (var connection = new SqliteConnection(ConnectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        if (_keep)
        {
            return;
        }

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }
}
