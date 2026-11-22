using System.Globalization;
using Microsoft.Data.Sqlite;
using Nordmann.LUI.Schema;
using Nordmann.LUI.Storage;

namespace Nordmann.LUI.Indexes;

/// <summary>
/// The stored indexes of a database: <c>lui_meta</c> and the statements that create and remove an index (
/// ADR-0006, ADR-0033, docs/api.md). The builders describe an index; creating it, checking the stored definition and dropping it happens
/// here, in one transaction each (review DevEx D6: the DDL out of the builder).
/// </summary>
internal static class IndexStore
{
    /// <summary>The one statement of the read path: what <c>lui_meta</c> holds about an index (ADR-0033).</summary>
    private const string ReadSql =
        "SELECT kind, source, definition, schema_version FROM lui_meta WHERE index_name = $name;";

    /// <summary>
    /// Sets the file up — <c>OpenOrCreate</c> only (ADR-0068): the write-ahead logging, when LUI created the file itself
    /// (ADR-0066 — the journal mode), and <c>lui_meta</c>, when the file has none — the database of an application, or a new file. A file
    /// that is a catalogue already is not written at all.
    /// </summary>
    /// <param name="database">The database.</param>
    internal static void SetUp(Database database)
    {
        using var connection = database.OpenConnection();
        database.Initialize(connection);

        if (HasMetaTable(connection))
        {
            EnsureFormat(database, connection);
            return;
        }

        using var meta = connection.CreateCommand();
        meta.CommandText = Meta.CreateTable;
        meta.ExecuteNonQuery();
    }

    /// <summary>
    /// Checks that the file is a catalogue — <c>Open</c> and <c>OpenReadOnly</c> (ADR-0068). It reads and writes nothing
    /// else: a file without <c>lui_meta</c> is no catalogue, and it is not made one.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <exception cref="LuiCatalogNotFoundException">The file has no <c>lui_meta</c>.</exception>
    internal static void Verify(Database database)
    {
        using var connection = database.OpenReadConnection();

        if (!HasMetaTable(connection))
        {
            throw new LuiCatalogNotFoundException(
                database.Path,
                $"The file '{database.Path}' is no catalog: it has no table 'lui_meta'. LuiCatalog.Open and LuiCatalog.OpenReadOnly "
                + "open a catalog that is there and do not turn a file into one — LuiCatalog.OpenOrCreate sets the file up.",
                inner: null);
        }

        EnsureFormat(database, connection);
    }

    /// <summary>
    /// Checks that the file is in the LUI format this version reads (ADR-0073): every row of <c>lui_meta</c> says
    /// <see cref="Meta.SchemaVersion"/>. A file without an index has no format to reject. One read of the table, when the
    /// catalog is opened — not at the first index, where it would be a misleading "another definition".
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="connection">A connection to it.</param>
    /// <exception cref="LuiFormatException">An index of the file was written in another format.</exception>
    private static void EnsureFormat(Database database, SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM lui_meta WHERE schema_version <> $format ORDER BY schema_version LIMIT 1;";
        command.Parameters.AddWithValue("$format", Meta.SchemaVersion);

        if (command.ExecuteScalar() is long found)
        {
            throw new LuiFormatException(database.Path, (int)Math.Clamp(found, int.MinValue, int.MaxValue), Meta.SchemaVersion);
        }
    }

