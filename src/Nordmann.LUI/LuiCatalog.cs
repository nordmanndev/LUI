using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Indexes;
using Nordmann.LUI.Normalization;
using Nordmann.LUI.Querying;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;
using Nordmann.LUI.Typos;

namespace Nordmann.LUI;

/// <summary>
/// The entry point: knows the path of the file and all indexes of a
/// database (docs/api.md, ADR-0066). A catalog and its indexes are
/// meant to be singletons; they are safe to use from many threads.
/// <para>
/// Every operation opens its own connection from the pool of
/// <c>Microsoft.Data.Sqlite</c>, so reads run in parallel (ADR-0015, ADR-0046).
/// <see cref="Dispose"/> waits for the operations that are running.
/// </para>
/// <para>
/// The catalog is a facade (review DevEx D4): the connections (taken from the pool, ADR-0070) and the lifetime are its
/// <see cref="Storage.Database"/>, the stored definitions and their statements <see cref="IndexStore"/>, the typo
/// dictionaries <see cref="Typos.TypoDictionaries"/>. No layer below the public API knows the catalog.
/// </para>
/// <para>
/// It has two ways into an index (ADR-0033, ADR-0073), both with a definition: <c>Create(definition)</c> creates one — once, after
/// <c>Build()</c> or in the migration of the application — and <c>Index(definition)</c> opens one that is there, without writing
/// anything; the definition names the types, once. Which of them may write is decided by the file: a catalog that was opened
/// read-only refuses every write with a <see cref="LuiReadOnlyException"/>.
/// </para>
/// </summary>
public sealed class LuiCatalog : IDisposable
{
    /// <summary>The oldest SQLite the library runs on: the trigram tokenizer needs <c>remove_diacritics</c>.</summary>
    internal static readonly Version MinimumSqliteVersion = new(3, 45, 0);

    private readonly LuiCatalogOptions _options;

    // The registry of the payload types, as it was when the catalog was opened (ADR-0072): a copy, so that a later call on the registry
    // the application holds changes nothing; null when the catalog has none
    private readonly FrozenPayloadRegistry? _payloads;

    /// <summary>The primary result code of SQLite for a file that cannot be opened (<c>SQLITE_CANTOPEN</c>).</summary>
    private const int SqliteCannotOpen = 14;

    /// <summary>The primary result code of SQLite for a file that is no database (<c>SQLITE_NOTADB</c>).</summary>
    private const int SqliteNotADatabase = 26;

    // The handles that were handed out, by the definition object (ADR-0075): the same definition gets the same handle, and
    // the lookup allocates nothing and executes no SQL. The table does not keep a definition alive.
    private readonly ConditionalWeakTable<LuiIndexDefinitionBase, HandleOfDefinition> _handles = [];
    private readonly Lock _handleGate = new();

    // The guards of the handles that are alive — held weakly, so that a handle which outlives its definition object is still found
    // by DropIndex (the table above lets go of a definition that is collected).
    private readonly List<WeakReference<IndexGuard>> _guards = [];
    private int _pruneAt = 64;

    private LuiCatalog(string path, SqliteOpenMode mode, bool createdTheFile, LuiCatalogOptions options, FrozenPayloadRegistry? payloads)
    {
        _options = options;
        _payloads = payloads;

        // the serialisation of the indexes that have no declared payload type, and the read path by name: the registry resolves
        // the type the caller asks for (ADR-0072)
        PayloadSerialization = payloads is not null
            ? new RegistryPayloadSerialization(payloads)
            : PayloadSerialization.None;
        Database = new Database(this, path, mode, createdTheFile, options);
        StoredIndexes = new StoredIndexes(Database);
        Runtimes = new IndexRuntimes(Database, TypoDictionaries, PayloadSerialization, StoredIndexes);
    }

    /// <summary>The path of the file the catalog was opened with (ADR-0066).</summary>
    public string Path => Database.Path;

    /// <summary>
    /// Whether the catalog was opened with <see cref="OpenReadOnly"/> (ADR-0068): it reads, and what writes — the triggers, the syncs, the outbox — is a
    /// <see cref="LuiReadOnlyException"/>. It is the way the catalog was opened and nothing else: there is no second switch. The hosting package asks it to refuse
    /// <c>AddLuiOutbox</c> on a catalog that cannot apply an outbox (ADR-0076).
    /// </summary>
    public bool IsReadOnly => Database.Mode == SqliteOpenMode.ReadOnly;

    internal TimeProvider TimeProvider => _options.TimeProvider;

    /// <summary>
    /// How the payloads of the indexes that have no declared payload type become bytes and back (ADR-0035, ADR-0072): the registry of
    /// the catalog, or the serialiser of the old calls, or none.
    /// </summary>
    internal PayloadSerialization PayloadSerialization { get; }

    /// <summary>How many payload types were looked up in the registry — the seam of the tests: once per type, however often it is used (ADR-0072).</summary>
    internal int PayloadLookups => _payloads?.Lookups ?? 0;

    /// <summary>The database: the connections it hands out, the lock of the writes, the operations that are running.</summary>
    internal Database Database { get; }

    /// <summary>How many handles of definitions are alive in the catalog — the seam of the tests of the lifetime (ADR-0075).</summary>
    internal int CachedHandles => _handles.Count();

    /// <summary>The typo dictionaries of the indexes, one per index.</summary>
    internal TypoDictionaries TypoDictionaries { get; } = new();

    /// <summary>
    /// The stored definitions, in memory (ADR-0075): read when the catalog is opened, and again when the schema counter of
    /// the file changed, an index was created or dropped here, or an unknown name was asked for.
    /// </summary>
    internal StoredIndexes StoredIndexes { get; }

    /// <summary>The runtime of every index that was opened through a definition: built once, kept as long as its definition holds (ADR-0075).</summary>
    internal IndexRuntimes Runtimes { get; }

    /// <summary>
    /// Opens the catalog of a file that is there, and <b>writes nothing</b> (ADR-0068): it checks the SQLite that is loaded and
    /// that the file is a catalog — a SQLite database with a <c>lui_meta</c>. A file that is not there, or is no catalog,
    /// is a <see cref="LuiCatalogNotFoundException"/>: <c>Open</c> never creates a file and never turns one into a catalog;
    /// <see cref="OpenOrCreate"/> does.
    /// <para>
    /// <b>The journal mode of the file stays what it is</b>: <c>Open</c> never sets it. LUI sets the write-ahead logging
    /// only in the one case where it creates the file itself, in <see cref="OpenOrCreate"/>.
    /// </para>
    /// <para>
    /// The path is a <b>file path</b> (ADR-0066): LUI builds every connection itself — pooling on, a private cache — and
    /// the application never sees a connection string. An empty path, <c>:memory:</c>, a URI (<c>file:…</c>) and a
    /// directory are refused.
    /// </para>
    /// </summary>
    /// <param name="path">The file of the catalog: the one of the application, or a file of LUI's own (ADR-0014).</param>
    /// <param name="options">The settings; <see langword="null"/> takes the defaults.</param>
    /// <returns>The catalog; dispose it when the application ends.</returns>
    /// <exception cref="ArgumentException">The path is empty, <c>:memory:</c>, a URI or a directory.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value of <see cref="LuiCatalogOptions.Pragmas"/> is out of its bounds.</exception>
    /// <exception cref="LuiCatalogNotFoundException">There is no file at the path, or the file is no catalog.</exception>
    /// <exception cref="LuiReadOnlyException">A catalog in write-ahead logging in a directory that may not be written.</exception>
    /// <exception cref="LuiEnvironmentException">
    /// SQLite is too old, has no FTS5 trigram tokenizer, or folds text
    /// differently from the table the library was measured with.
    /// </exception>
    public static LuiCatalog Open(string path, LuiCatalogOptions? options = null) =>
        OpenCatalog(path, options, SqliteOpenMode.ReadWrite, create: false);

