using System.Globalization;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using System.Collections.Concurrent;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Typos;

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

    // One typo dictionary per index of the catalog, however many times the index is ensured (review of pull
    // request 10, S1): two instances would build 138 MB each and not see each other's writes.
    private readonly ConcurrentDictionary<string, TypoDictionary> _typoDictionaries = new(StringComparer.Ordinal);
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

    internal TimeProvider TimeProvider => _options.TimeProvider;

    /// <summary>
    /// Opens a catalog: checks the SQLite that is loaded, sets the pragmas
    /// and creates <c>lui_meta</c> if it is missing.
    /// <para>
    /// <b>This switches the database to write-ahead logging</b> unless
    /// <see cref="LuiCatalogOptions.WriteAheadLog"/> says otherwise. WAL is a
    /// property of the file: it stays, adds <c>-wal</c> and <c>-shm</c> files and does
    /// not work on network file systems. Turn it off when LUI shares the database of
    /// an application that chose its own mode.
    /// </para>
    /// </summary>
    /// <param name="connectionString">The database: the one of the application, or a file of LUI's own (E9).</param>
    /// <param name="options">The settings; <see langword="null"/> takes the defaults.</param>
    /// <returns>The catalog; dispose it when the application ends.</returns>
    /// <exception cref="LuiEnvironmentException">
    /// SQLite is too old, has no FTS5 trigram tokenizer, or folds text
    /// differently from the table the library was measured with.
    /// </exception>
    public static LuiCatalog Open(string connectionString, LuiCatalogOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        options ??= new LuiCatalogOptions();

        EnsureSqliteVersion(LoadedSqliteVersion());
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

    /// <summary>
    /// Starts describing an index: <c>Index("tags").SingleWord().Owned&lt;long&gt;().Ensure()</c>. Each step
    /// offers only what may follow it, so an index without a kind or a source does not compile.
    /// </summary>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <returns>The first step: the kind of the index.</returns>
    public LuiIndexBuilder Index(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new LuiIndexBuilder(this, Identifier.ValidateIndexName(name, nameof(name)));
    }

    /// <summary>
    /// Removes an index with its tables, indexes and triggers and its row in <c>lui_meta</c>. It works whatever
    /// the stored definition is — it is the way out of a <see cref="LuiDefinitionMismatchException"/>. Index
    /// objects that were created before do not survive it: ensure the index again and use the new object.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <returns><see langword="true"/> when the index existed.</returns>
    public bool DropIndex(string name)
    {
        Identifier.ValidateIndexName(name, nameof(name));

        using var scope = Begin();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        var existed = ReadDefinition(connection, transaction, name) is not null;

        foreach (var statement in ContentSource.DropStatements(name))
        {
            Execute(connection, transaction, statement);
        }

        using (var meta = connection.CreateCommand())
        {
            meta.Transaction = transaction;
            meta.CommandText = "DELETE FROM lui_meta WHERE index_name = $name;";
            meta.Parameters.AddWithValue("$name", name);
            meta.ExecuteNonQuery();
        }

        transaction.Commit();

        // the words of the dropped index are not the words of the next one
        ForgetTypoDictionary(name);
        return existed;
    }

    /// <summary>
    /// Creates an index from its statements, or checks that the stored definition is the same — in one
    /// transaction, so that two processes cannot both create it.
    /// </summary>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    internal void EnsureIndex(string name, IndexDefinition definition, IEnumerable<string> createStatements)
    {
        using var scope = Begin();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        var stored = ReadDefinition(connection, transaction, name);
        if (stored is null)
        {
            foreach (var statement in createStatements)
            {
                Execute(connection, transaction, statement);
            }

            WriteDefinition(connection, transaction, name, definition);
        }
        else if (stored != definition)
        {
            throw new LuiDefinitionMismatchException(name, stored.ToJson(), definition.ToJson());
        }

        transaction.Commit();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string statement)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = statement;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Waits until the running operations are done, then releases the
    /// connections. Operations that start afterwards throw
    /// <see cref="ObjectDisposedException"/>.
    /// <para>
    /// The connection pool of <c>Microsoft.Data.Sqlite</c> belongs to the connection
    /// string: disposing a catalog clears the pool of <b>every</b> connection that uses
    /// the same string, the application's own included. At the end of the process that
    /// is what is wanted; if a catalog is disposed and opened again at run time, give
    /// LUI its own connection string (for instance another <c>Default Timeout</c>).
    /// </para>
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

    /// <summary>The typo dictionary of an index — the same object for every instance of that index.</summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="normalization">The normalisation of the index.</param>
    /// <param name="texts">Reads <c>(text, weight)</c> of every entry, when the dictionary is built.</param>
    internal TypoDictionary TypoDictionaryFor(
        string indexName,
        NormalizationOptions normalization,
        Func<IEnumerable<(string Text, int Weight)>> texts) =>
        _typoDictionaries.GetOrAdd(
            indexName,
            static (_, state) => new TypoDictionary(state.Normalization, state.Texts),
            (Normalization: normalization, Texts: texts));

    /// <summary>Forgets the typo dictionary of an index that was dropped: the next index of that name starts empty.</summary>
    internal void ForgetTypoDictionary(string indexName) => _typoDictionaries.TryRemove(indexName, out _);

    /// <summary>The lock that serialises the statements on <see cref="Writer"/>.</summary>
    internal Lock WriterGate { get; } = new();

    /// <summary>
    /// The connection of the catalog's own writes. SQLite has one writer per
    /// file (§15), so one long-lived connection serves them all — and keeps
    /// the prepared statements (see <see cref="Writing.WriteStatements"/>).
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

    private void WriteDefinition(
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
        command.Parameters.AddWithValue("$version", Meta.SchemaVersion);
        command.Parameters.AddWithValue(
            "$created",
            TimeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    /// <summary>The version of the SQLite library of this process — the same for every connection.</summary>
    private static string LoadedSqliteVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        return (string)command.ExecuteScalar()!;
    }

    /// <summary>Refuses a SQLite older than <see cref="MinimumSqliteVersion"/>, naming both versions.</summary>
    internal static void EnsureSqliteVersion(string loaded)
    {
        if (!Version.TryParse(loaded, out var parsed) || parsed < MinimumSqliteVersion)
        {
            throw new LuiEnvironmentException(
                $"LUI needs SQLite {MinimumSqliteVersion} or newer (the trigram tokenizer with "
                + $"remove_diacritics); the loaded SQLite is {loaded}.");
        }
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

    private void Initialize()
    {
        using var connection = OpenConnection();

        if (_options.WriteAheadLog)
        {
            using var wal = connection.CreateCommand();

            // WAL lets many readers run beside one writer (§7.5). It is a property of the
            // file; for a database that cannot have it (in memory) SQLite keeps its mode.
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            wal.ExecuteNonQuery();
        }

        using var meta = connection.CreateCommand();
        meta.CommandText = Meta.CreateTable;
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
