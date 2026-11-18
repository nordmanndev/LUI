using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Nordmann.LUI.Storage;

/// <summary>
/// The database of a catalog (ADR-0046, ADR-0015, ADR-0046, ADR-0066): opens connections with the pragmas of the
/// catalog, keeps the one long-lived connection of LUI's own writes and the lock that serialises them, and counts the
/// operations that are running, so that <see cref="Dispose"/> waits for them. It knows no index — the layers above it
/// ask it for connections (review DevEx D4: the catalog taken apart).
/// <para>
/// <b>LUI builds the connections</b> (ADR-0066): the path of the catalog, the mode of the method that opened it,
/// pooling on and a private cache — the application cannot set any of it.
/// </para>
/// <para>
/// It does not guard the file (ADR-0068): a catalogue opened with <c>OpenReadOnly</c> has read-only connections, SQLite
/// refuses every write on them, and <see cref="ReadOnlyRefusal"/> turns the refusal into a <see cref="LuiReadOnlyException"/>
/// that says why.
/// </para>
/// </summary>
internal sealed class Database : IDisposable
{
    private readonly LuiCatalogOptions _options;
    private readonly object _owner;
    private readonly object _lifetime = new();
    private int _running;
    private bool _disposed;
    private SqliteConnection? _writer;

    /// <summary>Whether LUI created the file and sets its journal mode — once, in <see cref="Initialize"/>.</summary>
    private readonly bool _setsJournalMode;

    /// <param name="owner">What an operation on a disposed database names: the catalog the application holds.</param>
    /// <param name="path">The full path of the file of the catalog.</param>
    /// <param name="mode">The mode of every connection of the catalog.</param>
    /// <param name="createdTheFile">Whether LUI created the file itself — the one case in which it sets the journal mode.</param>
    /// <param name="options">The settings of the catalog.</param>
    internal Database(object owner, string path, SqliteOpenMode mode, bool createdTheFile, LuiCatalogOptions options)
    {
        _owner = owner;
        _options = options;
        _setsJournalMode = createdTheFile;
        Path = path;
        Mode = mode;
        ConnectionString = BuildConnectionString(path, mode).ToString();
        ReadConnectionString = BuildConnectionString(path, SqliteOpenMode.ReadOnly).ToString();
    }

    /// <summary>The full path of the file of the catalog.</summary>
    internal string Path { get; }

    /// <summary>The mode of every connection of the catalog (ADR-0066).</summary>
    internal SqliteOpenMode Mode { get; }

    /// <summary>The connection string LUI built (ADR-0066) — the application never sees one.</summary>
    internal string ConnectionString { get; }

    /// <summary>
    /// The connection string of the read path (ADR-0067): the same file, <c>Mode=ReadOnly</c>, pooling on, a private
    /// cache — whichever way the catalogue was opened. It is a pool of its own only because the provider keeps one pool per
    /// connection string.
    /// </summary>
    internal string ReadConnectionString { get; }

    /// <summary>
    /// The plan of a connection (ADR-0066): the path of the file, the mode of the catalog, pooling on and a private
    /// cache. Pooling and the cache mode are LUI's — an application that turned the pooling off would open a physical
    /// connection per query (136,6 µs instead of 13,0 µs), and a shared cache would put the connections of a process into
    /// SQLite's shared-cache mode, with table locks.
    /// </summary>
    /// <param name="path">The full path of the file of the catalog.</param>
    /// <param name="mode">The mode of the connection.</param>
    /// <returns>The plan; the connection string is what its <c>ToString</c> says.</returns>
    internal static SqliteConnectionStringBuilder BuildConnectionString(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = true,
            Cache = SqliteCacheMode.Private,
        };

    /// <summary>SQLite's primary result code for a write into a database that may not be written (<c>SQLITE_READONLY</c>).</summary>
    private const int SqliteReadOnly = 8;

    /// <summary>
    /// Whether SQLite refused a write because the database may not be written (ADR-0068). <b>SQLite is the guard</b>: LUI
    /// asks nothing beforehand, it translates the refusal — <see cref="ReadOnlyRefusal"/>.
    /// </summary>
    /// <param name="exception">The error of SQLite.</param>
    /// <returns><see langword="true"/> when the write was refused for want of rights.</returns>
    internal static bool IsReadOnlyRefusal(SqliteException exception) => exception.SqliteErrorCode == SqliteReadOnly;

    /// <summary>
    /// The exception for a write that SQLite refused (ADR-0068): it names the reason — the catalogue was opened with
    /// <c>OpenReadOnly</c>, or the file or its directory may not be written — the operation and the index. The error of SQLite
    /// is its inner exception.
    /// </summary>
    /// <param name="operation">What was refused, in words: "writing an entry".</param>
    /// <param name="indexName">The index the write was meant for.</param>
    /// <param name="refusal">The error of SQLite.</param>
    /// <returns>The exception to throw.</returns>
    internal LuiReadOnlyException ReadOnlyRefusal(string operation, string indexName, SqliteException refusal) =>
        new(operation, Mode == SqliteOpenMode.ReadOnly ? OpenedReadOnly(operation, indexName) : FileRefused(operation, indexName), refusal);