    private static bool HasMetaTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'lui_meta';";
        return (long)command.ExecuteScalar()! != 0;
    }

    /// <summary>
    /// Creates an index from the statements of its source, or checks that the stored definition is the same — in one
    /// transaction, so that two processes cannot both create it.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="source">The source of the index: its name, its definition, its statements.</param>
    /// <param name="createdAt">The time stored with a new index.</param>
    /// <param name="verify">A check of the environment of the index in the same transaction — for <c>Linked</c>, the
    /// application's table — that runs every time, not only when the index is created.</param>
    /// <exception cref="LuiDefinitionMismatchException">The index exists with another definition.</exception>
    /// <exception cref="LuiReadOnlyException">The file may not be written.</exception>
    internal static void Create(
        Database database,
        ContentSource source,
        DateTimeOffset createdAt,
        Action<SqliteConnection, SqliteTransaction>? verify = null) =>
        CreateAll(database, [(source, verify)], createdAt);

    /// <summary>
    /// Creates several indexes — or opens them, if they exist with the same definition — in <b>one transaction</b>: all or
    /// none (ADR-0073). The exception names the index that failed.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="indexes">The sources, each with the check of the application's table of a linked index.</param>
    /// <param name="createdAt">The time that is stored with a new index.</param>
    /// <exception cref="LuiDefinitionMismatchException">An index exists with another definition.</exception>
    internal static void CreateAll(
        Database database,
        IReadOnlyList<(ContentSource Source, Action<SqliteConnection, SqliteTransaction>? Verify)> indexes,
        DateTimeOffset createdAt)
    {
        string? current = null;
        try
        {
            CreateIndexes(database, indexes, createdAt, name => current = name);
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            // the refusal can come at the start of the transaction, before any index was looked at: then it is the first
            // index that needs a write — one that does not exist yet — and not simply the first one of the call
            current ??= FirstMissing(database, indexes) ?? indexes[0].Source.IndexName;
            throw database.ReadOnlyRefusal("creating an index", current, exception);
        }
    }

    private static string? FirstMissing(
        Database database,
        IReadOnlyList<(ContentSource Source, Action<SqliteConnection, SqliteTransaction>? Verify)> indexes)
    {
        try
        {
            return indexes.Select(index => index.Source.IndexName).FirstOrDefault(name => !Exists(database, name));
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    private static void CreateIndexes(
        Database database,
        IReadOnlyList<(ContentSource Source, Action<SqliteConnection, SqliteTransaction>? Verify)> indexes,
        DateTimeOffset createdAt,
        Action<string> onIndex)
    {
        using var scope = database.Begin();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        foreach (var (source, verify) in indexes)
        {
            var name = source.IndexName;
            onIndex(name);
            Verify(verify, connection, transaction, name);

            var definition = source.Definition;
            var stored = ReadDefinition(connection, transaction, name);
            if (stored is null)
            {
                foreach (var statement in source.CreateStatements())
                {
                    Execute(connection, transaction, statement);
                }

                WriteDefinition(connection, transaction, name, definition, createdAt);
            }
            else if (stored != definition)
            {
                throw Mismatch(name, stored, definition);
            }
        }

        transaction.Commit();
    }

    /// <summary>
    /// Checks the application's table of a linked index. The exception of the check names the index it was meant for
    /// (ADR-0073): the message gets the name, the table detail stays, and the original is the inner exception — the type
    /// stays <see cref="LuiLinkedTableException"/>.
    /// </summary>
    private static void Verify(
        Action<SqliteConnection, SqliteTransaction>? verify, SqliteConnection connection, SqliteTransaction transaction, string name)
    {
        try
        {
            verify?.Invoke(connection, transaction);
        }
        catch (LuiLinkedTableException exception)
        {
            throw new LuiLinkedTableException($"Index '{name}': {exception.Message}", exception);
        }
    }

    /// <summary>The mismatch of a stored definition and a declared one, with the fields that differ and the way out (ADR-0073).</summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="stored">The definition in <c>lui_meta</c>.</param>
    /// <param name="declared">The definition the application declared.</param>
    /// <returns>The exception.</returns>
    internal static LuiDefinitionMismatchException Mismatch(string name, IndexDefinition stored, IndexDefinition declared) =>
        new(name, stored.ToJson(), declared.ToJson(), linked: stored.Link is not null, fields: DifferingFields(stored, declared));

    /// <summary>Whether an index of that name is in <c>lui_meta</c>.</summary>
    /// <param name="database">The database.</param>
    /// <param name="name">The name of the index.</param>
    /// <returns><see langword="true"/> when the index exists.</returns>
    internal static bool Exists(Database database, string name)
    {
        using var connection = database.OpenReadConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM lui_meta WHERE index_name = $name;";
        command.Parameters.AddWithValue("$name", name);
        return (long)command.ExecuteScalar()! > 0;
    }

    /// <summary>
    /// The fields in which two definitions differ, named as <c>lui_meta</c> stores them. Only what is stored is compared:
    /// the similarity and the map of a payload are code, and they are no part of the definition (ADR-0073).
    /// </summary>
    private static List<string> DifferingFields(IndexDefinition stored, IndexDefinition declared)
    {
        var fields = new List<string>();
        Add(fields, "source", stored.Source, declared.Source);
        Add(fields, "kind", stored.Kind, declared.Kind);
        Add(fields, "keyType", stored.KeyType, declared.KeyType);
        Add(fields, "germanTransliteration", stored.GermanTransliteration, declared.GermanTransliteration);
        Add(fields, "foldTable", stored.FoldTable, declared.FoldTable);
        Add(fields, "typoDistance", stored.TypoDistance, declared.TypoDistance);

        if (stored.Link is null || declared.Link is null)
        {
            if (!(stored.Link is null && declared.Link is null))
            {
                fields.Add("link");
            }

            return fields;
        }

        Add(fields, "link.table", stored.Link.Table, declared.Link.Table);
        Add(fields, "link.key", stored.Link.Key, declared.Link.Key);
        Add(fields, "link.text", stored.Link.Text, declared.Link.Text);
        Add(fields, "link.weight", stored.Link.Weight, declared.Link.Weight);
        Add(fields, "link.priority", stored.Link.Priority, declared.Link.Priority);
        if (!SameColumns(stored.Link.PayloadColumns, declared.Link.PayloadColumns))
        {
            fields.Add("link.payloadColumns");
        }

        return fields;
    }

    private static bool SameColumns(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.SequenceEqual(right, StringComparer.Ordinal);

    private static void Add<T>(List<string> fields, string name, T stored, T declared)
    {
        if (!EqualityComparer<T>.Default.Equals(stored, declared))
        {
            fields.Add(name);
        }
    }

    internal static bool Drop(Database database, string name, bool dropTriggers)
    {
        try
        {
            return DropIndex(database, name, dropTriggers);
        }
        catch (SqliteException exception) when (Database.IsReadOnlyRefusal(exception))
        {
            throw database.ReadOnlyRefusal("dropping an index", name, exception);
        }
    }

    private static bool DropIndex(Database database, string name, bool dropTriggers)
    {
        using var scope = database.Begin();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        var stored = ReadDefinition(connection, transaction, name);
        var existed = stored is not null;

        if (!dropTriggers && stored?.Link is { } link && HasTriggers(connection, transaction, name))
        {
            throw new InvalidOperationException(
                $"The linked index '{name}' has triggers on the table '{link.Table}'. They write into the tables of the index "
                + "and go with it: call DropIndex(name, dropTriggers: true), and take them out of the application's migration "
                + "too. After a new Create() the index is empty and has no triggers — Sync() it and create them again.");
        }

        foreach (var statement in ContentSources.DropStatements(name, stored))
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
        return existed;
    }

    /// <summary>
    /// The stored index of a name — its kind, its content source, its definition and the LUI format version it was
    /// written with — or <see langword="null"/> when there is no such index.
    /// <para>
    /// It reads with a plain <c>SELECT</c> on a connection of its own: no transaction, no write lock, no table
    /// (ADR-0033). A reader that must not write — a replica, a file that is read-only, a service that may not block the
    /// writer — comes through here.
    /// </para>
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="indexName">The name of the index.</param>
    /// <returns>What <c>lui_meta</c> holds, or <see langword="null"/> when there is no such index.</returns>
    internal static IndexStoreEntry? ReadIndex(Database database, string indexName)
    {
        using var connection = database.OpenReadConnection();
        return ReadIndex(connection, null, indexName);
    }

    /// <summary>
    /// The same read on a connection and in a transaction that are given: the row as the snapshot of an operation sees it
    /// (ADR-0075) — what the guard of a handle has to judge, not what another connection sees a moment later.
    /// </summary>
    /// <param name="connection">A connection to the file.</param>
    /// <param name="transaction">Its transaction, when it has one.</param>
    /// <param name="indexName">The name of the index.</param>
    /// <returns>What <c>lui_meta</c> holds, or <see langword="null"/> when there is no such index.</returns>
    internal static IndexStoreEntry? ReadIndex(SqliteConnection connection, SqliteTransaction? transaction, string indexName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ReadSql;
        command.Parameters.AddWithValue("$name", indexName);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Parse(reader, indexName, firstColumn: 0) : null;
    }

    /// <summary>
    /// Every index of the file with what <c>lui_meta</c> holds about it, in one read (ADR-0075): the catalog keeps the
    /// definitions in memory. A row that cannot be read — a kind this version does not know, an empty definition — is
    /// recorded as <see langword="null"/> and not thrown: one row that is not readable must not keep the catalog from
    /// opening, and the index that is asked for reads its row again and throws what was wrong with it
    /// (<see cref="ReadIndex(Database, string)"/>), as it did before the definitions were kept.
    /// </summary>
    /// <param name="connection">A connection to the file.</param>
    /// <param name="transaction">Its transaction, when it has one — the counter and the definitions are read in one snapshot.</param>
    /// <returns>The entries by the name of the index; <see langword="null"/> for a row that cannot be read.</returns>
    internal static Dictionary<string, IndexStoreEntry?> ReadAll(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT index_name, kind, source, definition, schema_version FROM lui_meta;";

        var entries = new Dictionary<string, IndexStoreEntry?>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            try
            {
                entries[name] = Parse(reader, name, firstColumn: 1);
            }
            catch (Exception exception) when (exception is LuiDefinitionMismatchException or InvalidOperationException or System.Text.Json.JsonException)
            {
                entries[name] = null;
            }
        }

        return entries;
    }

    /// <summary>The entry of a row: kind, source, definition and the LUI format version, from the column <paramref name="firstColumn"/> on.</summary>
    private static IndexStoreEntry Parse(SqliteDataReader reader, string indexName, int firstColumn)
    {
        var kind = ReadKind(reader.GetString(firstColumn), indexName);
        var source = reader.GetString(firstColumn + 1);
        var definition = IndexDefinition.FromJson(reader.GetString(firstColumn + 2))
            ?? throw new InvalidOperationException(
                $"The definition of the index '{indexName}' in lui_meta is empty; the file was written by another version "
                + "of LUI and cannot be read: remove the index (LuiCatalog.DropIndex) and create it again.");
        var schemaVersion = reader.GetInt32(firstColumn + 3);

        return new IndexStoreEntry(kind, source, definition, schemaVersion);
    }

    /// <summary>
    /// The names of the indexes of the file — the list of what <c>lui_meta</c> holds (ADR-0043: the warm-up of a host
    /// asks the catalog for its indexes).
    /// </summary>
    /// <param name="database">The database.</param>
    /// <returns>The names, in the order of the table.</returns>
    internal static IReadOnlyList<string> ReadIndexNames(Database database)
    {
        using var connection = database.OpenReadConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT index_name FROM lui_meta ORDER BY index_name;";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>The kind of the stored index; a kind this version does not know is a file of another version.</summary>
    private static LuiIndexKind ReadKind(string kind, string indexName) =>
        Enum.TryParse(kind, out LuiIndexKind parsed)
            ? parsed
            : throw new LuiDefinitionMismatchException(
                indexName,
                $"the kind {kind}",
                $"a kind of this version ({string.Join(", ", Enum.GetNames<LuiIndexKind>())})",
                "The index was written by another version of LUI: remove it with LuiCatalog.DropIndex(name) and create "
                + "it again.");

    /// <summary>The stored definition of an index, or <see langword="null"/> when there is none.</summary>
    internal static IndexDefinition? ReadDefinition(SqliteConnection connection, SqliteTransaction? transaction, string indexName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT definition FROM lui_meta WHERE index_name = $name;";
        command.Parameters.AddWithValue("$name", indexName);

        return command.ExecuteScalar() is string json ? IndexDefinition.FromJson(json) : null;
    }

    private static void WriteDefinition(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string indexName,
        IndexDefinition definition,
        DateTimeOffset createdAt)
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
        command.Parameters.AddWithValue("$created", createdAt.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static bool HasTriggers(SqliteConnection connection, SqliteTransaction transaction, string indexName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = LinkedSource.TriggerCountSql(indexName);
        return (long)command.ExecuteScalar()! > 0;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string statement)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = statement;
        command.ExecuteNonQuery();
    }
}
