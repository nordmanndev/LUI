using System.Globalization;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexing;
using Nordmann.LUI.Normalization;

namespace Nordmann.LUI;

/// <summary>
/// The entry point: knows the connection string and all indexes of a
/// database (LUI-Spezifikation §2, §12.1). A catalog and its indexes are
/// meant to be singletons; they are safe to use from many threads.
/// <para>
/// Every operation opens its own connection from the pool of
/// <c>Microsoft.Data.Sqlite</c>, so reads run in parallel (§15).
/// <see cref="Dispose"/> waits for the operations that are running.
/// </para>
/// </summary>
public sealed class LuiCatalog : IDisposable
{
    /// <summary>The oldest SQLite the library runs on: the trigram tokenizer needs <c>remove_diacritics</c>.</summary>
    internal static readonly Version MinimumSqliteVersion = new(3, 45, 0);

    private readonly LuiCatalogOptions _options;
    private readonly object _lifetime = new();
    private int _running;
    private bool _disposed;
    private SqliteConnection? _writer;

    private LuiCatalog(string connectionString, LuiCatalogOptions options)
    {
        ConnectionString = connectionString;
        _options = options;
    }

    /// <summary>The connection string the catalog was opened with.</summary>
    public string ConnectionString { get; }

    internal TimeProvider TimeProvider => _options.TimeProviderValue;

    /// <summary>
    /// Opens a catalog: checks the SQLite that is loaded, sets the pragmas
    /// and creates <c>lui_meta</c> if it is missing.
    /// </summary>
    /// <param name="connectionString">The database: the one of the application, or a file of LUI's own (E9).</param>
    /// <param name="configure">Optional settings.</param>
    /// <returns>The catalog; dispose it when the application ends.</returns>
    /// <exception cref="LuiEnvironmentException">
    /// SQLite is too old, has no FTS5 trigram tokenizer, or folds text
    /// differently from the table the library was measured with.
    /// </exception>
    public static LuiCatalog Open(string connectionString, Action<LuiCatalogOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new LuiCatalogOptions();
        configure?.Invoke(options);

        FoldProbe.EnsureVerified();

        var catalog = new LuiCatalog(connectionString, options);
        try
        {
            catalog.Initialize();
            return catalog;
        }
        catch
        {
            catalog.Dispose();
            throw;
        }
    }

    /// <summary>Starts describing an index.</summary>
    /// <typeparam name="TKey">The key of the application: <see cref="long"/> or <see cref="string"/>.</typeparam>
    /// <typeparam name="TPayload">The payload that comes back with a hit.</typeparam>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <returns>The builder; <c>Ensure()</c> finishes it.</returns>
    public LuiIndexBuilder<TKey, TPayload> Index<TKey, TPayload>(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new LuiIndexBuilder<TKey, TPayload>(this, Identifier.ValidateIndexName(name, nameof(name)));
    }

    /// <summary>
    /// Waits until the running operations are done, then releases the
    /// connections. Operations that start afterwards throw
    /// <see cref="ObjectDisposedException"/>.
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

        // The pool belongs to the connection string; a file that stays locked
        // by a pooled connection cannot be deleted.
        using var connection = new SqliteConnection(ConnectionString);
        SqliteConnection.ClearPool(connection);
    }

    /// <summary>The lock that serialises the statements on <see cref="Writer"/>.</summary>
    internal Lock WriterGate { get; } = new();

    /// <summary>
    /// The connection of the catalog's own writes. SQLite has one writer per
    /// file (§15), so one long-lived connection serves them all — and keeps
    /// the prepared statements (see <see cref="Indexing.WriteStatements"/>).
    /// </summary>
    internal SqliteConnection Writer
    {
        get
        {
            lock (_lifetime)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _writer ??= OpenConnection();
            }
        }
    }

    /// <summary>
    /// Marks the start of an operation. The returned scope has to be
    /// disposed when the operation ends.
    /// </summary>
    internal OperationScope Begin()
    {
        lock (_lifetime)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _running++;
        }

        return new OperationScope(this);
    }

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

    internal static IndexDefinition? ReadDefinition(SqliteConnection connection, SqliteTransaction? transaction, string indexName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT definition FROM lui_meta WHERE index_name = $name;";
        command.Parameters.AddWithValue("$name", indexName);

        return command.ExecuteScalar() is string json ? IndexDefinition.FromJson(json) : null;
    }

    internal void WriteDefinition(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string indexName,
        IndexDefinition definition)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO lui_meta(index_name, kind, source, definition, schema_version, created_at) "
            + "VALUES ($name, $kind, $source, $definition, $version, $created);";
        command.Parameters.AddWithValue("$name", indexName);
        command.Parameters.AddWithValue("$kind", definition.Kind.ToString());
        command.Parameters.AddWithValue("$source", definition.Source);
        command.Parameters.AddWithValue("$definition", definition.ToJson());
        command.Parameters.AddWithValue("$version", OwnedSchema.SchemaVersion);
        command.Parameters.AddWithValue(
            "$created",
            TimeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private string Pragmas()
    {
        var timeout = (long)_options.BusyTimeoutValue.TotalMilliseconds;
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

    private void Initialize()
    {
        using var connection = OpenConnection();

        using (var version = connection.CreateCommand())
        {
            version.CommandText = "SELECT sqlite_version();";
            var text = (string)version.ExecuteScalar()!;

            if (!Version.TryParse(text, out var parsed) || parsed < MinimumSqliteVersion)
            {
                throw new LuiEnvironmentException(
                    $"LUI needs SQLite {MinimumSqliteVersion} or newer (the trigram tokenizer with "
                    + $"remove_diacritics); the loaded SQLite is {text}.");
            }
        }

        using (var wal = connection.CreateCommand())
        {
            // WAL lets many readers run beside one writer (§7.5). It is a property of the
            // file; for a database that cannot have it (in memory) SQLite keeps its mode.
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            wal.ExecuteNonQuery();
        }

        using var meta = connection.CreateCommand();
        meta.CommandText = OwnedSchema.MetaTable;
        meta.ExecuteNonQuery();
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
    internal readonly struct OperationScope(LuiCatalog catalog) : IDisposable
    {
        public void Dispose() => catalog.End();
    }
}