    /// <summary>
    /// Opens a catalog and sets the file up completely (ADR-0068): the file is created when it is missing, and
    /// <c>lui_meta</c> is created when the file has none — in a new file, and in the database of an application. A file
    /// that is a catalog already is not written.
    /// <para>
    /// <b>This sets the journal mode of the file — in one case only: when LUI creates the file itself.</b> A new file
    /// is put into write-ahead logging, once. A file that is there keeps the mode it has, whether it is empty or not:
    /// the file is the application's, and LUI does not change it.
    /// </para>
    /// <para>
    /// The path is a <b>file path</b> (ADR-0066): LUI builds every connection itself — pooling on, a private cache — and
    /// the application never sees a connection string. An empty path, <c>:memory:</c>, a URI (<c>file:…</c>) and a
    /// directory are refused.
    /// </para>
    /// </summary>
    /// <param name="path">The file of the catalog: the one of the application, or a file of LUI's own (ADR-0014).</param>
    /// <param name="options">The settings; <see langword="null"/> takes the defaults.</param>
    /// <returns>The catalog; dispose it when the application ends.</returns>
    /// <exception cref="ArgumentException">The path is empty, <c>:memory:</c>, a URI or a directory.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value of <see cref="LuiCatalogOptions.Pragmas"/> is out of its bounds.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory of the file is missing: LUI creates the file, not its directory.</exception>
    /// <exception cref="LuiCatalogNotFoundException">The file is there and is no SQLite database.</exception>
    /// <exception cref="LuiReadOnlyException">The file or its directory may not be written.</exception>
    /// <exception cref="LuiEnvironmentException">
    /// SQLite is too old, has no FTS5 trigram tokenizer, or folds text
    /// differently from the table the library was measured with.
    /// </exception>
    public static LuiCatalog OpenOrCreate(string path, LuiCatalogOptions? options = null) =>
        OpenCatalog(path, options, SqliteOpenMode.ReadWriteCreate, create: true);

    /// <summary>
    /// Opens a catalog that may only be read (ADR-0068): every connection of it is read-only, SQLite refuses every write,
    /// and LUI turns the refusal into a <see cref="LuiReadOnlyException"/> that names the reason. Like <see cref="Open"/>
    /// it checks the file and writes nothing: the file has to be there, and it has to be a catalog. The journal mode is
    /// never touched.
    /// </summary>
    /// <param name="path">The file of the catalog.</param>
    /// <param name="options">The settings; <see langword="null"/> takes the defaults.</param>
    /// <returns>The catalog; dispose it when the application ends.</returns>
    /// <exception cref="ArgumentException">The path is empty, <c>:memory:</c>, a URI or a directory.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value of <see cref="LuiCatalogOptions.Pragmas"/> is out of its bounds.</exception>
    /// <exception cref="LuiCatalogNotFoundException">There is no file at the path, or the file is no catalog.</exception>
    /// <exception cref="LuiReadOnlyException">A catalog in write-ahead logging in a directory that may not be written: SQLite cannot create its <c>-wal</c> and <c>-shm</c> files.</exception>
    public static LuiCatalog OpenReadOnly(string path, LuiCatalogOptions? options = null) =>
        OpenCatalog(path, options, SqliteOpenMode.ReadOnly, create: false);

    /// <summary>
    /// Opens the catalog: the path is checked, the settings are checked, the environment is checked — and only then the
    /// file is touched. A value that is out of its bounds is refused <b>before</b> a connection is opened, so a file that
    /// is not written is not created either (ADR-0066).
    /// </summary>
    private static LuiCatalog OpenCatalog(string path, LuiCatalogOptions? options, SqliteOpenMode mode, bool create)
    {
        var fullPath = ValidatePath(path);
        options ??= new LuiCatalogOptions();
        ValidatePragmas(options.Pragmas);
        var payloads = FreezePayloads(options);

        EnsureSqliteVersion(LoadedSqliteVersion());
        FoldProbe.EnsureVerified();

        var createdTheFile = create && CreateTheFile(fullPath);

        var catalog = new LuiCatalog(fullPath, mode, createdTheFile, options, payloads);
        try
        {
            if (create)
            {
                IndexStore.SetUp(catalog.Database);
            }
            else
            {
                IndexStore.Verify(catalog.Database);
            }

            // every stored definition, once (ADR-0075): opening an index afterwards is a lookup
            _ = catalog.StoredIndexes.Reload();
            return catalog;
        }
        catch (SqliteException exception) when (MeansNoCatalog(exception, fullPath))
        {
            catalog.Dispose();
            DiscardTheFileWhenItIsStillEmpty(fullPath, createdTheFile);
            throw Translate(exception, fullPath, create);
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            catalog.Dispose();
            DiscardTheFileWhenItIsStillEmpty(fullPath, createdTheFile);
            throw new LuiReadOnlyException(
                "opening the catalog",
                create
                    ? $"SQLite refuses to set up '{fullPath}': the file or its directory may not be written, so 'lui_meta' cannot be "
                      + "created. Give the process write access, or open the catalog with LuiCatalog.Open or LuiCatalog.OpenReadOnly."
                    : $"SQLite refuses to open '{fullPath}': the directory '{System.IO.Path.GetDirectoryName(fullPath)}' (or the file) may not "
                      + "be written, and a catalog in write-ahead logging needs its '-wal' and '-shm' files even to read. Give the "
                      + "process write access to the directory, or open a copy of the file in a directory that may be written.",
                exception);
        }
        catch
        {
            catalog.Dispose();
            DiscardTheFileWhenItIsStillEmpty(fullPath, createdTheFile);
            throw;
        }
    }

    /// <summary>
    /// Whether SQLite's error says that there is no catalogue at the path (ADR-0068): the file is not there
    /// (<c>SQLITE_CANTOPEN</c>, and the file system confirms that there is no file — a directory that may not be searched
    /// is a problem of rights, not a missing file), or it is no database (<c>SQLITE_NOTADB</c>). Every other error stays what
    /// it is.
    /// </summary>
    private static bool MeansNoCatalog(SqliteException exception, string fullPath) =>
        exception.SqliteErrorCode == SqliteNotADatabase
        || (exception.SqliteErrorCode == SqliteCannotOpen && FileIsMissing(fullPath));

