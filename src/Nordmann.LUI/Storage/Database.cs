using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Storage;

/// <summary>
/// The database of a catalog (LUI-Spezifikation §7.5, §15): opens connections with the pragmas of the catalog, keeps
/// the one long-lived connection of LUI's own writes and the lock that serialises them, and counts the operations
/// that are running, so that <see cref="Dispose"/> waits for them. It knows no index — the layers above it ask it for
/// connections (review DevEx D4: the catalog taken apart).
/// </summary>
internal sealed class Database : IDisposable
{
    private readonly LuiCatalogOptions _options;
    private readonly object _owner;
    private readonly object _lifetime = new();
    private int _running;
    private bool _disposed;
    private SqliteConnection? _writer;

    /// <param name="owner">What an operation on a disposed database names: the catalog the application holds.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="options">The settings of the catalog.</param>
    internal Database(object owner, string connectionString, LuiCatalogOptions options)
    {
        _owner = owner;
        ConnectionString = connectionString;
        _options = options;
    }

    /// <summary>The connection string the catalog was opened with.</summary>
    internal string ConnectionString { get; }

    /// <summary>The lock that serialises the statements on <see cref="Writer"/>.</summary>
    internal Lock WriterGate { get; } = new();

    /// <summary>
    /// The connection of the catalog's own writes. SQLite has one writer per file (§15), so one long-lived connection
    /// serves them all — and keeps the prepared statements (ADR-0009).
    /// </summary>
    internal SqliteConnection Writer
    {
        get
        {
            lock (_lifetime)
            {
                ThrowIfDisposed();
                return _writer ??= OpenConnection();
            }
        }
    }

    /// <summary>Marks the start of an operation. The returned scope has to be disposed when the operation ends.</summary>
    internal OperationScope Begin()
    {
        lock (_lifetime)
        {
            ThrowIfDisposed();
            _running++;
        }

        return new OperationScope(this);
    }

    /// <summary>Throws when the catalog is disposed.</summary>
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, _owner);

    /// <summary>Opens a connection with the pragmas of the catalog applied.</summary>
    internal SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = Pragmas();
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Switches the file to write-ahead logging (§7.5), if the options ask for it. WAL is a property of the file; for
    /// a database that cannot have it (in memory) SQLite keeps its mode.
    /// </summary>
    internal void Initialize(SqliteConnection connection)
    {
        if (!_options.WriteAheadLog)
        {
            return;
        }

        using var wal = connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode = WAL;";
        wal.ExecuteNonQuery();
    }

    /// <summary>
    /// Waits until the running operations are done, then releases the connections. The pool of
    /// <c>Microsoft.Data.Sqlite</c> belongs to the connection string: it is cleared for every connection that uses it.
    /// </summary>
    public void Dispose()
    {
        lock (_lifetime)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            while (_running > 0)
            {
                Monitor.Wait(_lifetime);
            }
        }

        _writer?.Dispose();

        // a file that stays locked by a pooled connection cannot be deleted
        using var connection = new SqliteConnection(ConnectionString);
        SqliteConnection.ClearPool(connection);
    }

    private string Pragmas()
    {
        var timeout = (long)_options.BusyTimeout.TotalMilliseconds;
        var pragmas = string.Create(CultureInfo.InvariantCulture, $"PRAGMA busy_timeout = {timeout}; PRAGMA synchronous = NORMAL;");

        if (_options.CacheSizeMegabytes is { } cache)
        {
            // a negative cache_size is a size in KiB
            pragmas += string.Create(CultureInfo.InvariantCulture, $" PRAGMA cache_size = -{cache * 1024L};");
        }

        if (_options.MemoryMapMegabytes is { } map)
        {
            pragmas += string.Create(CultureInfo.InvariantCulture, $" PRAGMA mmap_size = {map * 1024L * 1024L};");
        }

        return pragmas;
    }

    private void End()
    {
        lock (_lifetime)
        {
            _running--;
            if (_running == 0)
            {
                Monitor.PulseAll(_lifetime);
            }
        }
    }

    /// <summary>One running operation; disposing it lets <see cref="Dispose"/> proceed.</summary>
    internal readonly struct OperationScope(Database database) : IDisposable
    {
        public void Dispose() => database.End();
    }
}