    /// <summary>
    /// Refuses the transaction of the application on a catalogue that was opened with <c>OpenReadOnly</c> (ADR-0068). SQLite
    /// cannot guard it: the transaction is on the connection of the application, which may write. Everything else is
    /// SQLite's to refuse.
    /// </summary>
    /// <param name="operation">What is refused, in words: "writing an entry".</param>
    /// <param name="indexName">The index the write was meant for.</param>
    /// <param name="transaction">The transaction of the application, if there is one.</param>
    /// <exception cref="LuiReadOnlyException">The catalogue was opened with <c>OpenReadOnly</c>.</exception>
    internal void RefuseTheTransactionOfTheApplication(string operation, string indexName, SqliteTransaction? transaction)
    {
        if (transaction is not null && Mode == SqliteOpenMode.ReadOnly)
        {
            throw new LuiReadOnlyException(operation, OpenedReadOnly(operation, indexName), inner: null);
        }
    }

    private string OpenedReadOnly(string operation, string indexName) =>
        $"The catalog '{Path}' was opened with OpenReadOnly — {operation} (index '{indexName}') is not allowed. "
        + "Open it with LuiCatalog.Open or LuiCatalog.OpenOrCreate to write.";

    private string FileRefused(string operation, string indexName) =>
        $"SQLite refuses to write to '{Path}' — {operation} (index '{indexName}') failed: the file or its directory may not "
        + "be written (rights, a read-only medium, or the '-wal' and '-shm' files cannot be created).";

    /// <summary>
    /// How long Microsoft.Data.Sqlite retries a busy command, in seconds — its smallest value that is not "forever" (0).
    /// The busy timeout of the catalog is the wait of SQLite itself (ADR-0046).
    /// </summary>
    internal const int ProviderRetrySeconds = 1;

    /// <summary>The lock that serialises the statements on <see cref="Writer"/>.</summary>
    internal Lock WriterGate { get; } = new();

    /// <summary>
    /// The connection of the catalog's own writes. SQLite has one writer per file (ADR-0015, ADR-0046), so one long-lived connection
    /// serves them all — and keeps the prepared statements (ADR-0009). A catalogue opened with <c>OpenReadOnly</c> has a
    /// writer like any other, in the read-only mode of its connections: SQLite refuses what it writes (ADR-0068).
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

    /// <summary>Opens a connection with the pragmas of the catalog applied, in the mode of the catalogue.</summary>
    internal SqliteConnection OpenConnection() => Open(ConnectionString);

    /// <summary>
    /// Opens the connection of the read path (ADR-0067): <b>read-only</b>, always — the read path never writes
    /// (ADR-0033), and a read-only connection says it to SQLite instead of a convention. The pragmas are the three of a
    /// connection and not the journal mode, which a read-only connection cannot set.
    /// </summary>
    internal SqliteConnection OpenReadConnection() => Open(ReadConnectionString);

    private SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();

            // Microsoft.Data.Sqlite retries a busy command on its own until the timeout of the command (30 s by default),
            // and every retry waits the busy timeout of SQLite again: the wait was 31 s whatever BusyTimeout said
            // (issue #70). The retries end after the first wait now — one second is the floor, 0 would mean "never" —
            // and the busy timeout of SQLite below is the wait. The timeout of the command bounds only the retries of
            // a busy command, not the time a statement runs (ADR-0046). Commands created afterwards inherit it.
            connection.DefaultTimeout = ProviderRetrySeconds;

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
    /// Switches the file to write-ahead logging — once, and <b>only when LUI created the file itself</b> (ADR-0066 — the
    /// journal mode). A file that was there keeps its mode, whether it holds tables or not: LUI neither switches it nor
    /// resets it. <c>Open</c> and <c>OpenReadOnly</c> never create the file, so they never set the mode (ADR-0068).
    /// </summary>
    internal void Initialize(SqliteConnection connection)
    {
        if (!_setsJournalMode)
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

        // a file that stays locked by a pooled connection cannot be deleted: the pool of the writer and the pool of the read path
        using (var connection = new SqliteConnection(ConnectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        using var reader = new SqliteConnection(ReadConnectionString);
        SqliteConnection.ClearPool(reader);
    }

    /// <summary>
    /// The pragmas of a connection: the three that belong to it (ADR-0066) — the busy timeout, the synchronous and the
    /// cache size. The journal mode is not among them: it is a property of the file, and a read-only connection cannot
    /// set it.
    /// </summary>
    internal string Pragmas()
    {
        var pragmas = _options.Pragmas;
        var timeout = (long)pragmas.BusyTimeout.TotalMilliseconds;
        var statement = string.Create(
            CultureInfo.InvariantCulture,
            $"PRAGMA busy_timeout = {timeout}; PRAGMA synchronous = {SqliteName(pragmas.Synchronous)};");

        if (pragmas.CacheSizeMegabytes is { } cache)
        {
            // a negative cache_size is a size in KiB
            statement += string.Create(CultureInfo.InvariantCulture, $" PRAGMA cache_size = -{cache * 1024L};");
        }

        return statement;
    }

    /// <summary>The name SQLite knows the value of the synchronous under.</summary>
    private static string SqliteName(LuiSynchronous synchronous) => synchronous switch
    {
        LuiSynchronous.Normal => "NORMAL",
        LuiSynchronous.Full => "FULL",
        _ => throw new ArgumentOutOfRangeException(
            nameof(synchronous),
            synchronous,
            $"The synchronous of a connection is {string.Join(" or ", Enum.GetNames<LuiSynchronous>())}."),
    };

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