    /// <summary>
    /// Asks the file system after SQLite refused: the file is missing, or it is not known (no rights, a broken link). Asked
    /// <b>after</b> the refusal, never before the open — there is no window between the check and the open.
    /// </summary>
    private static bool FileIsMissing(string fullPath)
    {
        try
        {
            _ = File.GetAttributes(fullPath);
            return false;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The exception of LUI for an error that <see cref="MeansNoCatalog"/> recognised.</summary>
    private static LuiCatalogNotFoundException Translate(SqliteException exception, string fullPath, bool create) =>
        exception.SqliteErrorCode != SqliteNotADatabase
            ? new LuiCatalogNotFoundException(fullPath)
            : new LuiCatalogNotFoundException(
                fullPath,
                create
                    ? $"The file '{fullPath}' exists and is no SQLite database, so LuiCatalog.OpenOrCreate cannot set it up. "
                      + "Correct the path, or remove the file."
                    : $"The file '{fullPath}' is no SQLite database, so it is no catalog. Correct the path; LuiCatalog.OpenOrCreate "
                      + "creates a catalog in a file that does not exist yet, or in a SQLite database.",
                exception);

    /// <summary>
    /// Takes back a file that LUI created and that is still empty when the opening failed (ADR-0066). A file that is
    /// empty has not got its write-ahead logging yet, and the next <see cref="OpenOrCreate"/> would find a file that is
    /// there and never set it. A file that has content already carries the mode LUI gave it, and stays. Best effort:
    /// a file that cannot be deleted is left as it is.
    /// </summary>
    /// <param name="fullPath">The full path of the file.</param>
    /// <param name="createdTheFile">Whether LUI created the file.</param>
    private static void DiscardTheFileWhenItIsStillEmpty(string fullPath, bool createdTheFile)
    {
        if (!createdTheFile)
        {
            return;
        }

        try
        {
            if (new FileInfo(fullPath) is { Exists: true, Length: 0 })
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the opening failed already; the file stays, and the error of the opening is the one that is reported
        }
    }

    /// <summary>
    /// The path of the file of the catalog (ADR-0066): a file path, resolved with <see cref="Path.GetFullPath(string)"/>.
    /// An empty path, <c>:memory:</c>, a URI (<c>file:…</c>) and a directory are refused — deliberately: an encrypted
    /// database, a VFS of its own and a URI file name are not supported.
    /// </summary>
    /// <param name="path">The path the application gave.</param>
    /// <returns>The full path of the file.</returns>
    /// <exception cref="ArgumentException">The path is empty, <c>:memory:</c>, a URI, a directory or no path at all.</exception>
    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The path of the catalog is a file path: ':memory:' is not one.",
                nameof(path));
        }

        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The path of the catalog is a file path: a URI ('file:…') is not one.",
                nameof(path));
        }

        string fullPath;
        try
        {
            fullPath = System.IO.Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"The path of the catalog is a file path: '{path}' is not one.", nameof(path), exception);
        }

        return Directory.Exists(fullPath)
            ? throw new ArgumentException($"The path of the catalog is a file path: '{path}' is a directory.", nameof(path))
            : fullPath;
    }

    /// <summary>
    /// Creates the file — <see cref="OpenOrCreate"/> only (ADR-0068) — and it creates it itself, before SQLite does: that is
    /// how LUI knows the file is its own to set up (ADR-0066 — the journal mode). The file is created exclusively, so of two processes that
    /// open a missing file at the same time one creates it and sets the journal mode, and the other finds a file that is
    /// there and leaves it alone. <c>Open</c> and <c>OpenReadOnly</c> do not call this: their connections never create a file,
    /// and the refusal of SQLite is what says that it is missing.
    /// </summary>
    /// <param name="fullPath">The full path of the file.</param>
    /// <returns>Whether LUI created the file.</returns>
    private static bool CreateTheFile(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
            return true;
        }
        catch (IOException) when (File.Exists(fullPath))
        {
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new LuiReadOnlyException(
                "creating the file of the catalog",
                $"The catalog '{fullPath}' cannot be created: the process may not write to the directory.",
                exception);
        }
    }

    /// <summary>The registry of the payload types is frozen when the catalog is opened (ADR-0072): the catalog works on a copy.</summary>
    /// <param name="options">The options of the catalog.</param>
    /// <returns>The copy of the registry; <see langword="null"/> when there is none.</returns>
    private static FrozenPayloadRegistry? FreezePayloads(LuiCatalogOptions options) => options.Payloads?.Freeze();

    /// <summary>
    /// The bounds of the settings of the connections (ADR-0066): the busy timeout is one second to ten minutes, the
    /// synchronous is one of the two values of <see cref="LuiSynchronous"/>, and the cache size is 1 to 1024 megabytes.
    /// </summary>
    /// <param name="pragmas">The settings of the connections.</param>
    /// <exception cref="ArgumentNullException">The settings are missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of its bounds.</exception>
    private static void ValidatePragmas(LuiPragmas pragmas)
    {
        ArgumentNullException.ThrowIfNull(pragmas);

        Setting.InRange(pragmas.BusyTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10), nameof(LuiPragmas.BusyTimeout));
        Setting.Defined(pragmas.Synchronous, nameof(LuiPragmas.Synchronous));

        if (pragmas.CacheSizeMegabytes is { } cache)
        {
            Setting.InRange(cache, 1, 1024, nameof(LuiPragmas.CacheSizeMegabytes));
        }
    }

    /// <summary>
    /// Creates the index of a definition, or opens it if it exists with the same definition — safe on every start
    /// (ADR-0073). An embedded definition gives an embedded handle. The index is created only: <b>nothing is synced and no
    /// trigger is installed</b>. A different stored definition throws a <see cref="LuiDefinitionMismatchException"/>
    /// that names the fields — the similarity is no part of the comparison. LUI does not migrate a definition: to change
    /// one, <see cref="DropIndex"/> the index, create it again and fill it, or give the new definition a new name.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <param name="definition">The declaration of the index.</param>
    /// <returns>The handle of the index; it reads and writes.</returns>
    /// <exception cref="LuiConfigurationException">The definition is not valid.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public LuiEmbeddedIndex<TKey> Create<TKey>(LuiIndexDefinition<TKey> definition)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(definition);
        CreateStored(definition);
        return Index(definition);
    }

    /// <summary>
    /// Creates the index of a linked definition, or opens it if it exists with the same definition (ADR-0073), and checks
    /// the table of the application every time. The index starts empty: <b>nothing is synced and no trigger is
    /// installed</b> — <c>CreateTriggers()</c> installs the triggers if the application wants them, and then <c>Sync()</c> fills the index: the triggers come first, so that no row written in between is lost.
    /// The table is the application's own schema and has to exist before this call. A new index reports
    /// <c>NeedsInitialSync == true</c> until its first complete <c>Sync()</c>; LUI does nothing on its own (ADR-0077).
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <param name="definition">The declaration of the index.</param>
    /// <returns>The handle of the index; it reads and syncs.</returns>
    /// <exception cref="LuiConfigurationException">The definition is not valid.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    /// <exception cref="LuiLinkedTableException">The table or a column is missing, or the key is not the table's INTEGER PRIMARY KEY.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public LuiLinkedIndex<TKey> Create<TKey>(LuiLinkedIndexDefinition<TKey> definition)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(definition);
        CreateStored(definition);
        return Index(definition);
    }

    /// <summary>
    /// Creates the index of a definition with a payload, or opens it if it exists with the same definition (ADR-0073). The
    /// handle writes typed payloads, which the serializer that the <see cref="LuiCatalogOptions.Payloads"/> registry of the catalog
    /// holds for <typeparamref name="TPayload"/> serializes; the type is looked up <b>before</b> anything is written, and the index
    /// stores the name of the payload type and the format of that serializer (ADR-0072). Everything else is as for
    /// <see cref="Create{TKey}(LuiIndexDefinition{TKey})"/>.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TPayload">The type of the payload.</typeparam>
    /// <param name="definition">The declaration of the index.</param>
    /// <returns>The handle of the index; it reads and writes.</returns>
    /// <exception cref="LuiConfigurationException">The definition is not valid, the catalog has no registry, or the registry does not know the payload type — or two contexts do.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public LuiEmbeddedIndex<TKey, TPayload> Create<TKey, TPayload>(LuiIndexDefinition<TKey, TPayload> definition)
        where TKey : notnull
        where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(definition);
        CreateStored(definition);
        return Index(definition);
    }

    /// <summary>
    /// Creates the index of a linked definition with a payload, or opens it if it exists with the same definition
    /// (ADR-0073), and checks the table of the application every time. The map of the definition builds the payload; the
    /// index starts empty: <b>nothing is synced and no trigger is installed</b>, and <c>NeedsInitialSync</c> is <see langword="true"/>
    /// until its first complete <c>Sync()</c> (ADR-0077).
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TPayload">The type the map builds.</typeparam>
    /// <param name="definition">The declaration of the index.</param>
    /// <returns>The handle of the index; it reads and syncs.</returns>
    /// <exception cref="LuiConfigurationException">The definition is not valid.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    /// <exception cref="LuiLinkedTableException">The table or a column is missing, or the key is not the table's INTEGER PRIMARY KEY.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public LuiLinkedIndex<TKey, TPayload> Create<TKey, TPayload>(LuiLinkedIndexDefinition<TKey, TPayload> definition)
        where TKey : notnull
        where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(definition);
        CreateStored(definition);
        return Index(definition);
    }

    /// <summary>
    /// Creates or opens several definitions <b>in one transaction</b> (ADR-0073): all or none. The exception names the
    /// definition that failed, and a name that occurs twice throws an <see cref="ArgumentException"/> before anything is
    /// written. It returns nothing — a variable typed <see cref="LuiIndexDefinitionBase"/> binds here; the handles come from
    /// <c>Index(definition)</c>.
    /// </summary>
    /// <param name="definitions">The declarations of the indexes.</param>
    /// <exception cref="ArgumentException">A name occurs twice, or a definition is missing.</exception>
    /// <exception cref="LuiConfigurationException">A definition is not valid.</exception>
    /// <exception cref="LuiDefinitionMismatchException">An index exists with another definition; nothing was created.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public void Create(params LuiIndexDefinitionBase[] definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        Database.ThrowIfDisposed();

        // every definition is resolved, and its serializer is found in the registry, before the first write: a definition that is not
        // valid, or whose payload type is not registered, fails here with its name and nothing is created
        var resolved = new List<(ResolvedIndex Index, IndexDefinition Declared)>(definitions.Length);
        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition, nameof(definitions));
            var index = definition.Resolve();
            resolved.Add((index, DeclaredOf(definition, index, out _)));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in resolved.Select(index => index.Index.Name).Where(name => !names.Add(name)))
        {
            throw new ArgumentException($"The index '{name}' is declared twice: a name occurs once in a call of Create.", nameof(definitions));
        }

        if (resolved.Count == 0)
        {
            return;
        }

        var sources = resolved.Select(index => PrepareCreate(index.Index.Name, index.Index.Description, index.Declared)).ToArray();
        IndexStore.CreateAll(Database, [.. sources.Select(source => (source.Source, source.Verify))], TimeProvider.GetUtcNow());
        _ = StoredIndexes.Reload();
    }

    /// <summary>
    /// Opens the index of a definition that exists, and writes nothing (ADR-0073): one name for both sources — an
    /// embedded definition gives an embedded handle, a linked definition a linked one. The stored definition is compared with
    /// the declared one; the similarity of the definition takes effect, it is code. A catalog that was opened with
    /// <c>OpenReadOnly</c> checks its definitions with this call instead of <c>Create</c>.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <param name="definition">The declaration of the index.</param>
    /// <returns>The handle of the index; it reads, and it writes if the file allows it.</returns>
    /// <exception cref="LuiConfigurationException">The definition is not valid.</exception>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The stored definition is another one; the message names the fields.</exception>
    public LuiEmbeddedIndex<TKey> Index<TKey>(LuiIndexDefinition<TKey> definition)
        where TKey : notnull =>
        HandleOf(definition, static (runtime, catalog) => new LuiEmbeddedIndex<TKey>(catalog.Database, (EmbeddedIndex)runtime.Index));

    /// <summary>
    /// Opens the index of a linked definition that exists, and writes nothing (ADR-0073) — the same call as for an
    /// embedded definition: one name for both sources.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <param name="definition">The declaration of the index.</param>
    /// <returns>The handle of the index; it reads and syncs, if the file allows it.</returns>
    /// <exception cref="LuiConfigurationException">The definition is not valid.</exception>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The stored definition is another one; the message names the fields.</exception>
    public LuiLinkedIndex<TKey> Index<TKey>(LuiLinkedIndexDefinition<TKey> definition)
        where TKey : notnull =>
        HandleOf(definition, static (runtime, catalog) => new LuiLinkedIndex<TKey>(catalog.Database, (LinkedIndex)runtime.Index));

    /// <summary>
    /// Opens the index of a definition with a payload that exists, and writes nothing (ADR-0073); the stored definition is
    /// compared with the declared one, as for a definition without payload — and the name of the payload type and the format of its serializer are two of the compared fields (ADR-0072).
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TPayload">The type of the payload.</typeparam>
    /// <param name="definition">The declaration of the index.</param>
    /// <returns>The handle of the index; it reads, and it writes if the file allows it.</returns>
    /// <exception cref="LuiConfigurationException">The definition is not valid, the catalog has no registry, or the registry does not know the payload type — or two contexts do.</exception>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The stored definition is another one; the message names the fields.</exception>
    public LuiEmbeddedIndex<TKey, TPayload> Index<TKey, TPayload>(LuiIndexDefinition<TKey, TPayload> definition)
        where TKey : notnull
        where TPayload : notnull =>
        HandleOf(
            definition,
            static (runtime, catalog) => new LuiEmbeddedIndex<TKey, TPayload>(catalog.Database, (EmbeddedIndex)runtime.Index));

    /// <summary>
    /// Opens the index of a linked definition with a payload that exists, and writes nothing (ADR-0073). The columns of the
    /// payload are compared with the stored ones; the map of the definition is code and takes effect.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TPayload">The type the map builds.</typeparam>
    /// <param name="definition">The declaration of the index.</param>
    /// <returns>The handle of the index; it reads and syncs, if the file allows it.</returns>
    /// <exception cref="LuiConfigurationException">The definition is not valid.</exception>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The stored definition is another one; the message names the fields.</exception>
    public LuiLinkedIndex<TKey, TPayload> Index<TKey, TPayload>(LuiLinkedIndexDefinition<TKey, TPayload> definition)
        where TKey : notnull
        where TPayload : notnull =>
        HandleOf(definition, static (runtime, catalog) => new LuiLinkedIndex<TKey, TPayload>(catalog.Database, (LinkedIndex)runtime.Index));

    /// <summary>
    /// Builds the typo dictionary of every index of the catalog (ADR-0043) — the warm-up of a host in one call.
    /// <para>
    /// The dictionary belongs to the index of the catalog, not to the instance of it: an index that the application
    /// opens later sees the same dictionary and reports it as built (ADR-0025).
    /// </para>
    /// <para>
    /// The build takes seconds and needs memory — 194 MB for 685,739 words (ADR-0021). A host that wants corrections
    /// from its first query warms the catalog at the start; <see cref="Initialize"/> does it, with the rest of the start
    /// (ADR-0076).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Checked between the indexes.</param>
    /// <exception cref="LuiDefinitionMismatchException">
    /// An index of the file was written by another version of the library (another LUI format, another folding).
    /// </exception>
    public void WarmUpTypoDictionaries(CancellationToken cancellationToken = default) =>
        WarmUp(IndexStore.ReadIndexNames(Database), cancellationToken);

    /// <summary>
    /// Builds the typo dictionary of the named indexes (ADR-0043) — for a catalog whose indexes do not all correct typos:
    /// a dictionary costs memory, 194 MB for 685,739 words (ADR-0021), and an index whose queries correct nothing needs
    /// none.
    /// </summary>
    /// <param name="names">The names of the indexes, embedded or linked.</param>
    /// <param name="cancellationToken">Checked between the indexes.</param>
    /// <exception cref="LuiIndexNotFoundException">The catalog holds no index of one of the names.</exception>
    /// <exception cref="LuiDefinitionMismatchException">
    /// One of the indexes was written by another version of the library (another LUI format, another folding).
    /// </exception>
    public void WarmUpTypoDictionaries(IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);

        // every name is checked before the first build: a typo in the list fails at once, not after minutes of building
        var checkedNames = names.ToArray();
        foreach (var name in checkedNames)
        {
            ReadEntry(name);
        }

        WarmUp(checkedNames, cancellationToken);
    }

    /// <summary>Builds the dictionary of each index of the list, one after the other.</summary>
    /// <param name="names">The names of the indexes.</param>
    /// <param name="cancellationToken">Checked between the indexes.</param>
    private void WarmUp(IEnumerable<string> names, CancellationToken cancellationToken)
    {
        foreach (var name in names)
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
    /// Describes every index of the catalog, by what is stored (ADR-0076) — for a tool, an admin endpoint or a health check that
    /// knows neither the definitions of the application nor its payload types. The list is in the order of the names.
    /// <para>
    /// <b>The definition fields</b> (the source, the kind, the payload type and its format, the key type, the typo distance) come from the
    /// definitions in the memory of the catalog (ADR-0075): no statement reads them. The only statement of the call asks whether the
    /// SQLite schema counter of the file moved; only then the definitions are read again — so an index that another process created or
    /// dropped is in the list. <b>The state fields</b> are read when the call is made: the state of the typo dictionary, from the dictionaries
    /// of this catalog — <c>NotBuilt</c> says that <i>this</i> catalog has not built it —, and <see cref="LuiIndexInfo.NeedsInitialization"/> (#266): the dictionary,
    /// and for each <b>linked</b> index the triggers, the flag of the first sync and its lease, read from the file in one snapshot with the guard of every
    /// operation (an embedded index costs no statement). Nothing else of the state is in the list — <see cref="Count(string)"/> and
    /// <see cref="Check(string)"/> are asked by name, and cost what they cost.
    /// </para>
    /// </summary>
    /// <returns>One record per index; empty when the catalog holds none. It is a snapshot.</returns>
    /// <exception cref="LuiDefinitionMismatchException">A row of the file cannot be read: another LUI format, another folding, a key type or a kind this version does not know, a definition of <c>null</c> or of malformed JSON (the error of the parser is the inner exception). It names the index, and the list is not given around it: <see cref="DropIndex"/> is the way out.</exception>
    /// <exception cref="ObjectDisposedException">The catalog is disposed.</exception>
    public IReadOnlyList<LuiIndexInfo> Indexes()
    {
        Database.ThrowIfDisposed();
        _ = StoredIndexes.ReloadIfChanged();

        var infos = new List<LuiIndexInfo>();
        foreach (var (name, stored) in StoredIndexes.Current.Entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            // a row that cannot be read says what is wrong with it when it is read again; an index that was dropped meanwhile is not listed
            var entry = stored ?? IndexStore.ReadIndex(Database, name);
            if (entry is null)
            {
                continue;
            }

            entry.EnsureReadable(name);
            var definition = entry.Definition;
            var linked = string.Equals(entry.Source, LinkedSource.SourceName, StringComparison.Ordinal);
            var typoState = TypoDictionaries.StateOf(name, definition.ToJson());

            // what the index needs at its start (ADR-0076): the dictionary of this process — and for a linked index the state of the file, read by
            // name with the guard of every operation. An index that was dropped under the call is not listed, as one dropped before it is not
            LuiInitializationNeeds needs;
            try
            {
                needs = NeedsOf(name, linked, definition.TypoDistance, typoState);
            }
            catch (LuiIndexNotFoundException)
            {
                continue;
            }

            infos.Add(
                new LuiIndexInfo
                {
                    Name = name,
                    Source = linked ? LuiIndexSource.Linked : LuiIndexSource.Embedded,
                    Kind = entry.Kind,
                    PayloadType = definition.PayloadTypeName,
                    PayloadFormat = definition.PayloadFormat,
                    KeyType = KeyCodec.TypeOf(definition.KeyType),
                    TypoDistance = definition.TypoDistance,
                    TypoDictionaryState = typoState,
                    NeedsInitialization = needs,
                });
        }

        return infos;
    }

    /// <summary>
    /// Brings every index to the state an application needs at the start of a process (ADR-0076) — <b>one explicit, synchronous call</b> with good defaults, in LUI's order,
    /// driven by the state of each index. LUI never does it on its own: without this call — or the single steps — a linked index has no triggers and no first sync, and a typo-tolerant
    /// index never corrects (a query never builds its dictionary, ADR-0042), and <see cref="LuiIndexInfo.NeedsInitialization"/> says so. The application decides <b>when</b> and
    /// <b>on which thread</b>; there is no <c>InitializeAsync</c> — the work is synchronous SQLite, and a <c>Task.Run</c> around it would only pretend otherwise.
    /// <para>
    /// <b>The order</b>, step by step across the selected indexes: (1) the triggers of every linked index whose triggers are missing or stale — <c>CreateTriggers()</c>, idempotent,
    /// before the sync, so that a row written in between is noted (ADR-0077); (2) the first complete sync of every linked index that still needs one, one after the other and in batches;
    /// (3) the outbox of every linked index, once; (4) the typo dictionaries, last — so that they know the words of the syncs and the outbox — and <b>one after the other</b>, so that the
    /// peak memory is that of the largest dictionary and not the sum. Each step runs only for the index whose state needs it, read first: a second call right after the first does nothing.
    /// A typo dictionary is built only for an index that has no file step left (no triggers missing, no first sync due or running elsewhere); the reason stays, and the next call builds it.
    /// Typo dictionaries are per process: every process builds its own — expected work, not a repetition. Each takes seconds and some hundred MB at the size of the German word list, so with
    /// several typo-tolerant indexes the time adds up.
    /// </para>
    /// <para>
    /// <b>Several processes.</b> The first sync of a linked index is guarded by the lease of ADR-0077. When another process holds it, this call skips that sync — and the steps after
    /// it for that index — and reports <see cref="LuiInitializationNeeds.InitialSyncRunningElsewhere"/>: it does not wait and does not fail. Of two processes that start together exactly
    /// one runs the sync. The triggers are idempotent, and the outbox is applied by one writer at a time (ADR-0046).
    /// </para>
    /// <para>
    /// <b>A failing index</b> does not stop the others: its failing step is recorded, its later steps are skipped, the other indexes go on, and at the end the call throws a
    /// <see cref="LuiInitializeException"/> with the complete report and the error of every failed index. <b>Cancellation</b> is checked between the steps and inside the syncs; it throws
    /// an <see cref="OperationCanceledException"/>, what was finished stays, and the next call continues by state.
    /// </para>
    /// <para>
    /// <b>A catalog opened with <see cref="OpenReadOnly"/></b> builds the typo dictionaries and nothing else: the file steps cannot be done and are left to the writing process.
    /// <see cref="LuiIndexInfo.NeedsInitialization"/> reports them, so a reader can tell "not ready yet" from "ready" — but its readiness looks only at
    /// <see cref="LuiInitializationNeeds.TypoDictionary"/>.
    /// </para>
    /// </summary>
    /// <param name="options">What to do and for which indexes; <see langword="null"/> is everything, for every index of the catalog.</param>
    /// <param name="cancellationToken">Checked between the steps and inside the syncs.</param>
    /// <returns>What was done, one line per index.</returns>
    /// <exception cref="LuiIndexNotFoundException">A definition of <see cref="LuiInitializeOptions.Indexes"/> names an index that does not exist — before any step.</exception>
    /// <exception cref="LuiInitializeException">One or more indexes failed, after every other index was initialized.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled; what was finished stays. The errors of indexes that had failed by then are not carried: <see cref="LuiIndexInfo.NeedsInitialization"/> still names what they need, and the next call runs their steps again.</exception>
    /// <exception cref="ObjectDisposedException">The catalog is disposed.</exception>
    public LuiInitializeReport Initialize(LuiInitializeOptions? options = null, CancellationToken cancellationToken = default)
    {
        Database.ThrowIfDisposed();
        return new CatalogInitialization(this, options ?? new LuiInitializeOptions(), cancellationToken).Run();
    }

    /// <summary>
    /// What an index needs at its start (ADR-0076): the dictionary of this process, and for a linked index the state of the file — the triggers, the flag of the first sync, the lease —
    /// read by name with the guard of every operation.
    /// </summary>
    private LuiInitializationNeeds NeedsOf(string name, bool linked, LuiTypoDistance distance, LuiTypoDictionaryState typoState)
    {
        var needs = distance != LuiTypoDistance.None && typoState != LuiTypoDictionaryState.Current ? LuiInitializationNeeds.TypoDictionary : LuiInitializationNeeds.None;
        return linked ? needs | Maintain(name, runtime => ((LinkedIndex)runtime.Index).ReadInitializationState().Needs) : needs;
    }

    /// <summary>
    /// The number of the entries of an index, by name (ADR-0076) — embedded or linked, without its definition and without a payload
    /// type. It meets the guard of ADR-0075 like the count of a handle: the schema counter is read in the snapshot of the count.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentException">The name is no index name.</exception>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database — or it was dropped under the call.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index was written by another version of the library (another LUI format, another folding), or its stored definition cannot be read (<c>null</c>, malformed JSON): the exception names the index, and <see cref="DropIndex"/> is the way out.</exception>
    /// <exception cref="ObjectDisposedException">The catalog is disposed.</exception>
    public long Count(string name) => Maintain(name, runtime => runtime.Index.Count());

    /// <summary>
    /// Checks the structure of an index, by name (ADR-0076): an embedded one — its definition, its triggers and FTS5's integrity check
    /// against the content table; a linked one — its definition, its table and its entries against the rows of the table. The report
    /// carries no keys. <b>It reads no payload and calls no map</b> (the payload columns of a linked index are stored, and the table is checked to still have them): whether the stored payloads can be read is the question of
    /// <c>CheckPayloads()</c> on a typed handle, which needs the payload type.
    /// <para>
    /// FTS5's integrity check is an insert that is rolled back and reads the whole index, so the check holds the write lock of the file for as long as it takes, and it needs a connection that may write: a catalog opened with
    /// <see cref="OpenReadOnly"/> refuses it with a <see cref="LuiReadOnlyException"/>, as the check of a handle does.
    /// </para>
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentException">The name is no index name.</exception>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database — or it was dropped under the call.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index was written by another version of the library (another LUI format, another folding), or its stored definition cannot be read (<c>null</c>, malformed JSON): the exception names the index, and <see cref="DropIndex"/> is the way out.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    /// <exception cref="ObjectDisposedException">The catalog is disposed.</exception>
    public LuiIntegrityReport Check(string name) => Maintain(name, runtime => runtime.Index.Check());

    /// <summary>
    /// Lets SQLite optimize the search index of an index, by name (ADR-0076) — embedded or linked, without its definition.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <exception cref="ArgumentException">The name is no index name.</exception>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database — or it was dropped under the call.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index was written by another version of the library (another LUI format, another folding), or its stored definition cannot be read (<c>null</c>, malformed JSON): the exception names the index, and <see cref="DropIndex"/> is the way out.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written; the message names the index.</exception>
    /// <exception cref="ObjectDisposedException">The catalog is disposed.</exception>
    public void Optimize(string name) =>
        Maintain(
            name,
            runtime =>
            {
                runtime.Index.Optimize();
                return true;
            });

    /// <summary>
    /// Rebuilds the search index of an embedded index from its content table, by name (ADR-0076): the triggers anew, the search index
    /// anew, merged — one transaction, whatever the payload type of the index is; no payload is read. It is for an <b>embedded</b> index
    /// only: the content of a linked index is the application's table, and its search index is built from it by <c>Sync()</c> on its handle.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <exception cref="ArgumentException">The name is no index name.</exception>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database — or it was dropped under the call.</exception>
    /// <exception cref="InvalidOperationException">The index is a linked one: "'chefs' is a linked index; rebuild it from its table with Sync() on its handle."</exception>
    /// <exception cref="LuiDefinitionMismatchException">The index was written by another version of the library (another LUI format, another folding), or its stored definition cannot be read (<c>null</c>, malformed JSON): the exception names the index, and <see cref="DropIndex"/> is the way out.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written; the message names the index.</exception>
    /// <exception cref="ObjectDisposedException">The catalog is disposed.</exception>
    public void Rebuild(string name) =>
        Maintain(
            name,
            runtime =>
            {
                ((EmbeddedIndex)runtime.Index).Rebuild();
                return true;
            },
            entry => string.Equals(entry.Source, LinkedSource.SourceName, StringComparison.Ordinal)
                ? new InvalidOperationException($"'{name}' is a linked index; rebuild it from its table with Sync() on its handle.")
                : null);

    /// <summary>
    /// The runtime of an index by its stored definition, for one operation by name (ADR-0076). The name is looked up in the definitions
    /// in memory — an unknown name reloads them once and is remembered as missing until the schema counter changes
    /// (<see cref="StoredIndexes.Find"/>, ADR-0075) — and the operation runs on a runtime that is built from what was found, with the guard
    /// of every operation: the schema counter is read inside the snapshot or the transaction of the work, so the check and the work are
    /// one step. A call by name declares no definition, so an index that another process created again with another definition is not a
    /// mismatch: when the guard says that the stored definition is not the one this call looked up, the call is made again from the
    /// definitions as the file holds them — it stops when the file stops moving, and then the exception of the guard is the answer.
    /// </summary>
    /// <typeparam name="T">What the operation returns.</typeparam>
    /// <param name="name">The name of the index.</param>
    /// <param name="work">The operation, on the internal index.</param>
    /// <param name="refuse">Whether the stored index is the wrong kind for the operation; the exception to throw, once the file has been looked at.</param>
    /// <returns>What the operation returned.</returns>
    internal T Maintain<T>(string name, Func<IndexRuntime, T> work, Func<IndexStoreEntry, Exception?>? refuse = null)
    {
        var validated = Identifier.ValidateIndexName(name, nameof(name));
        Database.ThrowIfDisposed();

        while (true)
        {
            var entry = FindStoredByName(validated, out var snapshot);
            if (refuse?.Invoke(entry) is { } refusal)
            {
                // thrown only from a snapshot that has been compared with the file: an index that another process created again
                // as the other kind is not refused for what it was
                if (StoredIndexes.Refresh(snapshot))
                {
                    continue;
                }

                throw refusal;
            }

            var runtime = Runtimes.BuildStored(validated, entry, snapshot.Counter);
            try
            {
                return work(runtime);
            }
            catch (Exception exception) when (exception is LuiIndexNotFoundException or LuiDefinitionMismatchException && runtime.Guard.HasFailed)
            {
                // the guard judged the index as the file holds it now; the definitions are read again, and the call is made again from
                // them unless nobody has moved the file since — then this is the answer
                if (StoredIndexes.Refresh(snapshot))
                {
                    continue;
                }

                throw;
            }
        }
    }

    /// <summary>
    /// The stored index of a name for an operation by name: from the definitions in memory, thrown as "not found" or as unreadable only
    /// when the file has been looked at for the snapshot the answer was judged on (<see cref="StoredIndexes.Refresh"/>).
    /// </summary>
    /// <param name="name">The validated name of the index.</param>
    /// <param name="snapshot">The definitions the answer was read from.</param>
    /// <returns>What <c>lui_meta</c> holds about the index.</returns>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The LUI format, the key type or the folding of the stored index is another one.</exception>
    private IndexStoreEntry FindStoredByName(string name, out StoredIndexes.Snapshot snapshot)
    {
        while (true)
        {
            var entry = StoredIndexes.Find(name, out snapshot) ?? throw new LuiIndexNotFoundException(name);
            try
            {
                entry.EnsureReadable(name);
                return entry;
            }
            catch (LuiDefinitionMismatchException)
            {
                // the same discipline as the open call: the unreadable index is thrown only when the file has not moved since
                if (StoredIndexes.Refresh(snapshot))
                {
                    continue;
                }

                throw;
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
    /// An index of the file was written by another version of the library (another LUI format, another folding).
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
    /// <param name="similarity">The similarity of the definition — code, not stored; <see langword="null"/> for the default of the kind.</param>
    /// <returns>The index; it reads, and it writes if the file allows it.</returns>
    private EmbeddedIndex OpenEmbedded(string name, IndexStoreEntry entry, ILuiSimilarity? similarity = null)
    {
        var source = new EmbeddedSource(name, entry.Definition);

        // An opened index takes the default of its kind and the distance of its stored definition: the
        // dictionary is built with it, and the queries of the instance correct unless they say otherwise
        // (LuiQueryOptions.TypoTolerance).
        return new EmbeddedIndex(
            Database,
            TypoDictionaries,
            source,
            PayloadSerialization,
            similarity ?? source.Rules.DefaultSimilarity,
            typoDistance: entry.Definition.TypoDistance);
    }

    /// <summary>
    /// Creates the index of a definition in the file, or checks that it is there with the same definition — the typed way
    /// (ADR-0073): the file and the definitions in memory are written, the handle comes from <c>Index(definition)</c>.
    /// </summary>
    /// <param name="definition">The declaration of the index.</param>
    private void CreateStored(LuiIndexDefinitionBase definition)
    {
        Database.ThrowIfDisposed();
        var resolved = definition.Resolve();
        _ = CreateInFile(resolved.Name, resolved.Description, DeclaredOf(definition, resolved, out _));
    }

    /// <summary>
    /// The definition as it is declared, in the form <c>lui_meta</c> stores it (ADR-0072): the description, the key type — and, for an
    /// embedded definition with a payload, the <b>name of the payload type and the format</b> of the serializer that the registry of the
    /// catalog holds for it. The serializer is looked up here, <b>before</b> anything is written or opened: a type that the registry does
    /// not know (or that two contexts know) is a <see cref="LuiConfigurationException"/> that names the definition, and no index is
    /// created for it. A linked definition, and an embedded one without a payload, store neither field and need no registry.
    /// </summary>
    /// <param name="definition">The declaration of the index.</param>
    /// <param name="resolved">The definition after its first use.</param>
    /// <param name="codec">The serializer of the payload type; <see langword="null"/> when the definition has none to find.</param>
    /// <returns>The definition as it would be stored.</returns>
    /// <exception cref="LuiConfigurationException">The catalog has no registry, the registry does not know the payload type, or two contexts do.</exception>
    private IndexDefinition DeclaredOf(LuiIndexDefinitionBase definition, ResolvedIndex resolved, out PayloadCodec? codec)
    {
        codec = null;
        if (definition.PayloadType is null || definition.Source != LuiIndexSource.Embedded)
        {
            return resolved.Description.ToStored(resolved.KeyType);
        }

        if (_payloads is null)
        {
            throw new LuiConfigurationException(
                $"The index definition '{resolved.Name}' has the payload type '{definition.PayloadType.FullName}', and the catalog has no payload "
                + "registry: set LuiCatalogOptions.Payloads = new LuiPayloadRegistry().Use(context), with the type listed in the JSON context "
                + "([JsonSerializable]) or registered with Use<T>(serializer).");
        }

        codec = definition.ResolveCodec(_payloads, resolved.Name);
        return resolved.Description.ToStored(resolved.KeyType, codec is null ? null : new PayloadFingerprint(codec.TypeName, codec.Format));
    }

    /// <summary>
    /// Creates the index in one transaction that holds the write lock and reads <c>lui_meta</c> inside it — two processes
    /// cannot both create it: one creates, the other finds it and compares (ADR-0075) — and reads the definitions of the file
    /// into memory.
    /// </summary>
    /// <returns>The source of the index.</returns>
    private ContentSource CreateInFile(string name, IndexDescription description, IndexDefinition declared)
    {
        var (source, verify) = PrepareCreate(name, description, declared);
        IndexStore.Create(Database, source, TimeProvider.GetUtcNow(), verify);
        _ = StoredIndexes.Reload();
        return source;
    }

    /// <summary>The source of a description, and — for a linked index — the check of the application's table.</summary>
    private static (ContentSource Source, Action<SqliteConnection, SqliteTransaction>? Verify) PrepareCreate(
        string name,
        IndexDescription description,
        IndexDefinition definition)
    {
        if (description.Source == LuiIndexSource.Embedded)
        {
            return (new EmbeddedSource(name, definition), null);
        }

        var link = description.Link!;
        var columns = link.PayloadColumns ?? [];
        return (new LinkedSource(name, definition), (connection, transaction) => LinkedTable.Verify(connection, transaction, link, columns));
    }

    /// <summary>
    /// The handle of a definition (ADR-0075): the one that was handed out for this definition object, or — at the first call —
    /// the handle over the runtime of the index. After the first call it is a lookup: no allocation, no SQL, and the
    /// definition is not compared again; a handle whose runtime failed (the index was dropped, or created again with another
    /// definition) is replaced by looking at the stored definitions once more.
    /// </summary>
    /// <param name="definition">The declaration of the index.</param>
    /// <param name="make">Puts the handle over the runtime.</param>
    /// <returns>The handle.</returns>
    private THandle HandleOf<THandle>(LuiIndexDefinitionBase definition, Func<IndexRuntime, LuiCatalog, THandle> make)
        where THandle : class
    {
        ArgumentNullException.ThrowIfNull(definition);
        Database.ThrowIfDisposed();

        return _handles.TryGetValue(definition, out var handed) && !handed.Guard.HasFailed
            ? (THandle)handed.Handle
            : OpenFirst(definition, make);
    }

    /// <summary>A seam of the tests (ADR-0075): runs when a call has entered the slow path of the open call, before it judges anything.</summary>
    internal Action? AfterSlowPathStarted { get; set; }

    /// <summary>A seam of the tests (ADR-0075): runs when a call has judged the stored definition and has not yet taken the lock of the handles.</summary>
    internal Action? BeforeHandleLock { get; set; }

    /// <summary>A seam of the tests (ADR-0075): runs in <c>DropIndex</c>, under the lock of the handles, after the handles are marked and before the definitions are read again.</summary>
    internal Action? AfterHandlesMarked { get; set; }

    /// <summary>
    /// The first call of a definition, and the call after its handle failed: the stored definition is found in memory, compared
    /// with the declared one, and the runtime is built. The handles are made and replaced under one lock, and a handle that failed
    /// is removed only there, and only when it is still the one in the table: a definition object has <b>one</b> handle, and no
    /// handle that was handed out escapes <c>DropIndex</c>. <c>DropIndex</c> marks the handles and reads the definitions again under
    /// the same lock, so a call that judged a snapshot from before it — the snapshot is not the current one any more when the call
    /// has the lock — judges again instead of building on it.
    /// </summary>
    private THandle OpenFirst<THandle>(LuiIndexDefinitionBase definition, Func<IndexRuntime, LuiCatalog, THandle> make)
        where THandle : class
    {
        AfterSlowPathStarted?.Invoke();

        while (true)
        {
            ResolvedIndex resolved;
            IndexStoreEntry entry;
            StoredIndexes.Snapshot snapshot;
            PayloadCodec? codec;
            try
            {
                resolved = definition.Resolve();

                // the serializer of the payload type is looked up here, once for the runtime that is built below (ADR-0072): an unregistered
                // type is refused at this call, and the name and the format it stands for are compared with the stored ones
                entry = FindStored(resolved.Name, DeclaredOf(definition, resolved, out codec), out snapshot);
            }
            catch
            {
                // a handle that failed is not kept — whether the definition finds its index again or not
                lock (_handleGate)
                {
                    if (_handles.TryGetValue(definition, out var failed) && failed.Guard.HasFailed)
                    {
                        _ = _handles.Remove(definition);
                    }
                }

                throw;
            }

            BeforeHandleLock?.Invoke();
            lock (_handleGate)
            {
                if (_handles.TryGetValue(definition, out var handed) && !handed.Guard.HasFailed)
                {
                    return (THandle)handed.Handle;
                }

                // DropIndex changes the counter, and replaces the snapshot under this lock: a reload that read the same counter is no reason to judge again
                if (StoredIndexes.Current.Counter != snapshot.Counter)
                {
                    continue;
                }

                var runtime = Runtimes.Build(resolved, entry, snapshot.Counter, codec);
                var handle = make(runtime, this);
                _handles.AddOrUpdate(definition, new HandleOfDefinition(handle, runtime.Guard));
                Track(runtime.Guard);
                return handle;
            }
        }
    }

    /// <summary>
    /// The stored index of a name, from the definitions in memory, after the comparison with the declared one. The definitions in
    /// memory may be old — another process may have created, dropped or created the index again — so an answer that is "not
    /// found" or a mismatch is thrown only when the file has been looked at for the snapshot it was judged on: while
    /// <see cref="StoredIndexes.Refresh"/> says that the snapshot was replaced or the file moved, the decision is made again from the
    /// current snapshot, and the call throws only from one that nobody has replaced since the counter of the file was compared.
    /// </summary>
    /// <param name="name">The validated name of the index.</param>
    /// <param name="declared">The definition the application declared.</param>
    /// <param name="snapshot">The definitions the answer was read from.</param>
    /// <returns>What <c>lui_meta</c> holds about the index.</returns>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The stored definition is another one; the message names the fields.</exception>
    private IndexStoreEntry FindStored(string name, IndexDefinition declared, out StoredIndexes.Snapshot snapshot)
    {
        while (true)
        {
            var entry = StoredIndexes.Find(name, out snapshot) ?? throw new LuiIndexNotFoundException(name);

            // an entry that cannot be read — another fold table, another key type, another LUI format — is a mismatch like another
            // definition: it is thrown only from a snapshot that has been compared with the file, because the migrator who drops the
            // index and creates it again is seen only there
            LuiDefinitionMismatchException? unreadable = null;
            try
            {
                entry.EnsureReadable(name);
            }
            catch (LuiDefinitionMismatchException exception)
            {
                unreadable = exception;
            }

            if (unreadable is null && entry.Definition == declared)
            {
                return entry;
            }

            if (!StoredIndexes.Refresh(snapshot))
            {
                throw unreadable ?? IndexStore.Mismatch(name, entry.Definition, declared);
            }
        }
    }

    /// <summary>The stored index of a name, with the two checks every index has to pass: the LUI format version it was written
    /// with, and the folding it was stored with.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <returns>What <c>lui_meta</c> holds about the index.</returns>
    /// <exception cref="LuiIndexNotFoundException">There is no index of that name in the database.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The LUI format or the folding is another one.</exception>
    private IndexStoreEntry ReadEntry(string name)
    {
        var validated = Identifier.ValidateIndexName(name, nameof(name));
        Database.ThrowIfDisposed();

        var entry = IndexStore.ReadIndex(Database, validated)
            ?? throw new LuiIndexNotFoundException(validated);

        entry.EnsureReadable(validated);
        return entry;
    }

    /// <summary>
    /// The read path of a linked index (ADR-0033): built from the stored definition, without writing anything. Both
    /// overloads of <c>LinkedIndex</c> come here, so they cannot drift apart.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="definition">The stored definition.</param>
    /// <param name="payload">How the payload is read: none, or the map of the application.</param>
    /// <param name="similarity">The similarity of the definition — code, not stored; <see langword="null"/> for the default of the kind.</param>
    /// <returns>The index; it reads, and it writes if the file allows it.</returns>
    private LinkedIndex OpenLinked(string name, IndexDefinition definition, PayloadReader payload, ILuiSimilarity? similarity = null)
    {
        var source = new LinkedSource(name, definition);
        return new LinkedIndex(
            Database,
            TypoDictionaries,
            source,
            payload,
            similarity ?? source.Rules.DefaultSimilarity,
            typoDistance: definition.TypoDistance);
    }

    /// <summary>The key type of the caller against the key type of the file.</summary>
    private static LuiDefinitionMismatchException Mismatch(string name, string stored, string requested) =>
        IndexStoreEntry.Mismatch(name, stored, requested);

    /// <summary>
    /// Removes an index with its tables, indexes and triggers and its row in <c>lui_meta</c>. It works whatever
    /// the stored definition is — it is the way out of a <see cref="LuiDefinitionMismatchException"/>. Index
    /// objects that were created before do not survive it: create the index again and use the new object.
    /// <para>
    /// For a <c>Linked</c> index the application's table and its rows stay. Its triggers, if the index has some on
    /// the table, are removed only with <paramref name="dropTriggers"/>: they write into the tables that go, so they
    /// cannot stay — but a migration of the application may count on them, and their removal is not done silently
    /// (review of pull request 16, M3). <b>After the drop and a new <c>Create()</c> a linked index is empty and has no
    /// triggers</b>: <c>CreateTriggers()</c> — or the migration — sets them again, <b>first</b>, and then <c>Sync()</c> fills it: a row written in between is noted by the triggers and not lost.
    /// </para>
    /// <para>
    /// <b>A row that cannot be read</b> — a definition of <c>null</c> or of malformed JSON — is dropped as what its source column says (ADR-0076):
    /// a <c>Linked</c> row with its outbox, search index and entries, and with the refusal of its triggers; an <c>Embedded</c> row as an embedded
    /// index. A row whose source is neither is refused, and nothing is removed.
    /// </para>
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="dropTriggers">Removes the triggers of a linked index on the application's table, too.</param>
    /// <returns><see langword="true"/> when the index existed.</returns>
    /// <exception cref="InvalidOperationException">A linked index has triggers, and <paramref name="dropTriggers"/> is not set; or an embedded index has triggers of its names on a table that is not its own.</exception>
    /// <exception cref="LuiDefinitionMismatchException">The row of the index cannot be read, and its source column is neither <c>Embedded</c> nor <c>Linked</c>: nothing is removed.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    public bool DropIndex(string name, bool dropTriggers = false)
    {
        Identifier.ValidateIndexName(name, nameof(name));
        var existed = IndexStore.Drop(Database, name, dropTriggers);

        // under the lock of the handles: a call that makes a handle takes it, too, and finds either its handle marked or the snapshot
        // of the definitions replaced
        lock (_handleGate)
        {
            // the words of the dropped index are not the words of the next one
            TypoDictionaries.Forget(name);

            // the handles that were handed out for it are stale from now on, and the definitions in memory are the file's
            StaleHandlesOf(name);
            AfterHandlesMarked?.Invoke();
            _ = StoredIndexes.Reload();
        }

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

    /// <summary>
    /// The index was dropped by this catalog: every handle that was handed out for it is stale from now on — the guard of its
    /// runtime says so — and the next open call builds a new one.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    private void StaleHandlesOf(string name)
    {
        // the guards that were marked or are dead are let go: a failed guard fails for ever, and its handle is replaced
        _ = _guards.RemoveAll(reference =>
        {
            if (!reference.TryGetTarget(out var guard))
            {
                return true;
            }

            if (!string.Equals(guard.Name, name, StringComparison.Ordinal))
            {
                return false;
            }

            guard.Dropped();
            return true;
        });
    }

    /// <summary>Remembers the guard of a handle that was made, weakly; the list is cleaned of the dead ones when it has doubled.</summary>
    /// <param name="guard">The guard of the runtime of the handle.</param>
    private void Track(IndexGuard guard)
    {
        if (_guards.Count >= _pruneAt)
        {
            _ = _guards.RemoveAll(reference => !reference.TryGetTarget(out _));
            _pruneAt = Math.Max(64, _guards.Count * 2);
        }

        _guards.Add(new WeakReference<IndexGuard>(guard));
    }

    /// <summary>A handle that was handed out for a definition, and the guard of the runtime it is a view of.</summary>
    private sealed record HandleOfDefinition(object Handle, IndexGuard Guard);
}
