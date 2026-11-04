using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexes;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
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
/// <para>
/// The catalog is a facade (review DevEx D4): the connections, the writer and the lifetime are its
/// <see cref="Storage.Database"/>, the stored definitions and their statements <see cref="IndexStore"/>, the typo
/// dictionaries <see cref="Typos.TypoDictionaries"/>. No layer below the public API knows the catalog.
/// </para>
/// </summary>
public sealed class LuiCatalog : IDisposable
{
    /// <summary>The oldest SQLite the library runs on: the trigram tokenizer needs <c>remove_diacritics</c>.</summary>
    internal static readonly Version MinimumSqliteVersion = new(3, 45, 0);

    private readonly LuiCatalogOptions _options;

    private LuiCatalog(string connectionString, LuiCatalogOptions options)
    {
        _options = options;
        Database = new Database(this, connectionString, options);
    }

    /// <summary>The connection string the catalog was opened with.</summary>
    public string ConnectionString => Database.ConnectionString;

    internal TimeProvider TimeProvider => _options.TimeProvider;

    /// <summary>The database: connections, the writer, the operations that are running.</summary>
    internal Database Database { get; }

    /// <summary>The typo dictionaries of the indexes, one per index.</summary>
    internal TypoDictionaries TypoDictionaries { get; } = new();

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
            IndexStore.Initialize(catalog.Database);
            return catalog;
        }
        catch
        {
            catalog.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Starts describing an index: <c>Index("tags").SingleWord().Ensure()</c>. Each step offers only what may
    /// follow it, so an index without a kind does not compile.
    /// </summary>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <returns>The first step: the kind of the index.</returns>
    public LuiIndexKindBuilder Index(string name)
    {
        Database.ThrowIfDisposed();
        return new LuiIndexKindBuilder(this, Identifier.ValidateIndexName(name, nameof(name)));
    }

    /// <summary>
    /// Removes an index with its tables, indexes and triggers and its row in <c>lui_meta</c>. It works whatever
    /// the stored definition is — it is the way out of a <see cref="LuiDefinitionMismatchException"/>. Index
    /// objects that were created before do not survive it: ensure the index again and use the new object.
    /// <para>
    /// For a <c>Linked</c> index the application's table and its rows stay. Its triggers, if the index has some on
    /// the table, are removed only with <paramref name="dropTriggers"/>: they write into the tables that go, so they
    /// cannot stay — but a migration of the application may count on them, and their removal is not done silently
    /// (review of pull request 16, M3). <b>After the drop and a new <c>Ensure()</c> a linked index is empty and has no
    /// triggers</b>: <c>Sync()</c> fills it, and <c>CreateTriggers()</c> — or the migration — sets them again.
    /// </para>
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="dropTriggers">Removes the triggers of a linked index on the application's table, too.</param>
    /// <returns><see langword="true"/> when the index existed.</returns>
    /// <exception cref="InvalidOperationException">A linked index has triggers, and <paramref name="dropTriggers"/> is not set.</exception>
    public bool DropIndex(string name, bool dropTriggers = false)
    {
        Identifier.ValidateIndexName(name, nameof(name));
        var existed = IndexStore.Drop(Database, name, dropTriggers);

        // the words of the dropped index are not the words of the next one
        TypoDictionaries.Forget(name);
        return existed;
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
    public void Dispose() => Database.Dispose();

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
}
