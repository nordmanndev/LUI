using System.Globalization;
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
    /// Starts describing an index: <c>Index("tags").SingleWord().Create()</c>. Each step offers only what may
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
    /// Opens an index that exists, and writes nothing (ADR-0033): the definition is read out of <c>lui_meta</c>, the
    /// index is rebuilt from it, and the file is not touched — no table, no definition, no write lock. It is the way
    /// into a database that is written elsewhere: a replica, a file that is read-only, a service that may not write.
    /// <para>
    /// The index comes as the untyped facade: the key as the text it is stored as, the payload as the raw JSON
    /// (<see langword="null"/> when the index stores none). The similarity is the default of the kind of the index,
    /// and the typo tolerance is off — a query asks for a correction (<see cref="LuiQueryOptions.TypoTolerance"/>).
    /// </para>
    /// </summary>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <returns>The index; it reads and does not write.</returns>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">
    /// The index was written by another version of the library: another schema version or another folding.
    /// </exception>
    public ILuiDocumentIndex OpenIndex(string name)
    {
        var entry = ReadEntry(name);
        var source = BuildSource(name, entry.Definition);

        return new DocumentIndex(Database, TypoDictionaries, new DocumentSource(source), source.Rules.DefaultSimilarity);
    }

    /// <summary>
    /// Opens an index that exists, and writes nothing (ADR-0033) — with the types of the application: the key is a
    /// <see cref="long"/> or a <see cref="string"/>, the payload the object of a serialiser.
    /// <para>
    /// The types are checked against the file: a key type the index does not have, or a payload the index does not
    /// store, is an error that says which one it is. The index is opened, not created: it reads and does not write.
    /// </para>
    /// </summary>
    /// <typeparam name="TKey">The key of the application: <see cref="long"/> or <see cref="string"/>.</typeparam>
    /// <typeparam name="TPayload">The payload that comes back with a hit.</typeparam>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <param name="payload">The serialiser of the payload; <see langword="null"/> when the index stores none.</param>
    /// <returns>The index; it reads and does not write.</returns>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">
    /// The index was written by another version of the library, or it is not the index the caller asked for: another
    /// schema version, another folding, another key type, another payload.
    /// </exception>
    public ILuiEmbeddedIndex<TKey, TPayload> OpenIndex<TKey, TPayload>(
        string name,
        ILuiPayloadSerializer<TPayload>? payload = null)
    {
        var entry = ReadEntry(name);
        var definition = entry.Definition;
        EnsureEmbedded(name, definition);
        EnsureKeyType<TKey>(name, definition);
        EnsurePayload<TPayload>(name, definition, payload);

        var source = new EmbeddedSource(name, definition);
        var index = new EmbeddedIndex<TKey, TPayload>(
            Database,
            TypoDictionaries,
            source,
            payload,
            source.Rules.DefaultSimilarity,
            typoTolerance: false);

        return new ReadOnlyEmbeddedIndex<TKey, TPayload>(index);
    }

    /// <summary>The stored index of a name, with the two checks every index has to pass: the schema version it was written
    /// with, and the folding it was stored with.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <returns>What <c>lui_meta</c> holds about the index.</returns>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The schema version or the folding is another one.</exception>
    private IndexStoreEntry ReadEntry(string name)
    {
        var validated = Identifier.ValidateIndexName(name, nameof(name));
        Database.ThrowIfDisposed();

        var entry = IndexStore.ReadIndex(Database, validated)
            ?? throw new LuiIndexNotFoundException(validated);

        if (entry.SchemaVersion != Meta.SchemaVersion)
        {
            throw Mismatch(
                validated,
                $"the schema version {entry.SchemaVersion.ToString(CultureInfo.InvariantCulture)}",
                $"the schema version {Meta.SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.Equals(entry.Definition.FoldTable, TokenizerFold.Version, StringComparison.Ordinal))
        {
            throw Mismatch(
                validated,
                $"the fold table {entry.Definition.FoldTable}",
                $"the fold table {TokenizerFold.Version}");
        }

        return entry;
    }

    /// <summary>The content source of the stored definition: <c>Embedded</c> or <c>Linked</c>.</summary>
    private static ContentSource BuildSource(string name, IndexDefinition definition) =>
        definition.Source switch
        {
            EmbeddedSource.SourceName => new EmbeddedSource(name, definition),
            LinkedSource.SourceName => new LinkedSource(name, definition),
            var other => throw Mismatch(name, $"the content source {other}", "a content source of this version (Embedded or Linked)"),
        };

    /// <summary>The typed path opens an embedded index; a linked one is read with the untyped facade.</summary>
    private static void EnsureEmbedded(string name, IndexDefinition definition)
    {
        if (!string.Equals(definition.Source, EmbeddedSource.SourceName, StringComparison.Ordinal))
        {
            throw Mismatch(name, $"the content source {definition.Source}", $"the content source {EmbeddedSource.SourceName}");
        }
    }

    /// <summary>The key type of the caller against the key type of the file.</summary>
    private static void EnsureKeyType<TKey>(string name, IndexDefinition definition)
    {
        var keyType = KeyCodec<TKey>.KeyType;
        if (!string.Equals(definition.KeyType, keyType, StringComparison.Ordinal))
        {
            throw Mismatch(name, $"the key type {definition.KeyType}", $"the key type {keyType}");
        }
    }

    /// <summary>The payload of the caller against the payload of the file.</summary>
    private static void EnsurePayload<TPayload>(
        string name,
        IndexDefinition definition,
        ILuiPayloadSerializer<TPayload>? payload)
    {
        var wanted = payload is not null;
        if (definition.Payload != wanted)
        {
            throw Mismatch(
                name,
                definition.Payload ? "a payload" : "no payload",
                wanted ? "a payload" : "no payload");
        }
    }

    /// <summary>
    /// The mismatch of the read path (ADR-0033): an index that is opened is never rebuilt, so the way out is another
    /// way to open it — or a new index.
    /// </summary>
    private static LuiDefinitionMismatchException Mismatch(string name, string stored, string requested) =>
        new(
            name,
            stored,
            requested,
            "An index that is opened is never rebuilt silently: ask for the types it has "
            + "(OpenIndex(name) — the untyped facade — gives the key as the text it is stored as and the payload as the "
            + "raw JSON), or remove the index with LuiCatalog.DropIndex(name) and create it again.");

    /// <summary>
    /// Removes an index with its tables, indexes and triggers and its row in <c>lui_meta</c>. It works whatever
    /// the stored definition is — it is the way out of a <see cref="LuiDefinitionMismatchException"/>. Index
    /// objects that were created before do not survive it: create the index again and use the new object.
    /// <para>
    /// For a <c>Linked</c> index the application's table and its rows stay. Its triggers, if the index has some on
    /// the table, are removed only with <paramref name="dropTriggers"/>: they write into the tables that go, so they
    /// cannot stay — but a migration of the application may count on them, and their removal is not done silently
    /// (review of pull request 16, M3). <b>After the drop and a new <c>Create()</c> a linked index is empty and has no
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
