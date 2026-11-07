using System.Globalization;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexes;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Querying;
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
/// <para>
/// It has two ways into an index (ADR-0033): <c>Create</c> creates one — once, in the migration of the application —
/// and <see cref="Index(string)"/> or <see cref="LinkedIndex(string)"/> opens one that is there, without writing
/// anything. Neither names a type (ADR-0035): the payload is read where it is used. Which of them may write is decided by the file: a catalog that was opened with
/// <c>Mode=ReadOnly</c> refuses every write with a <see cref="LuiReadOnlyException"/>.
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

    /// <summary>The serialiser of the payloads (ADR-0035); <see langword="null"/> when the indexes store none.</summary>
    internal ILuiPayloadSerializer? PayloadSerializer => _options.PayloadSerializer;

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
    /// <para>
    /// A file that may not be written (<c>Mode=ReadOnly</c>) is opened as it is: no write-ahead logging, and no
    /// <c>lui_meta</c> — the table has to be there, and if it is not, the catalog says so (ADR-0033).
    /// </para>
    /// </summary>
    /// <param name="connectionString">The database: the one of the application, or a file of LUI's own (E9).</param>
    /// <param name="options">The settings; <see langword="null"/> takes the defaults.</param>
    /// <returns>The catalog; dispose it when the application ends.</returns>
    /// <exception cref="LuiEnvironmentException">
    /// SQLite is too old, has no FTS5 trigram tokenizer, or folds text
    /// differently from the table the library was measured with.
    /// </exception>
    /// <exception cref="LuiReadOnlyException">
    /// The file may not be written and has no <c>lui_meta</c>: a read-only catalog cannot create it.
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
    /// Creates an index: <c>catalog.Create("tags", index =&gt; index.SingleWord())</c>. It is the write path, and it belongs
    /// in the migration of the application: the index is created if it is missing, and the stored definition is compared
    /// with the one that was described if it is there.
    /// <para>
    /// The builder chain is the <b>parameter</b>, not the way in: the catalog takes a description and does the
    /// creating itself. The builder is not reachable from the catalog otherwise — the runtime path cannot create an
    /// index (ADR-0033). No type is named (ADR-0035): an entry carries a payload of any type the serialiser of the
    /// catalog writes.
    /// </para>
    /// </summary>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <param name="index">The description of the index: the kind and, if they are texts, the kind of the key.</param>
    /// <returns>The index; it reads and writes.</returns>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public ILuiEmbeddedIndex Create(string name, Func<LuiIndexKindBuilder, LuiEmbeddedIndexBuilder> index)
    {
        ArgumentNullException.ThrowIfNull(index);

        var builder = index(KindBuilder(name));
        return builder.Create();
    }

    /// <summary>
    /// Creates an index over a table of the application (<c>Linked</c>): the description starts with
    /// <c>LinkTo(…)</c>, and the index reads the table of the application instead of a table of the library.
    /// <para>
    /// The index starts empty: fill it with <c>Sync()</c>. That holds after a <c>DropIndex</c> too — a new linked
    /// index is empty and has no triggers until <c>Sync()</c> and, if wanted, <c>CreateTriggers()</c>.
    /// </para>
    /// </summary>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <param name="index">The description of the index: the kind, the table, the payload columns.</param>
    /// <returns>The index; it reads and writes.</returns>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    /// <exception cref="LuiLinkedTableException">The table or a column is missing, or the key is not the table's INTEGER PRIMARY KEY.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public ILuiLinkedIndex Create(string name, Func<LuiIndexKindBuilder, LuiLinkedIndexBuilder> index)
    {
        ArgumentNullException.ThrowIfNull(index);

        var builder = index(KindBuilder(name));
        return builder.Create();
    }

    /// <summary>
    /// Opens an embedded index that exists, and writes nothing (ADR-0033): the definition is read out of
    /// <c>lui_meta</c>, the index is rebuilt from it, and the file is not touched — no table, no definition, no write
    /// lock. It is the way into a database that is written elsewhere: a replica, a file that is read-only, a service
    /// that may not write.
    /// <para>
    /// No type is named (ADR-0035): the kind of the key comes from the file, and a payload is read where it is used,
    /// with <see cref="LuiHit.GetPayload{T}"/> and the serialiser of the catalog. The index it hands out writes when the
    /// file allows it — the file decides, not the index.
    /// </para>
    /// </summary>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <returns>The index; it reads, and it writes if the file allows it.</returns>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">
    /// The index was written by another version of the library (another schema version, another folding), or it is a
    /// linked index — open that with <see cref="LinkedIndex(string)"/>.
    /// </exception>
    public ILuiEmbeddedIndex Index(string name)
    {
        var entry = ReadEntry(name);
        EnsureEmbedded(name, entry.Definition);

        return OpenEmbedded(name, entry);
    }

    /// <summary>
    /// Builds the typo dictionary of every index of the catalog (ADR-0043) — the warm-up of a host in one call.
    /// <para>
    /// The dictionary belongs to the index of the catalog, not to the instance of it: an index that the application
    /// opens later sees the same dictionary and reports it as built (ADR-0025).
    /// </para>
    /// <para>
    /// The build takes seconds and needs memory — 138 MB for 685,829 words (ADR-0021). A host that wants corrections
    /// from its first query warms the catalog at the start; the registration <c>AddLuiWarmup()</c> of the hosting
    /// package does it for the host (ADR-0043).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Checked between the indexes.</param>
    /// <exception cref="LuiDefinitionMismatchException">
    /// An index of the file was written by another version of the library (another schema version, another folding).
    /// </exception>
    public void WarmUpTypoDictionaries(CancellationToken cancellationToken = default)
    {
        foreach (var name in IndexStore.ReadIndexNames(Database))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = ReadEntry(name);
            if (string.Equals(entry.Source, EmbeddedSource.SourceName, StringComparison.Ordinal))
            {
                OpenEmbedded(name, entry).WarmUpTypoDictionary(cancellationToken);
            }
            else
            {
                OpenLinked(name, entry.Definition, new NoPayload(name)).WarmUpTypoDictionary(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Applies the outbox of every linked index of the catalog: the rows the triggers noted come into the index
    /// (ADR-0015, ADR-0043).
    /// <para>
    /// The table of the application is not touched — the outbox is brought into the index, and the index is as current
    /// as the last call.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Checked between the indexes.</param>
    /// <returns>The number of rows brought in.</returns>
    /// <exception cref="LuiDefinitionMismatchException">
    /// An index of the file was written by another version of the library (another schema version, another folding).
    /// </exception>
    public int ApplyPending(CancellationToken cancellationToken = default)
    {
        var applied = 0;

        foreach (var name in IndexStore.ReadIndexNames(Database))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = ReadEntry(name);
            if (!string.Equals(entry.Source, LinkedSource.SourceName, StringComparison.Ordinal))
            {
                continue;
            }

            applied += OpenLinked(name, entry.Definition, new NoPayload(name)).ApplyPending(cancellationToken);
        }

        return applied;
    }

    /// <summary>
    /// The read path of an embedded index (ADR-0033): built from the stored definition, without writing anything.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="entry">What the file holds about the index.</param>
    /// <returns>The index; it reads, and it writes if the file allows it.</returns>
    private EmbeddedIndex OpenEmbedded(string name, IndexStoreEntry entry)
    {
        var source = new EmbeddedSource(name, entry.Definition);

        // an opened index takes the default of its kind and no typo tolerance: a query asks for a correction
        // (LuiQueryOptions.TypoTolerance)
        return new EmbeddedIndex(
            Database,
            TypoDictionaries,
            source,
            PayloadSerializer,
            source.Rules.DefaultSimilarity,
            typoTolerance: false);
    }

    /// <summary>
    /// Opens an index over a table of the application that exists and has no payload columns, and writes nothing
    /// (ADR-0033). A linked index keeps no copy of the row: the text of a hit is read from the table of the application
    /// when the hit is read.
    /// </summary>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <returns>The index; it reads, and it writes if the file allows it.</returns>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">
    /// The index is an embedded one, or it has payload columns — open that with a map,
    /// <see cref="LinkedIndex{TPayload}(string, Func{LuiPayloadRow, TPayload})"/>.
    /// </exception>
    public ILuiLinkedIndex LinkedIndex(string name)
    {
        var definition = ReadEntry(name).Definition;
        EnsureLinked(name, definition);

        var columns = definition.Link?.PayloadColumns ?? [];
        if (columns.Count > 0)
        {
            throw Mismatch(name, $"the payload columns ({string.Join(", ", columns)})", "no payload");
        }

        return OpenLinked(name, definition, new NoPayload(name));
    }

    /// <summary>
    /// Opens an index over a table of the application that exists and has payload columns, and writes nothing
    /// (ADR-0033). The columns are in the definition; the map is code of the application and is handed over here — the
    /// type of the payload follows from it, and the hit gives it back with <see cref="LuiHit.GetPayload{T}"/>.
    /// </summary>
    /// <typeparam name="TPayload">What the map builds.</typeparam>
    /// <param name="name">The name of the index: <c>^[a-z][a-z0-9_]{0,40}$</c>.</param>
    /// <param name="map">Builds the payload from the row of the table; it sees only the columns of the definition.</param>
    /// <returns>The index; it reads, and it writes if the file allows it.</returns>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index is an embedded one, or it has no payload columns.</exception>
    public ILuiLinkedIndex LinkedIndex<TPayload>(string name, Func<LuiPayloadRow, TPayload> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var definition = ReadEntry(name).Definition;
        EnsureLinked(name, definition);

        var columns = definition.Link?.PayloadColumns ?? [];
        if (columns.Count == 0)
        {
            throw Mismatch(name, "no payload columns", "the columns of the payload");
        }

        return OpenLinked(name, definition, new ColumnPayload(name, columns, row => map(row)));
    }

    /// <summary>The first step of the description of an index: the kind of it.</summary>
    /// <param name="name">The name of the index.</param>
    /// <returns>The builder of the kind.</returns>
    private LuiIndexKindBuilder KindBuilder(string name)
    {
        Database.ThrowIfDisposed();
        return new LuiIndexKindBuilder(this, Identifier.ValidateIndexName(name, nameof(name)));
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

        if (!KeyCodec.IsKnown(entry.Definition.KeyType))
        {
            throw Mismatch(validated, $"the key type {entry.Definition.KeyType}", "the key type long or string");
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

    /// <summary>
    /// The read path of a linked index (ADR-0033): built from the stored definition, without writing anything. Both
    /// overloads of <c>LinkedIndex</c> come here, so they cannot drift apart.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="definition">The stored definition.</param>
    /// <param name="payload">How the payload is read: none, or the map of the application.</param>
    /// <returns>The index; it reads, and it writes if the file allows it.</returns>
    private LinkedIndex OpenLinked(string name, IndexDefinition definition, PayloadReader payload)
    {
        var source = new LinkedSource(name, definition);
        return new LinkedIndex(
            Database,
            TypoDictionaries,
            source,
            payload,
            source.Rules.DefaultSimilarity,
            typoTolerance: false);
    }

    private static void EnsureEmbedded(string name, IndexDefinition definition)
    {
        if (!string.Equals(definition.Source, EmbeddedSource.SourceName, StringComparison.Ordinal))
        {
            throw Mismatch(name, $"the content source {definition.Source}", $"the content source {EmbeddedSource.SourceName}");
        }
    }

    /// <summary>The linked path opens a linked index.</summary>
    private static void EnsureLinked(string name, IndexDefinition definition)
    {
        if (!string.Equals(definition.Source, LinkedSource.SourceName, StringComparison.Ordinal))
        {
            throw Mismatch(name, $"the content source {definition.Source}", $"the content source {LinkedSource.SourceName}");
        }
    }

    /// <summary>The key type of the caller against the key type of the file.</summary>
    private static LuiDefinitionMismatchException Mismatch(string name, string stored, string requested) =>
        new(
            name,
            stored,
            requested,
            "An index that is opened is never rebuilt silently: open it as what it is, or remove it with "
            + "LuiCatalog.DropIndex(name) and create it again.");

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
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
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
